param(
    [switch]$Build,
    [switch]$SkipClusterCreate
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot

Write-Host "=== Khoi dong cum Kubernetes (k3d + Agones) cho Fomoxa ===" -ForegroundColor Cyan

# 1. Kiem tra cac cong cu can thiet
function Test-Command {
    param([string]$Name, [string]$InstallHint)
    if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) {
        throw "Khong tim thay lenh '$Name'. Hay cai dat truoc: $InstallHint"
    }
}

Test-Command "docker" "Cai Docker Desktop tren Windows"
Test-Command "kubectl" "winget install Kubernetes.kubectl"
Test-Command "k3d" "winget install k3d-io.k3d  (hoac: choco install k3d / scoop install k3d)"
Test-Command "helm" "winget install Helm.Helm  (hoac: choco install kubernetes-helm / scoop install helm)"

# Kiem tra Docker engine
try {
    $null = docker info 2>&1
}
catch {
    throw "Docker daemon chua chay. Hay khoi dong Docker Desktop."
}

# 2. Tao cum k3d fomoxa
$clusterName = "fomoxa"
$existingClusters = k3d cluster list -o json 2>$null | ConvertFrom-Json
$clusterExists = $false
if ($existingClusters) {
    foreach ($c in $existingClusters) {
        if ($c.name -eq $clusterName) {
            $clusterExists = $true
            break
        }
    }
}

if (-not $clusterExists -and -not $SkipClusterCreate) {
    Write-Host "`n1/6 Tao cum k3d '$clusterName' voi port mappings..." -ForegroundColor Yellow
    # Map UDP 7777 (Gateway), TCP 9600 (Backend), TCP 7350 (Nakama - tuy chon)
    k3d cluster create $clusterName `
        --port "7777:7777/udp@loadbalancer" `
        --port "9600:9600@loadbalancer" `
        --port "9643:9643@loadbalancer" `
        --port "7350:7350@loadbalancer" `
        --wait
}
else {
    Write-Host "`n1/6 Cum k3d '$clusterName' da ton tai." -ForegroundColor Green
}

# Dam bao dung context
kubectl config use-context "k3d-$clusterName" | Out-Null
Write-Host "Context hien tai: $((kubectl config current-context).Trim())" -ForegroundColor Green

# 3. Cai dat / Cap nhat Agones qua Helm
Write-Host "`n2/6 Cai dat Agones via Helm..." -ForegroundColor Yellow
# Dat $env:AGONES_VERSION (vi du 1.47.0) de ghim version chart; de trong = ban moi nhat.
helm repo add agones https://agones.dev/chart/stable --force-update
if ($LASTEXITCODE -ne 0) { throw "helm repo add agones that bai" }
helm repo update agones
if ($LASTEXITCODE -ne 0) { throw "helm repo update agones that bai" }

$agonesInstalled = helm list -n agones-system -q 2>$null
if (-not $agonesInstalled) {
    $versionArgs = @()
    if ($env:AGONES_VERSION) { $versionArgs = @("--version", $env:AGONES_VERSION) }
    # Project khong dung GameServerAllocation => tat allocator va ping.
    # Allocator la LoadBalancer cong 443, dung voi Traefik cua k3s tren cum 1 node.
    helm install agones agones/agones @versionArgs `
        --namespace agones-system `
        --create-namespace `
        --set "agones.allocator.install=false" `
        --set "agones.ping.install=false" `
        --wait --timeout 5m
}
else {
    Write-Host "Agones da duoc cai dat trong agones-system." -ForegroundColor Green
}

# 4. Build va Import Docker images vao k3d
if ($Build) {
    Write-Host "`n3/6 Build Docker images..." -ForegroundColor Yellow
    & "$PSScriptRoot\build-images.ps1"
}

Write-Host "`n4/6 Import images vao k3d cluster..." -ForegroundColor Yellow
k3d image import `
    fomoxa-server:latest `
    fomoxa-directory:latest `
    fomoxa-gateway:latest `
    fomoxa-backend:latest `
    -c $clusterName

# 5. Tao namespace va Secrets
Write-Host "`n5/6 Tao namespace va secrets..." -ForegroundColor Yellow
kubectl apply -f "$repoRoot\deploy\k8s\namespace.yaml"
& "$PSScriptRoot\gen-k8s-secrets.ps1" -Namespace "fomoxa"

# 6. Apply K8s manifests
Write-Host "`n6/6 Apply K8s manifests..." -ForegroundColor Yellow
kubectl apply -f "$repoRoot\deploy\k8s\postgres.yaml"
kubectl apply -f "$repoRoot\deploy\k8s\nakama.yaml"
kubectl apply -f "$repoRoot\deploy\k8s\directory.yaml"
kubectl apply -f "$repoRoot\deploy\k8s\gateway.yaml"
kubectl apply -f "$repoRoot\deploy\k8s\backend.yaml"
kubectl apply -f "$repoRoot\deploy\k8s\tls-proxy.yaml"
kubectl apply -f "$repoRoot\deploy\k8s\fleet.yaml"

Write-Host "`nCho cac dich vu khoi dong..." -ForegroundColor Cyan
kubectl rollout status deployment/postgres -n fomoxa --timeout=90s
kubectl rollout status deployment/nakama -n fomoxa --timeout=120s
kubectl rollout status deployment/directory -n fomoxa --timeout=60s
kubectl rollout status deployment/gateway -n fomoxa --timeout=60s
kubectl rollout status deployment/backend -n fomoxa --timeout=60s

Write-Host "`nKiem tra GameServers:" -ForegroundColor Cyan
kubectl get gs -n fomoxa

Write-Host "`n=== Cum K8s Fomoxa da san sang! ===" -ForegroundColor Green

