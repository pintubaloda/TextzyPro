using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Textzy.Api.Data;
using Textzy.Api.Models;
using Textzy.Api.Services;

namespace Textzy.Api.Controllers;

[ApiController]
[Route("api/support")]
public class SupportTicketsController(
    ControlDbContext db,
    AuthContext auth,
    TenancyContext tenancy,
    SecretCryptoService crypto,
    AuditLogService audit) : ControllerBase
{
    [HttpGet("context")]
    public async Task<IActionResult> GetContext(CancellationToken ct)
    {
        if (!auth.IsAuthenticated || !tenancy.IsSet) return Unauthorized();

        var tenant = await db.Tenants.AsNoTracking().FirstOrDefaultAsync(x => x.Id == tenancy.TenantId, ct);
        if (tenant is null) return NotFound("Tenant not found.");

        var company = await db.TenantCompanyProfiles.AsNoTracking().FirstOrDefaultAsync(x => x.TenantId == tenancy.TenantId, ct);
        var latestSub = await db.TenantSubscriptions.AsNoTracking()
            .Where(x => x.TenantId == tenancy.TenantId)
            .OrderByDescending(x => x.CreatedAtUtc)
            .FirstOrDefaultAsync(ct);
        var plan = latestSub is null
            ? null
            : await db.BillingPlans.AsNoTracking().FirstOrDefaultAsync(x => x.Id == latestSub.PlanId, ct);

        var branding = await db.PlatformSettings.AsNoTracking()
            .Where(x => x.Scope == "platform-branding")
            .ToListAsync(ct);
        var brandingMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in branding)
            brandingMap[row.Key] = crypto.Decrypt(row.ValueEncrypted);

        var summaryRows = await db.SupportTickets.AsNoTracking()
            .Where(x => x.TenantId == tenancy.TenantId)
            .GroupBy(x => x.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var catalog = SupportTicketCatalog.Build(plan?.Code ?? string.Empty, plan?.Name ?? string.Empty);
        var ticketServices = await db.SupportTickets.AsNoTracking()
            .Where(x => x.TenantId == tenancy.TenantId)
            .Select(x => new { x.ServiceKey, x.ServiceName })
            .Distinct()
            .ToListAsync(ct);

        var serviceOptions = catalog
            .Select(x => new { key = x.Key, name = x.Name })
            .Concat(ticketServices.Select(x => new { key = x.ServiceKey, name = x.ServiceName }))
            .GroupBy(x => x.key, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderBy(x => x.name)
            .ToList();

        return Ok(new
        {
            project = new
            {
                tenantId = tenant.Id,
                tenantSlug = tenant.Slug,
                projectName = tenant.Name,
                companyName = string.IsNullOrWhiteSpace(company?.CompanyName) ? tenant.Name : company.CompanyName,
                legalName = company?.LegalName ?? string.Empty
            },
            support = new
            {
                platformName = FirstNonEmpty(brandingMap, "platformName", "legalName") ?? "Textzy",
                legalName = FirstNonEmpty(brandingMap, "legalName", "platformName") ?? "Textzy",
                gstin = FirstNonEmpty(brandingMap, "gstin"),
                address = FirstNonEmpty(brandingMap, "address"),
                supportEmail = FirstNonEmpty(brandingMap, "supportEmail", "billingEmail"),
                supportPhone = FirstNonEmpty(brandingMap, "supportPhone", "billingPhone"),
                website = FirstNonEmpty(brandingMap, "website")
            },
            serviceOptions,
            currentPlan = plan is null ? null : new { code = plan.Code, name = plan.Name },
            summary = new
            {
                total = summaryRows.Sum(x => x.Count),
                open = summaryRows.Where(x => string.Equals(x.Status, "open", StringComparison.OrdinalIgnoreCase)).Sum(x => x.Count),
                waitingOnCustomer = summaryRows.Where(x => string.Equals(x.Status, "waiting_on_customer", StringComparison.OrdinalIgnoreCase)).Sum(x => x.Count),
                closed = summaryRows.Where(x => string.Equals(x.Status, "closed", StringComparison.OrdinalIgnoreCase)).Sum(x => x.Count)
            }
        });
    }

    [HttpGet("tickets")]
    public async Task<IActionResult> List(
        [FromQuery] string status = "",
        [FromQuery] string service = "",
        [FromQuery] string q = "",
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        if (!auth.IsAuthenticated || !tenancy.IsSet) return Unauthorized();

        var normalizedStatus = SupportTicketCatalog.NormalizeStatus(status);
        var serviceFilter = (service ?? string.Empty).Trim().ToLowerInvariant();
        var search = (q ?? string.Empty).Trim().ToLowerInvariant();
        var safePageSize = Math.Clamp(pageSize, 10, 100);
        var requestedPage = Math.Max(1, page);

        var baseQuery = db.SupportTickets.AsNoTracking()
            .Where(x => x.TenantId == tenancy.TenantId);

        if (!string.IsNullOrWhiteSpace(normalizedStatus))
            baseQuery = baseQuery.Where(x => x.Status == normalizedStatus);

        if (!string.IsNullOrWhiteSpace(serviceFilter))
        {
            baseQuery = baseQuery.Where(x =>
                x.ServiceKey.ToLower() == serviceFilter ||
                x.ServiceName.ToLower().Contains(serviceFilter));
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            baseQuery = baseQuery.Where(x =>
                x.TicketNo.ToLower().Contains(search) ||
                x.Subject.ToLower().Contains(search) ||
                x.ServiceName.ToLower().Contains(search) ||
                x.CompanyName.ToLower().Contains(search) ||
                x.CreatedByName.ToLower().Contains(search) ||
                x.CreatedByEmail.ToLower().Contains(search));
        }

        var totalCount = await baseQuery.CountAsync(ct);
        var totalPages = Math.Max(1, (int)Math.Ceiling(totalCount / (double)safePageSize));
        var safePage = Math.Min(requestedPage, totalPages);
        var skip = (safePage - 1) * safePageSize;

        var messageCounts = db.SupportTicketMessages.AsNoTracking()
            .Where(x => x.TenantId == tenancy.TenantId)
            .GroupBy(x => x.TicketId)
            .Select(g => new { TicketId = g.Key, Count = g.Count() });

        var rows = await (
            from ticket in baseQuery
            from messageCount in messageCounts.Where(x => x.TicketId == ticket.Id).DefaultIfEmpty()
            orderby ticket.LastMessageAtUtc descending, ticket.CreatedAtUtc descending
            select new
            {
                ticket.Id,
                ticket.TicketNo,
                ticket.Subject,
                ticket.ServiceKey,
                ticket.ServiceName,
                ticket.Status,
                ticket.Priority,
                ticket.CompanyName,
                ticket.TenantName,
                ticket.TenantSlug,
                ticket.CreatedByName,
                ticket.CreatedByEmail,
                ticket.LastMessagePreview,
                ticket.LastActorType,
                ticket.LastMessageAtUtc,
                ticket.CreatedAtUtc,
                ticket.UpdatedAtUtc,
                MessageCount = messageCount != null ? messageCount.Count : 0
            })
            .Skip(skip)
            .Take(safePageSize)
            .ToListAsync(ct);

        var statusSummary = await db.SupportTickets.AsNoTracking()
            .Where(x => x.TenantId == tenancy.TenantId)
            .GroupBy(x => x.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        return Ok(new
        {
            page = safePage,
            pageSize = safePageSize,
            totalCount,
            totalPages,
            hasPreviousPage = safePage > 1,
            hasNextPage = safePage < totalPages,
            summary = new
            {
                total = statusSummary.Sum(x => x.Count),
                open = statusSummary.Where(x => x.Status == "open").Sum(x => x.Count),
                waitingOnCustomer = statusSummary.Where(x => x.Status == "waiting_on_customer").Sum(x => x.Count),
                closed = statusSummary.Where(x => x.Status == "closed").Sum(x => x.Count)
            },
            items = rows.Select(x => new
            {
                id = x.Id,
                ticketNo = x.TicketNo,
                subject = x.Subject,
                serviceKey = x.ServiceKey,
                serviceName = x.ServiceName,
                status = x.Status,
                priority = x.Priority,
                companyName = x.CompanyName,
                tenantName = x.TenantName,
                tenantSlug = x.TenantSlug,
                createdByName = x.CreatedByName,
                createdByEmail = x.CreatedByEmail,
                lastMessagePreview = x.LastMessagePreview,
                lastActorType = x.LastActorType,
                lastMessageAtUtc = x.LastMessageAtUtc,
                createdAtUtc = x.CreatedAtUtc,
                updatedAtUtc = x.UpdatedAtUtc,
                messageCount = x.MessageCount,
                canReply = x.Status != "closed",
                canReopen = x.Status == "closed"
            })
        });
    }

    [HttpGet("tickets/{ticketId:guid}")]
    public async Task<IActionResult> Details(Guid ticketId, CancellationToken ct)
    {
        if (!auth.IsAuthenticated || !tenancy.IsSet) return Unauthorized();

        var ticket = await db.SupportTickets.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == ticketId && x.TenantId == tenancy.TenantId, ct);
        if (ticket is null) return NotFound("Ticket not found.");

        var messages = await db.SupportTicketMessages.AsNoTracking()
            .Where(x => x.TicketId == ticketId && x.TenantId == tenancy.TenantId)
            .OrderBy(x => x.CreatedAtUtc)
            .ToListAsync(ct);

        return Ok(MapDetail(ticket, messages));
    }

    [HttpPost("tickets")]
    public async Task<IActionResult> Create([FromBody] CreateSupportTicketRequest request, CancellationToken ct)
    {
        if (!auth.IsAuthenticated || !tenancy.IsSet) return Unauthorized();

        string subject;
        string serviceName;
        string body;
        try
        {
            subject = InputGuardService.RequireTrimmed(request.Subject, "Subject", 180);
            serviceName = InputGuardService.RequireTrimmed(request.ServiceName, "Service", 120);
            body = InputGuardService.RequireTrimmed(request.Body, "Message", 4000);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }

        var tenant = await db.Tenants.FirstOrDefaultAsync(x => x.Id == tenancy.TenantId, ct);
        if (tenant is null) return NotFound("Tenant not found.");

        var company = await db.TenantCompanyProfiles.AsNoTracking().FirstOrDefaultAsync(x => x.TenantId == tenancy.TenantId, ct);
        var now = DateTime.UtcNow;
        var ticket = new SupportTicket
        {
            Id = Guid.NewGuid(),
            TicketNo = SupportTicketCatalog.FormatTicketNo(),
            TenantId = tenancy.TenantId,
            OwnerGroupId = tenant.OwnerGroupId,
            CreatedByUserId = auth.UserId,
            TenantName = tenant.Name,
            TenantSlug = tenant.Slug,
            CompanyName = string.IsNullOrWhiteSpace(company?.CompanyName) ? tenant.Name : company.CompanyName,
            CreatedByName = string.IsNullOrWhiteSpace(auth.FullName) ? auth.Email : auth.FullName,
            CreatedByEmail = auth.Email,
            ServiceKey = NormalizeServiceKey(request.ServiceKey, serviceName),
            ServiceName = serviceName,
            Subject = subject,
            Status = "open",
            Priority = SupportTicketCatalog.NormalizePriority(request.Priority),
            LastMessagePreview = SupportTicketCatalog.BuildPreview(body),
            LastActorType = "customer",
            LastMessageAtUtc = now,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        var message = new SupportTicketMessage
        {
            Id = Guid.NewGuid(),
            TicketId = ticket.Id,
            TenantId = tenancy.TenantId,
            AuthorUserId = auth.UserId,
            AuthorName = ticket.CreatedByName,
            AuthorEmail = auth.Email,
            AuthorType = "customer",
            Body = body,
            CreatedAtUtc = now
        };

        db.SupportTickets.Add(ticket);
        db.SupportTicketMessages.Add(message);
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync("support.ticket.created", $"ticket={ticket.TicketNo}; service={ticket.ServiceKey}", ct);

        return Ok(MapDetail(ticket, new[] { message }));
    }

    [HttpPost("tickets/{ticketId:guid}/reply")]
    public async Task<IActionResult> Reply(Guid ticketId, [FromBody] ReplySupportTicketRequest request, CancellationToken ct)
    {
        if (!auth.IsAuthenticated || !tenancy.IsSet) return Unauthorized();

        string body;
        try
        {
            body = InputGuardService.RequireTrimmed(request.Body, "Reply", 4000);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }

        var ticket = await db.SupportTickets.FirstOrDefaultAsync(x => x.Id == ticketId && x.TenantId == tenancy.TenantId, ct);
        if (ticket is null) return NotFound("Ticket not found.");
        if (ticket.Status == "closed") return BadRequest("Closed ticket cannot be replied to. Reopen it first.");

        var now = DateTime.UtcNow;
        var authorName = string.IsNullOrWhiteSpace(auth.FullName) ? auth.Email : auth.FullName;
        var message = new SupportTicketMessage
        {
            Id = Guid.NewGuid(),
            TicketId = ticket.Id,
            TenantId = ticket.TenantId,
            AuthorUserId = auth.UserId,
            AuthorName = authorName,
            AuthorEmail = auth.Email,
            AuthorType = "customer",
            Body = body,
            CreatedAtUtc = now
        };

        db.SupportTicketMessages.Add(message);
        ticket.Status = "open";
        ticket.LastActorType = "customer";
        ticket.LastMessagePreview = SupportTicketCatalog.BuildPreview(body);
        ticket.LastMessageAtUtc = now;
        ticket.UpdatedAtUtc = now;
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync("support.ticket.reply", $"ticket={ticket.TicketNo}", ct);

        var messages = await db.SupportTicketMessages.AsNoTracking()
            .Where(x => x.TicketId == ticket.Id)
            .OrderBy(x => x.CreatedAtUtc)
            .ToListAsync(ct);
        return Ok(MapDetail(ticket, messages));
    }

    [HttpPost("tickets/{ticketId:guid}/reopen")]
    public async Task<IActionResult> Reopen(Guid ticketId, [FromBody] ReopenSupportTicketRequest? request, CancellationToken ct)
    {
        if (!auth.IsAuthenticated || !tenancy.IsSet) return Unauthorized();

        var ticket = await db.SupportTickets.FirstOrDefaultAsync(x => x.Id == ticketId && x.TenantId == tenancy.TenantId, ct);
        if (ticket is null) return NotFound("Ticket not found.");

        var now = DateTime.UtcNow;
        ticket.Status = "open";
        ticket.ReopenedByUserId = auth.UserId;
        ticket.ReopenedAtUtc = now;
        ticket.ClosedAtUtc = null;
        ticket.ClosedByUserId = null;
        ticket.UpdatedAtUtc = now;

        var note = (request?.Message ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(note))
        {
            if (note.Length > 4000) return BadRequest("Message is too long.");
            var authorName = string.IsNullOrWhiteSpace(auth.FullName) ? auth.Email : auth.FullName;
            var message = new SupportTicketMessage
            {
                Id = Guid.NewGuid(),
                TicketId = ticket.Id,
                TenantId = ticket.TenantId,
                AuthorUserId = auth.UserId,
                AuthorName = authorName,
                AuthorEmail = auth.Email,
                AuthorType = "customer",
                Body = note,
                CreatedAtUtc = now
            };
            db.SupportTicketMessages.Add(message);
            ticket.LastActorType = "customer";
            ticket.LastMessagePreview = SupportTicketCatalog.BuildPreview(note);
            ticket.LastMessageAtUtc = now;
        }

        await db.SaveChangesAsync(ct);
        await audit.WriteAsync("support.ticket.reopen", $"ticket={ticket.TicketNo}", ct);

        var messages = await db.SupportTicketMessages.AsNoTracking()
            .Where(x => x.TicketId == ticket.Id)
            .OrderBy(x => x.CreatedAtUtc)
            .ToListAsync(ct);
        return Ok(MapDetail(ticket, messages));
    }

    private static object MapDetail(SupportTicket ticket, IReadOnlyList<SupportTicketMessage> messages)
    {
        return new
        {
            ticket = new
            {
                id = ticket.Id,
                ticketNo = ticket.TicketNo,
                tenantId = ticket.TenantId,
                tenantName = ticket.TenantName,
                tenantSlug = ticket.TenantSlug,
                companyName = ticket.CompanyName,
                createdByUserId = ticket.CreatedByUserId,
                createdByName = ticket.CreatedByName,
                createdByEmail = ticket.CreatedByEmail,
                serviceKey = ticket.ServiceKey,
                serviceName = ticket.ServiceName,
                subject = ticket.Subject,
                status = ticket.Status,
                priority = ticket.Priority,
                lastMessagePreview = ticket.LastMessagePreview,
                lastActorType = ticket.LastActorType,
                lastMessageAtUtc = ticket.LastMessageAtUtc,
                closedAtUtc = ticket.ClosedAtUtc,
                reopenedAtUtc = ticket.ReopenedAtUtc,
                createdAtUtc = ticket.CreatedAtUtc,
                updatedAtUtc = ticket.UpdatedAtUtc,
                canReply = ticket.Status != "closed",
                canReopen = ticket.Status == "closed"
            },
            messages = messages.Select(x => new
            {
                id = x.Id,
                ticketId = x.TicketId,
                authorUserId = x.AuthorUserId,
                authorName = x.AuthorName,
                authorEmail = x.AuthorEmail,
                authorType = x.AuthorType,
                body = x.Body,
                createdAtUtc = x.CreatedAtUtc
            })
        };
    }

    private static string? FirstNonEmpty(Dictionary<string, string> values, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
                return value.Trim();
        }
        return null;
    }

    private static string NormalizeServiceKey(string? serviceKey, string serviceName)
    {
        var raw = string.IsNullOrWhiteSpace(serviceKey) ? serviceName : serviceKey!;
        raw = raw.Trim().ToLowerInvariant();
        var chars = raw.Select(ch => char.IsLetterOrDigit(ch) ? ch : '-').ToArray();
        var normalized = new string(chars);
        while (normalized.Contains("--", StringComparison.Ordinal))
            normalized = normalized.Replace("--", "-", StringComparison.Ordinal);
        return normalized.Trim('-');
    }

    public sealed class CreateSupportTicketRequest
    {
        public string ServiceKey { get; set; } = string.Empty;
        public string ServiceName { get; set; } = string.Empty;
        public string Subject { get; set; } = string.Empty;
        public string Priority { get; set; } = "normal";
        public string Body { get; set; } = string.Empty;
    }

    public sealed class ReplySupportTicketRequest
    {
        public string Body { get; set; } = string.Empty;
    }

    public sealed class ReopenSupportTicketRequest
    {
        public string Message { get; set; } = string.Empty;
    }
}
