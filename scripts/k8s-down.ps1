param(
    [switch]$KeepCluster
)

$ErrorActionPreference = "Stop"
$clusterName = "fomoxa"

if ($KeepCluster) {
    Write-Host "Xoa namespace 'fomoxa' tren cum '$clusterName'..." -ForegroundColor Yellow
    kubectl delete namespace fomoxa --ignore-not-found=true
    Write-Host "Da don dep namespace fomoxa." -ForegroundColor Green
}
else {
    Write-Host "Xoa hoan toan cum k3d '$clusterName'..." -ForegroundColor Yellow
    k3d cluster delete $clusterName
    Write-Host "Da xoa cum k3d $clusterName." -ForegroundColor Green
}
