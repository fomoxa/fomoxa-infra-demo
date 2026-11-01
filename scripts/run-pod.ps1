param(
    [string]$Name = "pod-1",
    [int]$Port = 7778,
    [string]$Room = "arena:1",
    [string]$Directory = "127.0.0.1:9500"
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$player = Join-Path $repoRoot "MyGame/Builds/MyGame.exe"

if (-not (Test-Path $player)) {
    throw "chua build player: $player (Unity: Tools/Demo/Build Windows Player)"
}

$logs = Join-Path $repoRoot "logs"
New-Item -ItemType Directory -Force -Path $logs | Out-Null
$logFile = Join-Path $logs "pod-$Name.log"

$env:POD_KEY_FILE = Join-Path $repoRoot "deploy/secrets/directory/pod"

Write-Host "pod ${Name}: udp $Port, room $Room, directory $Directory, log $logFile"
& $player -batchmode -nographics -logFile $logFile -pod "-podname:$Name" "-podport:$Port" "-room:$Room" "-directory:$Directory"
