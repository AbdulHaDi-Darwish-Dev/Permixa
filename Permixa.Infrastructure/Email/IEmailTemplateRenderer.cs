using Permixa.Infrastructure.Email.Templates;

namespace Permixa.Infrastructure.Email;

/// <summary>
/// Renders verification emails. Hosts may replace the entire implementation via DI.
/// </summary>
public interface IEmailTemplateRenderer
{
    RenderedEmail RenderEmailConfirmationOtp(EmailConfirmationOtpTemplateModel model);

    RenderedEmail RenderEmailConfirmationLink(EmailConfirmationLinkTemplateModel model);

    RenderedEmail RenderPasswordReset(PasswordResetTemplateModel model);
}
