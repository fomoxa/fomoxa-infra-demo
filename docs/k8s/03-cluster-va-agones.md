# 03.3 Tạo cluster k3d, cài Agones, nạp image

Lên đầu tài liệu: [03-TRIEN-KHAI-K8S](../03-TRIEN-KHAI-K8S.md) · Trước: [02-build-image.md](02-build-image.md) · Tiếp: [04-secrets.md](04-secrets.md)

## Bước 5. Tạo cluster k3d

**Mục tiêu:** một cluster Kubernetes một node tên `fomoxa`, đã **đưa 4 cổng của máy bạn vào cluster**.

**Lệnh:**

```bash
k3d cluster create fomoxa \
    --port "7777:7777/udp@loadbalancer" \
    --port "9600:9600@loadbalancer" \
    --port "9643:9643@loadbalancer" \
    --port "7350:7350@loadbalancer" \
    --wait
```

**Ý nghĩa từng dòng `--port`** (định dạng `cổng-máy:cổng-trong-cluster[/giao-thức]@loadbalancer`):

| Tham số | Cổng của máy bạn → Service trong cluster | Ai dùng |
|---|---|---|
| `7777:7777/udp` | UDP 7777 → `gateway` | **client** (chơi game) |
| `9600:9600` | TCP 9600 → `backend` | **client** (đăng nhập, phòng) |
| `9643:9643` | TCP 9643 → `tls-proxy` | client, bản mã hoá TLS (tuỳ chọn) |
| `7350:7350` | TCP 7350 → `nakama` | **chỉ script test**, client thật không dùng |

- `@loadbalancer` nghĩa là qua container load balancer của k3d; nó chỉ chuyển cổng tới các `Service` kiểu `LoadBalancer` có đúng số cổng đó (đã khai báo sẵn trong manifest).
- `--wait` đợi cluster sẵn sàng rồi mới trả lệnh.
- **Phải khai báo cổng ngay lúc tạo.** k3d không thêm cổng cho cluster đã tạo; thiếu cổng thì phải xoá và tạo lại (`k3d cluster delete fomoxa`).

**Hoặc chạy gọn:** `./scripts/k8s-up.sh` (kiểm tra cổng trống rồi chạy đúng lệnh trên nếu cluster chưa có).

**Kết quả mong đợi (mẫu):**

```
INFO[0000] Prep: Network
INFO[0000] Created network 'k3d-fomoxa'
INFO[0000] Created image volume k3d-fomoxa-images
INFO[0001] Creating node 'k3d-fomoxa-server-0'
INFO[0001] Creating LoadBalancer 'k3d-fomoxa-serverlb'
INFO[0009] Cluster 'fomoxa' created successfully!
INFO[0009] You can now use it like this:
kubectl cluster-info
```

**Kiểm tra:**

```bash
kubectl config use-context k3d-fomoxa
kubectl get nodes
docker ps --format '{{.Names}}' | grep k3d
docker port k3d-fomoxa-serverlb
```

**Ý nghĩa:** `use-context` chọn cluster mà `kubectl` sẽ nói chuyện (một máy có thể có nhiều cluster - chọn nhầm context là lỗi rất hay gặp). `docker port` liệt kê cổng đã publish.

**Kết quả mong đợi (mẫu):**

```
NAME                  STATUS   ROLES                  AGE   VERSION
k3d-fomoxa-server-0   Ready    control-plane,master   30s   v1.30.4+k3s1
k3d-fomoxa-serverlb
k3d-fomoxa-server-0
7350/tcp -> 0.0.0.0:7350
7777/udp -> 0.0.0.0:7777
9600/tcp -> 0.0.0.0:9600
9643/tcp -> 0.0.0.0:9643
```

(Có thể còn thêm dòng `6443/tcp` - cổng API của Kubernetes.)

**Lỗi thường gặp:**

| Triệu chứng | Cách xử lý |
|---|---|
| `port is already allocated` | Bước 1 chưa dọn cổng. Tìm ai giữ: `docker ps \| grep 9600`, `ss -ltnp \| grep 9600` |
| Node `NotReady` rất lâu | thiếu RAM; xem `docker logs k3d-fomoxa-server-0 \| tail` |
| `kubectl` trỏ cluster khác | `kubectl config get-contexts`, rồi `kubectl config use-context k3d-fomoxa` |

---

## Bước 6. Cài Agones bằng Helm

**Mục tiêu:** cài **Agones** - bộ điều khiển quản lý vòng đời game server (tạo/giám sát/thay thế pod, trạng thái `Ready` → `Allocated` → `Shutdown`). Nó thêm các loại tài nguyên mới vào Kubernetes: `Fleet`, `GameServer`, `FleetAutoscaler`.

**Lệnh:**

