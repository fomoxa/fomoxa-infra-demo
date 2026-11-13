param(
    [string]$Namespace = "fomoxa"
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$secretRoot = Join-Path $repoRoot "deploy/secrets"
$genKeysScript = Join-Path $PSScriptRoot "gen-keys.ps1"

# 1. Đảm bảo keys đã tồn tại
if (-not (Test-Path (Join-Path $secretRoot "directory/root"))) {
    Write-Host "[gen-k8s-secrets] No keys found, calling gen-keys.ps1..." -ForegroundColor Yellow
    & $genKeysScript
}

Write-Host "[gen-k8s-secrets] Creating Kubernetes Secrets in namespace '$Namespace'..." -ForegroundColor Cyan

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

# 5. Tạo secret fomoxa-backend-pod-key (pod -> backend, chỉ chứa key backend-pod)
kubectl create secret generic fomoxa-backend-pod-key `
    --namespace=$Namespace `
    --from-file=backend-pod=(Join-Path $secretRoot "backend-pod") `
    --dry-run=client -o yaml | kubectl apply -f -

# 6. Nakama: key riêng, mật khẩu console và DB
$nakama = Join-Path $secretRoot "nakama"
kubectl create secret generic fomoxa-nakama `
    --namespace=$Namespace `
    --from-file=server-key=(Join-Path $nakama "server-key") `
    --from-file=http-key=(Join-Path $nakama "http-key") `
    --from-file=console-password=(Join-Path $nakama "console-password") `
    --from-file=session-key=(Join-Path $nakama "session-key") `
    --from-file=refresh-key=(Join-Path $nakama "refresh-key") `
    --from-file=db-password=(Join-Path $nakama "db-password") `
    --dry-run=client -o yaml | kubectl apply -f -

# 7. TLS cho cổng 9643
$tls = Join-Path $secretRoot "tls"
if (-not (Test-Path (Join-Path $tls "tls.crt"))) {
    & "$PSScriptRoot\gen-tls-cert.ps1" | Out-Null
}
kubectl create secret tls fomoxa-tls `
    --namespace=$Namespace `
    --cert=(Join-Path $tls "tls.crt") `
    --key=(Join-Path $tls "tls.key") `
    --dry-run=client -o yaml | kubectl apply -f -

Write-Host "[gen-k8s-secrets] Finished creating K8s secrets!" -ForegroundColor Green

