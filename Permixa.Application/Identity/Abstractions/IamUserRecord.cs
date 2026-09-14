namespace Permixa.Application.Identity.Abstractions;

public sealed record IamUserRecord(Guid UserId, string? UserName, string? Email);
