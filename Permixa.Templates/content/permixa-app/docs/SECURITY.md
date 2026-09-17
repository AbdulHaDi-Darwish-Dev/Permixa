# Security

> **Purpose:** Host-app security expectations and secret handling.  
> **Authority:** This application's security posture (Permixa has its own model).  
> **Update when:** Auth/config/threat-model changes.

## Host responsibilities

- Protect connection strings, JWT PEMs, bootstrap passwords, Resend API keys
- Disable bootstrap after first successful Owner setup and remove OwnerPassword
- Production must not auto-migrate by default (Development-only migrate in template)
- Configure forwarded headers / TLS for your deployment

## Defaults in this template

- Bootstrap `Enabled=false` in committed `appsettings.json`
- No PEM files committed
- Login endpoint uses a sample Permixa rate-limit policy

## Reporting

Define your vulnerability reporting process for this application. For Permixa framework issues, follow Permixa's `SECURITY.md` upstream.
