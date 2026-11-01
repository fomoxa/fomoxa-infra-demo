param(
    [string]$Room = "arena:1",
    [string]$FakeRoom = "fake:1",
    [int]$JoinTimeoutSeconds = 40
)

# Kiểm chứng DoD của P4 trên cụm đang chạy (scripts/start-cluster.ps1):
#   1. 2 client ghép trận qua Nakama -> cùng ticket -> cùng pod -> thấy nhau
#   2. room không có ai ghép -> FAILED_PRECONDITION
#
# Yêu cầu: Nakama + Postgres đang chạy, và cụm đã start với Nakama bật.

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$player = Join-Path $repoRoot "MyGame/Builds/MyGame.exe"
$logs = Join-Path $repoRoot "logs"
$stamp = Get-Date -Format "HHmmss"

if (-not (Test-Path $player)) {
    throw "chua build player: $player"
}

New-Item -ItemType Directory -Force -Path $logs | Out-Null

function Start-Client {
    param([string]$Name, [string]$Device, [string]$TargetRoom, [string]$Log)

    $arguments = @(
        "-batchmode", "-nographics",
        "-logFile", $Log,
        "-autojoin", "-name:$Name", "-device:$Device", "-room:$TargetRoom"
    )
    Start-Process -FilePath $player `
        -WorkingDirectory (Split-Path -Parent $player) `
        -ArgumentList $arguments `
        -WindowStyle Hidden -PassThru
}

function Wait-Log {
    param([string]$Log, [string]$Pattern, [int]$Seconds)

    $deadline = (Get-Date).AddSeconds($Seconds)
    while ((Get-Date) -lt $deadline) {
        if ((Test-Path $Log) -and (Select-String -Path $Log -Pattern $Pattern -SimpleMatch -Quiet)) {
            return
        }

        Start-Sleep -Milliseconds 300
    }

    throw "khong thay '$Pattern' trong $Log sau $Seconds giay"
}

Write-Host "1/2 hai client cung room $Room"

$anaLog = Join-Path $logs "p4-test-ana.log"
$boLog = Join-Path $logs "p4-test-bo.log"
Remove-Item $anaLog, $boLog -ErrorAction SilentlyContinue

$ana = Start-Client "ana" "fomoxa-device-ana-$stamp" $Room $anaLog
Start-Sleep -Milliseconds 400
$bo = Start-Client "bo" "fomoxa-device-bo-$stamp" $Room $boLog

try {
    Wait-Log $anaLog "mine=True" $JoinTimeoutSeconds
    Wait-Log $boLog "mine=False" $JoinTimeoutSeconds
    Wait-Log $boLog "mine=True" $JoinTimeoutSeconds
    Wait-Log $anaLog "mine=False" $JoinTimeoutSeconds

    $anaTicket = Select-String -Path $anaLog -Pattern "-> 127.0.0.1" | Select-Object -First 1
    $boTicket = Select-String -Path $boLog -Pattern "-> 127.0.0.1" | Select-Object -First 1
    "   ana: $($anaTicket.Line.Trim())"
    "   bo : $($boTicket.Line.Trim())"
}
finally {
    Stop-Process -Id $ana.Id, $bo.Id -Force -ErrorAction SilentlyContinue
}

Write-Host "2/2 room $FakeRoom phai bi tu choi"

$fakeLog = Join-Path $logs "p4-test-fake.log"
Remove-Item $fakeLog -ErrorAction SilentlyContinue
$fake = Start-Client "ana" "fomoxa-device-fake-$stamp" $FakeRoom $fakeLog

try {
    Wait-Log $fakeLog "FAILED_PRECONDITION" ($JoinTimeoutSeconds + 10)
    $line = Select-String -Path $fakeLog -Pattern "FAILED_PRECONDITION" | Select-Object -First 1
    "   $($line.Line.Trim())"
}
finally {
    Stop-Process -Id $fake.Id -Force -ErrorAction SilentlyContinue
}

Write-Host ""
Write-Host "OK: 2 client cung room thay nhau, room gia bi FAILED_PRECONDITION"
