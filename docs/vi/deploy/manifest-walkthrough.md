# 03.9 Đọc các file manifest trong `deploy/k8s/`

Lên đầu tài liệu: [03-TRIEN-KHAI-K8S](README.md) · Trước: [08-test-client-va-don-dep.md](08-test-client-and-cleanup.md)

Phần này đi qua từng file YAML mà [bước 9](05-apply-manifests.md) áp dụng: nó tạo ra gì, trường nào quan trọng, sửa gì khi cần. Khái niệm nền xem [00](concepts.md).

Mười một file trong `deploy/k8s/`:

| File | Tạo ra | Áp dụng ở |
|---|---|---|
| `namespace.yaml` | namespace `fomoxa` | bước 8 |
| `postgres.yaml` | PVC, Deployment, Service `postgres` | bước 9.1 |
| `nakama.yaml` | ConfigMap `nakama-modules`, Deployment, Service `nakama`, `nakama-console` | bước 9.2 |
| `directory.yaml` (+ `directory.k8s.toml`) | ConfigMap `directory-config`, Deployment, Service `directory` | bước 9.3 |
| `gateway.yaml` (+ `gateway.k8s.toml`) | ConfigMap `gateway-config`, Deployment, Service `gateway` | bước 9.4 |
| `backend.yaml` | Deployment, Service `backend`, `backend-pods` | bước 9.5 |
| `tls-proxy.yaml` | ConfigMap, Deployment, Service `tls-proxy` | bước 9.6 |
| `fleet.yaml` | Fleet `fomoxa-server` | bước 9.7 |
| `fleet-autoscaler.yaml` | FleetAutoscaler (tuỳ chọn) | `AUTOSCALE=1` |
| `networkpolicy.yaml` | NetworkPolicy (tuỳ chọn) | `APPLY_NETPOL=1` |
| `ingress.yaml` | Ingress thử nghiệm (tuỳ chọn) | `APPLY_INGRESS=1` |

Các file `*.k8s.toml` là bản gốc của cấu hình nhúng trong ConfigMap; nếu bạn sửa một nơi, sửa cả nơi kia (script `k8s-up.sh` dùng bản trong YAML).

## Những mẫu lặp lại ở mọi file

Đọc kỹ một lần, các file sau chỉ khác chi tiết.

Khối nhận dạng. Mọi tài nguyên bắt đầu bằng:

```yaml
apiVersion: apps/v1     # nhóm API; Deployment thuộc apps/v1, Service thuộc v1
kind: Deployment        # loại tài nguyên
metadata:
  name: backend         # tên, duy nhất trong namespace
  namespace: fomoxa
```

Nhãn và selector. Deployment tìm pod của mình bằng nhãn, Service cũng vậy:

```yaml
selector:
  matchLabels: { app: backend }     # Deployment: "pod nào là của tôi"
template:
  metadata:
    labels: { app: backend }        # nhãn gắn lên pod
```

Selector của Service (`app: backend`) phải khớp nhãn pod, nếu không `kubectl get endpoints` sẽ rỗng và kết nối bị từ chối.

Tài nguyên. `requests` là mức đặt chỗ để xếp pod lên node; `limits` là mức trần. Vượt trần bộ nhớ thì container bị giết (`OOMKilled`); vượt trần CPU thì bị làm chậm.

Bảo mật container. `directory`, `gateway`, `backend`, `tls-proxy` và game server đều có (postgres và nakama thì không):

```yaml
securityContext:
  allowPrivilegeEscalation: false      # không leo quyền
  capabilities: { drop: ["ALL"] }      # bỏ mọi quyền Linux đặc biệt
  seccompProfile: { type: RuntimeDefault }   # lọc syscall theo mặc định của runtime
```

`imagePullPolicy: IfNotPresent`. Dùng image đã có trên node. Các image `fomoxa-*:latest` chỉ tồn tại vì bạn đã `k3d image import`; nếu để mặc định với tag `latest`, Kubernetes sẽ cố kéo từ Docker Hub và lỗi `ImagePullBackOff`.

Gắn secret và config. Luôn là cặp `volumes` (khai báo nguồn) và `volumeMounts` (đường dẫn trong container), `readOnly: true`.

## `namespace.yaml`

