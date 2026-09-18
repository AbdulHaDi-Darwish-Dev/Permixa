# Permixa.Templates

**Preview (`0.1.0-preview.2` published)** — optional .NET project templates for applications that consume **Permixa** IAM from NuGet.

| Package | Role |
|---------|------|
| **Permixa** (`Permixa.AspNetCore`, …) | IAM / security **framework** |
| **Permixa.Templates** | Full **ASP.NET Core application starter** built around Permixa |

Templates are optional. You can still `dotnet add package Permixa.AspNetCore --prerelease` into an existing host.

## Install

```bash
dotnet new install Permixa.Templates --version 0.1.0-preview.2
```

## Create an application

```bash
dotnet new permixa-app -n ClinicSystem
dotnet new permixa-app -n ClinicRedis --redis
dotnet new permixa-app -n ClinicEmail --resend
dotnet new permixa-app -n ClinicFull --redis --resend
```

Requires **.NET 8**. Generated apps restore Permixa packages from **nuget.org**.

### Options

| Option | Default | Effect |
|--------|---------|--------|
| `--redis` | false | `Permixa.Caching.Redis` authz cache + Compose Redis |
| `--resend` | false | `Permixa.Email.Resend` + email confirmation endpoints; `RequireConfirmedEmail=true` |

Without `--redis`, authorization caching uses the in-process memory cache. Redis failures fail open to SQL (source of truth).

Without `--resend`, email delivery is not configured; confirmation is off by default.

Hyphenated `-n` values (e.g. `clinic-system`) are supported: the folder may keep hyphens; C# identifiers use `safe_namespace` (`clinic_system`). Prefer PascalCase when folder and project names should match.

## What you get

- Clean Architecture: Domain / Application / Infrastructure / Api
- Unit + infrastructure + integration tests (Testcontainers)
- Application docs under `docs/`
- Docker Compose: SQL Server (always); Redis when `--redis`
- First-run secrets: `scripts/init-dev-secrets.(ps1|sh)`
- OpenAPI / Swagger in Development
- Reference feature: `Reference/SampleNotes` (Create / Get / List) — **safe to delete** when you start real domain work

## First run (generated app)

```bash
cp .env.example .env
docker compose up -d
./scripts/init-dev-secrets.sh   # or scripts/init-dev-secrets.ps1
dotnet run --project src/<Name>.Api
```

Development may auto-migrate and bootstrap when configured. Do **not** rely on Production auto-migration.

## License

Apache-2.0 for this template package. Generated applications choose their own license. Permixa framework packages remain Apache-2.0.
