using Textzy.Api.Models;

namespace Textzy.Api.Services;

public enum SessionValidationFailure
{
    None = 0,
    InvalidOrExpired = 1,
    IdleTimeout = 2,
    IpRejected = 3,
    IpChanged = 4
}

public sealed record SessionValidationResult(SessionToken? Session, SessionValidationFailure Failure, string Message)
{
    public bool IsValid => Session is not null && Failure == SessionValidationFailure.None;
}

public sealed record SessionRotationResult(string? Token, SessionValidationFailure Failure, string Message)
{
    public bool Succeeded => !string.IsNullOrWhiteSpace(Token) && Failure == SessionValidationFailure.None;
}
