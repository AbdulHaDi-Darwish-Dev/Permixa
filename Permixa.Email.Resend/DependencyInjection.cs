using Permixa.Infrastructure.Email;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Resend;

namespace Permixa.Email.Resend;

public static class DependencyInjection
{
    /// <summary>
    /// Registers the Resend email transport (<see cref="IEmailSender"/>).
    /// Does not configure the provider-neutral delivery pipeline — call
    /// <c>AddPermixaEmailDelivery</c> separately for templates, links, and dispatcher.
    /// </summary>
    public static IServiceCollection AddPermixaResendEmail(
        this IServiceCollection services,
        Action<PermixaResendEmailOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new PermixaResendEmailOptions();
        configure(options);
        ValidateResendOptions(options);

        services.AddSingleton(Options.Create(CloneResendOptions(options)));

        services.AddResend(o =>
        {
            o.ApiToken = options.ApiKey;
            o.ThrowExceptions = true;
        });

        services.AddScoped<IEmailSender, ResendEmailSender>();

        return services;
    }

    private static void ValidateResendOptions(PermixaResendEmailOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            throw new InvalidOperationException(
                "Permixa Resend email requires ApiKey. Supply it from configuration/secrets.");
        }
    }

    private static PermixaResendEmailOptions CloneResendOptions(PermixaResendEmailOptions source) =>
        new()
        {
            ApiKey = source.ApiKey
        };
}
