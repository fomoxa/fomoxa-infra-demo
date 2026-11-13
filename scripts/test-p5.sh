#!/usr/bin/env bash
set -euo pipefail

ROOM1="${1:-arena:1}"
ROOM2="${2:-arena:2}"
REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

echo "=== P5 DoD check on WSL: K8s GameServer + Agones ==="

echo -e "\n1/3 Checking GameServers in the K8s cluster..."
if ! kubectl get gs -n fomoxa >/dev/null 2>&1; then
    echo "Error: Cannot connect to the K8s cluster or the fleet was not applied in namespace fomoxa." >&2
    echo "Run: bash scripts/k8s-up.sh first." >&2
    exit 1
fi

kubectl get gs -n fomoxa -o custom-columns=NAME:.metadata.name,STATE:.status.state,ADDRESS:.status.address,PORT:.status.ports[0].port

echo -e "\n2/3 Starting test clients..."
# Unity Player la ban build Windows (.exe): goi PowerShell qua WSL interop.
# kubectl trong test-p5.ps1 duoc chuyen ve kubectl cua WSL (noi co kubeconfig k3d).
if command -v powershell.exe >/dev/null 2>&1; then
    echo "Windows Host Interop detected, launching test-p5.ps1 (kubectl via WSL)..."
    WIN_SCRIPT=$(wslpath -w "$REPO_ROOT/scripts/test-p5.ps1")
    export FOMOXA_KUBECTL_WSL=1
    export WSLENV="FOMOXA_KUBECTL_WSL${WSLENV:+:$WSLENV}"
    EXTRA=()
    [[ "${TEST_SCALE:-}" == "1" ]] && EXTRA+=(-TestScale)
    powershell.exe -ExecutionPolicy Bypass -File "$WIN_SCRIPT" -Room1 "$ROOM1" -Room2 "$ROOM2" ${EXTRA[@]+"${EXTRA[@]}"}
else
    echo "Error: powershell.exe not found. The Unity Player is a Windows build (.exe)." >&2
    echo "Open PowerShell on Windows and run: powershell -ExecutionPolicy Bypass -File scripts\\test-p5.ps1" >&2
    exit 1
fi

echo -e "\n3/3 Agones GameServer state after the clients exit:"
kubectl get gs -n fomoxa
