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
# Unity Player la ban build Windows (.exe): goi PowerShell qua WSL interop.
# kubectl trong test-p5.ps1 duoc chuyen ve kubectl cua WSL (noi co kubeconfig k3d).
if command -v powershell.exe >/dev/null 2>&1; then
    echo "Phat hien Windows Host Interop, khoi chay test-p5.ps1 (kubectl qua WSL)..."
    WIN_SCRIPT=$(wslpath -w "$REPO_ROOT/scripts/test-p5.ps1")
    export FOMOXA_KUBECTL_WSL=1
    export WSLENV="FOMOXA_KUBECTL_WSL${WSLENV:+:$WSLENV}"
    EXTRA=()
    [[ "${TEST_SCALE:-}" == "1" ]] && EXTRA+=(-TestScale)
    powershell.exe -ExecutionPolicy Bypass -File "$WIN_SCRIPT" -Room1 "$ROOM1" -Room2 "$ROOM2" ${EXTRA[@]+"${EXTRA[@]}"}
else
    echo "Loi: khong thay powershell.exe. Unity Player la ban build Windows (.exe)." >&2
    echo "Hay mo PowerShell tren Windows chay: powershell -ExecutionPolicy Bypass -File scripts\\test-p5.ps1" >&2
    exit 1
fi

echo -e "\n3/3 Trang thai Agones GameServers sau khi client thoat:"
kubectl get gs -n fomoxa
