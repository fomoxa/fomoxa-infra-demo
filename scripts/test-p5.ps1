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
    throw "Unity player not built: $player"
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
    if ($LASTEXITCODE -ne 0) { throw "kubectl $($KubeArgs -join ' ') failed (exit $LASTEXITCODE)" }
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
    throw "pattern '$Pattern' not found in $Log after $Seconds seconds"
}

Write-Host "=== P5 DoD check: game server running on K8s + Agones ===" -ForegroundColor Cyan

# 1. Check the GameServers in the K8s cluster
Write-Host "`n1/4 Checking GameServer state before joining..." -ForegroundColor Yellow
$gs = (Invoke-Kube get gs -n fomoxa -o json) -join "`n" | ConvertFrom-Json
if (-not $gs -or -not $gs.items -or $gs.items.Count -lt 2) {
    throw "K8s cluster has no enough GameServers in namespace fomoxa. Run k8s-up first."
}
$allocatedBefore = Get-AllocatedCount
if ($allocatedBefore -ne 0) {
    throw "Before joining there are already $allocatedBefore Allocated pods. Wait until it drops to 0 (empty pods Shutdown themselves and the Fleet creates new ones), then run again."
}
Invoke-Kube get gs -n fomoxa -o "custom-columns=NAME:.metadata.name,STATE:.status.state"

# 2. Start 4 clients: each room needs EXACTLY 2 players for Nakama to match
# (ana+bo -> Room1, cai+dan -> Room2)
Write-Host "`n2/4 Starting 4 clients: ana+bo ($Room1), cai+dan ($Room2) via Gateway 127.0.0.1:7777..." -ForegroundColor Yellow
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

    Write-Host "`n3/4 Waiting for 4 clients to connect and spawn..." -ForegroundColor Yellow
    foreach ($c in $clients) {
        Wait-For $c.Log "mine=True" $TimeoutSeconds
        Write-Host "   $($c.Name) -> $($c.Room) ... mine=True (OK)" -ForegroundColor Green
    }

    # 4. One pod per room => exactly 2 Allocated pods
    Write-Host "`n4/4 Checking Agones GameServer state after players joined..." -ForegroundColor Yellow
    $expected = 2
    $deadline = (Get-Date).AddSeconds(15)
    do {
        $allocatedCount = Get-AllocatedCount
        if ($allocatedCount -eq $expected) { break }
        Start-Sleep -Seconds 1
    } while ((Get-Date) -lt $deadline)

    Invoke-Kube get gs -n fomoxa -o "custom-columns=NAME:.metadata.name,STATE:.status.state"
    if ($allocatedCount -ne $expected) {
        throw "Expected $expected Allocated pods but found $allocatedCount"
    }
    Write-Host "Pods in Allocated state: $allocatedCount (as expected)" -ForegroundColor Green

    # Optional: test scaling the Fleet 4 -> 5 -> 4
    if ($TestScale) {
        $base = [int](Invoke-Kube get fleet fomoxa-server -n fomoxa -o "jsonpath={.spec.replicas}")
        $target = $base + 1
        Write-Host "`n[Bonus] Fleet scale test $base -> $target..." -ForegroundColor Yellow
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
            throw "Scaling up to $target failed: only $total GameServers"
        }
        Write-Host "Fleet scaled up to $target replicas successfully!" -ForegroundColor Green
        Invoke-Kube get gs -n fomoxa

        Write-Host "Scaling back to $base..." -ForegroundColor Yellow
        Invoke-Kube scale fleet fomoxa-server -n fomoxa "--replicas=$base" | Out-Null
    }
}
finally {
    if ($procs.Count -gt 0) {
        Stop-Process -Id ($procs | ForEach-Object { $_.Id }) -Force -ErrorAction SilentlyContinue
    }
}

Write-Host "`n=== P5 DoD COMPLETED SUCCESSFULLY! ===" -ForegroundColor Green
