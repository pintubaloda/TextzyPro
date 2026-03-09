using Textzy.Api.Models;

namespace Textzy.Api.Providers;

public interface IMessageProvider
{
    Task<string> SendAsync(ChannelType channel, string recipient, string body, SmsSendContext? context = null, CancellationToken ct = default);
}

public sealed class SmsSendContext
{
    public Guid TenantId { get; init; }
    public string MessageType { get; init; } = string.Empty;
}
