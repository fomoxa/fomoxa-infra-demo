# Sinh chứng chỉ TLS tự ký cho proxy TLS trước backend (cổng 9643) và in vân tay SHA-256 để client "pin".
# Cần openssl (có sẵn trong Git for Windows hoặc WSL). Production nên dùng chứng chỉ do CA công cộng cấp.
param(
    [string]$HostName = "fomoxa.local",
    [switch]$Force
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$dir = Join-Path $repoRoot "deploy/secrets/tls"
New-Item -ItemType Directory -Force -Path $dir | Out-Null

if (-not (Get-Command openssl -ErrorAction SilentlyContinue)) {
    throw "openssl is required in PATH (bundled with Git for Windows: C:\Program Files\Git\usr\bin\openssl.exe), or run scripts/gen-tls-cert.sh in WSL."
}

$crt = Join-Path $dir "tls.crt"
$key = Join-Path $dir "tls.key"
if ((Test-Path $crt) -and -not $Force) {
    Write-Host "Certificate already exists at $dir (add -Force to regenerate)."
}
else {
    openssl req -x509 -newkey rsa:2048 -nodes -days 730 -keyout $key -out $crt `
        -subj "/CN=$HostName" `
        -addext "subjectAltName=DNS:$HostName,DNS:localhost,IP:127.0.0.1" `
        -addext "extendedKeyUsage=serverAuth" 2>$null
    if ($LASTEXITCODE -ne 0) { throw "openssl failed" }
    Write-Host "Created $crt and $key (CN=$HostName)"
}

$bytes = [Convert]::FromBase64String(((Get-Content $crt -Raw) -replace "-----[A-Z ]+-----", "" -replace "\s", ""))
$pin = ([BitConverter]::ToString([Security.Cryptography.SHA256]::HashData($bytes))) -replace "-", ""
Write-Host "SHA-256 fingerprint (pin): $pin"
Write-Host "Unity client: -backend:127.0.0.1:9643 -tlspin:$pin"
