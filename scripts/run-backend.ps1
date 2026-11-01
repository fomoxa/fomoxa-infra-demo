$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$secrets = Join-Path $repoRoot "deploy/secrets"

$env:Fomoxa__Directory__Address = "127.0.0.1:9500"
$env:Fomoxa__Directory__Key__File = Join-Path $secrets "directory/backend"
$env:Fomoxa__Pods__Key__File = Join-Path $secrets "backend-pod"

$nakama = "http://127.0.0.1:7350"
try {
    $null = Invoke-WebRequest "$nakama/healthcheck" -UseBasicParsing -TimeoutSec 2
    $env:Fomoxa__Nakama__Url = $nakama
}
catch {
    Write-Warning "Nakama chưa chạy, backend bỏ qua matchmaking (bật: docker compose -f deploy/compose.yaml up -d)"
}

Write-Host "backend: directory 127.0.0.1:9500, key $env:Fomoxa__Directory__Key__File"
dotnet run --project (Join-Path $repoRoot "MyGame.Backend/MyGame.Backend/MyGame.Backend.csproj")
