param(
    [int]$Pods = 1,
    [string]$Room = "arena:1",
    [int]$FirstPodPort = 7778,
    [ValidateSet("console", "unity")]
    [string]$Server = "console"
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$logs = Join-Path $repoRoot "logs"
New-Item -ItemType Directory -Force -Path $logs | Out-Null

$directoryExe = Join-Path $repoRoot "gateway/target/release/fomoxa-directory.exe"
$gatewayExe = Join-Path $repoRoot "gateway/target/release/fomoxa-gateway.exe"
$backendExe = Join-Path $repoRoot "MyGame.Backend/MyGame.Backend/bin/Debug/net8.0/MyGame.Backend.exe"
$playerExe = Join-Path $repoRoot "MyGame/Builds/MyGame.exe"
$serverExe = Join-Path $repoRoot "MyGame/Server~/bin/Debug/net8.0/MyGame.Server.exe"

foreach ($exe in @($directoryExe, $gatewayExe)) {
    if (-not (Test-Path $exe)) {
        throw "missing $exe"
    }
}

if ($Server -eq "unity" -and -not (Test-Path $playerExe)) {
    throw "missing $playerExe"
}

if ($Server -eq "console" -and -not (Test-Path $serverExe)) {
    Write-Host "console server not built, building..."
    & dotnet build (Join-Path $repoRoot "MyGame/Server~/MyGame.Server.csproj") | Select-Object -Last 3
    if (-not (Test-Path $serverExe)) {
        throw "build console server failed: $serverExe"
    }
}

if (-not (Test-Path $backendExe)) {
    Write-Host "backend not built, building..."
    & dotnet build (Join-Path $repoRoot "MyGame.Backend/MyGame.Backend.sln") | Select-Object -Last 3
    if (-not (Test-Path $backendExe)) {
        throw "build backend failed: $backendExe"
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

# Nakama là tuỳ chọn: có thì backend bắt buộc ghép trận trước khi cấp ticket.
$nakama = "http://127.0.0.1:7350"
try {
    $null = Invoke-WebRequest "$nakama/healthcheck" -UseBasicParsing -TimeoutSec 2
    $env:Fomoxa__Nakama__Url = $nakama
    Write-Host "    nakama: $nakama"
}
catch {
    Write-Warning "Nakama is not running, backend will skip matchmaking (enable: docker compose -f deploy/compose.yaml up -d)"
}
Start-Process -FilePath $backendExe `
    -WorkingDirectory (Split-Path -Parent $backendExe) `
    -RedirectStandardOutput (Join-Path $logs "backend.log") `
    -RedirectStandardError (Join-Path $logs "backend.err.log") `
    -WindowStyle Hidden

Start-Sleep -Milliseconds 1500

$env:POD_KEY_FILE = Join-Path $repoRoot "deploy/secrets/directory/pod"

$roomPrefix = if ($Room.Contains(":")) { $Room.Split(":")[0] } else { $Room }

for ($i = 0; $i -lt $Pods; $i++) {
    $name = "pod-$($i + 1)"
    $port = $FirstPodPort + $i
    $podRoom = if ($Pods -eq 1) { $Room } else { "${roomPrefix}:$($i + 1)" }
    Write-Host "4/4 $name udp $port (room $podRoom, $Server)"

    if ($Server -eq "unity") {
        Start-Process -FilePath $playerExe `
            -ArgumentList "-batchmode", "-nographics", "-logFile", (Join-Path $logs "$name.log"), "-pod", "-podname:$name", "-podport:$port", "-room:$podRoom", "-directory:127.0.0.1:9500" `
            -WorkingDirectory (Split-Path -Parent $playerExe) `
            -WindowStyle Hidden
    }
    else {
        Start-Process -FilePath $serverExe `
            -ArgumentList "--name=$name", "--port=$port", "--room=$podRoom", "--directory=127.0.0.1:9500", "--trace" `
            -WorkingDirectory (Split-Path -Parent $serverExe) `
            -RedirectStandardOutput (Join-Path $logs "$name.log") `
            -RedirectStandardError (Join-Path $logs "$name.err.log") `
            -WindowStyle Hidden
    }

    Start-Sleep -Milliseconds 1500
}

Write-Host ""
Write-Host "logs at $logs"
Get-Content (Join-Path $logs "backend.log") -ErrorAction SilentlyContinue | Select-Object -Last 3
Get-Content (Join-Path $logs "gateway.log") -ErrorAction SilentlyContinue | Select-Object -Last 3
Get-Content (Join-Path $logs "pod-1.log") -ErrorAction SilentlyContinue | Select-String -Pattern "\[Pod\]" | Select-Object -Last 5 | ForEach-Object { $_.Line.Trim() }
