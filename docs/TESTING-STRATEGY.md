# Testing strategy

## Test projects

| Project | Role |
|---------|------|
| `Permixa.Domain.Tests` | Pure domain rules |
| `Permixa.Application.Tests` | Use cases with mocks (Moq) |
| `Permixa.Infrastructure.Tests` | SQL Server Testcontainers, Redis where needed, migrations, concurrency, security regressions |
| `Permixa.AspNetCore.Tests` | JWT/authz HTTP mapping, Rate Limiting config, minimal test host endpoints |
| `Permixa.IntegrationTests` | Composed host + SQL + Redis containers; end-to-end IAM scenarios |

## Containers

Infrastructure and Integration tests use **Testcontainers** for SQL Server and Redis where applicable. Project-by-project execution is acceptable when Docker contention occurs (verified operational practice during later phases).

## Categories commonly covered

- Unit / use-case success and failure paths
- Hierarchy and permission security regressions
- Migration/schema assertions
- Concurrency (authorization version, hierarchy lock, MFA consume)
- Secret exclusion in audit metadata
- Custom DI override for audit sink
- Sensitive logging static assertions on known log sites

## Baseline reporting

Only publish pass counts after an actual test run. Do not invent coverage percentages.

Latest verified totals: see [CURRENT-STATE.md](CURRENT-STATE.md).
