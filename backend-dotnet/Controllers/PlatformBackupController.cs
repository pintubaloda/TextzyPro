using System.Data.Common;
using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Textzy.Api.Data;
using Textzy.Api.Services;
using static Textzy.Api.Services.PermissionCatalog;

namespace Textzy.Api.Controllers;

[ApiController]
[Route("api/platform/backup")]
public class PlatformBackupController(
    ControlDbContext db,
    AuthContext auth,
    RbacService rbac) : ControllerBase
{
    [HttpGet("sql")]
    public async Task<IActionResult> ExportSql(CancellationToken ct)
    {
        if (!auth.IsAuthenticated) return Unauthorized();
        if (!rbac.HasPermission(PlatformSettingsRead)) return Forbid();

        var conn = db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open)
            await conn.OpenAsync(ct);

        var sb = new StringBuilder(1_000_000);
        sb.AppendLine("-- Textzy platform SQL backup");
        sb.AppendLine($"-- GeneratedAtUtc: {DateTime.UtcNow:O}");
        sb.AppendLine("BEGIN;");
        sb.AppendLine("SET client_encoding = 'UTF8';");
        sb.AppendLine("SET standard_conforming_strings = on;");
        sb.AppendLine();

        var tables = await GetPublicTablesAsync(conn, ct);
        foreach (var table in tables)
        {
            var columns = await GetTableColumnsAsync(conn, table, ct);
            if (columns.Count == 0) continue;

            sb.AppendLine($"-- Table: public.{table}");
            sb.AppendLine($"DROP TABLE IF EXISTS {Q(table)};");
            sb.AppendLine($"CREATE TABLE {Q(table)} (");
            for (var i = 0; i < columns.Count; i++)
            {
                var c = columns[i];
                var comma = i == columns.Count - 1 ? "" : ",";
                var notNull = c.NotNull ? " NOT NULL" : "";
                var def = string.IsNullOrWhiteSpace(c.DefaultExpr) ? "" : $" DEFAULT {c.DefaultExpr}";
                sb.AppendLine($"  {Q(c.Name)} {c.Type}{def}{notNull}{comma}");
            }

            var pk = await GetPrimaryKeyColumnsAsync(conn, table, ct);
            if (pk.Count > 0)
            {
                sb.AppendLine($",  PRIMARY KEY ({string.Join(", ", pk.Select(Q))})");
            }
            sb.AppendLine(");");

            await AppendRowsAsInsertAsync(conn, table, columns, sb, ct);
            sb.AppendLine();
        }

        sb.AppendLine("COMMIT;");

        var bytes = Encoding.UTF8.GetBytes(sb.ToString());
        var fileName = $"textzy_platform_backup_{DateTime.UtcNow:yyyyMMdd_HHmmss}.sql";
        return File(bytes, "application/sql; charset=utf-8", fileName);
    }

    private static async Task<List<string>> GetPublicTablesAsync(DbConnection conn, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT table_name
            FROM information_schema.tables
            WHERE table_schema='public' AND table_type='BASE TABLE'
            ORDER BY table_name;
            """;
        var tables = new List<string>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            tables.Add(r.GetString(0));
        return tables;
    }

    private sealed class TableColumn
    {
        public string Name { get; init; } = string.Empty;
        public string Type { get; init; } = "text";
        public bool NotNull { get; init; }
        public string DefaultExpr { get; init; } = string.Empty;
    }

    private static async Task<List<TableColumn>> GetTableColumnsAsync(DbConnection conn, string table, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT a.attname, pg_catalog.format_type(a.atttypid, a.atttypmod) AS data_type,
                   a.attnotnull, COALESCE(pg_get_expr(ad.adbin, ad.adrelid), '')
            FROM pg_attribute a
            JOIN pg_class c ON c.oid = a.attrelid
            JOIN pg_namespace n ON n.oid = c.relnamespace
            LEFT JOIN pg_attrdef ad ON ad.adrelid = a.attrelid AND ad.adnum = a.attnum
            WHERE n.nspname='public' AND c.relname = @table AND a.attnum > 0 AND NOT a.attisdropped
            ORDER BY a.attnum;
            """;
        var p = cmd.CreateParameter();
        p.ParameterName = "table";
        p.Value = table;
        cmd.Parameters.Add(p);

        var cols = new List<TableColumn>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            cols.Add(new TableColumn
            {
                Name = r.GetString(0),
                Type = r.GetString(1),
                NotNull = r.GetBoolean(2),
                DefaultExpr = r.GetString(3)
            });
        }
        return cols;
    }

    private static async Task<List<string>> GetPrimaryKeyColumnsAsync(DbConnection conn, string table, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT kcu.column_name
            FROM information_schema.table_constraints tc
            JOIN information_schema.key_column_usage kcu
              ON kcu.constraint_name = tc.constraint_name
             AND kcu.table_schema = tc.table_schema
             AND kcu.table_name = tc.table_name
            WHERE tc.table_schema='public'
              AND tc.table_name=@table
              AND tc.constraint_type='PRIMARY KEY'
            ORDER BY kcu.ordinal_position;
            """;
        var p = cmd.CreateParameter();
        p.ParameterName = "table";
        p.Value = table;
        cmd.Parameters.Add(p);

        var cols = new List<string>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            cols.Add(r.GetString(0));
        return cols;
    }

    private static async Task AppendRowsAsInsertAsync(DbConnection conn, string table, List<TableColumn> columns, StringBuilder sb, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT * FROM {Q(table)};";
        await using var r = await cmd.ExecuteReaderAsync(ct);
        var colNames = string.Join(", ", columns.Select(c => Q(c.Name)));

        while (await r.ReadAsync(ct))
        {
            var vals = new string[r.FieldCount];
            for (var i = 0; i < r.FieldCount; i++)
            {
                var v = r.GetValue(i);
                vals[i] = ToSqlLiteral(v);
            }
            sb.AppendLine($"INSERT INTO {Q(table)} ({colNames}) VALUES ({string.Join(", ", vals)});");
        }
    }

    private static string ToSqlLiteral(object? value)
    {
        if (value is null || value == DBNull.Value) return "NULL";

        return value switch
        {
            string s => $"'{s.Replace("'", "''")}'",
            bool b => b ? "TRUE" : "FALSE",
            byte bt => bt.ToString(CultureInfo.InvariantCulture),
            short sh => sh.ToString(CultureInfo.InvariantCulture),
            int i => i.ToString(CultureInfo.InvariantCulture),
            long l => l.ToString(CultureInfo.InvariantCulture),
            float f => f.ToString(CultureInfo.InvariantCulture),
            double d => d.ToString(CultureInfo.InvariantCulture),
            decimal m => m.ToString(CultureInfo.InvariantCulture),
            Guid g => $"'{g}'",
            DateTime dt => $"'{dt:O}'",
            DateTimeOffset dto => $"'{dto:O}'",
            byte[] bytes => $"decode('{Convert.ToHexString(bytes)}','hex')",
            Array arr when value is not byte[] => $"ARRAY[{string.Join(", ", arr.Cast<object?>().Select(ToSqlLiteral))}]",
            _ => $"'{value.ToString()?.Replace("'", "''") ?? string.Empty}'"
        };
    }

    private static string Q(string identifier) => $"\"{identifier.Replace("\"", "\"\"")}\"";
}
