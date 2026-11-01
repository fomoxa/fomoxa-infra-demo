# Hướng Dẫn Triển Khai Hạ Tầng Fomoxa trên Ubuntu / WSL2

> **Dành cho ai**: Người mới clone dự án về, chưa biết Kubernetes, muốn hiểu từng bước thực sự làm gì.  
> **Mục tiêu**: Sau guide này bạn có một cụm Kubernetes cục bộ (k3d) chạy đầy đủ: Postgres + Nakama + Directory + Gateway + Backend + Agones Fleet - và đã verify hoạt động qua các bài test thực tế.  
> **Hệ điều hành**: Ubuntu 22.04 / 24.04 (WSL2 trên Windows hoặc máy Linux thuần).

---

## Mục lục

1. [Giới thiệu & Sơ đồ kiến trúc](#1-giới-thiệu--sơ-đồ-kiến-trúc)
2. [Yêu cầu máy](#2-yêu-cầu-máy)
3. [Cài Docker](#3-cài-docker)
4. [Cài kubectl, k3d, helm](#4-cài-kubectl-k3d-helm)
5. [Clone repo & cấu trúc thư mục](#5-clone-repo--cấu-trúc-thư-mục)
6. [Sinh secrets (khóa bảo mật)](#6-sinh-secrets-khóa-bảo-mật)
7. [Tạo cluster k3d](#7-tạo-cluster-k3d)
8. [Cài Agones bằng Helm](#8-cài-agones-bằng-helm)
9. [Build Docker images & import vào k3d](#9-build-docker-images--import-vào-k3d)
10. [Tạo Kubernetes Secrets & apply manifests](#10-tạo-kubernetes-secrets--apply-manifests)
11. [Kiểm tra trạng thái pods / GameServers](#11-kiểm-tra-trạng-thái-pods--gameservers)
12. [Cấu hình quan trọng](#12-cấu-hình-quan-trọng)
13. [Test end-to-end](#13-test-end-to-end)
14. [Vận hành: logs, scale, drain, restart](#14-vận-hành-logs-scale-drain-restart)
15. [Dọn dẹp](#15-dọn-dẹp)
16. [Troubleshooting chi tiết](#16-troubleshooting-chi-tiết)
17. [Checklist tự xác nhận](#17-checklist-tự-xác-nhận)
18. [Phụ lục: Lệnh hay dùng & Script tương đương](#18-phụ-lục-lệnh-hay-dùng--script-tương-đương)

---

## 1. Giới thiệu & Sơ đồ kiến trúc

### Hệ thống sau khi hoàn tất bao gồm

```
┌─────────────────────────────────────────────────────────────────┐
│                     CLIENT (Unity Game)                         │
│         TCP → 127.0.0.1:9600  (backend – login/matchmaking)     │
│         UDP → 127.0.0.1:7777  (gateway – game packets)          │
└──────────────────────────┬──────────────────────────────────────┘
                           │  (cổng map qua k3d LoadBalancer)
┌──────────────────────────▼──────────────────────────────────────┐
│                   k3d Cluster "fomoxa"                          │
│                   namespace: fomoxa                             │
│                                                                 │
│  ┌───────────┐  ┌──────────┐  ┌───────────┐  ┌─────────────┐  │
│  │  postgres  │  │  nakama  │  │ directory │  │   gateway   │  │
│  │  :5432     │  │  :7350   │  │ :9500     │  │ UDP :7777   │  │
│  └───────────┘  └──────────┘  └───────────┘  └──────┬──────┘  │
│                                                      │ route   │
│  ┌─────────────┐      ┌──────────────────────────────▼──────┐  │
│  │   backend   │      │  fomoxa-server (Agones Fleet)       │  │
│  │ TCP :9600   │      │  UDP :7778  × 2 replicas            │  │
│  └─────────────┘      └─────────────────────────────────────┘  │
│                                                                 │
│  namespace: agones-system  (Agones controller + CRD)            │
└─────────────────────────────────────────────────────────────────┘
```

### Luồng kết nối của một game session

1. Client gọi **backend** (TCP 9600) → đăng nhập, xin matchmaking.
2. Backend hỏi **directory** → nhận địa chỉ game server (pod).
3. Directory trả về `public_address` → **gateway** (127.0.0.1:7777).
4. Client kết nối **gateway** (UDP 7777) với connect token → gateway route đến đúng pod.
5. **Game server** (Agones pod) nhận packets → chạy game logic.

### k3d là gì?

k3d chạy **k3s** (Kubernetes nhẹ) bên trong Docker container. Mọi thứ - control plane, worker node, load balancer - đều là container trên máy bạn. Không cần cloud, không cần VM riêng.

---

## 2. Yêu cầu máy

| Thứ | Tối thiểu | Ghi chú |
|-----|-----------|---------|
| RAM | 8 GB | k3d + Agones + Nakama ngốn ~3–4 GB |
| CPU | 4 core | |
| Disk | 20 GB free | Images Docker tổng ~3–4 GB |
| OS | Ubuntu 22.04+ | WSL2 hoặc bare metal |
| Docker | 24.x+ | Xem Bước 3 |
| Internet | Cần | Kéo images lần đầu |

---

## 3. Cài Docker

> **Quan trọng - WSL2 vs Ubuntu thuần là hai engine khác nhau.**
>
> - **WSL2 trên Windows**: Docker chạy trên Windows host qua Docker Desktop. WSL2 dùng Docker qua socket chia sẻ. **Không** cài Docker trong WSL2 khi đã có Docker Desktop.
> - **Ubuntu thuần (bare metal / VM)**: Cài Docker Engine trực tiếp vào Linux.

### Nếu bạn dùng WSL2 (Ubuntu trên Windows)

**Bước 3-WSL-1**: Cài Docker Desktop cho Windows  
Tải tại: https://www.docker.com/products/docker-desktop/  
Cài đặt bình thường, restart máy nếu được yêu cầu.

**Bước 3-WSL-2**: Bật WSL Integration  
Mở Docker Desktop → Settings (bánh răng) → Resources → WSL Integration  
→ Bật toggle cho distro **Ubuntu** của bạn → Apply & Restart.

**Bước 3-WSL-3**: Kiểm tra trong terminal Ubuntu

```bash
docker version
# Kết quả mong đợi:
# Client: Docker Engine - Community
#  Version: 27.x.x
# Server: Docker Desktop
#  Engine: 27.x.x
```

```bash
docker ps
# Kết quả mong đợi: bảng danh sách (có thể rỗng, không lỗi)
```

> Nếu thấy `Cannot connect to the Docker daemon` → Docker Desktop chưa chạy hoặc chưa bật WSL Integration (xem [Troubleshooting 16.1](#161--cannot-connect-to-the-docker-daemon)).

---

### Nếu bạn dùng Ubuntu thuần (không có WSL2)

**Bước 3-Linux-1**: Cài Docker Engine

```bash
# Cập nhật apt và cài dependencies
sudo apt update
sudo apt install -y ca-certificates curl gnupg

# Thêm GPG key chính thức của Docker
sudo install -m 0755 -d /etc/apt/keyrings
curl -fsSL https://download.docker.com/linux/ubuntu/gpg \
    | sudo gpg --dearmor -o /etc/apt/keyrings/docker.gpg
sudo chmod a+r /etc/apt/keyrings/docker.gpg

# Thêm repository Docker
echo \
  "deb [arch=$(dpkg --print-architecture) signed-by=/etc/apt/keyrings/docker.gpg] \
  https://download.docker.com/linux/ubuntu \
  $(. /etc/os-release && echo "$VERSION_CODENAME") stable" | \
  sudo tee /etc/apt/sources.list.d/docker.list > /dev/null

# Cài Docker Engine
sudo apt update
sudo apt install -y docker-ce docker-ce-cli containerd.io docker-buildx-plugin
```

**Bước 3-Linux-2**: Thêm user vào group docker (tránh phải gõ sudo mỗi lần)

```bash
sudo usermod -aG docker $USER
# Đăng xuất rồi đăng nhập lại để group có hiệu lực
# Hoặc dùng lệnh sau để áp dụng ngay trong session hiện tại:
newgrp docker
```

**Bước 3-Linux-3**: Bật Docker khởi động cùng hệ thống

```bash
sudo systemctl enable docker
sudo systemctl start docker
```

**Bước 3-Linux-4**: Kiểm tra

```bash
docker run --rm hello-world
# Kết quả mong đợi: "Hello from Docker!" và giải thích ngắn
```

---

## 4. Cài kubectl, k3d, helm

### Bước 4.1 - Cài các gói cơ bản

```bash
sudo apt update && sudo apt install -y curl openssl git ca-certificates
```

**Lệnh này làm gì**: Cài `curl` (tải file), `openssl` (sinh khóa bảo mật), `git` (clone repo), `ca-certificates` (xác thực HTTPS).

---

### Bước 4.2 - Cài kubectl

`kubectl` là CLI để giao tiếp với Kubernetes cluster. Mọi lệnh `kubectl get`, `kubectl apply`, `kubectl logs` đều dùng tool này.

```bash
# Lấy phiên bản stable mới nhất
KUBECTL_VERSION=$(curl -L -s https://dl.k8s.io/release/stable.txt)
echo "Sẽ cài kubectl $KUBECTL_VERSION"

# Tải binary
curl -LO "https://dl.k8s.io/release/${KUBECTL_VERSION}/bin/linux/amd64/kubectl"

# Cài vào /usr/local/bin (cần sudo)
sudo install -o root -g root -m 0755 kubectl /usr/local/bin/kubectl

# Xóa file tải về
rm kubectl

# Kiểm tra
kubectl version --client
```

**Kết quả mong đợi**:
```
Client Version: v1.31.x
Kustomize Version: v5.x.x
```

---

### Bước 4.3 - Cài k3d

k3d là wrapper tạo/quản lý cluster k3s (Kubernetes nhẹ) trong Docker. Toàn bộ cluster chạy như container trên máy bạn.

```bash
# Cài k3d phiên bản 5.7.4 (phiên bản đã test với project này)
curl -s https://raw.githubusercontent.com/k3d-io/k3d/main/install.sh | TAG=v5.7.4 bash

# Kiểm tra
k3d version
```

**Kết quả mong đợi**:
```
k3d version v5.7.4
k3s version v1.30.x+k3s1
```

> **Vì sao dùng tag cụ thể?** Phiên bản mới hơn có thể thay đổi cú pháp `--port` mapping. Tag `v5.7.4` đã được kiểm tra với cấu hình này.

---

### Bước 4.4 - Cài Helm

Helm là package manager cho Kubernetes. Dùng để cài Agones (game server framework) bằng một lệnh thay vì apply hàng chục YAML file thủ công.

```bash
curl -fsSL https://raw.githubusercontent.com/helm/helm/main/scripts/get-helm-3 | bash

# Kiểm tra
helm version --short
```

**Kết quả mong đợi**:
```
v3.16.x+gxxxxxxx
```

---

### Kiểm tra nhanh tất cả công cụ

```bash
echo "Docker:  $(docker version --format '{{.Client.Version}}')"
echo "kubectl: $(kubectl version --client -o json | grep gitVersion | head -1)"
echo "k3d:     $(k3d version | head -1)"
echo "helm:    $(helm version --short)"
```

---

## 5. Clone repo & cấu trúc thư mục

### Bước 5.1 - Clone hoặc trỏ đến repo

**Nếu dùng WSL2** và repo đã có trên Windows (ví dụ `D:\Documents\fomoxa`):

```bash
# Trỏ vào thư mục trên Windows - WSL2 mount ổ đĩa Windows vào /mnt/
cd /mnt/d/Documents/fomoxa
```

**Nếu clone mới**:

```bash
git clone <url-repo-fomoxa> ~/fomoxa
cd ~/fomoxa
```

**Bước 5.2 - Cấp quyền thực thi cho các script**

```bash
chmod +x scripts/*.sh
```

**Lệnh này làm gì**: Các file `.sh` trong `scripts/` mặc định không có quyền execute. Lệnh này cấp quyền để chạy trực tiếp bằng `./scripts/xxx.sh`.

### Bước 5.3 - Hiểu cấu trúc thư mục quan trọng

```
fomoxa/
├── deploy/
│   ├── k8s/                    ← Kubernetes manifest (YAML)
│   │   ├── namespace.yaml      ← Tạo namespace "fomoxa"
│   │   ├── postgres.yaml       ← Database
│   │   ├── nakama.yaml         ← Matchmaking server + Lua modules
│   │   ├── directory.yaml      ← Service discovery (ConfigMap + Deployment + Service)
│   │   ├── gateway.yaml        ← UDP gateway (ConfigMap + Deployment + Service)
│   │   ├── backend.yaml        ← fRPC backend (Deployment + Service)
│   │   ├── fleet.yaml          ← Agones Fleet (game server pool)
│   │   └── ingress.yaml        ← (tùy chọn) ingress rules
│   ├── secrets/                ← KHÔNG commit - git-ignored
│   │   ├── directory/
│   │   │   ├── root            ← hex string 64 ký tự
│   │   │   ├── admin           ← hex string 48 ký tự
│   │   │   ├── backend         ← hex string 48 ký tự
│   │   │   ├── gateway         ← hex string 48 ký tự
│   │   │   └── pod             ← hex string 48 ký tự
│   │   └── backend-pod         ← hex string 48 ký tự
│   ├── Dockerfile.server       ← Build fomoxa-server image
│   ├── Dockerfile.directory    ← Build fomoxa-directory image
│   └── Dockerfile.gateway      ← Build fomoxa-gateway image
├── MyGame.Backend/
│   └── Dockerfile              ← Build fomoxa-backend image
├── MyGame/Server~/             ← Console server C# (nguồn fomoxa-server)
├── scripts/
│   ├── gen-keys.sh             ← Sinh secrets (openssl)
│   ├── gen-k8s-secrets.sh      ← Tạo K8s Secret objects từ file secrets
│   ├── build-images.sh         ← docker build 4 images
│   ├── k8s-up.sh               ← Khởi động toàn bộ cluster
│   ├── k8s-down.sh             ← Dọn dẹp cluster
│   ├── test-nakama.sh          ← Test matchmaking cơ bản
│   └── test-nakama-pool.sh     ← Test auto room pool
└── docs/
    ├── UBUNTU_GUIDE.md         ← File này
    └── FOMOXA_UNITY_GUIDE.md   ← Hướng dẫn phía Unity client
```

---

## 6. Sinh secrets (khóa bảo mật)

### Tại sao cần secrets?

Directory service dùng các khóa này để xác thực các thành phần trong cluster:
- `root`: khóa cao nhất (admin operations)
- `backend`: backend dùng để đăng ký pod với directory
- `gateway`: gateway dùng để nhận route từ directory
- `pod`: game server pod dùng để đăng ký với directory
- `admin`, `backend-pod`: dùng cho các thao tác quản trị và deploy

**Những file này là secret - đã được git-ignore. Không bao giờ commit vào repo.**

### Bước 6.1 - Tạo thư mục secrets

```bash
# Đứng từ thư mục gốc repo
mkdir -p deploy/secrets/directory
```

### Bước 6.2 - Sinh từng khóa bằng openssl

```bash
# openssl rand -hex N sinh N byte ngẫu nhiên dạng hex (2N ký tự)
# tr -d '\n' xóa ký tự xuống dòng cuối - quan trọng, nếu có \n sẽ lỗi khi đọc

# root key: 32 bytes = 64 ký tự hex
openssl rand -hex 32 | tr -d '\n' > deploy/secrets/directory/root

# admin key: 24 bytes = 48 ký tự hex
openssl rand -hex 24 | tr -d '\n' > deploy/secrets/directory/admin

# backend key (directory dùng để xác thực backend)
openssl rand -hex 24 | tr -d '\n' > deploy/secrets/directory/backend

# gateway key
openssl rand -hex 24 | tr -d '\n' > deploy/secrets/directory/gateway

# pod key (game server pod dùng để đăng ký với directory)
openssl rand -hex 24 | tr -d '\n' > deploy/secrets/directory/pod

# backend-pod key (backend dùng để gửi lệnh cho pod)
openssl rand -hex 24 | tr -d '\n' > deploy/secrets/backend-pod
```

### Bước 6.3 - Kiểm tra file được tạo đúng

```bash
ls -la deploy/secrets/directory/
# Kết quả mong đợi: 5 file (root admin backend gateway pod)

wc -c deploy/secrets/directory/root
# Kết quả mong đợi: 64 deploy/secrets/directory/root

wc -c deploy/secrets/directory/backend
# Kết quả mong đợi: 48 deploy/secrets/directory/backend

# Đảm bảo không có ký tự xuống dòng
xxd deploy/secrets/directory/root | tail -1
# Phải kết thúc bằng hex chars, không phải "0a" (= newline)
```

> **Hoặc chạy gọn**: `./scripts/gen-keys.sh` - script làm đúng các lệnh trên với kiểm tra "đã tồn tại thì bỏ qua".

---

## 7. Tạo cluster k3d

### Tại sao phải map cổng ngay khi tạo cluster?

**Đây là ràng buộc quan trọng**: k3d không cho phép thêm port mapping sau khi cluster đã tạo. Nếu quên, phải xóa và tạo lại cluster.

k3d tạo một container `k3d-fomoxa-serverlb` đóng vai trò LoadBalancer. Container này forward traffic từ máy host vào cluster. Phải khai báo port mapping ngay lúc khởi tạo để container này biết lắng nghe cổng nào.

### Bước 7.1 - Kiểm tra cluster có tồn tại chưa

```bash
k3d cluster list
# Nếu thấy "fomoxa" trong danh sách → cluster đã tồn tại, bỏ qua Bước 7.2
```

### Bước 7.2 - Tạo cluster với port mapping

```bash
k3d cluster create fomoxa \
    --port "7777:7777/udp@loadbalancer" \
    --port "9600:9600@loadbalancer" \
    --port "7350:7350@loadbalancer" \
    --wait
```

**Giải thích từng flag**:

| Flag | Ý nghĩa |
|------|---------|
| `fomoxa` | Tên cluster - kubectl context sẽ là `k3d-fomoxa` |
| `--port "7777:7777/udp@loadbalancer"` | Map UDP port 7777 host → 7777 trong cluster (gateway game traffic) |
| `--port "9600:9600@loadbalancer"` | Map TCP port 9600 host → 9600 trong cluster (backend fRPC) |
| `--port "7350:7350@loadbalancer"` | Map TCP port 7350 host → 7350 trong cluster (Nakama API - dùng cho test) |
| `--wait` | Đợi cho đến khi node sẵn sàng |

**Tại sao UDP phải khai báo `/udp`?** Docker mặc định map TCP. UDP cần khai báo tường minh, nếu thiếu thì game client sẽ không gửi được packets đến gateway.

**Kết quả mong đợi** (khoảng 30–60 giây):

```
INFO[0000] Prep: Network
INFO[0000] Created network 'k3d-fomoxa'
INFO[0000] Creating node 'k3d-fomoxa-server-0'
INFO[0015] Creating LoadBalancer 'k3d-fomoxa-serverlb'
INFO[0020] Cluster 'fomoxa' created successfully!
INFO[0020] You can now use it with: kubectl cluster-info
```

### Bước 7.3 - Chuyển kubectl context sang cluster mới

```bash
kubectl config use-context k3d-fomoxa

# Xác nhận context hiện tại
kubectl config current-context
# Kết quả mong đợi: k3d-fomoxa
```

**Tại sao bước này quan trọng?** Nếu có nhiều cluster (ví dụ đang dùng minikube), lệnh `kubectl` sẽ nói chuyện với cluster sai nếu không chuyển context.

### Bước 7.4 - Kiểm tra cluster hoạt động

```bash
kubectl cluster-info
# Kết quả mong đợi:
# Kubernetes control plane is running at https://0.0.0.0:XXXXX
# CoreDNS is running at https://...

kubectl get nodes
# Kết quả mong đợi:
# NAME                   STATUS   ROLES                  AGE   VERSION
# k3d-fomoxa-server-0    Ready    control-plane,master   1m    v1.30.x+k3s1
```

---

## 8. Cài Agones bằng Helm

### Agones là gì?

Agones là Kubernetes operator (từ Google) quản lý **game server lifecycle**. Nó thêm các Custom Resource Definition (CRD) như `Fleet`, `GameServer`, `GameServerAllocation` vào Kubernetes - cho phép cluster tự động spawn, health-check, và drain game server pods.

### Bước 8.1 - Thêm Helm repo của Agones

```bash
helm repo add agones https://agones.dev/chart/helm
```

**Lệnh này làm gì**: Đăng ký địa chỉ chart repository của Agones vào Helm local. Giống "thêm PPA" trong apt.

```bash
helm repo update agones
```

**Lệnh này làm gì**: Tải danh sách charts mới nhất từ repo vừa thêm.

### Bước 8.2 - Cài Agones vào namespace agones-system

```bash
helm install agones agones/agones \
    --namespace agones-system \
    --create-namespace \
    --set "agones.ping.http.expose=false" \
    --wait \
    --timeout 3m
```

**Giải thích từng flag**:

| Flag | Ý nghĩa |
|------|---------|
| `agones agones/agones` | Tên release là `agones`, dùng chart `agones` từ repo `agones` |
| `--namespace agones-system` | Cài vào namespace riêng, tách với app Fomoxa |
| `--create-namespace` | Tự tạo namespace nếu chưa có |
| `--set "agones.ping.http.expose=false"` | Tắt endpoint ping HTTP (không cần cho local dev) |
| `--wait` | Đợi tất cả pod Agones Ready trước khi return |
| `--timeout 3m` | Timeout 3 phút - Agones kéo nhiều image, có thể chậm |

**Kết quả mong đợi** (1–3 phút):

```
NAME: agones
LAST DEPLOYED: ...
NAMESPACE: agones-system
STATUS: deployed
```

### Bước 8.3 - Xác nhận Agones đang chạy

```bash
kubectl get pods -n agones-system
```

**Kết quả mong đợi**:

```
NAME                                             READY   STATUS    RESTARTS   AGE
agones-allocator-xxxxxxx-xxxxx                   1/1     Running   0          2m
agones-controller-xxxxxxx-xxxxx                  1/1     Running   0          2m
agones-extensions-xxxxxxx-xxxxx                  1/1     Running   0          2m
agones-ping-xxxxxxx-xxxxx                        1/1     Running   0          2m
```

```bash
# Kiểm tra CRD của Agones đã được đăng ký
kubectl get crd | grep agones
# Kết quả mong đợi: thấy gameservers.agones.dev, fleets.agones.dev, ...
```

> **Nếu đã cài Agones trước đó** (cluster còn tồn tại): `helm upgrade agones agones/agones -n agones-system --wait`

---

## 9. Build Docker images & import vào k3d

### Tại sao phải import vào k3d?

k3d chạy Kubernetes trong Docker container. Container đó không chia sẻ Docker image cache với host. Nếu chỉ build image trên host, Kubernetes trong container không thể tìm thấy image đó. Phải dùng `k3d image import` để copy image từ host vào trong container.

Đây là lý do manifest dùng `imagePullPolicy: IfNotPresent` - không kéo từ registry, dùng image đã import.

### Bước 9.1 - Clone gateway source (nếu chưa có)

Gateway là binary closed-source từ Fomoxa team, được build từ source:

```bash
# Kiểm tra có chưa
ls gateway/ 2>/dev/null || echo "Chưa có"

# Nếu chưa có, clone
git clone --depth 1 -b 0.2.0 https://github.com/fomoxa/gateway.git gateway
```

**Lệnh này làm gì**: Clone repo gateway ở tag `0.2.0` (shallow clone, chỉ lấy commit mới nhất). Thư mục `gateway/` được git-ignore nên không commit vào repo này.

### Bước 9.2 - Build image fomoxa-server

`fomoxa-server` là game server C# - nhận UDP packets từ clients qua gateway.

```bash
docker build -f deploy/Dockerfile.server -t fomoxa-server:latest .
```

**Lệnh này làm gì**: Build image từ `Dockerfile.server` trong thư mục `deploy/`, context là thư mục gốc repo (`.`). Đặt tag `fomoxa-server:latest`.

**Kết quả mong đợi**: Kết thúc bằng:
```
Successfully built <hash>
Successfully tagged fomoxa-server:latest
```

### Bước 9.3 - Build image fomoxa-directory

Directory service quản lý đăng ký và định tuyến game server pods.

```bash
docker build -f deploy/Dockerfile.directory -t fomoxa-directory:latest .
```

### Bước 9.4 - Build image fomoxa-gateway

Gateway nhận UDP packets từ client, giải mã connect token, route đến đúng game server pod.

```bash
docker build -f deploy/Dockerfile.gateway -t fomoxa-gateway:latest .
```

### Bước 9.5 - Build image fomoxa-backend

Backend là fRPC server C# - xử lý login, matchmaking, phát connect tokens.

```bash
docker build -f MyGame.Backend/Dockerfile -t fomoxa-backend:latest MyGame.Backend
```

**Lưu ý**: Context là `MyGame.Backend` (không phải `.`) vì Dockerfile của backend chỉ cần source trong thư mục đó.

### Bước 9.6 - Kiểm tra images đã build

```bash
docker images | grep fomoxa
```

**Kết quả mong đợi**:

```
REPOSITORY           TAG       IMAGE ID       CREATED        SIZE
fomoxa-server        latest    abc123...      1 minute ago   ???
fomoxa-directory     latest    def456...      1 minute ago   ???
fomoxa-gateway       latest    ghi789...      1 minute ago   ???
fomoxa-backend       latest    jkl012...      1 minute ago   ???
```

### Bước 9.7 - Import tất cả images vào k3d

```bash
k3d image import \
    fomoxa-server:latest \
    fomoxa-directory:latest \
    fomoxa-gateway:latest \
    fomoxa-backend:latest \
    -c fomoxa
```

**Lệnh này làm gì**: Copy 4 image từ Docker daemon của host vào Docker daemon bên trong container k3d cluster tên `fomoxa`. Quá trình này có thể mất 1–3 phút tuỳ kích thước image.

**Kết quả mong đợi**:

```
INFO[0000] Importing image(s) into cluster 'fomoxa'
INFO[0000] Starting new tools node...
INFO[0002] Importing images into nodes...
INFO[0060] Removing tools node...
INFO[0060] Successfully imported image(s) into cluster 'fomoxa'!
```

> **Hoặc chạy gọn toàn bộ build + import**: `./scripts/build-images.sh && k3d image import fomoxa-server:latest fomoxa-directory:latest fomoxa-gateway:latest fomoxa-backend:latest -c fomoxa`

---

## 10. Tạo Kubernetes Secrets & apply manifests

### Bước 10.1 - Tạo namespace fomoxa

```bash
kubectl apply -f deploy/k8s/namespace.yaml
```

**Lệnh này làm gì**: Tạo namespace `fomoxa` trong cluster nếu chưa có. Namespace là "phòng" cách ly - mọi resource của Fomoxa app đều sống trong đây.

**Kiểm tra**:
```bash
kubectl get namespace fomoxa
# NAME     STATUS   AGE
# fomoxa   Active   5s
```

### Bước 10.2 - Tạo Kubernetes Secrets từ file

Bước này chuyển các file hex trong `deploy/secrets/` thành Kubernetes Secret objects. Các Deployment sau này mount secret này vào container dưới dạng file.

```bash
# Secret 1: fomoxa-directory-keys (directory service dùng)
kubectl create secret generic fomoxa-directory-keys \
    --namespace=fomoxa \
    --from-file=backend=deploy/secrets/directory/backend \
    --from-file=pod=deploy/secrets/directory/pod \
    --from-file=gateway=deploy/secrets/directory/gateway \
    --from-file=admin=deploy/secrets/directory/admin \
    --from-file=root=deploy/secrets/directory/root \
    --dry-run=client -o yaml | kubectl apply -f -
```

**Giải thích**:
- `--from-file=KEY=PATH`: đọc nội dung file `PATH`, lưu vào secret với key là `KEY`
- `--dry-run=client -o yaml | kubectl apply -f -`: thay vì `create` (lỗi nếu đã tồn tại), dùng pattern này để idempotent - chạy lại không báo lỗi

```bash
# Secret 2: fomoxa-backend-keys (backend service dùng)
kubectl create secret generic fomoxa-backend-keys \
    --namespace=fomoxa \
    --from-file=backend=deploy/secrets/directory/backend \
    --from-file=backend-pod=deploy/secrets/backend-pod \
    --dry-run=client -o yaml | kubectl apply -f -
```

```bash
# Secret 3: fomoxa-pod-key (game server pod dùng)
kubectl create secret generic fomoxa-pod-key \
    --namespace=fomoxa \
    --from-file=pod=deploy/secrets/directory/pod \
    --dry-run=client -o yaml | kubectl apply -f -
```

**Kiểm tra secrets đã tạo**:

```bash
kubectl get secrets -n fomoxa
# NAME                     TYPE     DATA   AGE
# fomoxa-directory-keys    Opaque   5      5s
# fomoxa-backend-keys      Opaque   2      5s
# fomoxa-pod-key           Opaque   1      5s
```

> **Hoặc chạy gọn**: `./scripts/gen-k8s-secrets.sh fomoxa` - script làm đúng 3 lệnh trên.

### Bước 10.3 - Apply manifest Postgres

```bash
kubectl apply -f deploy/k8s/postgres.yaml
```

**Lệnh này tạo**: Deployment `postgres` + Service `postgres` trong namespace `fomoxa`. Nakama dùng Postgres làm database lưu accounts, match state.

**Đợi Postgres sẵn sàng**:

```bash
kubectl rollout status deployment/postgres -n fomoxa --timeout=90s
# Kết quả mong đợi: deployment "postgres" successfully rolled out
```

### Bước 10.4 - Apply manifest Nakama

```bash
kubectl apply -f deploy/k8s/nakama.yaml
```

**Lệnh này tạo**:
- ConfigMap `nakama-modules`: chứa Lua module `main.lua` (matchmaking logic)
- Deployment `nakama`: Nakama server (image `heroiclabs/nakama:3.41.0`)
  - initContainer `migrate`: chạy database migration trước khi Nakama start
- Service `nakama`: expose port 7350 (API) và 7351 (console) trong cluster

**Đợi Nakama sẵn sàng** (có thể mất 60–120 giây lần đầu - kéo image):

```bash
kubectl rollout status deployment/nakama -n fomoxa --timeout=120s
```

### Bước 10.5 - Apply manifest Directory

```bash
kubectl apply -f deploy/k8s/directory.yaml
```

**Lệnh này tạo**:
- ConfigMap `directory-config`: chứa `directory.toml` (cấu hình service)
- Deployment `directory`: service discovery binary
- Service `directory`: expose port 9500 (API) và 9501 (admin) - chỉ trong cluster

```bash
kubectl rollout status deployment/directory -n fomoxa --timeout=60s
```

### Bước 10.6 - Apply manifest Gateway

```bash
kubectl apply -f deploy/k8s/gateway.yaml
```

**Lệnh này tạo**:
- ConfigMap `gateway-config`: chứa `gateway.toml`
- Deployment `gateway`: UDP gateway binary
- Service `gateway` (type: LoadBalancer): expose UDP port 7777 - k3d forward ra host port 7777

```bash
kubectl rollout status deployment/gateway -n fomoxa --timeout=60s
```

### Bước 10.7 - Apply manifest Backend

```bash
kubectl apply -f deploy/k8s/backend.yaml
```

**Lệnh này tạo**:
- Deployment `backend`: C# fRPC server
  - readinessProbe và livenessProbe dùng `fomoxa-rpc-health.dll` - health check TCP 9600
- Service `backend` (type: LoadBalancer): expose TCP port 9600 - k3d forward ra host port 9600

```bash
kubectl rollout status deployment/backend -n fomoxa --timeout=60s
```

### Bước 10.8 - Apply Agones Fleet (game server pool)

```bash
kubectl apply -f deploy/k8s/fleet.yaml
```

**Lệnh này tạo**: Fleet `fomoxa-server` với `replicas: 2`. Agones sẽ tự tạo 2 GameServer pods (mỗi pod có 2 container: `fomoxa-server` + `agones-gameserver-sidecar`).

**Khác với Deployment thông thường**: Không dùng `rollout status` - thay vào đó xem trạng thái GameServer (Bước 11).

### Bước 10.9 - Apply ingress (tùy chọn)

```bash
# Chỉ cần nếu file tồn tại
if [ -f deploy/k8s/ingress.yaml ]; then
    kubectl apply -f deploy/k8s/ingress.yaml
fi
```

---

## 11. Kiểm tra trạng thái pods / GameServers

### Bước 11.1 - Xem tất cả pods trong namespace fomoxa

```bash
kubectl get pods -n fomoxa
```

**Kết quả mong đợi** (sau 2–3 phút):

```
NAME                          READY   STATUS    RESTARTS   AGE
postgres-xxxxxxxxx-xxxxx      1/1     Running   0          3m
nakama-xxxxxxxxx-xxxxx        1/1     Running   0          2m
directory-xxxxxxxxx-xxxxx     1/1     Running   0          90s
gateway-xxxxxxxxx-xxxxx       1/1     Running   0          80s
backend-xxxxxxxxx-xxxxx       1/1     Running   0          70s
fomoxa-server-xxxx-yyyy       2/2     Running   0          60s
fomoxa-server-xxxx-zzzz       2/2     Running   0          60s
```

**Ý nghĩa các cột**:

| Cột | Ý nghĩa |
|-----|---------|
| `READY` | `1/1` = 1/1 container trong pod đang sẵn sàng. `2/2` = 2 container (game server + agones sidecar) |
| `STATUS` | `Running` = bình thường. `Pending` = đang kéo image hoặc chờ tài nguyên. `CrashLoopBackOff` = lỗi, xem log |
| `RESTARTS` | Số lần restart. `> 0` thường là dấu hiệu lỗi |

**Xem thêm chi tiết một pod**:

```bash
kubectl describe pod <tên-pod> -n fomoxa
# Ví dụ:
kubectl describe pod backend-xxxxxxxxx-xxxxx -n fomoxa
```

### Bước 11.2 - Xem trạng thái Agones GameServers

```bash
kubectl get gameservers -n fomoxa
# Hoặc viết tắt:
kubectl get gs -n fomoxa
```

**Kết quả mong đợi**:

```
NAME                       STATE   ADDRESS      PORT   NODE                  AGE
fomoxa-server-xxxx-yyyy    Ready   10.42.0.x    7778   k3d-fomoxa-server-0   2m
fomoxa-server-xxxx-zzzz    Ready   10.42.0.y    7778   k3d-fomoxa-server-0   2m
```

**Ý nghĩa các STATE**:

| STATE | Ý nghĩa |
|-------|---------|
| `Starting` | Pod đang khởi động, chưa đăng ký với Agones sidecar |
| `Ready` | Pod đã khởi động, đã báo ready với Agones, **sẵn sàng nhận player** |
| `Allocated` | Có player đang chơi trên pod này - directory đã assign cho session |
| `Shutdown` | Pod đang drain (chờ hết player rồi terminate) |

> **Quan trọng**: Game server pod phải ở trạng thái **Ready** trước khi nó có thể đăng ký với directory service để nhận phòng (`arena:1`, `arena:2`, ...). Code trong `AgonesLifecycle.cs` gọi `sdk.Ready()` → Agones chuyển state sang `Ready` → rồi mới gọi register với directory.

### Bước 11.3 - Xem Fleet status

```bash
kubectl get fleets -n fomoxa
```

**Kết quả mong đợi**:

```
NAME             SCHEDULING   DESIRED   CURRENT   ALLOCATED   READY   AGE
fomoxa-server    Packed       2         2         0           2       5m
```

- `DESIRED`: số replicas mong muốn (fleet.yaml `replicas: 2`)
- `CURRENT`: số GameServer đang tồn tại
- `ALLOCATED`: số đang có player
- `READY`: số đang ở trạng thái Ready (có thể nhận player mới)

---

## 12. Cấu hình quan trọng

### 12.1 - public_address trong directory.toml

File: `deploy/k8s/directory.yaml` (phần ConfigMap)

```toml
public_address = "127.0.0.1:7777"
```

**Đây là địa chỉ client thấy** - địa chỉ Gateway mà Directory sẽ trả về cho client khi tìm được game server.

**Tại sao không dùng DNS nội bộ cluster (`gateway.fomoxa.svc.cluster.local`)?**
Client chạy bên ngoài cluster. Client không giải được DNS nội bộ cluster. Directory phải trả về địa chỉ client có thể reach.

- **Môi trường local (k3d)**: `127.0.0.1:7777` - k3d forward port 7777 từ host vào cluster
- **Môi trường cloud**: IP hoặc hostname public của LoadBalancer/ingress
- **Môi trường staging**: IP của máy chủ Linux

**Thay đổi** khi deploy lên server thật:

```toml
public_address = "203.0.113.42:7777"   # IP public của server
```

### 12.2 - drain_seconds và terminationGracePeriodSeconds

Trong `directory.toml`:
```toml
drain_seconds = 300
```

Trong `fleet.yaml`:
```yaml
terminationGracePeriodSeconds: 360
```

**Ràng buộc quan trọng**: `terminationGracePeriodSeconds` **phải lớn hơn** `drain_seconds`.

**Tại sao?** Khi Kubernetes muốn terminate một pod (rolling update, scale down):
1. Kubernetes gửi SIGTERM → pod bắt đầu drain (không nhận player mới, chờ hết player cũ)
2. Drain kéo dài tối đa `drain_seconds` (300s = 5 phút)
3. Kubernetes đợi `terminationGracePeriodSeconds` (360s) trước khi SIGKILL

Nếu `terminationGracePeriodSeconds` ≤ `drain_seconds`, Kubernetes sẽ SIGKILL pod trước khi drain xong → player bị disconnect đột ngột.

### 12.3 - Cấu hình version

Trong `fleet.yaml`:
```yaml
- name: FOMOXA_VERSION
  value: "1.0"
```

Trong `directory.toml`:
```toml
[[placement]]
filter = "pod.version == client.version"
```

Directory chỉ assign player vào pod có cùng version. Khi build Unity client, version phải khớp với `FOMOXA_VERSION` trong fleet. Nếu khác version, client không được assign pod → không connect được game.

### 12.4 - Cấu hình gateway.toml

```toml
listen = "0.0.0.0:7777"          # Lắng nghe UDP trên mọi interface
pod_bind = "0.0.0.0:0"           # Bind port ngẫu nhiên để forward đến pod
directory = "directory.fomoxa.svc.cluster.local:9500"   # DNS nội bộ cluster

[keys]
gateway = { file = "/secrets/directory/gateway" }   # Xác thực với directory
```

**`pod_bind = "0.0.0.0:0"`**: Gateway cần kết nối đến pod (trong cùng cluster). Bind `:0` = kernel chọn port ngẫu nhiên, không cần config cứng.

---

## 13. Test end-to-end

### Bước 13.1 - Test Nakama matchmaking (curl thủ công)

Test này không cần script, chỉ dùng `curl`:

```bash
# Biến môi trường
NAKAMA="http://127.0.0.1:7350"
SERVER_KEY="defaultkey"
HTTP_KEY="defaulthttpkey"
ROOM="arena:1"

# Tạo base64 credentials
AUTH_SERVER=$(printf "%s:" "$SERVER_KEY" | base64)
AUTH_HTTP=$(printf "%s:" "$HTTP_KEY" | base64)
```

**Bước 1: Đăng nhập device A**

```bash
RESP_A=$(curl -s -X POST "$NAKAMA/v2/account/authenticate/device?create=true" \
    -H "Authorization: Basic $AUTH_SERVER" \
    -H "Content-Type: application/json" \
    -d '{"id": "test-device-ana", "create": true}')

TOKEN_A=$(echo "$RESP_A" | grep -o '"token":"[^"]*' | cut -d'"' -f4)
echo "Token A: ${TOKEN_A:0:20}..."
```

**Kết quả mong đợi**: TOKEN_A không rỗng.

**Bước 2: Đăng nhập device B**

```bash
RESP_B=$(curl -s -X POST "$NAKAMA/v2/account/authenticate/device?create=true" \
    -H "Authorization: Basic $AUTH_SERVER" \
    -H "Content-Type: application/json" \
    -d '{"id": "test-device-bo", "create": true}')

TOKEN_B=$(echo "$RESP_B" | grep -o '"token":"[^"]*' | cut -d'"' -f4)
echo "Token B: ${TOKEN_B:0:20}..."
```

**Bước 3: Reset queue (đảm bảo bắt đầu sạch)**

```bash
curl -s -X POST "$NAKAMA/v2/rpc/reset_match?unwrap" \
    -H "Authorization: Basic $AUTH_HTTP" \
    -H "Content-Type: application/json" \
    -d "{\"room\":\"$ROOM\"}"
# Kết quả mong đợi: {"ok":true}
```

**Bước 4: A tìm match → phải waiting**

```bash
MATCH_A=$(curl -s -X POST "$NAKAMA/v2/rpc/find_match?unwrap" \
    -H "Authorization: Bearer $TOKEN_A" \
    -H "Content-Type: application/json" \
    -d "{\"room\":\"$ROOM\"}")
echo "A: $MATCH_A"
# Kết quả mong đợi: {"status":"waiting","room":"arena:1"}
```

**Bước 5: B tìm match → phải matched**

```bash
MATCH_B=$(curl -s -X POST "$NAKAMA/v2/rpc/find_match?unwrap" \
    -H "Authorization: Bearer $TOKEN_B" \
    -H "Content-Type: application/json" \
    -d "{\"room\":\"$ROOM\"}")
echo "B: $MATCH_B"
# Kết quả mong đợi: {"status":"matched","room":"arena:1","ticket":"<uuid>","exp":<unix>}
```

**Bước 6: Lấy ticket**

```bash
TICKET=$(echo "$MATCH_B" | grep -o '"ticket":"[^"]*' | cut -d'"' -f4)
echo "Ticket: $TICKET"
# Phải là UUID, không rỗng
```

**Bước 7: A poll → cũng phải thấy matched với cùng ticket**

```bash
POLL_A=$(curl -s -X POST "$NAKAMA/v2/rpc/poll_match?unwrap" \
    -H "Authorization: Bearer $TOKEN_A" \
    -H "Content-Type: application/json" \
    -d "{\"room\":\"$ROOM\"}")
echo "Poll A: $POLL_A"
# Kết quả mong đợi: {"status":"matched",...,"ticket":"<cùng UUID với MATCH_B>"}
```

> **Hoặc chạy gọn**: `./scripts/test-nakama.sh` - script làm đúng 7 bước trên với assertions tự động.

---

### Bước 13.2 - Kiểm tra GameServer thủ công

```bash
# Xem tất cả GameServers và địa chỉ
kubectl get gs -n fomoxa -o wide

# Kết quả mong đợi: STATE = Ready
# NAME                       STATE   ADDRESS      PORT   ...
# fomoxa-server-xxxx-yyyy    Ready   10.42.0.x    7778   ...
```

```bash
# Xem log của một game server pod
kubectl logs -n fomoxa <tên-pod-fomoxa-server> -c fomoxa-server --tail=30

# Kết quả mong đợi trong log:
# [Pod] registering pod-xxxx (10.42.0.x:7778, version 1.0) at directory:9500
# [Pod] registered id=1 changed=false
# [Pod] holds arena:1
```

### Bước 13.3 - Test auto room pool (4 người chơi)

```bash
# Kiểm tra kết quả pool routing thủ công:
# Gửi 4 người chơi với room="auto", kiểm tra nhận 2 phòng khác nhau

# Reset pool counter
curl -s -X POST "$NAKAMA/v2/rpc/reset_match?unwrap" \
    -H "Authorization: Basic $AUTH_HTTP" \
    -H "Content-Type: application/json" \
    -d '{"room":"auto"}'
```

> **Hoặc chạy gọn**: `./scripts/test-nakama-pool.sh` - test đầy đủ với 4 người chơi, xác nhận 2 cặp nhận 2 phòng khác nhau.

---

## 14. Vận hành: logs, scale, drain, restart

### Xem logs

```bash
# Log của một service cụ thể (theo deployment name)
kubectl logs -n fomoxa deployment/backend --tail=50

# Log realtime (follow)
kubectl logs -n fomoxa deployment/gateway -f

# Log của một pod cụ thể, container cụ thể
kubectl logs -n fomoxa <pod-name> -c fomoxa-server -f

# Log của tất cả pods có label app=nakama
kubectl logs -n fomoxa -l app=nakama --tail=100

# Log của pod đã crash (container trước đó)
kubectl logs -n fomoxa <pod-name> --previous
```

### Scale Fleet (thêm/bớt game server)

```bash
# Scale lên 4 game servers
kubectl scale fleet fomoxa-server --replicas=4 -n fomoxa

# Scale xuống 1 (Agones sẽ drain pods không có player trước khi xóa)
kubectl scale fleet fomoxa-server --replicas=1 -n fomoxa

# Kiểm tra sau scale
kubectl get gs -n fomoxa
```

### Restart một service

```bash
# Restart deployment (rolling restart - không downtime)
kubectl rollout restart deployment/backend -n fomoxa
kubectl rollout restart deployment/gateway -n fomoxa
kubectl rollout restart deployment/directory -n fomoxa
kubectl rollout restart deployment/nakama -n fomoxa

# Đợi cho đến khi restart xong
kubectl rollout status deployment/backend -n fomoxa
```

### Sau khi build lại image

```bash
# 1. Build image mới
docker build -f deploy/Dockerfile.server -t fomoxa-server:latest .

# 2. Import vào k3d
k3d image import fomoxa-server:latest -c fomoxa

# 3. Restart để pods dùng image mới
kubectl rollout restart deployment/gateway -n fomoxa
# Với Fleet (Agones): xóa Fleet rồi apply lại
kubectl delete fleet fomoxa-server -n fomoxa
kubectl apply -f deploy/k8s/fleet.yaml
```

### Drain một game server (graceful shutdown)

Drain = báo cho pod "không nhận player mới, chờ hết player hiện tại rồi tắt":

```bash
# Xem GameServer Allocated
kubectl get gs -n fomoxa

# Xóa một GameServer cụ thể - Agones sẽ drain (không SIGKILL ngay)
kubectl delete gs <gameserver-name> -n fomoxa

# Fleet sẽ tự tạo pod mới để duy trì replicas
```

### Xem events trong namespace

```bash
kubectl get events -n fomoxa --sort-by='.lastTimestamp' | tail -20
```

**Hữu ích khi debug**: Events cho biết tại sao pod không start, image pull fail, mount secret lỗi, v.v.

---

## 15. Dọn dẹp

### Tùy chọn A - Chỉ xóa app, giữ cluster k3d

Xóa toàn bộ namespace `fomoxa` (app + secrets + config), nhưng giữ cluster k3d và Agones:

```bash
kubectl delete namespace fomoxa --ignore-not-found=true
```

**Kết quả**: Xóa mọi resource trong namespace `fomoxa`. k3d cluster còn đó, lần sau chỉ cần apply lại secrets + manifests (Bước 10).

> **Hoặc gọn**: `./scripts/k8s-down.sh --keep-cluster`

### Tùy chọn B - Xóa toàn bộ cluster k3d

```bash
k3d cluster delete fomoxa
```

**Kết quả**: Xóa hoàn toàn cluster, bao gồm Agones, tất cả namespaces, tất cả volumes. Lần sau phải làm lại từ Bước 7 (tạo cluster).

Docker images vẫn còn trên host - không cần build lại.

> **Hoặc gọn**: `./scripts/k8s-down.sh`

### Tùy chọn C - Xóa cả Docker images

```bash
docker rmi fomoxa-server:latest fomoxa-directory:latest fomoxa-gateway:latest fomoxa-backend:latest
```

---

## 16. Troubleshooting chi tiết

### 16.1 - `Cannot connect to the Docker daemon`

**Triệu chứng**:
```
docker: Cannot connect to the Docker daemon at unix:///var/run/docker.sock.
```

**Nguyên nhân A - WSL2**: Docker Desktop chưa chạy hoặc WSL Integration chưa bật.
```bash
# Kiểm tra
ls /var/run/docker.sock
# Nếu không có file này → Docker Desktop chưa chạy hoặc chưa bật WSL Integration
```
**Cách sửa**: Mở Docker Desktop trên Windows → Settings → Resources → WSL Integration → bật Ubuntu → Apply & Restart.

**Nguyên nhân B - Linux thuần**: Docker daemon chưa start.
```bash
sudo systemctl start docker
sudo systemctl status docker
```

**Nguyên nhân C - Permission**: User chưa trong group docker.
```bash
groups $USER | grep docker
# Nếu không thấy "docker": sudo usermod -aG docker $USER && newgrp docker
```

---

### 16.2 - Port đã bị chiếm khi tạo cluster

**Triệu chứng**:
```
failed to create cluster ... port "7777" already allocated
```

**Tìm process đang dùng cổng**:
```bash
sudo ss -tulnp | grep 7777
# Hoặc
sudo lsof -i :7777
```

**Kill process đó**:
```bash
sudo kill -9 <PID>
```

**Nếu do cluster cũ còn tồn tại**:
```bash
k3d cluster list
k3d cluster delete fomoxa
# Rồi tạo lại
```

---

### 16.3 - Pod ở trạng thái `CrashLoopBackOff`

**Xem log để tìm lỗi**:
```bash
kubectl logs -n fomoxa <pod-name> --previous
```

**Xem events**:
```bash
kubectl describe pod <pod-name> -n fomoxa | grep -A 20 Events
```

**Nguyên nhân thường gặp**:

| Lỗi trong log | Nguyên nhân | Cách sửa |
|---------------|-------------|----------|
| `open /secrets/directory/backend: no such file` | Secret chưa được mount | Kiểm tra K8s Secret tồn tại: `kubectl get secret fomoxa-directory-keys -n fomoxa` |
| `connection refused: directory:9500` | Directory pod chưa ready | Đợi directory pod Running, kiểm tra `kubectl get pods -n fomoxa` |
| `failed to pull image` | Image chưa import vào k3d | Chạy lại `k3d image import ... -c fomoxa` |
| `EPERM: operation not permitted` | Container cần quyền đặc biệt | Xem `securityContext` trong manifest |

---

### 16.4 - GameServer mãi ở trạng thái `Starting`, không lên `Ready`

**Xem log container fomoxa-server**:
```bash
kubectl logs -n fomoxa <gameserver-pod-name> -c fomoxa-server
```

**Xem log container agones sidecar**:
```bash
kubectl logs -n fomoxa <gameserver-pod-name> -c agones-gameserver-sidecar
```

**Nguyên nhân thường gặp**:

| Vấn đề | Dấu hiệu | Cách sửa |
|---------|----------|----------|
| Game server không gọi `sdk.Ready()` | Agones sidecar log "waiting for SDK Ready" | Kiểm tra `AgonesLifecycle.cs` - `Ready()` có được gọi không |
| Directory không respond | `dial tcp directory:9500: connection refused` | `kubectl rollout status deployment/directory -n fomoxa` |
| Pod key sai | `unauthorized` trong log | Tạo lại secrets và apply lại `fomoxa-pod-key` |

---

### 16.5 - Nakama trả về lỗi `lua runtime error`

**Xem log Nakama**:
```bash
kubectl logs -n fomoxa deployment/nakama -f | grep -i error
```

**Nguyên nhân thường gặp**: Lua module `main.lua` có lỗi cú pháp.

**Kiểm tra ConfigMap**:
```bash
kubectl get configmap nakama-modules -n fomoxa -o yaml | grep -A 5 "main.lua"
```

**Sau khi sửa `main.lua`**:
```bash
# Apply lại ConfigMap
kubectl apply -f deploy/k8s/nakama.yaml
# Restart Nakama để reload module
kubectl rollout restart deployment/nakama -n fomoxa
```

---

### 16.6 - `kubectl` trỏ sai cluster

**Triệu chứng**: Lệnh kubectl báo lỗi lạ hoặc resource không tìm thấy.

```bash
# Xem context hiện tại
kubectl config current-context
# Phải là: k3d-fomoxa

# Xem tất cả contexts
kubectl config get-contexts

# Chuyển sang đúng context
kubectl config use-context k3d-fomoxa
```

---

### 16.7 - `k3d image import` chậm hoặc bị treo

**Nguyên nhân**: Image quá lớn, hoặc Docker daemon bận.

**Cách sửa**: Chạy từng image một thay vì cùng lúc:
```bash
k3d image import fomoxa-server:latest -c fomoxa
k3d image import fomoxa-directory:latest -c fomoxa
k3d image import fomoxa-gateway:latest -c fomoxa
k3d image import fomoxa-backend:latest -c fomoxa
```

---

### 16.8 - Nakama matchmaking: player A xong B nhưng không matched

**Nguyên nhân thường gặp**: Queue còn dữ liệu từ lần test trước.

**Cách sửa**: Reset queue trước khi test:
```bash
curl -s -X POST "http://127.0.0.1:7350/v2/rpc/reset_match?unwrap" \
    -H "Authorization: Basic $(printf 'defaulthttpkey:' | base64)" \
    -H "Content-Type: application/json" \
    -d '{"room":"arena:1"}'
```

Hoặc restart Nakama (xóa sạch in-memory state):
```bash
kubectl rollout restart deployment/nakama -n fomoxa
```

---

## 17. Checklist tự xác nhận

Tôi đã triển khai thành công khi:

- [ ] `docker ps` không báo lỗi, liệt kê containers bình thường
- [ ] `kubectl version --client` in phiên bản ≥ 1.28
- [ ] `k3d version` in phiên bản `v5.x`
- [ ] `helm version --short` in phiên bản `v3.x`
- [ ] `k3d cluster list` thấy cluster `fomoxa` ở trạng thái Running
- [ ] `kubectl config current-context` in `k3d-fomoxa`
- [ ] `kubectl get pods -n agones-system` thấy `agones-controller` ở trạng thái `1/1 Running`
- [ ] Thư mục `deploy/secrets/directory/` có 5 file: `root`, `admin`, `backend`, `gateway`, `pod`
- [ ] `kubectl get secrets -n fomoxa` thấy 3 secrets: `fomoxa-directory-keys`, `fomoxa-backend-keys`, `fomoxa-pod-key`
- [ ] `kubectl get pods -n fomoxa` tất cả pods đều `Running`, không có `CrashLoopBackOff`
- [ ] `kubectl get gs -n fomoxa` thấy ít nhất 1 GameServer ở trạng thái `Ready`
- [ ] Test matchmaking: A vào → `waiting`, B vào → `matched`, cùng ticket UUID
- [ ] Log game server pod chứa `[Pod] holds arena:1`

---

## 18. Phụ lục: Lệnh hay dùng & Script tương đương

### Lệnh kubectl hay dùng

```bash
# Xem tất cả resources trong namespace fomoxa
kubectl get all -n fomoxa

# Watch pods realtime
kubectl get pods -n fomoxa -w

# Exec vào container để debug
kubectl exec -it <pod-name> -n fomoxa -- /bin/sh

# Xem resource usage (cần metrics-server)
kubectl top pods -n fomoxa

# Port-forward để test service trực tiếp
kubectl port-forward -n fomoxa svc/nakama 7350:7350

# Xem toàn bộ config của một resource
kubectl get deployment backend -n fomoxa -o yaml

# Xóa một pod (Deployment sẽ tạo lại tự động)
kubectl delete pod <pod-name> -n fomoxa

# Xem configmap
kubectl get configmap directory-config -n fomoxa -o yaml
```

### Map Script ↔ Lệnh tương đương

| Script | Tương đương lệnh tay |
|--------|---------------------|
| `./scripts/gen-keys.sh` | Bước 6.2: 6 lệnh `openssl rand -hex ... > deploy/secrets/...` |
| `./scripts/build-images.sh` | Bước 9.1–9.5: clone gateway + 4 lệnh `docker build -t ...` |
| `./scripts/gen-k8s-secrets.sh fomoxa` | Bước 10.2: 3 lệnh `kubectl create secret generic ... --dry-run=client -o yaml \| kubectl apply -f -` |
| `./scripts/k8s-up.sh` | Bước 7 + 8 + 9.7 + 10: tạo cluster, cài Agones, import images, apply manifests |
| `./scripts/k8s-down.sh` | `k3d cluster delete fomoxa` |
| `./scripts/k8s-down.sh --keep-cluster` | `kubectl delete namespace fomoxa` |
| `./scripts/test-nakama.sh` | Bước 13.1: 7 lệnh curl thủ công |
| `./scripts/test-nakama-pool.sh` | Bước 13.3: test 4 người chơi với room auto |

### Xem log theo dõi cụm (một lệnh)

```bash
# Theo dõi tất cả log cùng lúc với stern (cài: https://github.com/stern/stern)
stern -n fomoxa "." --tail 20

# Hoặc dùng kubectl watch từng service:
kubectl logs -n fomoxa deployment/backend -f &
kubectl logs -n fomoxa deployment/directory -f &
kubectl logs -n fomoxa deployment/gateway -f &
# Ctrl+C để dừng tất cả
```

---

*Tài liệu này phản ánh kiến trúc tại commit `dc49499`. Khi có thay đổi cấu trúc cluster hoặc Dockerfile, cập nhật các bước tương ứng trong Mục 9 và 10.*
