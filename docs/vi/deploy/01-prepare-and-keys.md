# 03.1 Chuẩn bị, cổng trống, khoá và chứng chỉ

Lên đầu tài liệu: [03-TRIEN-KHAI-K8S](README.md) · Tiếp: [02-build-image.md](02-build-images.md)

## Bước 1. Chuẩn bị và kiểm tra cổng trống

**Mục tiêu:** đứng đúng thư mục và chắc chắn các cổng mà cluster sắp chiếm chưa bị ai giữ.

**Lệnh:**

```bash
cd ~/fomoxa-infra-demo

# Docker đang chạy?
docker info --format 'Docker {{.ServerVersion}} - OK'

# Có container nào đang giữ cổng cần dùng không? (compose Nakama, cụm cũ, ...)
docker ps --format '{{.Names}}\t{{.Ports}}' | grep -E ':(7777|9600|9643|7350)->' || echo "Các cổng cần dùng đang trống"

# Có tiến trình ngoài Docker nào nghe các cổng đó không? (cụm giả ở tài liệu 02, ...)
ss -ltnu | grep -E ':(7777|9600|9643|7350)\b' || echo "Không có tiến trình nào nghe"
```

**Ý nghĩa:** cluster k3d đưa 4 cổng của máy bạn vào cluster; nếu cổng đã bị giữ, `k3d cluster create` sẽ thất bại giữa chừng.

**Kết quả mong đợi (mẫu):**

```
Docker 27.3.1 - OK
Các cổng cần dùng đang trống
Không có tiến trình nào nghe
```

**Lỗi thường gặp:**

