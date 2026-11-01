param(
    [string]$Namespace = "fomoxa"
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$secretRoot = Join-Path $repoRoot "deploy/secrets"
$genKeysScript = Join-Path $PSScriptRoot "gen-keys.ps1"

# 1. Đảm bảo keys đã tồn tại
if (-not (Test-Path (Join-Path $secretRoot "directory/root"))) {
    Write-Host "[gen-k8s-secrets] Chưa có keys, gọi gen-keys.ps1..." -ForegroundColor Yellow
    & $genKeysScript
}

Write-Host "[gen-k8s-secrets] Đang tạo Kubernetes Secrets trong namespace '$Namespace'..." -ForegroundColor Cyan

# 2. Tạo secret fomoxa-directory-keys
kubectl create secret generic fomoxa-directory-keys `
    --namespace=$Namespace `
    --from-file=backend=(Join-Path $secretRoot "directory/backend") `
    --from-file=pod=(Join-Path $secretRoot "directory/pod") `
    --from-file=gateway=(Join-Path $secretRoot "directory/gateway") `
    --from-file=admin=(Join-Path $secretRoot "directory/admin") `
    --from-file=root=(Join-Path $secretRoot "directory/root") `
    --dry-run=client -o yaml | kubectl apply -f -

# 3. Tạo secret fomoxa-backend-keys (chứa backend key và backend-pod key)
kubectl create secret generic fomoxa-backend-keys `
    --namespace=$Namespace `
    --from-file=backend=(Join-Path $secretRoot "directory/backend") `
    --from-file=backend-pod=(Join-Path $secretRoot "backend-pod") `
    --dry-run=client -o yaml | kubectl apply -f -

# 4. Tạo secret fomoxa-pod-key (cho game server pod)
kubectl create secret generic fomoxa-pod-key `
    --namespace=$Namespace `
    --from-file=pod=(Join-Path $secretRoot "directory/pod") `
    --dry-run=client -o yaml | kubectl apply -f -

Write-Host "[gen-k8s-secrets] Hoàn tất tạo K8s secrets!" -ForegroundColor Green

