# Chay "cum gia" cho client Unity: directory gia (:9500) + backend gia (:9600) + cac pod that.
# Khong can Docker, K8s, Nakama, Agones hay gateway. Chi can .NET 8 SDK.
# Tham so them se truyen cho MockCluster, vi du: -ExtraArgs "--pods=2","--match-delay=3"
param(
    [string[]]$ExtraArgs = @()
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $repoRoot

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw ".NET 8 SDK is required (https://dotnet.microsoft.com/download)."
}

Write-Host "=== Building server pod and MockCluster ===" -ForegroundColor Cyan
dotnet publish "MyGame/Server~/MyGame.Server.csproj" -c Release -o .mock/server -v q -nologo
if ($LASTEXITCODE -ne 0) { throw "build MyGame.Server failed" }
dotnet publish "tools/MockCluster/MockCluster.csproj" -c Release -o .mock/cluster -v q -nologo
if ($LASTEXITCODE -ne 0) { throw "build MockCluster failed" }

Write-Host "=== Running mock cluster (Ctrl+C to stop) ===" -ForegroundColor Green
dotnet .mock/cluster/MockCluster.dll "--server=.mock/server/MyGame.Server.dll" @ExtraArgs
