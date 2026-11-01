#!/usr/bin/env bash
set -euo pipefail

ROOM1="${1:-arena:1}"
ROOM2="${2:-arena:2}"
REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

echo "=== Kiem tra DoD P5 tren WSL: GameServer K8s + Agones ==="

echo -e "\n1/3 Kiem tra cac GameServer trong cum K8s..."
if ! kubectl get gs -n fomoxa >/dev/null 2>&1; then
    echo "Loi: Khong ket noi duoc cluster K8s hoac chua apply fleet trong namespace fomoxa." >&2
    echo "Hay chay: bash scripts/k8s-up.sh truoc." >&2
    exit 1
fi

kubectl get gs -n fomoxa -o custom-columns=NAME:.metadata.name,STATE:.status.state,ADDRESS:.status.address,PORT:.status.ports[0].port

echo -e "\n2/3 Khoi dong client test..."
# Neu co powershell.exe tren host Windows (WSL goi interop)
if command -v powershell.exe >/dev/null 2>&1; then
    echo "Phat hien Windows Host Interop, khoi chay test-p5.ps1..."
    WIN_SCRIPT=$(wslpath -w "$REPO_ROOT/scripts/test-p5.ps1")
    powershell.exe -ExecutionPolicy Bypass -File "$WIN_SCRIPT" -Room1 "$ROOM1" -Room2 "$ROOM2"
else
    echo "Luu y: Unity Player la ban build Windows (.exe). Hay mo PowerShell tren Windows chay:"
    echo "  powershell -ExecutionPolicy Bypass -File scripts\\test-p5.ps1"
fi

echo -e "\n3/3 Kiem tra lai trang thai Agones GameServers:"
kubectl get gs -n fomoxa
