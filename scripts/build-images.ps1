param(
    [string]$Tag = "latest",
    [switch]$NoCache
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $repoRoot

$cacheFlag = if ($NoCache) { @("--no-cache") } else { @() }

Write-Host "1/4 building fomoxa-server:$Tag"
docker build -f deploy/Dockerfile.server -t "fomoxa-server:$Tag" @cacheFlag .
if ($LASTEXITCODE -ne 0) { throw "build fomoxa-server failed" }

Write-Host "2/4 building fomoxa-directory:$Tag"
docker build -f deploy/Dockerfile.directory -t "fomoxa-directory:$Tag" @cacheFlag .
if ($LASTEXITCODE -ne 0) { throw "build fomoxa-directory failed" }

Write-Host "3/4 building fomoxa-gateway:$Tag"
docker build -f deploy/Dockerfile.gateway -t "fomoxa-gateway:$Tag" @cacheFlag .
if ($LASTEXITCODE -ne 0) { throw "build fomoxa-gateway failed" }

Write-Host "4/4 building fomoxa-backend:$Tag"
docker build -f MyGame.Backend/Dockerfile -t "fomoxa-backend:$Tag" @cacheFlag MyGame.Backend
if ($LASTEXITCODE -ne 0) { throw "build fomoxa-backend failed" }

Write-Host "OK: ca 4 images da duoc build thanh cong voi tag '$Tag'"

