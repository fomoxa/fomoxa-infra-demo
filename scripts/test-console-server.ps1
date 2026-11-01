param(
    [string]$Room = "arena:1",
    [int]$TimeoutSeconds = 40
)

# Kiem chung buoc 1 cua P5: game server la MyGame.Server (console), khong phai
# MyGame.exe -pod. Client Unity van join qua backend + gateway nhu P4.
#
#   1. pod console da register va giu room
#   2. 2 client vao cung room, thay nhau
#   3. client bot di chuyen -> client kia thay vi tri doi (input -> server -> client)
#
# Yeu cau: scripts\start-cluster.ps1 dang chay voi -Server console.

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$player = Join-Path $repoRoot "MyGame/Builds/MyGame.exe"
$logs = Join-Path $repoRoot "logs"
$stamp = Get-Date -Format "HHmmss"

if (-not (Test-Path $player)) {
    throw "chua build player: $player"
}

New-Item -ItemType Directory -Force -Path $logs | Out-Null

function Wait-For {
    param([string]$Log, [string]$Pattern, [int]$Seconds)

    $deadline = (Get-Date).AddSeconds($Seconds)
    while ((Get-Date) -lt $deadline) {
        if ((Test-Path $Log) -and (Select-String -Path $Log -Pattern $Pattern -Quiet)) {
            return
        }

        Start-Sleep -Milliseconds 300
    }

    throw "khong thay '$Pattern' trong $Log sau $Seconds giay"
}

$server = Get-Process -Name "MyGame.Server" -ErrorAction SilentlyContinue
if (-not $server) {
    throw "MyGame.Server khong chay: bat cum bang scripts\start-cluster.ps1 -Server console"
}

Write-Host "1/3 pod console dang giu $Room"
$podLog = Join-Path $logs "pod-1.log"
Wait-For $podLog "holds $Room" $TimeoutSeconds
"   $((Select-String -Path $podLog -Pattern "registered id=" | Select-Object -Last 1).Line.Trim())"

Write-Host "2/3 hai client cung room $Room"
$anaLog = Join-Path $logs "p5-console-ana.log"
$boLog = Join-Path $logs "p5-console-bo.log"
Remove-Item $anaLog, $boLog -ErrorAction SilentlyContinue

$ana = Start-Process -FilePath $player `
    -WorkingDirectory (Split-Path -Parent $player) `
    -ArgumentList "-batchmode", "-nographics", "-logFile", $anaLog, "-autojoin", "-name:ana", "-device:p5-console-ana-$stamp", "-room:$Room", "-bot" `
    -WindowStyle Hidden -PassThru

Start-Sleep -Milliseconds 500

$bo = Start-Process -FilePath $player `
    -WorkingDirectory (Split-Path -Parent $player) `
    -ArgumentList "-batchmode", "-nographics", "-logFile", $boLog, "-autojoin", "-name:bo", "-device:p5-console-bo-$stamp", "-room:$Room", "-trace" `
    -WindowStyle Hidden -PassThru

try {
    Wait-For $anaLog "mine=True" $TimeoutSeconds
    Wait-For $boLog "mine=True" $TimeoutSeconds
    Wait-For $boLog "mine=False" $TimeoutSeconds

    $spawn = Select-String -Path $boLog -Pattern "spawn on client id=(\d+).*mine=False" | Select-Object -First 1
    $opponent = $spawn.Matches[0].Groups[1].Value
    "   ana/bo thay nhau, doi thu cua bo la id=$opponent"

    Write-Host "3/3 bot di chuyen, client kia thay vi tri doi"
    $positions = @()
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        $positions = @(
            Select-String -Path $boLog -Pattern "\[Pos\] id=$opponent .*pos=\(([-0-9.]+)," |
                ForEach-Object { $_.Matches[0].Groups[1].Value }
        )
        if (($positions | Select-Object -Unique).Count -ge 2) {
            break
        }

        Start-Sleep -Milliseconds 500
    }

    $distinct = @($positions | Select-Object -Unique)
    if ($distinct.Count -lt 2) {
        throw "vi tri cua id=$opponent khong doi: bot khong di chuyen hoac server khong gui transform"
    }

    "   x cua doi thu: $($distinct[0]) -> $($distinct[-1]) ($($distinct.Count) gia tri khac nhau)"
}
finally {
    Stop-Process -Id $ana.Id, $bo.Id -Force -ErrorAction SilentlyContinue
}

Write-Host ""
Write-Host "OK: console server giu room, 2 client thay nhau, input bot di qua server toi client kia"
