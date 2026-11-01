# 03 - Triển khai cụm Kubernetes (k3d + Agones) từng bước

Tài liệu này dựng **cụm thật** trên máy bạn: Postgres, Nakama, directory, gateway, backend và Fleet game server của Agones, chạy trong k3d.
Làm tay từng bước để hiểu; cuối tài liệu có đường tắt **một lệnh** (`./scripts/k8s-up.sh --build`) và bảng cho biết script đó đã làm những bước nào.

> **Điều kiện:** đã xong [01 - Chuẩn bị môi trường](01-CHUAN-BI-MOI-TRUONG.md) (`docker`, `kubectl`, `k3d`, `helm`, `openssl`, `git`).
> **Thời gian:** lần đầu 30-60 phút, phần lớn là build image Rust (directory, gateway) và kéo image.
> **Chưa kiểm chứng:** các bước 4 (build image Rust), 5-11 (k3d/Agones/manifest) chưa được chạy lại khi viết tài liệu; lệnh lấy nguyên từ `scripts/k8s-up.sh`,
> `scripts/build-images.sh`, `scripts/gen-*.sh` và manifest. Output ghi *(mẫu)*. Các bước 2 và 12 đã chạy thật.

## Bản đồ các bước

| # | Việc | Kết quả |
|---|---|---|
| 1 | Chuẩn bị, kiểm tra cổng trống | máy sẵn sàng |
| 2 | Sinh khoá và chứng chỉ | thư mục `deploy/secrets/` |
| 3 | Lấy mã nguồn `gateway` (Rust) | thư mục `gateway/` |
| 4 | Build 4 Docker image | `fomoxa-{server,directory,gateway,backend}:latest` |
| 5 | Tạo cluster k3d | cluster `fomoxa` + cổng 7777/9600/9643/7350 |
| 6 | Cài Agones bằng Helm | CRD `Fleet`/`GameServer` |
| 7 | Import image vào k3d | cluster thấy được image |
| 8 | Namespace và Secrets | namespace `fomoxa` + 6 secret |
| 9 | Apply manifest theo thứ tự | các dịch vụ chạy |
| 10 | Kiểm tra toàn cụm | pod `Running`, GameServer `Ready` |
| 11 | Kiểm tra từ phía "client" | cổng 9600/9643/7777 mở |
| 12 | Test Nakama bằng `curl` | ghép trận đúng |
| 13 | Test bằng client Unity | GameServer chuyển `Allocated` |
| 14 | Dọn dẹp | |

