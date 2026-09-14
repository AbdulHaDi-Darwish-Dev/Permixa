namespace Permixa.Infrastructure.Bootstrap;

/// <summary>
/// Explicit first-time IAM initialization. Invoked by the consuming host; not automatic.
/// </summary>
public interface IPermixaBootstrapper
{
    Task BootstrapAsync(CancellationToken cancellationToken = default);
}
