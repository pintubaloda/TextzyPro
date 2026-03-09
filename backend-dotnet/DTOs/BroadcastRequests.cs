using Textzy.Api.Models;

namespace Textzy.Api.DTOs;

public class CreateBroadcastJobRequest
{
    public string Name { get; set; } = string.Empty;
    public ChannelType Channel { get; set; }
    public string MessageBody { get; set; } = string.Empty;
    public List<string> Recipients { get; set; } = [];
}
