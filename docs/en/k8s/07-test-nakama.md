# 03.7 Test Nakama with curl

Top of this document: [03-DEPLOY-KUBERNETES](../03-DEPLOY-KUBERNETES.md) · Previous: [06-verify-cluster.md](06-verify-cluster.md) · Next: [08-test-client-and-cleanup.md](08-test-client-and-cleanup.md)

## Step 12. Test Nakama with `curl`

Goal: show that Nakama pairs two players into one room, issues a valid ticket, and rejects outsiders. This test was run for real, against the Nakama from `docker compose` that has the same `main.lua` module.

Get the cluster's Nakama keys:

```bash
BASE=http://127.0.0.1:7350
SERVER_KEY=$(kubectl get secret fomoxa-nakama -n fomoxa -o jsonpath='{.data.server-key}' | base64 -d)
HTTP_KEY=$(kubectl get secret fomoxa-nakama -n fomoxa -o jsonpath='{.data.http-key}' | base64 -d)
echo "server-key=${SERVER_KEY:0:6}...  http-key=${HTTP_KEY:0:6}..."
```

The `server-key` lets players log in to Nakama. The `http-key` is only for the backend's internal RPCs. `base64 -d` decodes the value stored in the secret. If you are testing the `docker compose` Nakama instead of the cluster, use `SERVER_KEY=defaultkey; HTTP_KEY=defaulthttpkey`.

### 12.1 Log in two players and get tokens

```bash
CREDS=$(printf '%s:' "$SERVER_KEY" | base64)
login() {
  curl -s -X POST "$BASE/v2/account/authenticate/device?create=true" \
       -H "Authorization: Basic $CREDS" -H 'Content-Type: application/json' \
       -d "{\"id\":\"$1\"}" | grep -o '"token":"[^"]*' | cut -d'"' -f4
}
TOKEN_A=$(login fomoxa-test-ana)
TOKEN_B=$(login fomoxa-test-bo)
echo "token A: ${TOKEN_A:0:20}...   token B: ${TOKEN_B:0:20}..."
```

`authenticate/device` creates the account if needed, tied to a device id, and returns a JWT `token`. The header `Authorization: Basic <base64(server_key:)>` is how Nakama authenticates the application.

### 12.2 Clear the queue, then both ask for `arena:1`

```bash
HTTP_CREDS=$(printf '%s:' "$HTTP_KEY" | base64)

# (backend only) clear the old queue, which needs the http_key
curl -s -X POST "$BASE/v2/rpc/reset_match?unwrap" -H "Authorization: Basic $HTTP_CREDS" \
     -H 'Content-Type: application/json' -d '{"room":"arena:1"}'; echo

# ana asks first: expect "waiting"
curl -s -X POST "$BASE/v2/rpc/find_match?unwrap" -H "Authorization: Bearer $TOKEN_A" \
     -H 'Content-Type: application/json' -d '{"room":"arena:1"}'; echo

# bo asks second: expect "matched" with a ticket
RESP_B=$(curl -s -X POST "$BASE/v2/rpc/find_match?unwrap" -H "Authorization: Bearer $TOKEN_B" \
     -H 'Content-Type: application/json' -d '{"room":"arena:1"}')
echo "$RESP_B"
TICKET=$(echo "$RESP_B" | grep -o '"ticket":"[^"]*' | cut -d'"' -f4)
```

Expected output (real):

```
{"ok":true}
{"room":"arena:1","status":"waiting"}
{"exp":1793533330,"room":"arena:1","status":"matched","ticket":"f32f69a2-72cc-4ecb-b7ee-5c23eb0fa788"}
```

The first player joins the queue (`waiting`). The second completes the pair, so Nakama creates a ticket (`matched`, valid until `exp`).

### 12.3 The backend verifies the ticket (and rejects an outsider)

