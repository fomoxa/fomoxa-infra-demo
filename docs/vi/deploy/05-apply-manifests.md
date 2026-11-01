# 03.5 Apply manifest theo thứ tự

Lên đầu tài liệu: [03-TRIEN-KHAI-K8S](README.md) · Trước: [04-secrets.md](04-secrets.md) · Tiếp: [06-kiem-tra-cum.md](06-verify-cluster.md)

## Bước 9. Apply manifest theo thứ tự

**Mục tiêu:** tạo các dịch vụ, mỗi dịch vụ đợi sẵn sàng rồi mới sang dịch vụ tiếp theo (vì có phụ thuộc: Nakama cần Postgres; backend cần directory và Nakama; game server cần directory và backend).

`kubectl apply -f <file>` đọc YAML mô tả "trạng thái mong muốn" và cập nhật cluster cho khớp; chạy lại nhiều lần an toàn.

### 9.1. Postgres (cơ sở dữ liệu của Nakama)

```bash
kubectl apply -f deploy/k8s/postgres.yaml
kubectl rollout status deployment/postgres -n fomoxa --timeout=120s
kubectl wait --for=condition=Ready pod -l app=postgres -n fomoxa --timeout=120s
```

**Ý nghĩa:** tạo PersistentVolumeClaim (ổ đĩa 1 GiB), Deployment và Service `postgres:5432`. `rollout status` đợi bản triển khai xong; `kubectl wait --for=condition=Ready` đợi pod báo sẵn sàng (readiness probe `pg_isready`).

**Kết quả mong đợi (mẫu):**

```
persistentvolumeclaim/postgres-data created
deployment.apps/postgres created
service/postgres created
deployment "postgres" successfully rolled out
pod/postgres-6d8f9c7b5-x2k4m condition met
```

### 9.2. Nakama (ghép trận + kho tài khoản)

```bash
kubectl apply -f deploy/k8s/nakama.yaml
kubectl rollout status deployment/nakama -n fomoxa --timeout=180s
kubectl wait --for=condition=Ready pod -l app=nakama -n fomoxa --timeout=180s
```

**Ý nghĩa:** `nakama.yaml` gồm ConfigMap `nakama-modules` (chứa `main.lua` - logic ghép trận và kho key-value), Deployment và hai Service: `nakama` (API 7350, kiểu `LoadBalancer`) và `nakama-console` (7351, **chỉ trong cluster**).
Pod chạy một *initContainer* `migrate` (tạo bảng trong Postgres) trước rồi mới tới container chính. Khoá lấy từ secret `fomoxa-nakama`.

**Kiểm tra:** `kubectl logs deploy/nakama -n fomoxa | grep -i "fomoxa match"` → dòng `fomoxa match module da nap (auto pool ho tro)` (module Lua đã nạp).

### 9.3. Directory

```bash
kubectl apply -f deploy/k8s/directory.yaml
kubectl rollout status deployment/directory -n fomoxa --timeout=120s
```

**Ý nghĩa:** ConfigMap `directory-config` chứa `directory.toml`, **trong đó có `public_address = "127.0.0.1:7777"`** - địa chỉ gateway mà *client* thấy (xem [08, mục cấu hình](../reference.md#2-cấu-hình-directory-và-gateway)).
Service `directory` mở 9500 (pod/backend/gateway nối vào) và 9501 (admin).

### 9.4. Gateway

```bash
kubectl apply -f deploy/k8s/gateway.yaml
kubectl rollout status deployment/gateway -n fomoxa --timeout=120s
```

**Ý nghĩa:** gateway nghe **UDP 7777** (Service `LoadBalancer`), hỏi directory pod nào giữ phòng nào rồi chuyển tiếp gói tin. Nó chỉ mount khoá `gateway` (không thấy khoá `root`/`admin`).

### 9.5. Backend

```bash
kubectl apply -f deploy/k8s/backend.yaml
kubectl rollout status deployment/backend -n fomoxa --timeout=120s
```

**Ý nghĩa:** backend nghe TCP 9600 (người chơi) và 9601 (WebSocket) qua Service `LoadBalancer`; cổng **9602 (pod báo cáo)** chỉ qua Service `backend-pods` (ClusterIP) nên không lộ ra ngoài. Nó nối Nakama (khoá từ secret) và directory.
Probe sức khoẻ chạy công cụ `fomoxa-rpc-health` trong container.

### 9.6. Proxy TLS (cổng 9643)

```bash
kubectl apply -f deploy/k8s/tls-proxy.yaml
kubectl rollout status deployment/tls-proxy -n fomoxa --timeout=120s
```

**Ý nghĩa:** nginx giải mã TLS ở cổng 9643 (chứng chỉ từ secret `fomoxa-tls`) rồi chuyển byte thô tới `backend:9600`. Đây là lớp bảo vệ mật khẩu đăng nhập khi đi qua mạng.

### 9.7. Fleet game server (Agones)

```bash
kubectl apply -f deploy/k8s/fleet.yaml
kubectl wait --for=jsonpath='{.status.state}'=Ready gs -l agones.dev/fleet=fomoxa-server -n fomoxa --timeout=300s
```

**Ý nghĩa:** `Fleet` là "bể" game server; `replicas: 4` → Agones tạo 4 `GameServer`, mỗi cái là một pod có **2 container** (`fomoxa-server` và sidecar của Agones). Mỗi pod đăng ký với directory, giữ **một** phòng trong `arena:1..16`.
Với GameServer không dùng `--for=condition=Ready` mà dùng `--for=jsonpath='{.status.state}'=Ready` vì Agones lưu trạng thái ở `.status.state`, không dùng `conditions`.

**Kết quả mong đợi (mẫu):**

```
fleet.agones.dev/fomoxa-server created
gameserver.agones.dev/fomoxa-server-hl9fz-2xkpq condition met
...(4 dòng)
```

**Hoặc chạy gọn cho cả Bước 8-9:** `./scripts/k8s-up.sh` (sau khi cluster, Agones và image đã sẵn sàng nó apply đúng thứ tự trên rồi `rollout status` từng deployment).

**Lỗi thường gặp (chung Bước 9):**

| Triệu chứng | Cách xử lý |
|---|---|
| Pod `ImagePullBackOff` / `ErrImagePull` | image chưa import vào k3d (Bước 7). `kubectl describe pod <tên> -n fomoxa` |
| Pod `CreateContainerConfigError` | thiếu secret (Bước 8): `kubectl describe pod ...` ghi rõ tên secret thiếu |
| Pod `CrashLoopBackOff` | `kubectl logs <pod> -n fomoxa --previous` xem lỗi (thường là khoá sai/thiếu, hoặc dịch vụ phụ thuộc chưa lên) |
| Nakama `Init:Error` | initContainer `migrate` không nối được Postgres: `kubectl logs deploy/nakama -n fomoxa -c migrate`; thường do mật khẩu DB lệch với dữ liệu cũ trong volume ([07](../troubleshooting.md#đổi-mật-khẩu-db-làm-nakama-không-khởi-động)) |
| GameServer mãi `Starting` | `kubectl logs <gs-pod> -n fomoxa -c fomoxa-server`; thường pod không nối được directory (9500) |

---