Năm dòng: tạo namespace `fomoxa`. Phải áp dụng trước mọi thứ khác vì các file còn lại đặt `namespace: fomoxa`.

## `postgres.yaml`

| Phần | Giá trị | Ý nghĩa |
|---|---|---|
| PVC `postgres-data` | `ReadWriteOnce`, 1 GiB | một ổ đĩa, một node gắn tại một thời điểm |
| image | `postgres:15` | bản chính thức |
| `POSTGRES_DB` | `nakama` | tạo sẵn database cho Nakama |
| `POSTGRES_PASSWORD` | từ secret `fomoxa-nakama`, khoá `db-password` | không nằm trong YAML |
| volumeMount | `/var/lib/postgresql/data` | nơi Postgres ghi dữ liệu |
| probes | `pg_isready -U postgres -d nakama` | ready sau 5 giây, live sau 15 giây |

Điều cần nhớ: Postgres chỉ đọc `POSTGRES_PASSWORD` lần đầu khi thư mục dữ liệu còn trống. Đổi secret sau này không đổi mật khẩu trong database đã tạo; xem [05 phần 3](../operations/03-tls-and-keys.md) cho cách đổi đúng.

## `nakama.yaml`

File dài nhất (475 dòng) vì chứa toàn bộ `main.lua` trong ConfigMap `nakama-modules`. Phần còn lại ngắn:

Hai bản của `main.lua`. Bản gốc nằm ở `deploy/nakama/modules/main.lua` (docker compose dùng), bản trong ConfigMap phải giống hệt. CI kiểm tra hai bản. Sửa Lua thì sửa gốc rồi chép vào YAML, rồi `kubectl apply` và `rollout restart deployment/nakama`.

initContainer `migrate`. Chạy `nakama migrate up` trước container chính để tạo/cập nhật bảng. Nếu Postgres chưa sẵn sàng, init container lặp lại cho đến khi qua; pod hiển thị `Init:0/1`, `Init:Error` hoặc `Init:CrashLoopBackOff` khi có lỗi (xem log: `kubectl logs <pod> -c migrate`).

Đối số dòng lệnh của Nakama. Khoá đi vào qua biến môi trường rồi được `$(TÊN)` thay vào đối số:

| Đối số | Nguồn |
|---|---|
| `--database.address` | `postgres:$(DB_PASSWORD)@postgres:5432/nakama` |
| `--socket.server_key` | secret `server-key` (client Unity dùng) |
| `--runtime.http_key` | secret `http-key` (backend gọi RPC dùng) |
| `--session.encryption_key`, `--session.refresh_encryption_key` | secret `session-key`, `refresh-key` |
| `--console.password` | secret `console-password` |
| `--session.token_expiry_sec` | 7200 (2 giờ) |
| `--metrics.prometheus_port` | 9100 |

Hai Service. `nakama` (7350, `LoadBalancer`) để máy bạn gọi API; `nakama-console` (7351, `ClusterIP`) chỉ để `port-forward`, vì bảng quản trị không nên lộ ra ngoài.

Đổi phiên bản Nakama. Tag `3.41.0` xuất hiện ở hai nơi (init container và container chính); phải đổi cả hai.

## `directory.yaml`

ConfigMap `directory-config` chứa `directory.toml`:

| Khoá | Giá trị | Ý nghĩa |
|---|---|---|
| `listen` | `0.0.0.0:9500` | cổng cho backend, gateway và pod kết nối vào |
| `admin_listen` | `0.0.0.0:9501` | cổng quản trị |
| `public_address` | `127.0.0.1:7777` | địa chỉ gateway mà client nhìn thấy, đưa vào ConnectToken |
| `grace_seconds` | 30 | thời gian chờ trước khi coi pod là mất |
| `report_interval_seconds` | 5 | chu kỳ pod báo cáo |
| `drain_seconds` | 300 | thời gian tối đa cho một pod drain |
| `ticket_seconds` | 30 | vé vào phòng hết hạn sau 30 giây |
| `[keys]` | năm khoá đọc từ `/secrets/directory/...` | `backend`, `pod`, `gateway`, `admin`, `root` |
| `[[placement]]` | ba luật chọn pod | cùng `version`; ưu tiên cùng `region`; xếp dồn (`packed`) |

