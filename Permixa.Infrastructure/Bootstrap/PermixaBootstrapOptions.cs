namespace Permixa.Infrastructure.Bootstrap;

/// <summary>
/// Configuration for one-time secure IAM bootstrap.
/// Credentials must be supplied by the host (environment, user secrets, secret managers).
/// After successful bootstrap, disable bootstrap and remove secrets from configuration.
/// </summary>
public sealed class PermixaBootstrapOptions
{
    /// <summary>
    /// When false (default), <see cref="IPermixaBootstrapper.BootstrapAsync"/> is a no-op.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Owner account email. Bootstrap identity key (requires unique emails in Identity).
    /// </summary>
    public string? OwnerEmail { get; set; }

    /// <summary>
    /// Owner user name used only when creating a new Owner account.
    /// </summary>
    public string? OwnerUserName { get; set; }

    /// <summary>
    /// Owner password passed to Identity on create. Never logged or returned.
    /// </summary>
    public string? OwnerPassword { get; set; }
}