```bash
helm repo add agones https://agones.dev/chart/stable --force-update
helm repo update agones

helm install agones agones/agones \
    --namespace agones-system \
    --create-namespace \
    --set "agones.allocator.install=false" \
    --set "agones.ping.install=false" \
    --wait --timeout 5m
```

**Ý nghĩa:**
- `helm repo add` đăng ký kho chart của Agones (như thêm PPA cho apt). URL đúng là `.../chart/stable`.
- `helm install <tên-release> <chart>` cài; `--namespace agones-system --create-namespace` đặt vào namespace riêng.
- `agones.allocator.install=false`: không cài allocator (là một Service `LoadBalancer` cổng 443, **trùng cổng Traefik của k3s** nên dễ làm `--wait` treo). Repo này không dùng `GameServerAllocation`.
- `agones.ping.install=false`: không cần dịch vụ ping.
- `--wait --timeout 5m` đợi mọi pod sẵn sàng (tối đa 5 phút).
- **Ghim phiên bản** (khuyến nghị cho môi trường ổn định): thêm `--version <x.y.z>` (xem `helm search repo agones/agones --versions | head`). Bản Agones phải hỗ trợ phiên bản Kubernetes của k3s (k3d 5.7.4 → k3s 1.30).

**Hoặc chạy gọn:** `./scripts/k8s-up.sh` chạy đúng các lệnh này (bỏ qua nếu `helm list -n agones-system` đã có `agones`); đặt `AGONES_VERSION=1.xx.y` để ghim.

**Kết quả mong đợi (mẫu):**

```
NAME: agones
LAST DEPLOYED: Sat Nov  1 10:12:03 2026
NAMESPACE: agones-system
STATUS: deployed
```

**Kiểm tra:**

```bash
kubectl get pods -n agones-system
kubectl get crd | grep agones.dev
```

```
NAME                                 READY   STATUS    RESTARTS   AGE   (mẫu)
agones-controller-6b9c7c6f5d-7xk2p   1/1     Running   0          2m
agones-controller-6b9c7c6f5d-q9wzl   1/1     Running   0          2m
agones-extensions-7d5b8c9d6-4hjfm    1/1     Running   0          2m
agones-extensions-7d5b8c9d6-ztk8s    1/1     Running   0          2m

fleetautoscalers.autoscaling.agones.dev   ...
fleets.agones.dev                         ...
gameservers.agones.dev                    ...
```

**Lỗi thường gặp:**

| Triệu chứng | Cách xử lý |
|---|---|
| `404 Not Found` khi `helm repo add` | dùng nhầm URL cũ `.../chart/helm`; đúng là `https://agones.dev/chart/stable` |
| `helm install ... timed out waiting for the condition` | pod Agones chưa lên (kéo image chậm). `kubectl get pods -n agones-system`, `kubectl describe pod ... -n agones-system`; chạy lại `helm uninstall agones -n agones-system` rồi cài lại |
| `release name "agones" in namespace ... exists` | đã cài rồi, bỏ qua bước này |
| CRD báo phiên bản Kubernetes không hỗ trợ | ghim `--version` cũ hơn cho khớp k3s |

---

## Bước 7. Import image vào k3d

**Mục tiêu:** cho cluster thấy 4 image vừa build.

**Vì sao cần:** k3s chạy bên trong container, có kho image **riêng**, không tự thấy image trong Docker của máy bạn. Manifest dùng `imagePullPolicy: IfNotPresent` nên nếu image đã nằm trong cluster thì dùng luôn, không cố kéo từ Internet (chúng không có trên Docker Hub).

**Lệnh:**

```bash
k3d image import \
    fomoxa-server:latest \
    fomoxa-directory:latest \
    fomoxa-gateway:latest \
    fomoxa-backend:latest \
    -c fomoxa
```

**Ý nghĩa:** đóng gói từng image thành file tar, nạp vào node `k3d-fomoxa-server-0`. `-c fomoxa` chọn cluster.

**Hoặc chạy gọn:** `./scripts/k8s-up.sh` (bước "4/6 Import images vào k3d cluster").

**Kết quả mong đợi (mẫu):**

```
INFO[0000] Importing image(s) into cluster 'fomoxa'
INFO[0006] Successfully imported 4 image(s) into 1 cluster(s)
```

**Kiểm tra:**

```bash
docker exec k3d-fomoxa-server-0 crictl images | grep fomoxa
```

Phải thấy 4 image `fomoxa-*`.

**Lỗi thường gặp:** `k3d image import` chạy rất lâu → bình thường với image vài trăm MB; đừng ngắt. Sau **mỗi lần build lại image** phải import lại *và* khởi động lại workload ([05, Cập nhật image](../ops/01-trang-thai-va-cap-nhat.md#cập-nhật-sau-khi-build-lại-image)).

---
