namespace Permixa.AspNetCore.Security;

/// <summary>
/// HTTP-authenticated Permixa user. Does not expose HttpContext.
/// </summary>
public interface ICurrentUser
{
    bool IsAuthenticated { get; }

    Guid? UserId { get; }
}
