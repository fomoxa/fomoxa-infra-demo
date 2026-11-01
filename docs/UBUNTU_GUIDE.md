# Hướng Dẫn Từng Bước Cài Đặt & Vận Hành Cụm Fomoxa Trên Ubuntu / WSL2

> **Dành cho người mới clone dự án về**: Hướng dẫn này đi từ máy Ubuntu trắng (Ubuntu 22.04 / 24.04 trên WSL2 hoặc máy chủ Linux) cho đến khi toàn bộ cụm Kubernetes (Postgres + Nakama + Directory + Gateway + Backend + Agones Game Server Fleet) chạy và vượt qua toàn bộ các bài kiểm thử.

---

## Mục lục
1. [Bước 1: Cài đặt công cụ phụ thuộc trên Ubuntu](#bước-1-cài-đặt-công-cụ-phụ-thuộc-trên-ubuntu)
2. [Bước 2: Clone dự án và phân quyền](#bước-2-clone-dự-án-và-phân-quyền)
3. [Bước 3: Vận hành cụm (Sinh khóa -> Build Image -> Khởi động K8s)](#bước-3-vận-hành-cụm-chỉ-với-3-lệnh)
4. [Bước 4: Kiểm tra trạng thái cụm K8s](#bước-4-kiểm-tra-trạng-thái-cụm-k8s)
5. [Bước 5: Chạy các bài kiểm thử thực tế](#bước-5-chạy-các-bài-kiểm-thử-thực-tế)
6. [Bước 6: Dọn dẹp & Tắt cụm khi hoàn tất](#bước-6-dọn-dẹp--tắt-cụm-khi-hoàn-tất)
7. [Xử lý các sự cố thường gặp](#xử-lý-các-sự-cố-thường-gặp)

---

## Bước 1: Cài đặt công cụ phụ thuộc trên Ubuntu

Mở terminal Ubuntu của bạn và copy các lệnh dưới đây để cài đặt 4 công cụ bắt buộc: **Docker**, **kubectl**, **k3d**, **helm**, cùng các tiện ích **curl**, **git**, **openssl**.

### 1.1. Cài đặt các gói cơ bản
```bash
sudo apt update && sudo apt install -y curl openssl git ca-certificates
```

### 1.2. Đảm bảo Docker đang hoạt động
- **Nếu chạy trên WSL2 (Windows)**:
  1. Cài đặt [Docker Desktop trên Windows](https://www.docker.com/products/docker-desktop/).
  2. Mở Docker Desktop -> góc phải chọn biểu tượng **Settings** (bánh răng) -> **Resources** -> **WSL Integration**.
  3. Bật công tắc (toggle) cho bản phân phối **Ubuntu** của bạn -> Nhấn **Apply & restart**.
  4. Kiểm tra trong terminal Ubuntu:
     ```bash
     docker ps
     ```
     *(Nếu hiển thị bảng danh sách container rỗng là đã thành công)*.

- **Nếu chạy trên máy Ubuntu độc lập**:
  ```bash
  curl -fsSL https://get.docker.com | sh
  sudo usermod -aG docker $USER
  newgrp docker
  docker ps
  ```

### 1.3. Cài đặt `kubectl` (K8s CLI)
```bash
if ! command -v kubectl >/dev/null 2>&1; then
    curl -LO "https://dl.k8s.io/release/$(curl -L -s https://dl.k8s.io/release/stable.txt)/bin/linux/amd64/kubectl"
    sudo install -o root -g root -m 0755 kubectl /usr/local/bin/kubectl
    rm kubectl
fi
kubectl version --client
```

### 1.4. Cài đặt `k3d` (Công cụ chạy K3s trong Docker)
```bash
if ! command -v k3d >/dev/null 2>&1; then
    curl -s https://raw.githubusercontent.com/k3d-io/k3d/main/install.sh | bash
fi
k3d version
```

### 1.5. Cài đặt `Helm` (Package Manager cho K8s)
```bash
if ! command -v helm >/dev/null 2>&1; then
    curl -fsSL https://raw.githubusercontent.com/helm/helm/main/scripts/get-helm-3 | bash
fi
helm version --short
```

---

## Bước 2: Clone dự án và phân quyền

Chuyển tới thư mục làm việc và clone mã nguồn:
```bash
# Đối với WSL2 (truy cập thư mục trên Windows, ví dụ ổ D:\)
cd /mnt/d/Documents/fomoxa

# Hoặc nếu clone mới:
# git clone <url-repo-fomoxa> fomoxa && cd fomoxa

# Cấp quyền thực thi cho toàn bộ bash script:
chmod +x scripts/*.sh
```

---

## Bước 3: Vận hành cụm (Chỉ với 3 lệnh)

### 3.1. Sinh bộ khóa bảo mật
```bash
./scripts/gen-keys.sh
```
*Lệnh này dùng `openssl` sinh ra các token bí mật cho `root`, `admin`, `backend`, `gateway`, `pod` và lưu tại `deploy/secrets/`.*

### 3.2. Đóng gói 4 Docker Images
```bash
./scripts/build-images.sh
```
*Script sẽ tự động kiểm tra upstream `fomoxa/gateway` (tag 0.2.0), tự clone nếu chưa có, sau đó đóng gói cả 4 container: `fomoxa-server`, `fomoxa-directory`, `fomoxa-gateway`, `fomoxa-backend`.*

### 3.3. Khởi động cụm Kubernetes + Agones
```bash
./scripts/k8s-up.sh
```
*Script sẽ tự động:*
1. Tạo cụm k3d `fomoxa` ánh xạ các cổng:
   - `7777/udp`: Gateway (kết nối Game Client).
   - `9600/tcp`: Backend (fRPC API login & matchmaking).
   - `7350/tcp`: Nakama API.
2. Cài đặt Agones Game Server Operator thông qua Helm vào namespace `agones-system`.
3. Import cả 4 images vừa build vào cụm k3d.
4. Sinh Kubernetes Secrets từ `deploy/secrets/`.
5. Triển khai toàn bộ hạ tầng trong `deploy/k8s/`: Postgres, Nakama, Directory, Gateway, Backend, và Agones Fleet.
6. Chờ tất cả pods chuyển sang trạng thái `Running` / `Ready`.

---

## Bước 4: Kiểm tra trạng thái cụm K8s

### 4.1. Xem danh sách Pods
```bash
kubectl get pods -n fomoxa
```
**Kết quả chuẩn:**
```text
NAME                          READY   STATUS    RESTARTS   AGE
postgres-xxx                  1/1     Running   0          2m
nakama-xxx                    1/1     Running   0          2m
directory-xxx                 1/1     Running   0          1m
gateway-xxx                   1/1     Running   0          1m
backend-xxx                   1/1     Running   0          1m
fomoxa-server-xxxx-yyyy       2/2     Running   0          45s
fomoxa-server-xxxx-zzzz       2/2     Running   0          45s
```
*(Mỗi pod `fomoxa-server` có 2 containers: 1 container game server và 1 container `agones-gameserver-sidecar`).*

### 4.2. Xem trạng thái Agones GameServers
```bash
kubectl get gs -n fomoxa
```
**Kết quả chuẩn:**
```text
NAME                      STATE   ADDRESS      PORT   NODE           AGE
fomoxa-server-xxxx-yyyy   Ready   10.42.0.xx   7778   k3d-fomoxa...  1m
fomoxa-server-xxxx-zzzz   Ready   10.42.0.yy   7778   k3d-fomoxa...  1m
```

---

## Bước 5: Chạy các bài kiểm thử thực tế

Tất cả các bài kiểm thử đều được viết sẵn thành script Bash:

### 5.1. Kiểm thử Nakama Matchmaking cơ bản (2 người chơi)
```bash
./scripts/test-nakama.sh
```
**Tiến trình kiểm thử:**
1. Tạo 2 người chơi ảo `ana` và `bo`.
2. `ana` gọi `find_match` -> nhận trạng thái `waiting`.
3. `bo` gọi `find_match` -> Nakama ghép đôi thành công -> nhận `matched` và cấp `ticket`.
4. `ana` poll trạng thái -> nhận đúng `ticket` của trận.
5. Backend gọi `verify_ticket` -> xác thực thành công (`ok: true`).
6. Kiểm tra người lạ hoặc ticket giả mạo -> bị từ chối chính xác.

### 5.2. Kiểm thử Auto Room Pool $\ge 4$ người chơi (P5.5)
```bash
./scripts/test-nakama-pool.sh
```
**Tiến trình kiểm thử:**
1. Tạo 4 người chơi ảo (`ana`, `bo`, `chi`, `dung`) gửi yêu cầu ghép với `room: "auto"`.
2. Cặp 1 (`ana` + `bo`) được tự động ghép và cấp phòng `arena:1`.
3. Cặp 2 (`chi` + `dung`) được tự động ghép và cấp phòng `arena:2` (xoay vòng trong pool `arena:1..4`).
4. Chứng minh 2 cặp nhận 2 phòng riêng biệt, không bị tranh chấp phòng.

### 5.3. Kiểm thử Game Server Agones & Client Unity (P5 DoD)
```bash
./scripts/test-p5.sh
```
- Script kiểm tra GameServers trên K8s.
- Nếu chạy trong WSL2 trên máy tính có Unity Client, script sẽ gọi client kết nối vào Gateway `127.0.0.1:7777`.
- Kiểm tra GameServer chuyển trạng thái từ `Ready` sang `Allocated` khi có người chơi tham gia.

---

## Bước 6: Dọn dẹp & Tắt cụm khi hoàn tất

### Tùy chọn A: Xóa toàn bộ cụm k3d
Khi không dùng nữa, xóa cụm để giải phóng RAM/CPU:
```bash
./scripts/k8s-down.sh
```

### Tùy chọn B: Chỉ xóa các app, giữ lại cụm k3d
Nếu muốn giữ cụm k3d để sau này apply lại cho nhanh:
```bash
./scripts/k8s-down.sh --keep-cluster
```

### Tùy chọn C: Dừng nhanh các tiến trình local
Nếu bạn từng chạy cụm local ngoài K8s:
```bash
./scripts/stop-all.sh
```

---

## Xử lý các sự cố thường gặp

### 1. Lỗi: `Cannot connect to the Docker daemon`
- **Nguyên nhân**: Docker Desktop chưa chạy hoặc chưa bật tích hợp WSL.
- **Khắc phục**: Khởi động Docker Desktop trên Windows -> Settings -> Resources -> WSL Integration -> Bật toggle Ubuntu.

### 2. Lỗi cổng bị chiếm: `failed to create cluster ... port 7777/udp already in use`
- **Khắc phục**: Chạy lệnh dừng các tiến trình đang chiếm cổng:
  ```bash
  ./scripts/stop-all.sh
  ```

### 3. Muốn build lại images khi sửa code
Chỉ cần chạy lại:
```bash
./scripts/build-images.sh
# Hoặc cập nhật thẳng vào k3d:
k3d image import fomoxa-server:latest fomoxa-backend:latest -c fomoxa
kubectl rollout restart deployment/backend -n fomoxa
```
