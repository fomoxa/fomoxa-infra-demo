$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$binary = Join-Path $repoRoot "gateway/target/release/fomoxa-directory.exe"

if (-not (Test-Path $binary)) {
    throw "chua build directory: $binary (chay cargo build --release -p fomoxa-directory trong gateway/)"
}

Push-Location (Join-Path $repoRoot "deploy")
try {
    Write-Host "directory: 0.0.0.0:9500 (admin 9501), config deploy/directory.toml"
    & $binary directory.toml
}
finally {
    Pop-Location
}
