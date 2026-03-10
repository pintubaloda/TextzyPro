using Microsoft.AspNetCore.Mvc;
using Textzy.Api.Data;
using Textzy.Api.Models;
using Textzy.Api.Services;

namespace Textzy.Api.Controllers;

[ApiController]
[Route("api/public")]
public class PublicSupportController(
    ControlDbContext db,
    AuditLogService audit) : ControllerBase
{
    [HttpPost("contact")]
    public async Task<IActionResult> Create([FromBody] PublicContactRequest request, CancellationToken ct)
    {
        string name;
        string email;
        string phone;
        string message;
        try
        {
            name = InputGuardService.RequireTrimmed(request.Name, "Name", 120);
            email = InputGuardService.RequireTrimmed(request.Email, "Email", 180);
            phone = (request.Phone ?? string.Empty).Trim();
            message = InputGuardService.RequireTrimmed(request.Message, "Message", 4000);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }

        var now = DateTime.UtcNow;
        var subject = string.IsNullOrWhiteSpace(request.Subject)
            ? "Contact request"
            : request.Subject.Trim();
        if (subject.Length > 180) subject = subject[..180];

        var ticket = new SupportTicket
        {
            Id = Guid.NewGuid(),
            TicketNo = SupportTicketCatalog.FormatTicketNo(),
            TenantId = Guid.Empty,
            OwnerGroupId = null,
            CreatedByUserId = Guid.Empty,
            TenantName = "Public",
            TenantSlug = "public",
            CompanyName = string.IsNullOrWhiteSpace(request.Company) ? "Public" : request.Company.Trim(),
            CreatedByName = name,
            CreatedByEmail = email,
            ServiceKey = "public_contact",
            ServiceName = "Public Contact",
            Subject = subject,
            Status = "open",
            Priority = "normal",
            LastMessagePreview = SupportTicketCatalog.BuildPreview(message),
            LastActorType = "customer",
            LastMessageAtUtc = now,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        var messageRow = new SupportTicketMessage
        {
            Id = Guid.NewGuid(),
            TicketId = ticket.Id,
            TenantId = Guid.Empty,
            AuthorUserId = Guid.Empty,
            AuthorName = name,
            AuthorEmail = email,
            AuthorType = "customer",
            Body = $"{message}\n\nContact phone/WhatsApp: {phone}".Trim(),
            CreatedAtUtc = now
        };

        db.SupportTickets.Add(ticket);
        db.SupportTicketMessages.Add(messageRow);
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync("support.ticket.public_created", $"ticket={ticket.TicketNo}; email={email}", ct);

        return Ok(new { ticketId = ticket.Id, ticketNo = ticket.TicketNo });
    }

    public sealed class PublicContactRequest
    {
        public string Name { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public string Subject { get; set; } = string.Empty;
        public string Company { get; set; } = string.Empty;
    }
}
