#!/usr/bin/env bash
# Kiem thu backend THAT + Nakama THAT (docker compose) bang cac pod that va "client" tu dong:
# dang ky / khoi phuc / doi mat khau (luu trong Nakama storage), ghep cap qua Nakama, ban be, loi moi, bang diem, ky luc.
# Directory + pod do MockCluster dung (chi de cap ticket); backend va Nakama la that.
#
#   ./scripts/test-real-backend.sh          # tu bat docker compose neu Nakama chua chay
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$REPO_ROOT"
WORK="$REPO_ROOT/.mock/real-test"
mkdir -p "$WORK"

cleanup() {
    [[ -n "${BACKEND_PID:-}" ]] && kill "$BACKEND_PID" 2>/dev/null || true
    [[ -n "${STARTED_COMPOSE:-}" ]] && (cd deploy && docker compose down -v >/dev/null 2>&1) || true
}
trap cleanup EXIT

if ! curl -fsS -m 2 http://127.0.0.1:7350/healthcheck >/dev/null 2>&1; then
    echo "=== Bat Nakama + Postgres (docker compose) ==="
    (cd deploy && docker compose up -d)
    STARTED_COMPOSE=1
    for _ in $(seq 1 60); do
        curl -fsS -m 2 http://127.0.0.1:7350/healthcheck >/dev/null 2>&1 && break
        sleep 2
    done
fi

echo "=== Build backend, server pod, MockCluster ==="
dotnet publish MyGame.Backend/MyGame.Backend/MyGame.Backend.csproj -c Release -p:FomoxaGenerate=false -o "$WORK/backend" -v q -nologo
dotnet publish MyGame/Server~/MyGame.Server.csproj -c Release -o "$WORK/server" -v q -nologo
dotnet publish tools/MockCluster/MockCluster.csproj -c Release -o "$WORK/mock" -v q -nologo

head -c 32 /dev/urandom > "$WORK/directory.key"
head -c 32 /dev/zero > "$WORK/pod.key"   # selftest dang nhap cong pod bang key 32 byte 0

echo "=== Chay backend that (Nakama http://127.0.0.1:7350) ==="
Fomoxa__Directory__Address=127.0.0.1:9500 \
Fomoxa__Directory__Key__File="$WORK/directory.key" \
Fomoxa__Pods__Key__File="$WORK/pod.key" \
Fomoxa__Players__WebSocket="" \
Fomoxa__Nakama__Url=http://127.0.0.1:7350 \
Fomoxa__Metrics__Listen=127.0.0.1:9610 \
    dotnet "$WORK/backend/MyGame.Backend.dll" > "$WORK/backend.log" 2>&1 &
BACKEND_PID=$!
sleep 6

echo "=== Chay selftest (cum gia chi lam directory + pod) ==="
dotnet "$WORK/mock/MockCluster.dll" \
    --players=127.0.0.1:19600 --pods-listen=127.0.0.1:19602 --no-tls --pods=4 \
    --server="$WORK/server/MyGame.Server.dll" \
    --selftest-target=9600,9602 --selftest | grep -E "selftest|Unhandled|Exception"
STATUS=${PIPESTATUS[0]}

echo "=== /metrics cua backend ==="
curl -fsS http://127.0.0.1:9610/metrics | grep -E "^fomoxa_(logins_total|joins_total|online_players|score_reports_total)" || true
exit "$STATUS"