---

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
| Docker không chạy | [01, Bước 3](01-CHUAN-BI-MOI-TRUONG.md#bước-3-cài-docker) |

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
- Chứng chỉ tự ký chỉ để dev/nội bộ. Môi trường thật nên dùng chứng chỉ do CA cấp (xem [05, mục TLS](05-VAN-HANH.md#tls-giữa-client-và-backend)).

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
| Đã có cluster cũ chạy với khoá cũ mà bạn sinh lại khoá | secret trong cluster vẫn là khoá cũ → lệch. Sau khi sinh lại phải làm lại [Bước 8](#bước-8-namespace-và-secrets) và restart các deployment |

---

## Bước 3. Lấy mã nguồn `gateway` (Rust)

**Mục tiêu:** có thư mục `gateway/` chứa mã nguồn của `fomoxa-directory` và `fomoxa-gateway` (hai thành phần viết bằng Rust, không nằm trong repo này nhưng cần để build image).

**Lệnh:**

```bash
[ -d gateway ] || git clone --depth 1 -b 0.2.0 https://github.com/fomoxa/gateway.git gateway
ls gateway | head
```

**Ý nghĩa:** clone đúng tag `0.2.0` (bản đã dùng cho repo này), `--depth 1` chỉ lấy commit mới nhất cho nhanh. Thư mục `gateway/` được `.gitignore`, không commit.
`[ -d gateway ] ||` bỏ qua nếu thư mục đã có.

**Hoặc chạy gọn:** `./scripts/build-images.sh` tự clone nếu thiếu (nó là bước đầu của script).

**Kết quả mong đợi (mẫu):** thấy `Cargo.toml` và các thư mục crate.

**Lỗi thường gặp:** `Repository not found` / đòi mật khẩu → repo `fomoxa/gateway` là repo riêng/không truy cập được từ tài khoản của bạn; xin quyền truy cập hoặc đặt sẵn mã vào `gateway/`.

---

## Bước 4. Build 4 Docker image

**Mục tiêu:** đóng gói 4 thành phần thành image Docker.

| Image | Dockerfile | Nội dung |
|---|---|---|
| `fomoxa-server` | `deploy/Dockerfile.server` | game server (.NET 8), build từ `MyGame/Server~` + `server-deps` |
| `fomoxa-directory` | `deploy/Dockerfile.directory` | directory (Rust, từ `gateway/`) |
| `fomoxa-gateway` | `deploy/Dockerfile.gateway` | gateway UDP (Rust, từ `gateway/`) |
| `fomoxa-backend` | `MyGame.Backend/Dockerfile` | backend (.NET 8) + công cụ health |

**Lệnh:**

```bash
docker build -f deploy/Dockerfile.server    -t fomoxa-server:latest    .
docker build -f deploy/Dockerfile.directory -t fomoxa-directory:latest .
docker build -f deploy/Dockerfile.gateway   -t fomoxa-gateway:latest   .
docker build -f MyGame.Backend/Dockerfile   -t fomoxa-backend:latest   MyGame.Backend
```

**Ý nghĩa:**
- `-f <Dockerfile>` chọn file mô tả; `-t <tên:tag>` đặt tên image; đối số cuối là **build context** (thư mục được gửi cho Docker làm "gốc" khi `COPY`).
  Ba image đầu dùng context là thư mục gốc repo (`.`); backend dùng context riêng `MyGame.Backend`.
- Mỗi Dockerfile là **multi-stage**: giai đoạn `build` có SDK/compiler nặng (`dotnet/sdk`, `rust`) để biên dịch; giai đoạn cuối chỉ copy kết quả sang image runtime nhỏ.
- `.dockerignore` loại `MyGame/Library`, `deploy/secrets`, `gateway/target`... khỏi context để build nhanh và **không đưa khoá vào image**.

**Hoặc chạy gọn:** `./scripts/build-images.sh` (clone `gateway/` nếu thiếu rồi chạy đúng 4 lệnh trên, in danh sách image ở cuối).

**Thời gian:** hai image Rust build lâu nhất (10-25 phút lần đầu vì tải và biên dịch crate); các lần sau nhanh nhờ cache.

**Kết quả mong đợi (mẫu, đoạn cuối):**

```
 => exporting to image
 => => naming to docker.io/library/fomoxa-server:latest
```

**Kiểm tra:**

```bash
docker images | grep -E '^fomoxa-(server|directory|gateway|backend)\b'
```

Phải thấy **4 dòng**, tag `latest`:

```
fomoxa-backend     latest   3f1c...   2 minutes ago   260MB   (mẫu)
fomoxa-gateway     latest   9a77...   6 minutes ago   95MB
fomoxa-directory   latest   c04b...   9 minutes ago   98MB
fomoxa-server      latest   e1d2...   12 minutes ago  235MB
```

**Lỗi thường gặp:**

| Triệu chứng | Cách xử lý |
|---|---|
| `COPY gateway/ .: not found` | chưa làm Bước 3 |
| `error NU1301 ... api.nuget.org` khi build image .NET | container build không ra được Internet (proxy công ty). Cấu hình proxy cho Docker daemon hoặc build ở mạng khác |
| Build Rust bị `Killed` / hết RAM | tăng RAM cho Docker Desktop/WSL2 ([01, Bước 1](01-CHUAN-BI-MOI-TRUONG.md#bước-1-kiểm-tra-máy-đủ-sức)); `docker builder prune` giải phóng cache |
| `no space left on device` | `docker system df` rồi `docker image prune -a` |

---

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

**Lỗi thường gặp:** `k3d image import` chạy rất lâu → bình thường với image vài trăm MB; đừng ngắt. Sau **mỗi lần build lại image** phải import lại *và* khởi động lại workload ([05, Cập nhật image](05-VAN-HANH.md#cập-nhật-sau-khi-build-lại-image)).

---

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

**Ý nghĩa:** ConfigMap `directory-config` chứa `directory.toml`, **trong đó có `public_address = "127.0.0.1:7777"`** - địa chỉ gateway mà *client* thấy (xem [08, mục cấu hình](08-THAM-KHAO.md#2-cấu-hình-directory-và-gateway)).
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
| Nakama `Init:Error` | initContainer `migrate` không nối được Postgres: `kubectl logs deploy/nakama -n fomoxa -c migrate`; thường do mật khẩu DB lệch với dữ liệu cũ trong volume ([07](07-XU-LY-SU-CO.md#đổi-mật-khẩu-db-làm-nakama-không-khởi-động)) |
| GameServer mãi `Starting` | `kubectl logs <gs-pod> -n fomoxa -c fomoxa-server`; thường pod không nối được directory (9500) |

---

## Bước 10. Kiểm tra toàn cụm

**Mục tiêu:** xác nhận mọi thành phần chạy và game server đã đăng ký với directory.

**Lệnh:**

```bash
kubectl get pods -n fomoxa
kubectl get svc -n fomoxa
kubectl get fleet,gs -n fomoxa
```

**Kết quả mong đợi (mẫu):**

```
NAME                         READY   STATUS    RESTARTS   AGE
backend-7f9d8c6b5-4hq2l      1/1     Running   0          3m
directory-5c7b9d8f6-m8x2p    1/1     Running   0          4m
fomoxa-server-hl9fz-2xkpq    2/2     Running   0          2m
fomoxa-server-hl9fz-8mnrd    2/2     Running   0          2m
fomoxa-server-hl9fz-c4v7t    2/2     Running   0          2m
fomoxa-server-hl9fz-wp5xs    2/2     Running   0          2m
gateway-6d5f7b8c9-z7k3q      1/1     Running   0          4m
nakama-79c8d6b5f4-9rt6w      1/1     Running   0          5m
postgres-6d8f9c7b5-x2k4m     1/1     Running   0          6m
tls-proxy-5b8f6d7c4-j2n9v    1/1     Running   0          3m

NAME                TYPE           PORT(S)
backend             LoadBalancer   9600:3xxxx/TCP,9601:3xxxx/TCP
backend-pods        ClusterIP      9602/TCP
directory           ClusterIP      9500/TCP,9501/TCP
gateway             LoadBalancer   7777:3xxxx/UDP
nakama              LoadBalancer   7350:3xxxx/TCP
nakama-console      ClusterIP      7351/TCP
postgres            ClusterIP      5432/TCP
tls-proxy           LoadBalancer   9643:3xxxx/TCP

NAME                                 SCHEDULING   DESIRED   CURRENT   ALLOCATED   READY   AGE
fleet.agones.dev/fomoxa-server       Packed       4         4         0           4       2m

NAME                                      STATE   ADDRESS      PORT   NODE                  AGE
gameserver.agones.dev/fomoxa-server-...   Ready   10.42.0.x    7778   k3d-fomoxa-server-0   2m
```

**Ý nghĩa các cột/trạng thái quan trọng:**
- `READY 2/2` ở pod game server: 2 container (game + sidecar Agones) đều sẵn sàng.
- `STATE` của GameServer: `Starting` → **`Ready`** (đã đăng ký, chờ người chơi) → **`Allocated`** (đã có người chơi đầu tiên) → `Shutdown` (phòng trống 30 giây thì tự tắt, Fleet tạo pod mới).
- `ALLOCATED 0 / READY 4`: 4 phòng trống đang chờ người chơi.

**Xem log xác nhận game server đã đăng ký phòng:**

```bash
kubectl logs -n fomoxa -l agones.dev/role=gameserver -c fomoxa-server --tail=20 --prefix | grep -E "registered|claimed|Ready|Backend"
```

**Kết quả mong đợi (khuôn dòng thật của chương trình, tên pod là mẫu):**

```
[pod/fomoxa-server-hl9fz-2xkpq/fomoxa-server] [Pod] registered id=1 changed=False
[pod/fomoxa-server-hl9fz-2xkpq/fomoxa-server] [Pod] listening on udp 7778
[pod/fomoxa-server-hl9fz-2xkpq/fomoxa-server] [Pod] claimed room from pool: arena:1
[pod/fomoxa-server-hl9fz-2xkpq/fomoxa-server] [Agones] Ready
[pod/fomoxa-server-hl9fz-2xkpq/fomoxa-server] [Backend] da ket noi
```

`[Backend] da ket noi` nghĩa là pod đã nối được backend (cổng 9602) để báo số người/bảng điểm.

**Xem log backend:**

```bash
kubectl logs deploy/backend -n fomoxa | head -5
```

```
[Nakama] cấu hình http://nakama.fomoxa.svc.cluster.local:7350
[Metrics] http://0.0.0.0:9610/metrics
backend: players on tcp 0.0.0.0:9600, websocket 0.0.0.0:9601; pods on tcp 0.0.0.0:9602; directory at directory.fomoxa.svc.cluster.local:9500
[Nakama] self test ok <uuid>
```

Directory và gateway (chương trình Rust) in log theo định dạng riêng; dấu hiệu tốt là không có dòng lỗi lặp lại: `kubectl logs deploy/directory -n fomoxa --tail=20`, `kubectl logs deploy/gateway -n fomoxa --tail=20`.

**Lỗi thường gặp:** pod game server không có dòng `registered` → xem `kubectl logs ... -c fomoxa-server` có `Connection refused` tới `directory...:9500` không (directory chưa lên hoặc **khoá `pod` sai** giữa secret `fomoxa-pod-key` và `fomoxa-directory-keys`).

---

## Bước 11. Kiểm tra từ phía "client"

**Mục tiêu:** xác nhận máy bạn nối được các cổng mà client sẽ dùng.

**Lệnh:**

```bash
# TCP 9600 (backend) và 9643 (TLS) và 7350 (Nakama, chỉ để test)
for p in 9600 9643 7350; do
  timeout 3 bash -c "</dev/tcp/127.0.0.1/$p" && echo "TCP $p: mở" || echo "TCP $p: KHÔNG nối được"
done

# UDP 7777: không thể "thử kết nối" UDP; kiểm tra cổng đã được publish
docker port k3d-fomoxa-serverlb | grep 7777

# Backend tự báo sức khoẻ (cùng lệnh mà Kubernetes dùng làm probe)
kubectl exec -n fomoxa deploy/backend -- dotnet /app/health/fomoxa-rpc-health.dll 127.0.0.1:9600; echo "exit=$?"

# Nakama
curl -s -o /dev/null -w "Nakama healthcheck: HTTP %{http_code}\n" http://127.0.0.1:7350/healthcheck
```

**Ý nghĩa:** UDP không có "bắt tay" nên không thể kiểm tra bằng cách nối thử; ta kiểm tra cổng đã được k3d publish, còn việc gateway nhận gói tin thật sự được chứng minh ở Bước 13 (client chơi được).
Công cụ `fomoxa-rpc-health` in `SERVING` và thoát mã 0 khi backend khoẻ.

**Kết quả mong đợi (mẫu):**

```
TCP 9600: mở
TCP 9643: mở
TCP 7350: mở
7777/udp -> 0.0.0.0:7777
SERVING
exit=0
Nakama healthcheck: HTTP 200
```

**Lỗi thường gặp:** `TCP 9600: KHÔNG nối được` → cluster tạo thiếu `--port` (Bước 5) hoặc Service backend chưa có `EXTERNAL-IP`: `kubectl get svc backend -n fomoxa`. Nếu client Unity ở **Windows** mà cluster ở **WSL2**: cần Docker Desktop (cổng ra `localhost` Windows); xem [07](07-XU-LY-SU-CO.md#client-windows-không-nối-được-cụm-trong-wsl2).

---

## Bước 12. Test Nakama bằng `curl`

**Mục tiêu:** chứng minh Nakama ghép đúng hai người vào cùng một phòng, phát ticket hợp lệ, và **chặn người ngoài**. Đây là bài test **đã chạy thật** (trên Nakama của docker compose có cùng module `main.lua`).

**Lấy khoá Nakama của cụm:**

```bash
BASE=http://127.0.0.1:7350
SERVER_KEY=$(kubectl get secret fomoxa-nakama -n fomoxa -o jsonpath='{.data.server-key}' | base64 -d)
HTTP_KEY=$(kubectl get secret fomoxa-nakama -n fomoxa -o jsonpath='{.data.http-key}' | base64 -d)
echo "server-key=${SERVER_KEY:0:6}...  http-key=${HTTP_KEY:0:6}..."
```

**Ý nghĩa:** `server-key` dùng để người chơi đăng nhập Nakama; `http-key` chỉ dành cho backend gọi RPC nội bộ. Lệnh `base64 -d` giải mã giá trị trong secret.
(Nếu bạn test Nakama của `docker compose` thay vì cụm: `SERVER_KEY=defaultkey; HTTP_KEY=defaulthttpkey`.)

**Bước 12.1. Đăng nhập hai người chơi (device) và lấy token:**

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

**Ý nghĩa:** `authenticate/device` tạo (nếu chưa có) tài khoản gắn với "device id" và trả về JWT `token`. Header `Authorization: Basic <base64(server_key:)>` là cách Nakama xác thực *ứng dụng*.

**Bước 12.2. Làm sạch hàng chờ, rồi hai người xin vào `arena:1`:**

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

**Kết quả mong đợi (thật):**

```
{"ok":true}
{"room":"arena:1","status":"waiting"}
{"exp":1793533330,"room":"arena:1","status":"matched","ticket":"f32f69a2-72cc-4ecb-b7ee-5c23eb0fa788"}
```

**Ý nghĩa:** người đầu tiên vào hàng chờ (`waiting`); người thứ hai làm đủ cặp nên Nakama tạo ticket (`matched`, hết hạn sau `exp`).

**Bước 12.3. Backend xác minh ticket (và từ chối người ngoài):**

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

**Kết quả mong đợi (thật):**

```
{"exp":1793533330,"ok":true,"room":"arena:1"}
{"message":"ticket khong thuoc user nay","ok":false}
```

**Bước 12.4. Người chơi KHÔNG được gọi RPC nội bộ:**

```bash
curl -s -X POST "$BASE/v2/rpc/verify_ticket?unwrap" -H "Authorization: Bearer $TOKEN_A" \
     -H 'Content-Type: application/json' -d '{}'; echo
```

**Kết quả mong đợi (thật):**

```
{"code":7,"error":{},"message":"chi backend duoc goi RPC nay"}
```

`code 7` = `PERMISSION_DENIED`. Các RPC `verify_ticket`, `reset_match`, `list_rooms`, `kv_get`, `kv_put` chỉ backend (có `http_key`) gọi được.

**Hoặc chạy gọn:** `./scripts/test-nakama.sh` làm đúng các bước 12.1-12.3 và kiểm tra tự động; script tự đọc khoá từ secret `fomoxa-nakama` khi Nakama chạy trong k3d.
`./scripts/test-nakama-pool.sh` thử **4 người chơi vào "auto"** (phải ra 2 phòng khác nhau).

**Kết quả mong đợi của `test-nakama.sh` (thật, đoạn cuối):**

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

**Kết quả mong đợi của `test-nakama-pool.sh` (thật, đoạn cuối):**

```
   -> Cap 1 matched vao room: arena:1 (ticket: e0d102e4-...)
   -> Cap 2 matched vao room: arena:2 (ticket: 324b9b2d-...)
Chung minh thanh cong: Cap 1 (arena:1) va Cap 2 (arena:2) duoc cap 2 room khac nhau tu pool!
=== DoD P5.5 HOAN TAT THANH CONG! ===
```

**Lỗi thường gặp:**

| Triệu chứng | Cách xử lý |
|---|---|
| Script dừng với "cổng 7350 đang trỏ tới container ... compose" | còn `docker compose` đang giữ 7350: `docker compose -f deploy/compose.yaml down`. Muốn cố ý test compose: `ALLOW_COMPOSE=1 ./scripts/test-nakama.sh` |
| `401 Unauthorized` khi `authenticate/device` | sai `SERVER_KEY` (dùng `defaultkey` cho cụm đã đổi khoá, hoặc ngược lại) |
| `lua runtime error` | module Lua lỗi: `kubectl logs deploy/nakama -n fomoxa \| grep -i lua` |
| Người thứ hai vẫn `waiting` | hai người xin **hai phòng khác nhau**, hoặc hàng chờ cũ chưa xoá (chạy lại `reset_match`) |

---

## Bước 13. Test bằng client Unity (người chơi thật)

**Mục tiêu:** chứng minh đường đi đầy đủ client → backend → Nakama → directory → gateway → game server, và trạng thái Agones thay đổi theo người chơi.

**13.1. Mở client** (build ở [04](04-CLIENT-UNITY.md)). Cụm k3d đưa 9600/7777 vào `localhost`, đúng mặc định của client nên **không cần tham số**:

```powershell
.\Builds\MyGame.exe
```

Trong game: **Đăng ký** → lưu mã khôi phục → bấm phòng `arena:1`. (Nakama bật nên phòng cụ thể cần **2 người** cùng bấm vào; mở client thứ hai, đăng ký tên khác, bấm cùng phòng.)

**13.2. Quan sát phía cluster** (terminal WSL2, trong lúc chơi):

```bash
watch -n 2 kubectl get gs,fleet -n fomoxa
```

**Kết quả mong đợi:** một GameServer chuyển `Ready` → **`Allocated`** (cột `ALLOCATED` của Fleet từ 0 lên 1). Log pod đó:

```bash
kubectl logs -n fomoxa -l agones.dev/role=gameserver -c fomoxa-server --prefix --tail=30 | grep -E "Agones|peer"
```

```
[Pod] peer 1 la 'ana'
[Agones] first player joined -> Allocate
[Agones] player 1 connected (active: 1)
...
[Agones] room empty, shutting down in 30s unless a player returns
[Agones] Shutting down...
```

Khi người chơi cuối thoát và phòng trống 30 giây, pod `Shutdown` và Fleet tạo pod mới ở trạng thái `Ready`.

**13.3. Bản tự động (cần `Builds/MyGame.exe`):** `./scripts/test-p5.sh`

**Script này làm gì (bung ra):**
1. `kubectl get gs -n fomoxa` - đảm bảo có GameServer và **0 pod `Allocated`** trước khi bắt đầu.
2. Gọi PowerShell của Windows (WSL2 interop) chạy `scripts/test-p5.ps1`, nó khởi chạy **4 bản `MyGame.exe`** ẩn cửa sổ:
   `-batchmode -nographics -autojoin -name:ana|bo|cai|dan -device:p5-k8s-<tên> -room:arena:1|arena:1|arena:2|arena:2`
   (mỗi phòng cần đúng 2 người nên ghép cặp ana+bo, cai+dan).
3. Đợi mỗi client in `mine=True` trong log (đã vào pod và có nhân vật của mình).
4. Dùng `kubectl` (qua `wsl.exe` nếu chạy từ `test-p5.sh`) đếm pod `Allocated`: **phải đúng 2**, nếu không script thất bại.
5. Tuỳ chọn `TEST_SCALE=1 ./scripts/test-p5.sh`: scale Fleet lên 5 rồi về 4 và kiểm tra số GameServer.

**Kết quả mong đợi (mẫu):**

```
=== Kiem tra DoD P5: Game server chay tren K8s + Agones ===
   ana -> arena:1 ... mine=True (OK)
   bo -> arena:1 ... mine=True (OK)
   cai -> arena:2 ... mine=True (OK)
   dan -> arena:2 ... mine=True (OK)
So pod o trang thai Allocated: 2 (dung ky vong)
=== DoD P5 HOAN TAT THANH CONG! ===
```

**Lỗi thường gặp:** xem [07](07-XU-LY-SU-CO.md): client không nối được, ở `waiting` mãi (thiếu người thứ hai), GameServer không chuyển `Allocated`.

---

## Bước 14. Dọn dẹp

Chọn một trong ba mức:

```bash
# A) Chỉ xoá ứng dụng, giữ cluster + Agones (dựng lại nhanh)
kubectl delete namespace fomoxa

# B) Xoá toàn bộ cluster k3d (gồm Agones, dữ liệu Postgres)
k3d cluster delete fomoxa

# C) Xoá luôn image Docker đã build
docker rmi fomoxa-server:latest fomoxa-directory:latest fomoxa-gateway:latest fomoxa-backend:latest
```

**Hoặc chạy gọn:** `./scripts/k8s-down.sh --keep-cluster` (mức A) hoặc `./scripts/k8s-down.sh` (mức B).

Khoá trong `deploy/secrets/` không bị xoá bởi các lệnh trên; xoá tay nếu muốn: `rm -rf deploy/secrets`.

---

## Đường tắt: một lệnh dựng cả cụm

Sau khi hiểu các bước trên, chạy gọn:

```bash
./scripts/k8s-up.sh --build
```

**Script làm đúng những bước nào:**

| Phần của `k8s-up.sh` | Tương ứng |
|---|---|
| kiểm `docker info`; tự cài k3d/helm nếu thiếu | [01](01-CHUAN-BI-MOI-TRUONG.md) |
| kiểm cổng 7777/9600/9643/7350 trống, `k3d cluster create fomoxa ...` | Bước 1, 5 |
| `helm repo add/update`, `helm install agones ...` | Bước 6 |
| `--build` → `scripts/build-images.sh` | Bước 3-4 |
| `k3d image import ...` | Bước 7 |
| `kubectl apply namespace` + `scripts/gen-k8s-secrets.sh` (tự gọi `gen-keys.sh`, `gen-tls-cert.sh` nếu thiếu) | Bước 2, 8 |
| `kubectl apply` postgres, nakama, directory, gateway, backend, tls-proxy, fleet | Bước 9 |
| `kubectl rollout status` từng deployment, `kubectl get gs` | Bước 9-10 |

Biến môi trường tuỳ chọn (đặt trước lệnh, ví dụ `AUTOSCALE=1 INSTALL_MONITORING=1 ./scripts/k8s-up.sh`):

| Biến | Tác dụng | Chi tiết |
|---|---|---|
| `AGONES_VERSION=1.xx.y` | ghim phiên bản chart Agones | Bước 6 |
| `AUTOSCALE=1` | bật `FleetAutoscaler` (giữ 2 pod Ready, 4..16 pod) | [05](05-VAN-HANH.md#autoscaling) |
| `INSTALL_MONITORING=1` | cài Prometheus/Alertmanager/Grafana/Loki | [05](05-VAN-HANH.md#giám-sát) |
| `APPLY_NETPOL=1` | bật NetworkPolicy (chưa thử trên cụm thật) | [05](05-VAN-HANH.md#networkpolicy-tuỳ-chọn) |
| `TLS_ONLY=1` | đóng cổng 9600 không mã hoá, chỉ còn TLS 9643 | [05](05-VAN-HANH.md#tls-giữa-client-và-backend) |
| `APPLY_INGRESS=1` | áp dụng Ingress thử nghiệm | [08](08-THAM-KHAO.md) |

---

## Checklist cuối

- [ ] `deploy/secrets/` có 14 file; `root` đúng 64 ký tự
- [ ] `docker images` thấy 4 image `fomoxa-*:latest`
- [ ] `kubectl get nodes` → `Ready`; `docker port k3d-fomoxa-serverlb` thấy 7777/udp, 9600, 9643, 7350
- [ ] `kubectl get pods -n agones-system` → controller/extensions `Running`
- [ ] 6 secret trong namespace `fomoxa`
- [ ] `kubectl get pods -n fomoxa` → mọi pod `Running`, game server `2/2`
- [ ] `kubectl get fleet,gs -n fomoxa` → `READY 4`, GameServer `Ready`
- [ ] Log pod game server có `registered`, `claimed room`, `[Backend] da ket noi`
- [ ] `./scripts/test-nakama.sh` kết thúc bằng `OK: 2 client ghep dung room...`
- [ ] Client Unity vào được phòng, GameServer chuyển `Allocated`
- [ ] `public_address` trong `directory-config` là `127.0.0.1:7777` (chạy local)

Tiếp theo: [04 - Client Unity](04-CLIENT-UNITY.md), [05 - Vận hành](05-VAN-HANH.md).
