using Microsoft.EntityFrameworkCore;
using Npgsql;
using Textzy.Api.Data;
using Textzy.Api.Models;

namespace Textzy.Api.Services;

public class TenantProvisioningService(
    ControlDbContext controlDb,
    IConfiguration config)
{
    public async Task<Tenant> ProvisionAsync(Guid requestingUserId, string name, string slug, CancellationToken ct = default)
    {
        slug = NormalizeSlug(slug);
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(slug))
            throw new InvalidOperationException("Name and slug are required.");

        var exists = await controlDb.Tenants.AnyAsync(t => t.Slug == slug, ct);
        if (exists) throw new InvalidOperationException("Tenant slug already exists.");

        var rootConn = config.GetConnectionString("Default")
            ?? throw new InvalidOperationException("Connection string 'Default' missing.");

        var dbName = $"textzy_tenant_{slug.Replace('-', '_')}";
        await CreateDatabaseIfNotExistsAsync(rootConn, dbName, ct);

        var tenantConn = BuildTenantConnection(rootConn, dbName);

        var tenant = new Tenant
        {
            Id = Guid.NewGuid(),
            Name = name.Trim(),
            Slug = slug,
            DataConnectionString = tenantConn
        };

        controlDb.Tenants.Add(tenant);
        controlDb.TenantUsers.Add(new TenantUser
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            UserId = requestingUserId,
            Role = "owner"
        });
        await controlDb.SaveChangesAsync(ct);

        using var tenantDb = SeedData.CreateTenantDbContext(tenantConn);
        await tenantDb.Database.EnsureCreatedAsync(ct);
        SeedData.InitializeTenant(tenantDb, tenant.Id);

        return tenant;
    }

    private static async Task CreateDatabaseIfNotExistsAsync(string rootConn, string dbName, CancellationToken ct)
    {
        var csb = new NpgsqlConnectionStringBuilder(rootConn) { Database = "postgres" };
        await using var conn = new NpgsqlConnection(csb.ConnectionString);
        await conn.OpenAsync(ct);

        var existsCmd = new NpgsqlCommand("SELECT 1 FROM pg_database WHERE datname = @n", conn);
        existsCmd.Parameters.AddWithValue("n", dbName);
        var exists = await existsCmd.ExecuteScalarAsync(ct);
        if (exists is not null) return;

        var safeDbName = dbName.Replace("\"", "\"\"");
        var createCmd = new NpgsqlCommand($"CREATE DATABASE \"{safeDbName}\"", conn);
        await createCmd.ExecuteNonQueryAsync(ct);
    }

    private static string BuildTenantConnection(string rootConn, string dbName)
    {
        var csb = new NpgsqlConnectionStringBuilder(rootConn)
        {
            Database = dbName
        };
        return csb.ConnectionString;
    }

    private static string NormalizeSlug(string slug)
    {
        var trimmed = slug.Trim().ToLowerInvariant();
        var chars = trimmed
            .Select(ch => char.IsLetterOrDigit(ch) ? ch : '-')
            .ToArray();
        var normalized = new string(chars);
        while (normalized.Contains("--", StringComparison.Ordinal)) normalized = normalized.Replace("--", "-", StringComparison.Ordinal);
        return normalized.Trim('-');
    }
}
