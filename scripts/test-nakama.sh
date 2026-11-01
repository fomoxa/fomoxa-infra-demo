#!/usr/bin/env bash
set -euo pipefail

BASE="${1:-http://127.0.0.1:7350}"
ROOM="${2:-arena:1}"
HTTP_KEY="${3:-}"
SERVER_KEY="${4:-}"
USER_A="${5:-fomoxa-test-ana}"
USER_B="${6:-fomoxa-test-bo}"

# Canh bao neu cong 7350 dang duoc giu boi Nakama cua docker compose (khong phai cum k3d)
if [[ "$BASE" == *"127.0.0.1:7350"* ]] && command -v docker >/dev/null 2>&1; then
    owner="$(docker ps --format '{{.Names}} {{.Ports}}' | grep -E '[:]7350->' | awk '{print $1}' || true)"
    if [[ -n "$owner" ]]; then
        echo "Luu y: cong 7350 dang tro toi container '$owner'."
        if [[ "$owner" != k3d-* ]]; then
            echo "  Do khong phai cum k3d => script se test Nakama cua compose, khong phai cum K8s." >&2
            [[ "${ALLOW_COMPOSE:-}" == "1" ]] || { echo "  Dat ALLOW_COMPOSE=1 neu chu y muon test compose." >&2; exit 1; }
        fi
    fi
fi

source "$(dirname "${BASH_SOURCE[0]}")/lib-nakama-keys.sh"
HTTP_KEY="${HTTP_KEY:-$DEFAULT_HTTP_KEY}"
SERVER_KEY="${SERVER_KEY:-$DEFAULT_SERVER_KEY}"

auth_device() {
    local device_id="$1"
    local creds
    creds=$(printf "%s:" "$SERVER_KEY" | base64)
    local resp
    resp=$(curl -s -X POST "$BASE/v2/account/authenticate/device?create=true" \
        -H "Authorization: Basic $creds" \
        -H "Content-Type: application/json" \
        -d "{\"id\": \"$device_id\", \"create\": true}")
    
    local token
    token=$(echo "$resp" | grep -o '"token":"[^"]*' | cut -d'"' -f4)
    if [[ -z "$token" ]]; then
        echo "Loi authenticate cho $device_id: $resp" >&2
        return 1
    fi
    
    # Parse JWT payload de lay uid/sub
    local payload
    payload=$(echo "$token" | cut -d'.' -f2 | tr '_-' '/+')
    # Pad base64
    local rem=$(( ${#payload} % 4 ))
    if [[ $rem -eq 2 ]]; then payload="${payload}=="; elif [[ $rem -eq 3 ]]; then payload="${payload}="; fi
    local decoded
    decoded=$(echo "$payload" | base64 -d 2>/dev/null || echo "{}")
    local uid
    uid=$(echo "$decoded" | grep -o '"uid":"[^"]*' | cut -d'"' -f4 || true)
    if [[ -z "$uid" ]]; then
        uid=$(echo "$decoded" | grep -o '"sub":"[^"]*' | cut -d'"' -f4 || true)
    fi
    echo "$token $uid"
}

rpc_user() {
    local name="$1"
    local token="$2"
    local data="$3"
    curl -s -X POST "$BASE/v2/rpc/${name}?unwrap" \
        -H "Authorization: Bearer $token" \
        -H "Content-Type: application/json" \
        -d "$data"
}

rpc_key() {
    local name="$1"
    local data="$2"
    local creds
    creds=$(printf "%s:" "$HTTP_KEY" | base64)
    curl -s -X POST "$BASE/v2/rpc/${name}?unwrap" \
        -H "Authorization: Basic $creds" \
        -H "Content-Type: application/json" \
        -d "$data"
}

echo "=== Kiem tra Nakama Matchmaking (bash) ==="

echo "1) Dang nhap device A va B..."
read -r TOKEN_A UID_A < <(auth_device "$USER_A")
read -r TOKEN_B UID_B < <(auth_device "$USER_B")
echo "   ana = $UID_A"
echo "   bo  = $UID_B"

ROOM_JSON="{\"room\":\"$ROOM\"}"

echo "2) Reset queue $ROOM..."
rpc_key "reset_match" "$ROOM_JSON" >/dev/null

echo "3) ana find_match..."
RESP_A=$(rpc_user "find_match" "$TOKEN_A" "$ROOM_JSON")
echo "   -> $RESP_A"
if ! echo "$RESP_A" | grep -q '"status":"waiting"'; then
    echo "Loi: ana phai o trang thai waiting" >&2
    exit 1
fi

echo "4) bo find_match..."
RESP_B=$(rpc_user "find_match" "$TOKEN_B" "$ROOM_JSON")
echo "   -> $RESP_B"
if ! echo "$RESP_B" | grep -q '"status":"matched"'; then
    echo "Loi: bo phai duoc matched" >&2
    exit 1
fi

TICKET_B=$(echo "$RESP_B" | grep -o '"ticket":"[^"]*' | cut -d'"' -f4)

echo "5) ana poll_match..."
POLL_A=$(rpc_user "poll_match" "$TOKEN_A" "$ROOM_JSON")
echo "   -> $POLL_A"
if ! echo "$POLL_A" | grep -q '"status":"matched"'; then
    echo "Loi: ana phai thay ket qua matched" >&2
    exit 1
fi

TICKET_A=$(echo "$POLL_A" | grep -o '"ticket":"[^"]*' | cut -d'"' -f4)
if [[ "$TICKET_A" != "$TICKET_B" ]]; then
    echo "Loi: hai client phai nhan cung ticket ($TICKET_A != $TICKET_B)" >&2
    exit 1
fi

echo "6) Backend verify_ticket..."
VERIFY=$(rpc_key "verify_ticket" "{\"ticket\":\"$TICKET_A\",\"room\":\"$ROOM\",\"user_id\":\"$UID_A\"}")
echo "   -> $VERIFY"
if ! echo "$VERIFY" | grep -q '"ok":true'; then
    echo "Loi: verify_ticket phai thanh cong" >&2
    exit 1
fi

echo "7) Verify voi user khac (phai bi tu choi)..."
read -r _ STRANGER_UID < <(auth_device "fomoxa-test-stranger")
BAD=$(rpc_key "verify_ticket" "{\"ticket\":\"$TICKET_A\",\"room\":\"$ROOM\",\"user_id\":\"$STRANGER_UID\"}")
echo "   -> $BAD"
if echo "$BAD" | grep -q '"ok":true'; then
    echo "Loi: stranger phai bi tu choi" >&2
    exit 1
fi

echo -e "\nOK: 2 client ghep dung room, ticket dung, user ngoai bi chan!"

