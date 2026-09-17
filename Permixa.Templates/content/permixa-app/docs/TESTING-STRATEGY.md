# Testing strategy

> **Purpose:** What each test project covers.  
> **Authority:** Test topology for this solution.  
> **Update when:** Test projects or container strategy change.

| Project | Role |
|---------|------|
| Domain.Tests | Pure domain rules |
| Application.Tests | Use cases with mocks |
| Infrastructure.Tests | EF / repositories via Testcontainers SQL |
| IntegrationTests | WebApplicationFactory + Testcontainers; authz happy path |

Integration tests do **not** depend on `docker compose` services.
Resend variants replace `IEmailSender` with a capturing fake — no real emails in CI.
