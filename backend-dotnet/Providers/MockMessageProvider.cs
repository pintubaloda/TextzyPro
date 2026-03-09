using Textzy.Api.Models;

namespace Textzy.Api.Providers;

public class MockMessageProvider : IMessageProvider
{
    public Task<string> SendAsync(ChannelType channel, string recipient, string body, SmsSendContext? context = null, CancellationToken ct = default)
    {
        var id = $"{channel.ToString().ToLowerInvariant()}_{Guid.NewGuid():N}";
        return Task.FromResult(id);
    }
}
