param(
    [string]$Name = "ana",
    [string]$Room = "arena:1",
    [switch]$Windowed
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$player = Join-Path $repoRoot "MyGame/Builds/MyGame.exe"

if (-not (Test-Path $player)) {
    throw "chua build player: $player (Unity: Tools/Demo/Build Windows Player)"
}

$logs = Join-Path $repoRoot "logs"
New-Item -ItemType Directory -Force -Path $logs | Out-Null
$logFile = Join-Path $logs "client-$Name.log"

$arguments = @("-autojoin", "-name:$Name", "-room:$Room")
if (-not $Windowed) {
    $arguments = @("-batchmode", "-nographics") + $arguments
}

Write-Host "client ${Name}: room $Room, log $logFile"
& $player @arguments -logFile $logFile
