param(
    [string]$Name = "pod-1",
    [int]$Port = 7778,
    [string]$Room = "arena:1",
    [string]$Directory = "127.0.0.1:9500",
    [ValidateSet("console", "unity")]
    [string]$Server = "console"
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$player = Join-Path $repoRoot "MyGame/Builds/MyGame.exe"
$console = Join-Path $repoRoot "MyGame/Server~/bin/Debug/net8.0/MyGame.Server.exe"
$logs = Join-Path $repoRoot "logs"

New-Item -ItemType Directory -Force -Path $logs | Out-Null
$logFile = Join-Path $logs "$Name.log"
$keyFile = Join-Path $repoRoot "deploy/secrets/directory/pod"

if ($Server -eq "unity") {
    if (-not (Test-Path $player)) {
        throw "chua build player: $player (Unity: Tools/Demo/Build Windows Player)"
    }

    $env:POD_KEY_FILE = $keyFile
    Write-Host "pod ${Name} (unity): udp $Port, room $Room, directory $Directory, log $logFile"
    & $player -batchmode -nographics -logFile $logFile -pod "-podname:$Name" "-podport:$Port" "-room:$Room" "-directory:$Directory"
    return
}

if (-not (Test-Path $console)) {
    Write-Host "chua build console server, dang build..."
    & dotnet build (Join-Path $repoRoot "MyGame/Server~/MyGame.Server.csproj") | Select-Object -Last 3
    if (-not (Test-Path $console)) {
        throw "build console server that bai: $console"
    }
}

Write-Host "pod ${Name} (console): udp $Port, room $Room, directory $Directory, log $logFile"
& $console "--name=$Name" "--port=$Port" "--room=$Room" "--directory=$Directory" "--key=$keyFile" "--trace"
