#!/usr/bin/env bash
set -euo pipefail

# Development-only helper. Do NOT use these values in production.
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
API_PROJECT="$ROOT/src/PermixaApp.Api/PermixaApp.Api.csproj"

echo "Initializing development user-secrets for PermixaApp.Api..."

dotnet user-secrets init --project "$API_PROJECT" >/dev/null 2>&1 || true

if ! command -v openssl >/dev/null 2>&1; then
  echo "openssl not found. Install openssl or set Permixa:Jwt:* PEMs manually."
  exit 1
fi

TMPDIR="$(mktemp -d)"
trap 'rm -rf "$TMPDIR"' EXIT
openssl genrsa -out "$TMPDIR/private.pem" 2048 >/dev/null 2>&1
openssl rsa -in "$TMPDIR/private.pem" -pubout -out "$TMPDIR/public.pem" >/dev/null 2>&1
PRIVATE_KEY="$(cat "$TMPDIR/private.pem")"
PUBLIC_KEY="$(cat "$TMPDIR/public.pem")"

dotnet user-secrets set "ConnectionStrings:Default" \
  "Server=localhost,1433;Database=PermixaApp;User Id=sa;Password=Your_strong_DevOnly_Password123;TrustServerCertificate=True;Encrypt=False" \
  --project "$API_PROJECT"

#if (redis)
dotnet user-secrets set "ConnectionStrings:Redis" "localhost:6379" --project "$API_PROJECT"
#endif

dotnet user-secrets set "Permixa:Jwt:Issuer" "https://localhost/permixaapp" --project "$API_PROJECT"
dotnet user-secrets set "Permixa:Jwt:Audience" "permixaapp" --project "$API_PROJECT"
dotnet user-secrets set "Permixa:Jwt:PrivateKeyPem" "$PRIVATE_KEY" --project "$API_PROJECT"
dotnet user-secrets set "Permixa:Jwt:PublicKeyPem" "$PUBLIC_KEY" --project "$API_PROJECT"

dotnet user-secrets set "Permixa:Bootstrap:Enabled" "true" --project "$API_PROJECT"
dotnet user-secrets set "Permixa:Bootstrap:OwnerEmail" "owner@localhost.dev" --project "$API_PROJECT"
dotnet user-secrets set "Permixa:Bootstrap:OwnerUserName" "owner" --project "$API_PROJECT"
dotnet user-secrets set "Permixa:Bootstrap:OwnerPassword" "ChangeMe-Owner-1!" --project "$API_PROJECT"

#if (resend)
echo "Set your Resend API key (never commit it):"
echo "  dotnet user-secrets set \"Permixa:Resend:ApiKey\" \"re_xxx\" --project \"$API_PROJECT\""
#endif

echo "Done. After first successful Owner login, set Permixa:Bootstrap:Enabled=false and remove OwnerPassword."
