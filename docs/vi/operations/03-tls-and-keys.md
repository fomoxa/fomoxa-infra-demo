# 05.3 TLS và khoá Nakama

Lên đầu tài liệu: [05-VAN-HANH](README.md) · Trước: [02-drain-va-tai-khoan.md](02-drain-and-accounts.md) · Tiếp: [04-giam-sat.md](04-monitoring.md)

## TLS giữa client và backend

fRPC của backend **không tự mã hoá**, mà mật khẩu đăng nhập đi qua đường này, nên môi trường thật phải đi qua **proxy TLS** (`tls-proxy`, nginx, cổng 9643). Đường UDP game đã được mã hoá bằng ticket của Fomoxa, không cần thêm.

### 8.1. Kiểm tra chứng chỉ proxy đang trình ra

```bash
# Vân tay chứng chỉ mà server TRÌNH RA (so với "pin" trong client)
openssl s_client -connect 127.0.0.1:9643 -servername fomoxa.local </dev/null 2>/dev/null \
    | openssl x509 -noout -fingerprint -sha256 -subject
```

**Kết quả mong đợi (thật, chạy với đầu cuối TLS của cụm giả):**

```
sha256 Fingerprint=C5:B5:02:A6:03:68:15:A3:9E:6E:60:AC:21:39:86:51:BF:FB:9A:D4:FF:19:BD:D5:C6:39:7D:9F:F7:4C:51:67
subject=CN = fomoxa-mock
```

Bỏ dấu `:` là ra chính xác chuỗi `-tlspin:` của client (`C5B502A6...5167`).

### 8.2. Đổi chứng chỉ (tạo mới, nạp vào cluster, restart proxy)

```bash
./scripts/gen-tls-cert.sh -f                     # tạo lại tls.crt/tls.key và in pin MỚI
kubectl create secret tls fomoxa-tls -n fomoxa \
    --cert=deploy/secrets/tls/tls.crt --key=deploy/secrets/tls/tls.key \
    --dry-run=client -o yaml | kubectl apply -f -
kubectl rollout restart deployment/tls-proxy -n fomoxa
```

Sau đó **cập nhật pin trong mọi client** (`-tlspin:` hoặc ô *Tls Pin Sha256* của `RoomJoiner`), nếu không client sẽ từ chối với "vân tay chứng chỉ không khớp".

### 8.3. Chỉ cho phép đường mã hoá

```bash
kubectl patch svc backend -n fomoxa -p '{"spec":{"type":"ClusterIP"}}'     # đóng 9600 ra ngoài
# (hoặc chạy lần đầu: TLS_ONLY=1 ./scripts/k8s-up.sh)
```

Sau đó client **bắt buộc** dùng `-backend:127.0.0.1:9643 -tlspin:...`. Nhớ rằng cụm tạo bằng k3d vẫn map cổng 9600 vào máy (Service không còn LoadBalancer nên không có gì sau cổng đó).
Hoàn tác: `kubectl apply -f deploy/k8s/backend.yaml`.

### 8.4. Môi trường thật (có tên miền)

Dùng chứng chỉ do CA công cộng cấp (ví dụ **cert-manager** + Let's Encrypt) cho secret `fomoxa-tls`; khi đó **không cần pin** (client dùng bộ gốc Mozilla có sẵn: chỉ truyền `-tls`).
Đặt tên miền trỏ vào IP của Service `tls-proxy`. Chứng chỉ phải có đúng tên miền trong SAN.

---

## 9. Khoá và mật khẩu Nakama

Cụm K8s **không còn dùng** `defaultkey`, `defaulthttpkey`, `admin:admin`, mật khẩu DB `localdb`. Chúng nằm trong secret `fomoxa-nakama`; `nakama`, `backend` và `postgres` đọc từ đó.
(`deploy/compose.yaml` — chỉ để dev local — vẫn giữ giá trị mặc định.)

**Xem khoá hiện tại:**

```bash
for k in server-key http-key console-password; do
  printf '%-18s ' "$k"; kubectl get secret fomoxa-nakama -n fomoxa -o jsonpath="{.data.$k}" | base64 -d; echo
done
```

**Xoay vòng (đổi) khoá Nakama**, **không** gồm mật khẩu DB:

```bash
openssl rand -hex 24 | tr -d '\n' > deploy/secrets/nakama/http-key       # ví dụ đổi http-key
kubectl create secret generic fomoxa-nakama -n fomoxa \
    --from-file=server-key=deploy/secrets/nakama/server-key \
    --from-file=http-key=deploy/secrets/nakama/http-key \
    --from-file=console-password=deploy/secrets/nakama/console-password \
    --from-file=session-key=deploy/secrets/nakama/session-key \
    --from-file=refresh-key=deploy/secrets/nakama/refresh-key \
    --from-file=db-password=deploy/secrets/nakama/db-password \
    --dry-run=client -o yaml | kubectl apply -f -
kubectl rollout restart deployment/nakama deployment/backend -n fomoxa
```

**Lưu ý quan trọng:**
- Đổi `session-key`/`refresh-key` làm mọi phiên đăng nhập hiện có mất hiệu lực (người chơi đăng nhập lại).
- **Đổi `db-password` làm Nakama không khởi động được** trên volume Postgres đã có dữ liệu (mật khẩu được lưu trong chính dữ liệu Postgres). Muốn đổi, hoặc `ALTER USER postgres PASSWORD '...'` trong Postgres trước rồi mới đổi secret, hoặc chấp nhận xoá dữ liệu (`kubectl delete pvc postgres-data -n fomoxa`).
- Các script `test-nakama*.sh` tự đọc khoá từ secret. Muốn ép khoá mặc định (khi test Nakama của `docker compose`): `NAKAMA_USE_DEFAULT_KEYS=1 ./scripts/test-nakama.sh`.

---
