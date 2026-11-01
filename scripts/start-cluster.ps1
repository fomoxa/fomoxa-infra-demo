param(
    [int]$Pods = 1,
    [string]$Room = "arena:1",
    [int]$FirstPodPort = 7778
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$logs = Join-Path $repoRoot "logs"
New-Item -ItemType Directory -Force -Path $logs | Out-Null

$directoryExe = Join-Path $repoRoot "gateway/target/release/fomoxa-directory.exe"
$gatewayExe = Join-Path $repoRoot "gateway/target/release/fomoxa-gateway.exe"
$backendExe = Join-Path $repoRoot "MyGame.Backend/MyGame.Backend/bin/Debug/net8.0/MyGame.Backend.exe"
$playerExe = Join-Path $repoRoot "MyGame/Builds/MyGame.exe"

foreach ($exe in @($directoryExe, $gatewayExe, $playerExe)) {
    if (-not (Test-Path $exe)) {
        throw "thieu $exe"
    }
}

if (-not (Test-Path $backendExe)) {
    Write-Host "chua build backend, dang build..."
    & dotnet build (Join-Path $repoRoot "MyGame.Backend/MyGame.Backend.sln") | Select-Object -Last 3
    if (-not (Test-Path $backendExe)) {
        throw "build backend that bai: $backendExe"
    }
}

Write-Host "1/4 directory 9500 (admin 9501)"
Start-Process -FilePath $directoryExe `
    -ArgumentList "directory.toml" `
    -WorkingDirectory (Join-Path $repoRoot "deploy") `
    -RedirectStandardOutput (Join-Path $logs "directory.log") `
    -RedirectStandardError (Join-Path $logs "directory.err.log") `
    -WindowStyle Hidden

Start-Sleep -Milliseconds 1200

Write-Host "2/4 gateway udp 7777"
Start-Process -FilePath $gatewayExe `
    -ArgumentList "gateway.toml" `
    -WorkingDirectory (Join-Path $repoRoot "deploy") `
    -RedirectStandardOutput (Join-Path $logs "gateway.log") `
    -RedirectStandardError (Join-Path $logs "gateway.err.log") `
    -WindowStyle Hidden

Start-Sleep -Milliseconds 1200

Write-Host "3/4 backend 9600/9601/9602"
$env:DOTNET_ROLL_FORWARD = "Major"
$env:Fomoxa__Directory__Address = "127.0.0.1:9500"
$env:Fomoxa__Directory__Key__File = Join-Path $repoRoot "deploy/secrets/directory/backend"
$env:Fomoxa__Pods__Key__File = Join-Path $repoRoot "deploy/secrets/backend-pod"
Start-Process -FilePath $backendExe `
    -WorkingDirectory (Split-Path -Parent $backendExe) `
    -RedirectStandardOutput (Join-Path $logs "backend.log") `
    -RedirectStandardError (Join-Path $logs "backend.err.log") `
    -WindowStyle Hidden

Start-Sleep -Milliseconds 1500

$env:POD_KEY_FILE = Join-Path $repoRoot "deploy/secrets/directory/pod"

for ($i = 0; $i -lt $Pods; $i++) {
    $name = "pod-$($i + 1)"
    $port = $FirstPodPort + $i
    Write-Host "4/4 $name udp $port (room $Room)"
    Start-Process -FilePath $playerExe `
        -ArgumentList "-batchmode", "-nographics", "-logFile", (Join-Path $logs "$name.log"), "-pod", "-podname:$name", "-podport:$port", "-room:$Room", "-directory:127.0.0.1:9500" `
        -WorkingDirectory (Split-Path -Parent $playerExe) `
        -WindowStyle Hidden
    Start-Sleep -Milliseconds 1500
}

Write-Host ""
Write-Host "log tai $logs"
Get-Content (Join-Path $logs "backend.log") -ErrorAction SilentlyContinue | Select-Object -Last 3
Get-Content (Join-Path $logs "gateway.log") -ErrorAction SilentlyContinue | Select-Object -Last 3
Get-Content (Join-Path $logs "pod-1.log") -ErrorAction SilentlyContinue | Select-String -Pattern "\[Pod\]" | Select-Object -Last 5 | ForEach-Object { $_.Line.Trim() }
