# 03.4 Namespace và Secrets

Lên đầu tài liệu: [03-TRIEN-KHAI-K8S](README.md) · Trước: [03-cluster-va-agones.md](03-cluster-and-agones.md) · Tiếp: [05-apply-manifest.md](05-apply-manifests.md)

## Bước 8. Namespace và Secrets

**Mục tiêu:** tạo namespace `fomoxa` và nạp các khoá ở Bước 2 vào cluster dưới dạng **secret** (đối tượng lưu thông tin nhạy cảm; pod đọc nó qua biến môi trường hoặc file mount).

**Lệnh:**

```bash
# namespace = "ngăn" riêng cho toàn bộ ứng dụng
kubectl apply -f deploy/k8s/namespace.yaml

# Khoá directory: directory đọc cả 5 khoá; gateway chỉ được mount khoá 'gateway'
kubectl create secret generic fomoxa-directory-keys -n fomoxa \
    --from-file=backend=deploy/secrets/directory/backend \
    --from-file=pod=deploy/secrets/directory/pod \
    --from-file=gateway=deploy/secrets/directory/gateway \
    --from-file=admin=deploy/secrets/directory/admin \
    --from-file=root=deploy/secrets/directory/root \
    --dry-run=client -o yaml | kubectl apply -f -

# Backend: khoá nói chuyện với directory + khoá cho cổng pod
kubectl create secret generic fomoxa-backend-keys -n fomoxa \
    --from-file=backend=deploy/secrets/directory/backend \
    --from-file=backend-pod=deploy/secrets/backend-pod \
    --dry-run=client -o yaml | kubectl apply -f -

# Game server: khoá đăng ký phòng với directory
kubectl create secret generic fomoxa-pod-key -n fomoxa \
    --from-file=pod=deploy/secrets/directory/pod \
    --dry-run=client -o yaml | kubectl apply -f -

# Game server: khoá báo số người/bảng điểm cho backend (tách riêng, KHÔNG kèm khoá backend->directory)
kubectl create secret generic fomoxa-backend-pod-key -n fomoxa \
    --from-file=backend-pod=deploy/secrets/backend-pod \
    --dry-run=client -o yaml | kubectl apply -f -

# Nakama + Postgres
kubectl create secret generic fomoxa-nakama -n fomoxa \
    --from-file=server-key=deploy/secrets/nakama/server-key \
    --from-file=http-key=deploy/secrets/nakama/http-key \
    --from-file=console-password=deploy/secrets/nakama/console-password \
    --from-file=session-key=deploy/secrets/nakama/session-key \
    --from-file=refresh-key=deploy/secrets/nakama/refresh-key \
    --from-file=db-password=deploy/secrets/nakama/db-password \
    --dry-run=client -o yaml | kubectl apply -f -

# Chứng chỉ TLS cho tls-proxy
kubectl create secret tls fomoxa-tls -n fomoxa \
    --cert=deploy/secrets/tls/tls.crt --key=deploy/secrets/tls/tls.key \
    --dry-run=client -o yaml | kubectl apply -f -
```

**Ý nghĩa:**
- `--from-file=<tên>=<đường-dẫn>` đưa nội dung file vào secret với khoá `<tên>`; trong pod nó thành file `/secrets/.../<tên>`.
- `--dry-run=client -o yaml | kubectl apply -f -` là mẹo để lệnh **chạy lại được nhiều lần**: thay vì `create` (lỗi nếu đã tồn tại), sinh YAML rồi `apply` (tạo mới hoặc cập nhật).
- Mỗi dịch vụ chỉ nhận **đúng khoá nó cần** (nguyên tắc ít quyền nhất): ví dụ pod game không bao giờ thấy khoá `root` hay `admin`.

**Hoặc chạy gọn:** `./scripts/gen-k8s-secrets.sh` (nếu thiếu khoá thì tự gọi `gen-keys.sh`, thiếu chứng chỉ thì tự gọi `gen-tls-cert.sh`).

**Kết quả mong đợi (mẫu):**

```
namespace/fomoxa created
secret/fomoxa-directory-keys created
secret/fomoxa-backend-keys created
secret/fomoxa-pod-key created
secret/fomoxa-backend-pod-key created
secret/fomoxa-nakama created
secret/fomoxa-tls created
```

**Kiểm tra:**

```bash
kubectl get secrets -n fomoxa
kubectl get secret fomoxa-directory-keys -n fomoxa -o jsonpath='{.data}' | tr ',' '\n' | cut -d'"' -f2
```

Phải thấy 6 secret; secret directory liệt kê 5 khoá: `admin backend gateway pod root`.

**Lỗi thường gặp:** `no such file or directory: deploy/secrets/...` → chưa làm Bước 2 (hoặc đang đứng sai thư mục). `Error from server (NotFound): namespaces "fomoxa" not found` → chưa chạy dòng `kubectl apply -f deploy/k8s/namespace.yaml`.

---

### Secret nào đi vào workload nào

| Secret | Khoá | Ai mount | Đường dẫn trong container |
|---|---|---|---|
| `fomoxa-directory-keys` | `backend` `pod` `gateway` `admin` `root` | `directory` (cả 5) | `/secrets/directory/*` |
| `fomoxa-directory-keys` | chỉ `gateway` (qua `items`) | `gateway` | `/secrets/directory/gateway` |
| `fomoxa-backend-keys` | `backend` `backend-pod` | `backend` | `/secrets/backend/*` |
| `fomoxa-pod-key` | `pod` | game server | `/secrets/pod/pod` |
| `fomoxa-backend-pod-key` | `backend-pod` | game server | `/secrets/backend-pod/backend-pod` |
| `fomoxa-nakama` | `server-key` `http-key` `console-password` `session-key` `refresh-key` `db-password` | `nakama`, `postgres`, `backend` (qua biến môi trường) | biến môi trường, không phải file |
| `fomoxa-tls` | `tls.crt` `tls.key` | `tls-proxy` | `/tls/*` |

Bảng này là lý do pod game không thấy được khoá `root`: nó chỉ nhận `fomoxa-pod-key` và `fomoxa-backend-pod-key`.

### Xác minh secret được mount đúng (sau bước 9)

```bash
kubectl exec deploy/gateway -n fomoxa -- ls /secrets/directory
```

**Kết quả mong đợi (mẫu):** chỉ có `gateway`. Nếu thấy cả `root` và `admin` thì `items` trong `gateway.yaml` đã bị mất.

```bash
kubectl exec deploy/directory -n fomoxa -- ls /secrets/directory
```

**Kết quả mong đợi (mẫu):** `admin backend gateway pod root`. Gateway dùng image nhỏ có thể không có `ls`; khi đó lỗi `executable file not found` là bình thường, hãy dùng `kubectl describe pod -l app=gateway -n fomoxa` và đọc mục `Mounts`.

### Sửa secret rồi làm gì

Secret được gắn thành file thì kubelet cập nhật file sau vài chục giây, nhưng tiến trình **không tự đọc lại**. Secret đi vào qua biến môi trường (như `fomoxa-nakama`) thì chỉ có hiệu lực khi pod được tạo lại. Cả hai trường hợp đều cần:

```bash
kubectl rollout restart deployment/backend -n fomoxa
```

Game server thuộc Fleet: đổi secret xong, xoá và áp dụng lại Fleet (xem [05 phần 1](../operations/01-status-and-updates.md)).
