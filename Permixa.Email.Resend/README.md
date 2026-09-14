# Permixa.Email.Resend

Optional **Resend email transport** for [Permixa](https://github.com/AbdulHaDi-Darwish-Dev/Permixa).

## Purpose

Implements `IEmailSender` using the Resend SDK. Verification templates, branding, URL templates, and the email dispatcher remain in core Infrastructure.

Resend is **not required** — hosts may supply any `IEmailSender`.

## Install

```bash
dotnet add package Permixa.AspNetCore --prerelease
dotnet add package Permixa.Email.Resend --prerelease
```

## Setup

```csharp
using Permixa.Infrastructure;
using Permixa.Email.Resend;

services.AddPermixaVerification();

services.AddPermixaEmailDelivery(o =>
{
    o.FromEmail = "noreply@example.com";
    o.FromName = "My App";
    o.Branding.ApplicationName = "My App";
    o.EmailConfirmationUrlTemplate =
        "https://app.example.com/verify?challengeId={challengeId}&token={token}";
    o.PasswordResetUrlTemplate =
        "https://app.example.com/reset?challengeId={challengeId}&token={token}";
});

services.AddPermixaResendEmail(o =>
{
    o.ApiKey = configuration["Permixa:Resend:ApiKey"]!;
});
```

### Custom transport

```csharp
services.AddPermixaEmailDelivery(...);
services.AddScoped<IEmailSender, MySmtpEmailSender>();
```

## License

Apache-2.0 — preview package (`0.1.0-preview.1`).
