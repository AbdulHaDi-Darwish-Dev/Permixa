using Permixa.Application.Common.Results;

namespace Permixa.Application.Authorization.Roles;

/// <summary>
/// Expected administration failure that must abort the current SQL transaction.
/// </summary>
internal sealed class RoleAdministrationFault : Exception
{
    public RoleAdministrationFault(Error error)
        : base(error.Description)
    {
        Error = error;
    }

    public Error Error { get; }
}
