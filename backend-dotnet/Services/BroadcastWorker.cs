using Microsoft.EntityFrameworkCore;
using Textzy.Api.Data;
using Textzy.Api.Models;
using Textzy.Api.Providers;

namespace Textzy.Api.Services;

public class BroadcastWorker(
    BroadcastQueueService queue,
    IServiceScopeFactory scopeFactory,
    SensitiveDataRedactor redactor,
    ILogger<BroadcastWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var job in queue.ReadAllAsync(stoppingToken))
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var controlDb = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
                var tenant = await controlDb.Tenants.FirstOrDefaultAsync(t => t.Id == job.TenantId, stoppingToken);
                if (tenant is null) continue;

                using var tenantDb = SeedData.CreateTenantDbContext(tenant.DataConnectionString);
                var provider = scope.ServiceProvider.GetRequiredService<IMessageProvider>();
                var contactPii = scope.ServiceProvider.GetRequiredService<ContactPiiService>();

                var recipients = (job.RecipientCsv ?? string.Empty)
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                job.Status = "Running";
                job.StartedAtUtc = DateTime.UtcNow;

                foreach (var recipient in recipients)
                {
                    // Compliance safeguard: skip obvious invalid numbers
                    if (recipient.Length < 8) { job.FailedCount++; continue; }

                    // Compliance safeguard: enforce opt-in for WhatsApp broadcasts
                    if (job.Channel == ChannelType.WhatsApp)
                    {
                        var recipientHash = contactPii.IsEnabled ? contactPii.ComputePhoneHash(recipient) : string.Empty;
                        var contact = contactPii.IsEnabled
                            ? await tenantDb.Contacts.FirstOrDefaultAsync(x => x.TenantId == job.TenantId && x.PhoneHash == recipientHash, stoppingToken)
                            : await tenantDb.Contacts.FirstOrDefaultAsync(x => x.TenantId == job.TenantId && x.Phone == recipient, stoppingToken);
                        if (contact is not null && !contact.OptInStatus.Equals("opted_in", StringComparison.OrdinalIgnoreCase))
                        {
                            job.FailedCount++;
                            continue;
                        }
                    }

                    // Lightweight throttling to avoid burst spikes
                    await Task.Delay(60, stoppingToken);

                    var delivered = false;
                    for (var attempt = 1; attempt <= Math.Max(job.MaxRetries, 1); attempt++)
                    {
                        try
                        {
                            var providerId = await provider.SendAsync(
                                job.Channel,
                                recipient,
                                job.MessageBody,
                                new SmsSendContext
                                {
                                    TenantId = job.TenantId,
                                    MessageType = "broadcast"
                                },
                                stoppingToken);
                            tenantDb.Messages.Add(new Message
                            {
                                Id = Guid.NewGuid(),
                                TenantId = job.TenantId,
                                Channel = job.Channel,
                                Recipient = recipient,
                                Body = job.MessageBody,
                                ProviderMessageId = providerId,
                                Status = "Accepted",
                                MessageType = "broadcast"
                            });
                            job.SentCount++;
                            delivered = true;
                            break;
                        }
                        catch when (attempt < Math.Max(job.MaxRetries, 1))
                        {
                            job.RetryCount++;
                            await Task.Delay(150 * attempt, stoppingToken);
                        }
                        catch
                        {
                            job.RetryCount++;
                        }
                    }

                    if (!delivered) job.FailedCount++;
                }

                job.Status = "Completed";
                job.CompletedAtUtc = DateTime.UtcNow;

                var persistedJob = await tenantDb.BroadcastJobs.FirstOrDefaultAsync(x => x.Id == job.Id, stoppingToken);
                if (persistedJob is not null)
                {
                    persistedJob.Status = job.Status;
                    persistedJob.StartedAtUtc = job.StartedAtUtc;
                    persistedJob.CompletedAtUtc = job.CompletedAtUtc;
                    persistedJob.SentCount = job.SentCount;
                    persistedJob.FailedCount = job.FailedCount;
                }

                await tenantDb.SaveChangesAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError("Broadcast worker failed for job {JobId}: {Error}", job.Id, redactor.RedactText(ex.Message));
            }
        }
    }
}
