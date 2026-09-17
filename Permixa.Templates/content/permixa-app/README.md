# PermixaApp

Professional ASP.NET Core starter consuming **Permixa** IAM from NuGet.

## Quick start

```bash
cp .env.example .env
docker compose up -d
./scripts/init-dev-secrets.sh   # or scripts/init-dev-secrets.ps1 on Windows
dotnet run --project src/PermixaApp.Api
```

Then:

1. `POST /auth/login` with the Owner credentials from user-secrets
2. Call `GET /sample/protected` with `Authorization: Bearer <accessToken>`

## Naming

`-n` should be a usable C# identifier when possible (`ClinicSystem`). Hyphenated names such as `clinic-system` still generate: the directory may keep hyphens while project/namespace identifiers become `clinic_system` (template `safe_namespace` form).

| Flag | Effect |
|------|--------|
| _(none)_ | Memory authz cache; `RequireConfirmedEmail=false` |
| `--redis` | Redis authz cache + Compose Redis |
| `--resend` | Resend email + confirmation endpoints; `RequireConfirmedEmail=true` |

## Auth endpoints

- `POST /auth/register`
- `POST /auth/login`
- `POST /auth/refresh`
- `POST /auth/logout`
- `GET /me`
- `GET /sample/protected`

With `--resend` also:

- `POST /auth/email-confirmation/request`
- `POST /auth/email-confirmation/confirm`

## License

Choose and add a license for **this application**. Permixa packages remain Apache-2.0.

## REFERENCE / SAFE TO DELETE

`Reference/SampleNotes` is a teaching slice. Remove it when you start real domain work.
