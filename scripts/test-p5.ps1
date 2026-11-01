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

# kubectl: mac dinh dung kubectl cua Windows. Khi chay tu test-p5.sh (WSL) se dat
# FOMOXA_KUBECTL_WSL=1 de goi kubectl trong WSL (noi co kubeconfig cua k3d).
function Invoke-Kube {
    param([Parameter(ValueFromRemainingArguments = $true)][string[]]$KubeArgs)
    if ($env:FOMOXA_KUBECTL_WSL -eq "1") {
        $out = & wsl.exe -e kubectl @KubeArgs
    }
    else {
        $out = & kubectl @KubeArgs
    }
    if ($LASTEXITCODE -ne 0) { throw "kubectl $($KubeArgs -join ' ') that bai (exit $LASTEXITCODE)" }
    return $out
}

function Get-AllocatedCount {
    $states = (Invoke-Kube get gs -n fomoxa -o "jsonpath={.items[*].status.state}") -join " "
    return @($states.Split(" ", [StringSplitOptions]::RemoveEmptyEntries) | Where-Object { $_ -eq "Allocated" }).Count
}

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
$gs = (Invoke-Kube get gs -n fomoxa -o json) -join "`n" | ConvertFrom-Json
if (-not $gs -or -not $gs.items -or $gs.items.Count -lt 2) {
    throw "Cum K8s khong co du GameServer trong namespace fomoxa. Hay chay k8s-up truoc."
}
$allocatedBefore = Get-AllocatedCount
if ($allocatedBefore -ne 0) {
    throw "Truoc khi join da co $allocatedBefore pod Allocated. Hay doi ve 0 (pod trong se tu Shutdown, Fleet tao pod moi) roi chay lai."
}
Invoke-Kube get gs -n fomoxa -o "custom-columns=NAME:.metadata.name,STATE:.status.state"

# 2. Khoi dong 4 client: moi room can DUNG 2 nguoi de Nakama ghep cap
# (ana+bo -> Room1, cai+dan -> Room2)
Write-Host "`n2/4 Khoi dong 4 client: ana+bo ($Room1), cai+dan ($Room2) qua Gateway 127.0.0.1:7777..." -ForegroundColor Yellow
$clients = @(
    @{ Name = "ana"; Room = $Room1 },
    @{ Name = "bo";  Room = $Room1 },
    @{ Name = "cai"; Room = $Room2 },
    @{ Name = "dan"; Room = $Room2 }
)
$procs = @()
foreach ($c in $clients) {
    $c.Log = Join-Path $logs "p5-k8s-$($c.Name).log"
    Remove-Item $c.Log -ErrorAction SilentlyContinue
}

try {
    foreach ($c in $clients) {
        $procs += Start-Process -FilePath $player `
            -WorkingDirectory (Split-Path -Parent $player) `
            -ArgumentList "-batchmode", "-nographics", "-logFile", $c.Log, "-autojoin", "-name:$($c.Name)", "-device:p5-k8s-$($c.Name)-$stamp", "-room:$($c.Room)" `
            -WindowStyle Hidden -PassThru
        Start-Sleep -Milliseconds 500
    }

    Write-Host "`n3/4 Cho 4 client ket noi thanh cong va spawn nhan vat..." -ForegroundColor Yellow
    foreach ($c in $clients) {
        Wait-For $c.Log "mine=True" $TimeoutSeconds
        Write-Host "   $($c.Name) -> $($c.Room) ... mine=True (OK)" -ForegroundColor Green
    }

    # 4. Moi room mot pod => dung 2 pod Allocated
    Write-Host "`n4/4 Kiem tra trang thai Agones GameServer sau khi co nguoi choi..." -ForegroundColor Yellow
    $expected = 2
    $deadline = (Get-Date).AddSeconds(15)
    do {
        $allocatedCount = Get-AllocatedCount
        if ($allocatedCount -eq $expected) { break }
        Start-Sleep -Seconds 1
    } while ((Get-Date) -lt $deadline)

    Invoke-Kube get gs -n fomoxa -o "custom-columns=NAME:.metadata.name,STATE:.status.state"
    if ($allocatedCount -ne $expected) {
        throw "Ky vong $expected pod Allocated nhung thuc te la $allocatedCount"
    }
    Write-Host "So pod o trang thai Allocated: $allocatedCount (dung ky vong)" -ForegroundColor Green

    # Tuy chon: Test scale Fleet 4 -> 5 -> 4
    if ($TestScale) {
        $base = [int](Invoke-Kube get fleet fomoxa-server -n fomoxa -o "jsonpath={.spec.replicas}")
        $target = $base + 1
        Write-Host "`n[Bonus] Test Scale Fleet $base -> $target..." -ForegroundColor Yellow
        Invoke-Kube scale fleet fomoxa-server -n fomoxa "--replicas=$target" | Out-Null
        $deadline = (Get-Date).AddSeconds(60)
        do {
            $states = (Invoke-Kube get gs -n fomoxa -o "jsonpath={.items[*].status.state}") -join " "
            $ready = @($states.Split(" ", [StringSplitOptions]::RemoveEmptyEntries) | Where-Object { $_ -eq "Ready" }).Count
            $total = @($states.Split(" ", [StringSplitOptions]::RemoveEmptyEntries)).Count
            if ($total -eq $target -and ($ready + $allocatedCount) -eq $target) { break }
            Start-Sleep -Seconds 2
        } while ((Get-Date) -lt $deadline)
        if ($total -ne $target) {
            throw "Scale len $target that bai: co $total GameServer"
        }
        Write-Host "Fleet da scale len $target replicas thanh cong!" -ForegroundColor Green
        Invoke-Kube get gs -n fomoxa

        Write-Host "Scale nguoc lai ve $base..." -ForegroundColor Yellow
        Invoke-Kube scale fleet fomoxa-server -n fomoxa "--replicas=$base" | Out-Null
    }
}
finally {
    if ($procs.Count -gt 0) {
        Stop-Process -Id ($procs | ForEach-Object { $_.Id }) -Force -ErrorAction SilentlyContinue
    }
}

Write-Host "`n=== DoD P5 HOAN TAT THANH CONG! ===" -ForegroundColor Green
