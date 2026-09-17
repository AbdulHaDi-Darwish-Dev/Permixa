# Development-only helper. Do NOT use these values in production.
$ErrorActionPreference = "Stop"

$Root = Split-Path -Parent $PSScriptRoot
$ApiProject = Join-Path $Root "src/PermixaApp.Api/PermixaApp.Api.csproj"

Write-Host "Initializing development user-secrets for PermixaApp.Api..."

dotnet user-secrets init --project $ApiProject 2>$null | Out-Null

$rsa = [System.Security.Cryptography.RSA]::Create(2048)
try {
  $privateKey = $rsa.ExportPkcs8PrivateKeyPem()
  $publicKey = $rsa.ExportSubjectPublicKeyInfoPem()
}
finally {
  $rsa.Dispose()
}

dotnet user-secrets set "ConnectionStrings:Default" `
  "Server=localhost,1433;Database=PermixaApp;User Id=sa;Password=Your_strong_DevOnly_Password123;TrustServerCertificate=True;Encrypt=False" `
  --project $ApiProject | Out-Null

#if (redis)
dotnet user-secrets set "ConnectionStrings:Redis" "localhost:6379" --project $ApiProject | Out-Null
#endif

dotnet user-secrets set "Permixa:Jwt:Issuer" "https://localhost/permixaapp" --project $ApiProject | Out-Null
dotnet user-secrets set "Permixa:Jwt:Audience" "permixaapp" --project $ApiProject | Out-Null
dotnet user-secrets set "Permixa:Jwt:PrivateKeyPem" $privateKey --project $ApiProject | Out-Null
dotnet user-secrets set "Permixa:Jwt:PublicKeyPem" $publicKey --project $ApiProject | Out-Null

dotnet user-secrets set "Permixa:Bootstrap:Enabled" "true" --project $ApiProject | Out-Null
dotnet user-secrets set "Permixa:Bootstrap:OwnerEmail" "owner@localhost.dev" --project $ApiProject | Out-Null
dotnet user-secrets set "Permixa:Bootstrap:OwnerUserName" "owner" --project $ApiProject | Out-Null
dotnet user-secrets set "Permixa:Bootstrap:OwnerPassword" "ChangeMe-Owner-1!" --project $ApiProject | Out-Null

#if (resend)
Write-Host "Set your Resend API key (never commit it):"
Write-Host "  dotnet user-secrets set `"Permixa:Resend:ApiKey`" `"re_xxx`" --project `"$ApiProject`""
#endif

Write-Host "Done. After first successful Owner login, set Permixa:Bootstrap:Enabled=false and remove OwnerPassword."
