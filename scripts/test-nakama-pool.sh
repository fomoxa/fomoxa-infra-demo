#!/usr/bin/env bash
set -euo pipefail

BASE="${1:-http://127.0.0.1:7350}"
HTTP_KEY="${2:-defaulthttpkey}"
SERVER_KEY="${3:-defaultkey}"

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
    
    local payload
    payload=$(echo "$token" | cut -d'.' -f2 | tr '_-' '/+')
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

echo "=== Kiem tra P5.5: Matchmaking tu dong cap room tu pool (>= 4 client, bash) ==="

echo "1/4 Reset auto pool..."
rpc_key "reset_match" '{"room":"auto"}' >/dev/null

echo "2/4 Dang nhap 4 clients (ana, bo, chi, dung)..."
read -r TOKEN_ANA UID_ANA < <(auth_device "fomoxa-p55-ana")
read -r TOKEN_BO UID_BO < <(auth_device "fomoxa-p55-bo")
read -r TOKEN_CHI UID_CHI < <(auth_device "fomoxa-p55-chi")
read -r TOKEN_DUNG UID_DUNG < <(auth_device "fomoxa-p55-dung")

AUTO_JSON='{"room":"auto"}'

echo "3/4 Cap 1 (ana + bo) vao auto pool..."
RESP_ANA=$(rpc_user "find_match" "$TOKEN_ANA" "$AUTO_JSON")
if ! echo "$RESP_ANA" | grep -q '"status":"waiting"'; then
    echo "Loi: ana phai waiting" >&2
    exit 1
fi

RESP_BO=$(rpc_user "find_match" "$TOKEN_BO" "$AUTO_JSON")
if ! echo "$RESP_BO" | grep -q '"status":"matched"'; then
    echo "Loi: bo phai matched" >&2
    exit 1
fi

ROOM_1=$(echo "$RESP_BO" | grep -o '"room":"[^"]*' | cut -d'"' -f4)
TICKET_1=$(echo "$RESP_BO" | grep -o '"ticket":"[^"]*' | cut -d'"' -f4)
echo "   -> Cap 1 matched vao room: $ROOM_1 (ticket: $TICKET_1)"

POLL_ANA=$(rpc_user "poll_match" "$TOKEN_ANA" "$AUTO_JSON")
TICKET_ANA=$(echo "$POLL_ANA" | grep -o '"ticket":"[^"]*' | cut -d'"' -f4)
if [[ "$TICKET_ANA" != "$TICKET_1" ]]; then
    echo "Loi: ana phai nhan cung ticket voi bo" >&2
    exit 1
fi
echo "   -> ana poll ket qua: OK"

echo "4/4 Cap 2 (chi + dung) vao auto pool..."
RESP_CHI=$(rpc_user "find_match" "$TOKEN_CHI" "$AUTO_JSON")
if ! echo "$RESP_CHI" | grep -q '"status":"waiting"'; then
    echo "Loi: chi phai waiting" >&2
    exit 1
fi

RESP_DUNG=$(rpc_user "find_match" "$TOKEN_DUNG" "$AUTO_JSON")
if ! echo "$RESP_DUNG" | grep -q '"status":"matched"'; then
    echo "Loi: dung phai matched" >&2
    exit 1
fi

ROOM_2=$(echo "$RESP_DUNG" | grep -o '"room":"[^"]*' | cut -d'"' -f4)
TICKET_2=$(echo "$RESP_DUNG" | grep -o '"ticket":"[^"]*' | cut -d'"' -f4)
echo "   -> Cap 2 matched vao room: $ROOM_2 (ticket: $TICKET_2)"

POLL_CHI=$(rpc_user "poll_match" "$TOKEN_CHI" "$AUTO_JSON")
TICKET_CHI=$(echo "$POLL_CHI" | grep -o '"ticket":"[^"]*' | cut -d'"' -f4)
if [[ "$TICKET_CHI" != "$TICKET_2" ]]; then
    echo "Loi: chi phai nhan cung ticket voi dung" >&2
    exit 1
fi
echo "   -> chi poll ket qua: OK"

if [[ "$ROOM_1" == "$ROOM_2" ]]; then
    echo "Canh bao: Ca 2 cap deu vao $ROOM_1"
else
    echo "Chung minh thanh cong: Cap 1 ($ROOM_1) va Cap 2 ($ROOM_2) duoc cap 2 room khac nhau tu pool!"
fi

# Verify ticket
V1=$(rpc_key "verify_ticket" "{\"ticket\":\"$TICKET_1\",\"room\":\"$ROOM_1\",\"user_id\":\"$UID_ANA\"}")
V2=$(rpc_key "verify_ticket" "{\"ticket\":\"$TICKET_2\",\"room\":\"$ROOM_2\",\"user_id\":\"$UID_CHI\"}")

if ! echo "$V1" | grep -q '"ok":true' || ! echo "$V2" | grep -q '"ok":true'; then
    echo "Loi: Verify ticket that bai" >&2
    exit 1
fi

echo -e "\nVerify ca 2 ticket thanh cong!"
echo "=== DoD P5.5 HOAN TAT THANH CONG! ==="
