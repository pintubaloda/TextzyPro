using Microsoft.EntityFrameworkCore;
using Textzy.Api.Data;
using Textzy.Api.Models;

namespace Textzy.Api.Services;

public class BillingLifecycleWorker(
    IServiceScopeFactory scopeFactory,
    IConfiguration config,
    SensitiveDataRedactor redactor,
    ILogger<BillingLifecycleWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunCycleAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogWarning("Billing lifecycle worker iteration failed: {Error}", redactor.RedactText(ex.Message));
            }

            try
            {
                await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
            }
            catch (TaskCanceledException)
            {
                break;
            }
        }
    }

    private async Task RunCycleAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
        var email = scope.ServiceProvider.GetRequiredService<IEmailService>();

        var now = DateTime.UtcNow;
        var graceDays = ResolveGraceDays(config);
        var renewalReminderDays = ResolveDayOffsets(config["Billing:RenewalReminderDays"] ?? config["BILLING_RENEWAL_REMINDER_DAYS"], [7, 3, 1, 0]);
        var dunningReminderDays = ResolveDayOffsets(config["Billing:DunningReminderDays"] ?? config["BILLING_DUNNING_REMINDER_DAYS"], [1, 3, 5]);

        var activeOrDue = await db.TenantSubscriptions
            .Where(x => x.Status == "active" || x.Status == "past_due")
            .OrderByDescending(x => x.CreatedAtUtc)
            .ToListAsync(ct);

        foreach (var sub in activeOrDue)
        {
            if (sub.RenewAtUtc >= DateTime.MaxValue.AddDays(-2)) continue;

            var daysLeft = (int)Math.Floor((sub.RenewAtUtc - now).TotalDays);
            if (string.Equals(sub.Status, "active", StringComparison.OrdinalIgnoreCase) &&
                renewalReminderDays.Contains(daysLeft))
            {
                var key = $"tenant={sub.TenantId};renew={sub.RenewAtUtc:yyyy-MM-dd};days={daysLeft}";
                var sent = await db.AuditLogs.AsNoTracking()
                    .AnyAsync(x => x.TenantId == sub.TenantId && x.Action == "billing.renewal.reminder" && x.Details == key, ct);
                if (!sent)
                {
                    var recipient = await ResolveRecipientAsync(db, sub.TenantId, ct);
                    if (!string.IsNullOrWhiteSpace(recipient.email))
                    {
                        await email.SendBillingEventAsync(
                            recipient.email,
                            recipient.name,
                            recipient.companyName,
                            "Renewal reminder",
                            $"Your subscription renews in {Math.Max(daysLeft, 0)} day(s).",
                            new Dictionary<string, string>
                            {
                                ["Renewal Date"] = sub.RenewAtUtc.ToString("yyyy-MM-dd"),
                                ["Status"] = sub.Status,
                                ["Billing Cycle"] = sub.BillingCycle
                            },
                            ct);
                    }

                    db.AuditLogs.Add(new AuditLog
                    {
                        Id = Guid.NewGuid(),
                        TenantId = sub.TenantId,
                        ActorUserId = Guid.Empty,
                        Action = "billing.renewal.reminder",
                        Details = key,
                        CreatedAtUtc = DateTime.UtcNow
                    });
                    await db.SaveChangesAsync(ct);
                }
            }

            if (string.Equals(sub.Status, "past_due", StringComparison.OrdinalIgnoreCase))
            {
                var daysPastDue = (int)Math.Floor((now - sub.RenewAtUtc).TotalDays);
                if (daysPastDue >= 0 && dunningReminderDays.Contains(daysPastDue))
                {
                    var key = $"tenant={sub.TenantId};renew={sub.RenewAtUtc:yyyy-MM-dd};dueDays={daysPastDue}";
                    var sent = await db.AuditLogs.AsNoTracking()
                        .AnyAsync(x => x.TenantId == sub.TenantId && x.Action == "billing.dunning.reminder" && x.Details == key, ct);
                    if (!sent)
                    {
                        var recipient = await ResolveRecipientAsync(db, sub.TenantId, ct);
                        if (!string.IsNullOrWhiteSpace(recipient.email))
                        {
                            var graceLeft = Math.Max(0, graceDays - daysPastDue);
                            await email.SendBillingEventAsync(
                                recipient.email,
                                recipient.name,
                                recipient.companyName,
                                "Payment pending reminder",
                                $"Your subscription payment is pending for {daysPastDue} day(s).",
                                new Dictionary<string, string>
                                {
                                    ["Status"] = sub.Status,
                                    ["Overdue Days"] = daysPastDue.ToString(),
                                    ["Grace Days Left"] = graceLeft.ToString(),
                                    ["Renewal Date"] = sub.RenewAtUtc.ToString("yyyy-MM-dd")
                                },
                                ct);
                        }

                        db.AuditLogs.Add(new AuditLog
                        {
                            Id = Guid.NewGuid(),
                            TenantId = sub.TenantId,
                            ActorUserId = Guid.Empty,
                            Action = "billing.dunning.reminder",
                            Details = key,
                            CreatedAtUtc = DateTime.UtcNow
                        });
                        await db.SaveChangesAsync(ct);
                    }
                }

                var deadline = sub.RenewAtUtc.AddDays(graceDays);
                if (now >= deadline)
                {
                    sub.Status = "suspended";
                    sub.UpdatedAtUtc = now;

                    var recipient = await ResolveRecipientAsync(db, sub.TenantId, ct);
                    if (!string.IsNullOrWhiteSpace(recipient.email))
                    {
                        await email.SendBillingEventAsync(
                            recipient.email,
                            recipient.name,
                            recipient.companyName,
                            "Subscription suspended",
                            "Your grace period ended. Please complete payment to reactivate service.",
                            new Dictionary<string, string>
                            {
                                ["Grace Deadline"] = deadline.ToString("yyyy-MM-dd HH:mm:ss 'UTC'"),
                                ["Status"] = "suspended"
                            },
                            ct);
                    }

                    db.AuditLogs.Add(new AuditLog
                    {
                        Id = Guid.NewGuid(),
                        TenantId = sub.TenantId,
                        ActorUserId = Guid.Empty,
                        Action = "billing.dunning.suspended",
                        Details = $"tenant={sub.TenantId};deadline={deadline:O}",
                        CreatedAtUtc = DateTime.UtcNow
                    });
                    await db.SaveChangesAsync(ct);
                }
            }
        }
    }

    private static int ResolveGraceDays(IConfiguration config)
    {
        var raw = (config["Billing:GraceDays"] ?? config["BILLING_GRACE_DAYS"] ?? "7").Trim();
        if (int.TryParse(raw, out var days)) return Math.Clamp(days, 0, 60);
        return 7;
    }

    private static HashSet<int> ResolveDayOffsets(string? raw, IEnumerable<int> defaults)
    {
        var set = new HashSet<int>();
        var source = string.IsNullOrWhiteSpace(raw) ? string.Join(",", defaults) : raw;
        foreach (var token in source.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (int.TryParse(token, out var day))
                set.Add(Math.Clamp(day, 0, 60));
        }
        if (set.Count == 0)
        {
            foreach (var d in defaults) set.Add(Math.Clamp(d, 0, 60));
        }
        return set;
    }

    private static async Task<(string email, string name, string companyName)> ResolveRecipientAsync(ControlDbContext db, Guid tenantId, CancellationToken ct)
    {
        var profile = await db.TenantCompanyProfiles.AsNoTracking().FirstOrDefaultAsync(x => x.TenantId == tenantId, ct);
        var tenant = await db.Tenants.AsNoTracking().FirstOrDefaultAsync(x => x.Id == tenantId, ct);
        var companyName = string.IsNullOrWhiteSpace(profile?.CompanyName) ? (tenant?.Name ?? "Textzy Workspace") : profile!.CompanyName;

        if (!string.IsNullOrWhiteSpace(profile?.BillingEmail))
            return (profile.BillingEmail.Trim(), profile.CompanyName, companyName);

        var ownerUserId = await db.TenantUsers.AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.Role.ToLower() == "owner")
            .OrderBy(x => x.CreatedAtUtc)
            .Select(x => x.UserId)
            .FirstOrDefaultAsync(ct);
        if (ownerUserId != Guid.Empty)
        {
            var owner = await db.Users.AsNoTracking().FirstOrDefaultAsync(x => x.Id == ownerUserId, ct);
            if (!string.IsNullOrWhiteSpace(owner?.Email))
                return (owner.Email.Trim(), owner.FullName, companyName);
        }

        return (string.Empty, string.Empty, companyName);
    }
}