`public_address` là giá trị bạn sẽ đổi khi đưa lên máy chủ thật (xem [05 phần 5](../operations/05-scaling-security-backup.md)). Với k3d trên máy bạn, `127.0.0.1:7777` đúng vì k3d map cổng đó ra máy.

Deployment: image `fomoxa-directory:latest`, đối số `/config/directory.toml`, hai cổng, ConfigMap và Secret `fomoxa-directory-keys` (đủ năm khoá) gắn read-only. Probe là `tcpSocket` cổng 9500.

## `gateway.yaml`

Gateway nhận UDP từ client và chuyển tiếp tới pod đã được chọn.

| Khoá `gateway.toml` | Ý nghĩa |
|---|---|
| `listen = "0.0.0.0:7777"` | UDP nhận từ client |
| `pod_bind = "0.0.0.0:0"` | cổng tạm để nói chuyện với pod |
| `directory = "directory.fomoxa.svc.cluster.local:9500"` | hỏi directory pod nào nhận vé |
| `retry_seconds = 1` | thử lại nếu chưa có |

Điểm bảo mật: secret `fomoxa-directory-keys` có năm khoá, nhưng gateway chỉ mount một (`items: - key: gateway`). Nếu gateway bị chiếm, kẻ tấn công không lấy được khoá `root` hay `admin`.

Service `gateway` kiểu `LoadBalancer`, UDP 7777. Đây là cổng duy nhất của game mà client chạm tới.

## `backend.yaml`

Cấu hình qua biến môi trường theo quy tắc .NET: dấu `__` thay cho cấp lồng, ví dụ `Fomoxa__Nakama__Url` ứng với `Fomoxa:Nakama:Url`.

| Biến | Giá trị | Ý nghĩa |
|---|---|---|
| `Fomoxa__Directory__Address` | `directory...:9500` | nơi đăng ký |
| `Fomoxa__Directory__Key__File` | `/secrets/backend/backend` | khoá backend↔directory |
| `Fomoxa__Pods__Key__File` | `/secrets/backend/backend-pod` | khoá backend↔pod |
| `Fomoxa__Nakama__Url` | `http://nakama...:7350` | gọi RPC Nakama |
| `Fomoxa__Nakama__ServerKey`, `HttpKey` | từ secret `fomoxa-nakama` | xác thực với Nakama |
| `Fomoxa__Players__Tcp` / `WebSocket` | `0.0.0.0:9600` / `9601` | cổng cho client |
| `Fomoxa__Pods__Tcp` | `0.0.0.0:9602` | cổng cho pod báo cáo |
| `Fomoxa__Metrics__Listen` | `0.0.0.0:9610` | `/metrics` cho Prometheus |

Hai Service: `backend` (`LoadBalancer`, 9600 và 9601) cho client; `backend-pods` (`ClusterIP`, 9602) chỉ cho pod. Tách ra để cổng 9602 không bao giờ ra ngoài.

Probe dùng `fomoxa-rpc-health.dll` mở kết nối fRPC tới `127.0.0.1:9600`, kiểm tra backend trả lời được RPC, chặt hơn việc chỉ xem cổng có mở không.

Annotation `prometheus.io/*` giúp Prometheus tự tìm cổng 9610 (xem [05 phần 4](../operations/04-monitoring.md)).

## `tls-proxy.yaml`

Xem toàn bộ đường đi từ client tới backend và nơi cấu hình chứng chỉ ở [Đường đi của lưu lượng và nơi cấu hình TLS](traffic-and-tls.md).

Nginx đóng vai ổ khoá phía trước backend. Client nối TLS vào 9643; nginx giải mã rồi chuyển byte thô sang `backend:9600`.

```
client ──TLS──► tls-proxy:9643 ──TCP thô──► backend:9600
```

Vì sao cần: fRPC của backend không tự mã hoá đoạn client-backend, mà mật khẩu đăng nhập đi qua đó. Hai điểm đáng chú ý trong `nginx.conf`:

- khối `stream { }` vì đây là TCP thô;
- `proxy_timeout 1h` giữ kết nối dài cho phiên chơi.

Chứng chỉ lấy từ secret `fomoxa-tls` (gắn tại `/tls`). Image `nginx-unprivileged` chạy không cần root, nên `pid` ghi vào `/tmp`.

## `fleet.yaml`

Fleet `fomoxa-server`, `replicas: 4`. Các trường quan trọng:

