namespace Permixa.Application.Verification.EmailChange;

public sealed class RequestEmailChangeRequest
{
    public Guid UserId { get; init; }

    public string CurrentPassword { get; init; } = string.Empty;

    public string NewEmail { get; init; } = string.Empty;
}
