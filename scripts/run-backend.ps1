$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$secrets = Join-Path $repoRoot "deploy/secrets"

$env:Fomoxa__Directory__Address = "127.0.0.1:9500"
$env:Fomoxa__Directory__Key__File = Join-Path $secrets "directory/backend"
$env:Fomoxa__Pods__Key__File = Join-Path $secrets "backend-pod"

Write-Host "backend: directory 127.0.0.1:9500, key $env:Fomoxa__Directory__Key__File"
dotnet run --project (Join-Path $repoRoot "MyGame.Backend/MyGame.Backend/MyGame.Backend.csproj")
