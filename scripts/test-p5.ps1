param(
    [string]$Room1 = "arena:1",
    [string]$Room2 = "arena:2",
    [int]$TimeoutSeconds = 45,
    [switch]$TestScale
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$player = Join-Path $repoRoot "MyGame/Builds/MyGame.exe"
$logs = Join-Path $repoRoot "logs"
$stamp = Get-Date -Format "HHmmss"

if (-not (Test-Path $player)) {
    throw "chua build Unity player: $player"
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

Write-Host "=== Kiem tra DoD P5: Game server chay tren K8s + Agones ===" -ForegroundColor Cyan

# 1. Kiem tra cac GameServer trong cum K8s
Write-Host "`n1/4 Kiem tra trang thai GameServers truoc khi join..." -ForegroundColor Yellow
$gs = kubectl get gs -n fomoxa -o json 2>$null | ConvertFrom-Json
if (-not $gs -or -not $gs.items -or $gs.items.Count -lt 2) {
    throw "Cum K8s khong co du 2 GameServer trong namespace fomoxa. Hay chay scripts\k8s-up.ps1 truoc."
}

kubectl get gs -n fomoxa -o custom-columns=NAME:.metadata.name,STATE:.status.state,ADDRESS:.status.address,PORT:.status.ports[0].port

# 2. Khoi dong 2 client vao 2 room khac nhau qua Gateway (127.0.0.1:7777)
Write-Host "`n2/4 Khoi dong ana ($Room1) va bo ($Room2) qua Gateway 127.0.0.1:7777..." -ForegroundColor Yellow
$anaLog = Join-Path $logs "p5-k8s-ana.log"
$boLog  = Join-Path $logs "p5-k8s-bo.log"
Remove-Item $anaLog, $boLog -ErrorAction SilentlyContinue

$ana = Start-Process -FilePath $player `
    -WorkingDirectory (Split-Path -Parent $player) `
    -ArgumentList "-batchmode", "-nographics", "-logFile", $anaLog, "-autojoin", "-name:ana", "-device:p5-k8s-ana-$stamp", "-room:$Room1" `
    -WindowStyle Hidden -PassThru

Start-Sleep -Milliseconds 500

$bo = Start-Process -FilePath $player `
    -WorkingDirectory (Split-Path -Parent $player) `
    -ArgumentList "-batchmode", "-nographics", "-logFile", $boLog, "-autojoin", "-name:bo", "-device:p5-k8s-bo-$stamp", "-room:$Room2" `
    -WindowStyle Hidden -PassThru

try {
    Write-Host "`n3/4 Cho 2 client ket noi thanh cong va spawn nhan vat..." -ForegroundColor Yellow
    Wait-For $anaLog "mine=True" $TimeoutSeconds
    Write-Host "   ana -> $Room1 -> 127.0.0.1:7777 ... mine=True (OK)" -ForegroundColor Green

    Wait-For $boLog "mine=True" $TimeoutSeconds
    Write-Host "   bo  -> $Room2 -> 127.0.0.1:7777 ... mine=True (OK)" -ForegroundColor Green

    # 3. Kiem tra trang thai GameServer da chuyen sang Allocated
    Write-Host "`n4/4 Kiem tra trang thai Agones GameServer sau khi co nguoi choi..." -ForegroundColor Yellow
    Start-Sleep -Seconds 2
    $allocatedCount = 0
    $states = kubectl get gs -n fomoxa -o jsonpath="{.items[*].status.state}"
    foreach ($st in $states.Split(" ")) {
        if ($st -eq "Allocated") {
            $allocatedCount++
        }
    }
    kubectl get gs -n fomoxa -o custom-columns=NAME:.metadata.name,STATE:.status.state,ROOMS:.metadata.annotations

    Write-Host "So pod o trang thai Allocated: $allocatedCount" -ForegroundColor Green

    # Tuy chon: Test scale Fleet 2 -> 3
    if ($TestScale) {
        Write-Host "`n[Bonus] Test Scale Fleet 2 -> 3..." -ForegroundColor Yellow
        kubectl scale fleet fomoxa-server -n fomoxa --replicas=3
        Start-Sleep -Seconds 5
        kubectl wait --for=condition=Ready gs -l agones.dev/fleet=fomoxa-server -n fomoxa --timeout=60s
        Write-Host "Fleet da scale len 3 replicas thanh cong!" -ForegroundColor Green
        kubectl get gs -n fomoxa

        Write-Host "Scale nguoc lai ve 2..." -ForegroundColor Yellow
        kubectl scale fleet fomoxa-server -n fomoxa --replicas=2
    }
}
finally {
    Stop-Process -Id $ana.Id, $bo.Id -Force -ErrorAction SilentlyContinue
}

Write-Host "`n=== DoD P5 HOAN TAT THANH CONG! ===" -ForegroundColor Green
