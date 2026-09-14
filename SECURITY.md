# Security Policy

## Supported versions

| Version | Supported |
|---------|-----------|
| `0.1.0-preview.x` | Yes (best-effort while preview) |
| `< 0.1.0-preview.1` | No public releases |

Permixa is pre-1.0. Security fixes for the current preview line are prioritized; older unpublished trees are not supported.

## Reporting a vulnerability

Please **do not** open a public GitHub issue for security vulnerabilities.

Prefer **GitHub private vulnerability reporting** for this repository:

[https://github.com/AbdulHaDi-Darwish-Dev/Permixa/security/advisories/new](https://github.com/AbdulHaDi-Darwish-Dev/Permixa/security/advisories/new)

If private reporting is unavailable in the UI, open a **private** contact path via the repository owner on GitHub rather than posting exploit details publicly.

### Please include

- Affected package(s) and version(s)
- Description of the issue and impact
- Reproduction steps or proof-of-concept (non-destructive if possible)
- Whether the issue is already being exploited (if known)
- Your preferred contact for follow-up

### Coordinated disclosure

Please allow reasonable time for investigation and remediation before public disclosure. We will acknowledge reports as promptly as practical.

## Security scope

In scope (examples):

- Authentication / session / refresh-token handling
- Authorization / permission bypasses
- MFA / verification / OTP weaknesses in Permixa-owned flows
- Secret leakage via logs, audit metadata, or HTTP problem details
- Package supply-chain issues in published Permixa packages

Out of scope (examples):

- Vulnerabilities only in host application code outside Permixa
- Misconfiguration (e.g. publishing private RSA keys, disabling TLS)
- Denial of service against third-party services (Redis, Resend, SQL)
- Issues in dependencies that are already fixed upstream (report upstream; link here if relevant)

## Hardening expectations

Hosts remain responsible for:

- Protecting connection strings, JWT PEM material, and bootstrap credentials
- TLS termination and forwarded-header trust
- Deploying Rate Limiting policies appropriate to their threat model
- Keeping .NET / SQL Server / Redis / Resend dependencies updated

See [docs/SECURITY-MODEL.md](docs/SECURITY-MODEL.md) for product security invariants.
