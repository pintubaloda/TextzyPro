namespace Textzy.Api.Services;

public sealed record EmailAttachment(string FileName, string ContentType, byte[] ContentBytes);
