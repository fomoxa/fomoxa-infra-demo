param(
    [switch]$KeepCluster
)

$ErrorActionPreference = "Stop"
$clusterName = "fomoxa"

if ($KeepCluster) {
    Write-Host "Deleting namespace 'fomoxa' from cluster '$clusterName'..." -ForegroundColor Yellow
    kubectl delete namespace fomoxa --ignore-not-found=true
    Write-Host "Namespace fomoxa cleaned up." -ForegroundColor Green
}
else {
    Write-Host "Deleting k3d cluster '$clusterName' entirely..." -ForegroundColor Yellow
    k3d cluster delete $clusterName
    Write-Host "Deleted k3d cluster $clusterName." -ForegroundColor Green
}

