namespace Textzy.Api.DTOs;

public class AssignConversationRequest
{
    public string UserId { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
}

public class AddConversationNoteRequest
{
    public string Body { get; set; } = string.Empty;
}

public class TypingEventRequest
{
    public Guid ConversationId { get; set; }
    public bool IsTyping { get; set; }
}

public class UpdateConversationLabelsRequest
{
    public string[] Labels { get; set; } = [];
}

public class TransferConversationRequest
{
    public string UserId { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
}
