using Textzy.Api.Data;
using Textzy.Api.Models;

namespace Textzy.Api.Services;

public class AuditLogService(ControlDbContext db, TenancyContext tenancy, AuthContext auth, IHttpContextAccessor httpContextAccessor)
{
    public async Task WriteAsync(string action, string details, CancellationToken ct = default)
    {
        if (!auth.IsAuthenticated) return;
        var http = httpContextAccessor.HttpContext;
        db.AuditLogs.Add(new AuditLog
        {
            Id = Guid.NewGuid(),
            TenantId = tenancy.IsSet ? tenancy.TenantId : null,
            ActorUserId = auth.UserId,
            Action = action,
            Details = details,
            IpAddress = RequestMetadata.GetClientIp(http),
            UserAgent = RequestMetadata.GetUserAgent(http),
            DeviceLabel = RequestMetadata.GetDeviceLabel(http),
            CreatedAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync(ct);
    }
}