```bash
# the user id is inside the token (the middle part of the JWT, base64url)
uid_of() { echo "$1" | cut -d. -f2 | tr '_-' '/+' | awk '{print $0 substr("====",1,(4-length($0)%4)%4)}' | base64 -d 2>/dev/null | grep -o '"uid":"[^"]*' | cut -d'"' -f4; }
UID_B=$(uid_of "$TOKEN_B")

# the right person: ok:true
curl -s -X POST "$BASE/v2/rpc/verify_ticket?unwrap" -H "Authorization: Basic $HTTP_CREDS" \
     -H 'Content-Type: application/json' \
     -d "{\"ticket\":\"$TICKET\",\"room\":\"arena:1\",\"user_id\":\"$UID_B\"}"; echo

# a stranger: rejected
curl -s -X POST "$BASE/v2/rpc/verify_ticket?unwrap" -H "Authorization: Basic $HTTP_CREDS" \
     -H 'Content-Type: application/json' \
     -d "{\"ticket\":\"$TICKET\",\"room\":\"arena:1\",\"user_id\":\"00000000-0000-0000-0000-000000000001\"}"; echo
```

Expected output (real):

```
{"exp":1793533330,"ok":true,"room":"arena:1"}
{"message":"ticket khong thuoc user nay","ok":false}
```

### 12.4 Players must not call internal RPCs

```bash
curl -s -X POST "$BASE/v2/rpc/verify_ticket?unwrap" -H "Authorization: Bearer $TOKEN_A" \
     -H 'Content-Type: application/json' -d '{}'; echo
```

Expected output (real):

```
{"code":7,"error":{},"message":"chi backend duoc goi RPC nay"}
```

Code 7 is `PERMISSION_DENIED`. The RPCs `verify_ticket`, `reset_match`, `list_rooms`, `kv_get` and `kv_put` can only be called by the backend, which holds the `http_key`.

Or run it in one go: `./scripts/test-nakama.sh` performs 12.1 to 12.3 and checks the results automatically. It reads the keys from the `fomoxa-nakama` secret when Nakama runs in k3d. `./scripts/test-nakama-pool.sh` sends four players to "auto", and they must land in two different rooms.

Expected output of `test-nakama.sh` (real, the end):

```
3) ana find_match...
   -> {"room":"arena:1","status":"waiting"}
4) bo find_match...
   -> {"exp":1793533330,"room":"arena:1","status":"matched","ticket":"f32f69a2-72cc-4ecb-b7ee-5c23eb0fa788"}
5) ana poll_match...
   -> {"exp":1793533330,"room":"arena:1","status":"matched","ticket":"f32f69a2-72cc-4ecb-b7ee-5c23eb0fa788"}
6) Backend verify_ticket...
   -> {"exp":1793533330,"ok":true,"room":"arena:1"}
7) Verify voi user khac (phai bi tu choi)...
   -> {"message":"ticket khong thuoc user nay","ok":false}

OK: 2 client ghep dung room, ticket dung, user ngoai bi chan!
```

Expected output of `test-nakama-pool.sh` (real, the end):

```
   -> Cap 1 matched vao room: arena:1 (ticket: e0d102e4-...)
   -> Cap 2 matched vao room: arena:2 (ticket: 324b9b2d-...)
Chung minh thanh cong: Cap 1 (arena:1) va Cap 2 (arena:2) duoc cap 2 room khac nhau tu pool!
=== DoD P5.5 HOAN TAT THANH CONG! ===
```

Common errors:

| Symptom | Fix |
|---|---|
| The script stops saying port 7350 points at a compose container | `docker compose` still holds 7350: `docker compose -f deploy/compose.yaml down`. To test compose on purpose: `ALLOW_COMPOSE=1 ./scripts/test-nakama.sh` |
| `401 Unauthorized` on `authenticate/device` | Wrong `SERVER_KEY` (`defaultkey` against a cluster with its own keys, or the reverse) |
| `lua runtime error` | The Lua module failed: `kubectl logs deploy/nakama -n fomoxa \| grep -i lua` |
| The second player stays `waiting` | The two players asked for different rooms, or an old queue entry remains (rerun `reset_match`) |
