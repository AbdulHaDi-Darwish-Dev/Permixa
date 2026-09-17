# Architecture

> **Purpose:** Layering, dependencies, and Permixa boundary.  
> **Authority:** Structural decisions for this application.  
> **Update when:** Project graph, persistence strategy, or host integration shape changes.

## Layers

```text
PermixaApp.Domain
        ↑
PermixaApp.Application
        ↑
PermixaApp.Infrastructure
        ↑
PermixaApp.Api  ── PackageReference → Permixa.AspNetCore (+ optional providers)
```

## Persistence

| Context | Owns | Migrations |
|---------|------|------------|
| Permixa `ApplicationDbContext` | IAM / Identity | Inside Permixa NuGet packages |
| `AppDbContext` | Business data | `Infrastructure/Persistence/Migrations` (`__AppMigrationsHistory`) |

Same SQL Server database and connection string by default.

## Composition root

`PermixaApp.Api/Program.cs` is the composition root: `AddAppInfrastructure` → `AddPermixaHost` → `AddApiServices`, then `InitializeDevelopmentAsync`, middleware, and `MapApiEndpoints`. Host-local helpers live under `Api/DependencyInjection` and `Api/Endpoints` — they compose existing Permixa NuGet APIs; they are not framework APIs.
