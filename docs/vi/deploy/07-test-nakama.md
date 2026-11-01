# 03.7 Test Nakama bằng curl

Lên đầu tài liệu: [03-TRIEN-KHAI-K8S](README.md) · Trước: [06-kiem-tra-cum.md](06-verify-cluster.md) · Tiếp: [08-test-client-va-don-dep.md](08-test-client-and-cleanup.md)

## Bước 12. Test Nakama bằng `curl`

Mục tiêu: chứng minh Nakama ghép đúng hai người vào cùng một phòng, phát ticket hợp lệ, và chặn người ngoài. Đây là bài test đã chạy thật (trên Nakama của docker compose có cùng module `main.lua`).

Lấy khoá Nakama của cụm:

```bash
BASE=http://127.0.0.1:7350
SERVER_KEY=$(kubectl get secret fomoxa-nakama -n fomoxa -o jsonpath='{.data.server-key}' | base64 -d)
HTTP_KEY=$(kubectl get secret fomoxa-nakama -n fomoxa -o jsonpath='{.data.http-key}' | base64 -d)
echo "server-key=${SERVER_KEY:0:6}...  http-key=${HTTP_KEY:0:6}..."
```

Ý nghĩa: `server-key` dùng để người chơi đăng nhập Nakama; `http-key` chỉ dành cho backend gọi RPC nội bộ. Lệnh `base64 -d` giải mã giá trị trong secret.
(Nếu bạn test Nakama của `docker compose` thay vì cụm: `SERVER_KEY=defaultkey; HTTP_KEY=defaulthttpkey`.)

### 12.1 Đăng nhập hai người chơi (device) và lấy token

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

Ý nghĩa: `authenticate/device` tạo (nếu chưa có) tài khoản gắn với "device id" và trả về JWT `token`. Header `Authorization: Basic <base64(server_key:)>` là cách Nakama xác thực *ứng dụng*.

### 12.2 Làm sạch hàng chờ, rồi hai người xin vào `arena:1`

```bash
HTTP_CREDS=$(printf '%s:' "$HTTP_KEY" | base64)

# (backend-only) xoá hàng chờ cũ - cần http_key
curl -s -X POST "$BASE/v2/rpc/reset_match?unwrap" -H "Authorization: Basic $HTTP_CREDS" \
     -H 'Content-Type: application/json' -d '{"room":"arena:1"}'; echo

# ana xin trước -> phải "waiting"
curl -s -X POST "$BASE/v2/rpc/find_match?unwrap" -H "Authorization: Bearer $TOKEN_A" \
     -H 'Content-Type: application/json' -d '{"room":"arena:1"}'; echo

# bo xin sau -> phải "matched" kèm ticket
RESP_B=$(curl -s -X POST "$BASE/v2/rpc/find_match?unwrap" -H "Authorization: Bearer $TOKEN_B" \
     -H 'Content-Type: application/json' -d '{"room":"arena:1"}')
echo "$RESP_B"
TICKET=$(echo "$RESP_B" | grep -o '"ticket":"[^"]*' | cut -d'"' -f4)
```

Kết quả mong đợi (thật):

```
{"ok":true}
{"room":"arena:1","status":"waiting"}
{"exp":1793533330,"room":"arena:1","status":"matched","ticket":"f32f69a2-72cc-4ecb-b7ee-5c23eb0fa788"}
```

Ý nghĩa: người đầu tiên vào hàng chờ (`waiting`); người thứ hai làm đủ cặp nên Nakama tạo ticket (`matched`, hết hạn sau `exp`).

### 12.3 Backend xác minh ticket (và từ chối người ngoài)

```bash
# user id của ana nằm trong token (phần giữa của JWT, base64url)
uid_of() { echo "$1" | cut -d. -f2 | tr '_-' '/+' | awk '{print $0 substr("====",1,(4-length($0)%4)%4)}' | base64 -d 2>/dev/null | grep -o '"uid":"[^"]*' | cut -d'"' -f4; }
UID_B=$(uid_of "$TOKEN_B")

# đúng người -> ok:true
curl -s -X POST "$BASE/v2/rpc/verify_ticket?unwrap" -H "Authorization: Basic $HTTP_CREDS" \
     -H 'Content-Type: application/json' \
     -d "{\"ticket\":\"$TICKET\",\"room\":\"arena:1\",\"user_id\":\"$UID_B\"}"; echo

# người lạ -> bị từ chối
curl -s -X POST "$BASE/v2/rpc/verify_ticket?unwrap" -H "Authorization: Basic $HTTP_CREDS" \
     -H 'Content-Type: application/json' \
     -d "{\"ticket\":\"$TICKET\",\"room\":\"arena:1\",\"user_id\":\"00000000-0000-0000-0000-000000000001\"}"; echo
```

Kết quả mong đợi (thật):

```
{"exp":1793533330,"ok":true,"room":"arena:1"}
{"message":"ticket khong thuoc user nay","ok":false}
```

### 12.4 Người chơi KHÔNG được gọi RPC nội bộ

```bash
curl -s -X POST "$BASE/v2/rpc/verify_ticket?unwrap" -H "Authorization: Bearer $TOKEN_A" \
     -H 'Content-Type: application/json' -d '{}'; echo
```

Kết quả mong đợi (thật):

```
{"code":7,"error":{},"message":"chi backend duoc goi RPC nay"}
```

`code 7` = `PERMISSION_DENIED`. Các RPC `verify_ticket`, `reset_match`, `list_rooms`, `kv_get`, `kv_put` chỉ backend (có `http_key`) gọi được.

Hoặc chạy gọn: `./scripts/test-nakama.sh` làm đúng các bước 12.1-12.3 và kiểm tra tự động; script tự đọc khoá từ secret `fomoxa-nakama` khi Nakama chạy trong k3d.
`./scripts/test-nakama-pool.sh` thử 4 người chơi vào "auto" (phải ra 2 phòng khác nhau).

Kết quả mong đợi của `test-nakama.sh` (thật, đoạn cuối):

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

Kết quả mong đợi của `test-nakama-pool.sh` (thật, đoạn cuối):

```
   -> Cap 1 matched vao room: arena:1 (ticket: e0d102e4-...)
   -> Cap 2 matched vao room: arena:2 (ticket: 324b9b2d-...)
Chung minh thanh cong: Cap 1 (arena:1) va Cap 2 (arena:2) duoc cap 2 room khac nhau tu pool!
=== DoD P5.5 HOAN TAT THANH CONG! ===
```

Lỗi thường gặp:

| Triệu chứng | Cách xử lý |
|---|---|
| Script dừng với "cổng 7350 đang trỏ tới container ... compose" | còn `docker compose` đang giữ 7350: `docker compose -f deploy/compose.yaml down`. Muốn cố ý test compose: `ALLOW_COMPOSE=1 ./scripts/test-nakama.sh` |
| `401 Unauthorized` khi `authenticate/device` | sai `SERVER_KEY` (dùng `defaultkey` cho cụm đã đổi khoá, hoặc ngược lại) |
| `lua runtime error` | module Lua lỗi: `kubectl logs deploy/nakama -n fomoxa \| grep -i lua` |
| Người thứ hai vẫn `waiting` | hai người xin hai phòng khác nhau, hoặc hàng chờ cũ chưa xoá (chạy lại `reset_match`) |