| Triệu chứng | Cách xử lý |
|---|---|
| Thấy `fomoxa-test-nakama-1` giữ `7350` | đó là `docker compose` của dev local: `docker compose -f deploy/compose.yaml down`. **Nếu không tắt**, các script test sẽ test nhầm Nakama của compose và báo thành công giả |
| Cụm giả (tài liệu 02) đang chạy | tắt nó (Ctrl+C) - nó cũng dùng 9600/9643 |
| Docker không chạy | [01, Bước 3](../getting-started/prepare-environment.md#bước-3-cài-docker) |

---

## Bước 2. Sinh khoá và chứng chỉ (secrets)

**Mục tiêu:** tạo các khoá ngẫu nhiên mà các dịch vụ dùng để xác thực lẫn nhau, mật khẩu/khoá riêng cho Nakama và chứng chỉ TLS. Tất cả nằm trong `deploy/secrets/` (đã được `.gitignore`, **không bao giờ commit**).

**Vì sao cần:** directory chỉ nhận backend/pod/gateway nào xuất trình đúng khoá; backend chỉ nhận pod có khoá riêng; Nakama dùng khoá riêng thay vì `defaultkey`.

**Lệnh:**

```bash
mkdir -p deploy/secrets/directory deploy/secrets/nakama deploy/secrets/tls

# gen <file> <số byte>: sinh <số byte> ngẫu nhiên, ghi dạng hex (độ dài chữ = 2 x số byte), không xuống dòng
gen() { openssl rand -hex "$2" | tr -d '\n' > "$1"; echo "  + $1 ($(wc -c < "$1") ký tự)"; }

# --- Khoá cho directory và các bên nối vào nó ---
gen deploy/secrets/directory/root     32   # khoá gốc của directory (BẮT BUỘC đúng 64 ký tự hex)
gen deploy/secrets/directory/admin    24   # quyền quản trị (drain, đọc danh sách pod)
gen deploy/secrets/directory/backend  24   # backend xin cấp ticket
gen deploy/secrets/directory/gateway  24   # gateway đọc danh sách pod
gen deploy/secrets/directory/pod      24   # game server đăng ký phòng
gen deploy/secrets/backend-pod        24   # game server báo số người + bảng điểm cho backend (cổng 9602)

# --- Nakama: khoá/mật khẩu riêng (thay cho defaultkey, defaulthttpkey, admin:admin, localdb) ---
gen deploy/secrets/nakama/server-key       16   # client/backend đăng nhập Nakama
gen deploy/secrets/nakama/http-key         24   # chỉ backend gọi RPC nội bộ (kv_get, verify_ticket...)
gen deploy/secrets/nakama/console-password 16   # mật khẩu console quản trị Nakama
gen deploy/secrets/nakama/session-key      24   # mã hoá session token
gen deploy/secrets/nakama/refresh-key      24   # mã hoá refresh token
gen deploy/secrets/nakama/db-password      16   # mật khẩu Postgres

# --- Chứng chỉ TLS tự ký cho proxy TLS (cổng 9643) ---
openssl req -x509 -newkey rsa:2048 -nodes -days 730 \
    -keyout deploy/secrets/tls/tls.key -out deploy/secrets/tls/tls.crt \
    -subj "/CN=fomoxa.local" \
    -addext "subjectAltName=DNS:fomoxa.local,DNS:localhost,IP:127.0.0.1" \
    -addext "extendedKeyUsage=serverAuth" 2>/dev/null
chmod 600 deploy/secrets/tls/tls.key

# Vân tay SHA-256 của chứng chỉ ("pin") - client dùng để chắc chắn đang nói chuyện với đúng proxy
openssl x509 -in deploy/secrets/tls/tls.crt -outform DER | openssl dgst -sha256 -hex | awk '{print toupper($NF)}'
```

**Ý nghĩa từng phần:**
- `openssl rand -hex N` in N byte ngẫu nhiên thành 2N ký tự hex. `tr -d '\n'` bỏ ký tự xuống dòng (khoá phải không có dòng thừa).
- Chứng chỉ tự ký chỉ để dev/nội bộ. Môi trường thật nên dùng chứng chỉ do CA cấp (xem [05, mục TLS](../operations/03-tls-and-keys.md#tls-giữa-client-và-backend)).

**Hoặc chạy gọn:** `./scripts/gen-keys.sh` (sinh 12 khoá ở trên) và `./scripts/gen-tls-cert.sh` (sinh chứng chỉ + in pin). Cả hai **không ghi đè** file đã có (thêm `-f` để ép tạo lại).

**Kết quả mong đợi (thật, từ `gen-keys.sh` và `gen-tls-cert.sh`):**

```
=== Sinh keys cho Fomoxa ===
  + directory/root
  + directory/admin
  + directory/backend
  + directory/gateway
  + directory/pod
  + secrets/backend-pod
  + nakama/server-key
  + nakama/http-key
  + nakama/console-password
  + nakama/session-key
  + nakama/refresh-key
  + nakama/db-password
Hoan tat sinh keys tai /home/you/fomoxa-infra-demo/deploy/secrets
Da tao /home/you/fomoxa-infra-demo/deploy/secrets/tls/tls.crt va tls.key (CN=fomoxa.local)
Van tay SHA-256 (pin): FB3FF3780A89AD4A3CB83402C454F0247AA0238C1B304EA8DB475497B73C94DE
```

**Kiểm tra:**

```bash
find deploy/secrets -type f | sort
wc -c deploy/secrets/directory/root deploy/secrets/directory/backend deploy/secrets/nakama/http-key
```

Phải thấy 14 file (12 khoá + `tls.crt` + `tls.key`); `root` đúng **64** byte, `backend` **48**, `http-key` **48**.

**Lỗi thường gặp:**

| Triệu chứng | Cách xử lý |
|---|---|
| `openssl: command not found` | `sudo apt install -y openssl` |
| Khoá `root` không đúng 64 ký tự | tạo lại bằng `gen deploy/secrets/directory/root 32` - directory từ chối khoá gốc sai độ dài |
| Đã có cluster cũ chạy với khoá cũ mà bạn sinh lại khoá | secret trong cluster vẫn là khoá cũ → lệch. Sau khi sinh lại phải làm lại [Bước 8](04-secrets.md#bước-8-namespace-và-secrets) và restart các deployment |

---
