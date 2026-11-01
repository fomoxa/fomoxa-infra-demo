#!/usr/bin/env bash
# Chay "cum gia" cho client Unity: directory gia (:9500) + backend gia (:9600) + cac pod that.
# Khong can Docker, K8s, Nakama, Agones hay gateway. Chi can .NET 8 SDK.
# Tham so them se truyen cho MockCluster, vi du: --pods=2 --match-delay=3 --strict-version
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$REPO_ROOT"

if ! command -v dotnet >/dev/null 2>&1; then
    echo "Loi: can .NET 8 SDK (https://dotnet.microsoft.com/download)." >&2
    exit 1
fi

echo "=== Build server pod va MockCluster ==="
dotnet publish MyGame/Server~/MyGame.Server.csproj -c Release -o .mock/server -v q -nologo
dotnet publish tools/MockCluster/MockCluster.csproj -c Release -o .mock/cluster -v q -nologo

echo "=== Chay cum gia (Ctrl+C de tat) ==="
exec dotnet .mock/cluster/MockCluster.dll --server=.mock/server/MyGame.Server.dll "$@"
