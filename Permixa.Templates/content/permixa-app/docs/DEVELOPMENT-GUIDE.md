# Development guide

> **Purpose:** Local workflows for developers.  
> **Authority:** How to run and extend this app day-to-day.  
> **Update when:** Secrets, Compose, EF, or auth flows change.

## First run

1. `cp .env.example .env`
2. `docker compose up -d`
3. `scripts/init-dev-secrets.ps1` or `scripts/init-dev-secrets.sh`
4. `dotnet run --project src/PermixaApp.Api`

Development startup migrates Permixa IAM + `AppDbContext`, bootstraps Owner (when `Permixa:Bootstrap:Enabled`), and seeds SampleNotes permissions onto Owner (when `Permixa:AppSeed:Enabled`).

Committed `appsettings.json` keeps `AppSeed:Enabled=false`. Enable it in `appsettings.Development.json` for the first-run experience (already set). Production never auto-seeds (`IsDevelopment` guard).

`AddPermixaVerification()` is always registered (required by authorization admin use cases). Without `--resend`, a host `UnconfiguredVerificationDispatcher` satisfies DI and fails fast if verification delivery is attempted. Resend email delivery remains optional (`--resend`).

`AppPermissionSeeder` is privileged Development initialization only — not a normal RBAC mutation API and not exposed over HTTP.

## Business migrations

```bash
export APP_CONNECTION_STRING="Server=...;Database=PermixaApp;..."
dotnet ef migrations add <Name> \
  --project src/PermixaApp.Infrastructure \
  --startup-project src/PermixaApp.Api \
  --context AppDbContext \
  --output-dir Persistence/Migrations
```

## Production migrations

Do **not** rely on startup migrate. Apply Permixa package migrations and `AppDbContext` migrations in your release process (order: Permixa first, then App).

## Bootstrap lifecycle

enable → run once → verify Owner → set `Permixa:Bootstrap:Enabled=false` → remove OwnerPassword secret.

## REFERENCE / SAFE TO DELETE

Remove `Reference/SampleNotes` across Domain/Application/Infrastructure/Api/tests when starting real features.