| Trường | Giá trị | Ý nghĩa |
|---|---|---|
| `ports[0]` | UDP 7778, `portPolicy: None` | cổng game của pod; `None` vì gateway nói chuyện qua IP pod, không cần map cổng ra node |
| `health` | delay 10, chu kỳ 5, ngưỡng 3 | Agones coi pod hỏng nếu ba lần kiểm tra liên tiếp thất bại |
| `terminationGracePeriodSeconds` | 360 | 6 phút cho drain trước khi bị giết cứng |
| `FOMOXA_DIRECTORY` | địa chỉ directory | pod đăng ký tại đây |
| `POD_IP`, `FOMOXA_POD_NAME` | từ `fieldRef` | Kubernetes điền IP và tên pod vào biến |
| `FOMOXA_AGONES` | `true` | bật gọi Agones SDK (`Ready`, `Allocated`, `Shutdown`) |
| `FOMOXA_CLAIM_ONE` | `true` | mỗi pod giữ một phòng |
| `FOMOXA_ROOM` | `arena:1` … `arena:16` | danh sách phòng có thể giữ; mỗi pod lấy một phòng còn trống |
| `FOMOXA_BACKEND` | `backend-pods...:9602` | nơi báo cáo số người, bảng điểm |
| `FOMOXA_METRICS` | `0.0.0.0:9611` | `/metrics` của pod |

Chuỗi `FOMOXA_ROOM` có 16 phòng vì `FleetAutoscaler` cho tối đa 16 pod: không đủ phòng thì pod thứ 17 không có phòng để giữ.

Fleet dùng hai secret: `fomoxa-pod-key` (pod↔directory) và `fomoxa-backend-pod-key` (pod↔backend).

## `fleet-autoscaler.yaml` (tuỳ chọn)

22 dòng: loại `Buffer` giữ 2 pod `Ready` dư, tối thiểu 4, tối đa 16. Cách tính và thử xem ở [05 phần 5](../operations/05-scaling-security-backup.md). Bật bằng `AUTOSCALE=1`.

## `networkpolicy.yaml` (tuỳ chọn)

132 dòng. Chính sách đầu tiên, `default-deny-ingress`, chặn mọi lưu lượng đi vào pod trong namespace; các chính sách sau mở từng đường cần thiết (ví dụ `postgres-from-nakama` chỉ cho pod `nakama` vào cổng 5432). Luồng được mở: client→gateway (UDP 7777)→pod (UDP 7778); client→backend (9600, 9601); backend→directory (9500) và nakama (7350); gateway và pod→directory (9500); pod→backend (9602).

k3s có sẵn bộ thực thi NetworkPolicy (kube-router). Nếu có gì không nối được, gỡ bằng `kubectl delete -f deploy/k8s/networkpolicy.yaml`. Bật bằng `APPLY_NETPOL=1`; bảng luồng chi tiết ở [05 phần 5](../operations/05-scaling-security-backup.md).

## `ingress.yaml` (tuỳ chọn, thử nghiệm)

Ingress cho lớp `traefik` (Traefik là bộ định tuyến HTTP đi kèm k3s), host `fomoxa.local`, đường `/ws` tới Service `backend` cổng 9601 (WebSocket). Giới hạn ghi ngay trong file:

- chưa có TLS;
- chỉ chuyển HTTP/WebSocket; cổng 9600 là fRPC trên TCP thô nên không đi qua Ingress được, client vẫn nối thẳng Service `backend`;
- không được `k8s-up.sh` áp dụng mặc định (cần `APPLY_INGRESS=1`).

## Tự kiểm tra manifest mà không cần cụm

```bash
kubectl apply --dry-run=client -f deploy/k8s/backend.yaml
```

`--dry-run=client` chỉ kiểm cú pháp ở phía máy bạn, không cần cụm. Dùng `--dry-run=server` (cần cụm) để API server kiểm tra luôn quy tắc hợp lệ. Với Fleet cần Agones đã cài.

Xem sự khác biệt trước khi áp dụng bản sửa:

```bash
kubectl diff -f deploy/k8s/directory.yaml
```

Dòng bắt đầu bằng `-` là hiện tại, `+` là sau khi áp dụng. Không có kết quả nghĩa là cụm đã khớp file.
