# Hướng Dẫn Cài Đặt Phụ Thuộc & Vận Hành Cụm Fomoxa Trên Ubuntu (WSL2 / Linux)

Tài liệu này hướng dẫn chi tiết cách chuẩn bị môi trường, cài đặt các công cụ phụ thuộc và vận hành toàn bộ hạ tầng game server Fomoxa + Nakama + Agones trên nền tảng **Ubuntu** (hỗ trợ cả WSL2 Ubuntu trên Windows và Ubuntu máy chủ độc lập).

---

## 1. Yêu cầu hệ thống & Phụ thuộc

| Công cụ | Phiên bản khuyến nghị | Mục đích |
|---|---|---|
| **Bash & curl, openssl** | Mặc định trên Ubuntu | Chạy script tự động hóa và sinh khóa mã hóa |
| **Docker Engine / Docker Desktop** | $\ge 24.0$ | Đóng gói và chạy các container |
| **kubectl** | $\ge 1.28$ | Điều khiển cụm Kubernetes |
| **k3d** | $\ge 5.6$ | Chạy cụm k3s gọn nhẹ trong Docker |
| **Helm** | $\ge 3.12$ | Cài đặt Agones Game Server Operator |

---

## 2. Cài đặt phụ thuộc trên Ubuntu

Mở terminal Ubuntu (nếu dùng WSL2, gõ `wsl -d Ubuntu`) và chạy các lệnh sau:

### 2.1. Cập nhật hệ thống và gói cơ bản
```bash
sudo apt update && sudo apt install -y curl openssl git ca-certificates
```

### 2.2. Kiểm tra Docker
- **Nếu dùng WSL2 kết hợp Docker Desktop**:
  - Mở Docker Desktop trên Windows -> **Settings** -> **Resources** -> **WSL Integration** -> Bật toggle cho **Ubuntu**.
  - Kiểm tra trong terminal Ubuntu:
    ```bash
    docker ps
    ```
- **Nếu dùng Ubuntu độc lập (Native Server)**:
  ```bash
  curl -fsSL https://get.docker.com | sh
  sudo usermod -aG docker $USER
  newgrp docker
  ```

### 2.3. Cài đặt `kubectl`
```bash
if ! command -v kubectl >/dev/null 2>&1; then
    curl -LO "https://dl.k8s.io/release/$(curl -L -s https://dl.k8s.io/release/stable.txt)/bin/linux/amd64/kubectl"
    sudo install -o root -g root -m 0755 kubectl /usr/local/bin/kubectl
    rm kubectl
fi
kubectl version --client
```

### 2.4. Cài đặt `k3d`
```bash
if ! command -v k3d >/dev/null 2>&1; then
    curl -s https://raw.githubusercontent.com/k3d-io/k3d/main/install.sh | bash
fi
k3d version
```

### 2.5. Cài đặt `Helm`
```bash
if ! command -v helm >/dev/null 2>&1; then
    curl https://raw.githubusercontent.com/helm/helm/main/scripts/get-helm-3 | bash
fi
helm version --short
```

---

## 3. Quy trình vận hành & Kiểm thử thực tế

Chuyển vào thư mục gốc của dự án trong Ubuntu:
```bash
# Đối với WSL2 (ổ D:\ của Windows nằm tại /mnt/d/)
cd /mnt/d/Documents/fomoxa

# Cấp quyền thực thi cho các script (nếu cần)
chmod +x scripts/*.sh
```

### Bước 1: Sinh bộ khóa bảo mật
Tạo các khóa hex 24-byte (48 hex chars) và root key 32-byte (64 hex chars):
```bash
./scripts/gen-keys.sh
```
> [!NOTE]
> Các khóa được lưu tại `deploy/secrets/` và tự động được loại trừ bởi `.gitignore`, không bao giờ bị lộ lên git.

---

### Bước 2: Đóng gói Docker Images
Đóng gói 4 thành phần container của hệ thống:
```bash
./scripts/build-images.sh
```
Lệnh sẽ lần lượt build:
1. `fomoxa-server:latest` (.NET 8 Runtime non-root)
2. `fomoxa-directory:latest` (Rust Binary trên Debian bookworm slim)
3. `fomoxa-gateway:latest` (Rust Binary UDP Reverse Proxy)
4. `fomoxa-backend:latest` (.NET 8 Web API + fRPC Health Probe)

---

### Bước 3: Khởi động cụm Kubernetes (k3d + Agones)
Khởi chạy cụm k3d tên `fomoxa` và triển khai stack:
```bash
./scripts/k8s-up.sh
```
Script sẽ tự động thực hiện:
1. Tạo cụm k3d với các cổng ánh xạ:
   - `7777:7777/udp` (Gateway UDP cho Game Client)
   - `9600:9600/tcp` (Backend fRPC cho Login / Matchmaking)
   - `7350:7350/tcp` (Nakama API)
2. Cài đặt Agones GameServer CRD và Controller qua Helm vào namespace `agones-system`.
3. Import 4 images vừa build vào cụm k3d.
4. Tạo namespace `fomoxa` và Kubernetes Secrets từ `deploy/secrets/`.
5. Áp dụng toàn bộ manifests: Postgres, Nakama, Directory, Gateway, Backend, Agones Fleet (`fomoxa-server`).
6. Chờ toàn bộ dịch vụ chuyển sang trạng thái `Ready`.

---

### Bước 4: Kiểm tra trạng thái cụm K8s

Kiểm tra các Pods trong hệ thống:
```bash
kubectl get pods -n fomoxa
```
*Kết quả kỳ vọng:*
```
NAME                          READY   STATUS    RESTARTS   AGE
postgres-xxx                  1/1     Running   0          2m
nakama-xxx                    1/1     Running   0          2m
directory-xxx                 1/1     Running   0          1m
gateway-xxx                   1/1     Running   0          1m
backend-xxx                   1/1     Running   0          1m
fomoxa-server-xxx             2/2     Running   0          45s
fomoxa-server-yyy             2/2     Running   0          45s
```

Kiểm tra GameServers được Agones quản lý:
```bash
kubectl get gs -n fomoxa
```
*Kết quả kỳ vọng:* Cả 2 GameServer đều ở trạng thái `Ready` (hoặc `Allocated` khi đã có phòng).

---

## 4. Chạy các bài kiểm thử tự động

### 4.1. Kiểm thử Matchmaking Nakama cơ bản (2 clients)
```bash
./scripts/test-nakama.sh
```
- Đăng nhập 2 tài khoản ảo (`ana`, `bo`).
- Client A gọi `find_match` -> nhận `waiting`.
- Client B gọi `find_match` -> Nakama ghép cặp -> nhận `matched` và phát `ticket`.
- Client A poll -> nhận cùng ticket.
- Backend xác thực `verify_ticket` -> thành công.
- Xác thực với người lạ hoặc ticket giả mạo -> bị từ chối chính xác.

### 4.2. Kiểm thử Matchmaking Auto Room Pool ($\ge 4$ clients - P5.5)
```bash
./scripts/test-nakama-pool.sh
```
- Khởi tạo 4 tài khoản (`ana`, `bo`, `chi`, `dung`).
- Cặp 1 (`ana` + `bo`) tìm trận với `room: "auto"` -> tự động được cấp `arena:1`.
- Cặp 2 (`chi` + `dung`) tìm trận với `room: "auto"` -> tự động được cấp `arena:2` (xoay vòng trong pool).
- Xác nhận các cặp nhận phòng độc lập, không bị chồng chéo phòng.

### 4.3. Kiểm thử kết nối Game Server qua Agones (P5 DoD)
```bash
./scripts/test-p5.sh
```
- Kiểm tra các GameServer Agones.
- Kết nối thông qua Unity Client (trên Windows Host) vào Gateway `127.0.0.1:7777`.
- Xác nhận GameServer chuyển trạng thái sang `Allocated`.

---

## 5. Dọn dẹp & Tắt cụm khi hoàn tất

- **Chỉ xóa các ứng dụng (giữ lại cluster k3d)**:
  ```bash
  ./scripts/k8s-down.sh --keep-cluster
  ```

- **Xóa hoàn toàn cụm k3d**:
  ```bash
  ./scripts/k8s-down.sh
  ```

- **Dừng nhanh các tiến trình local (nếu có)**:
  ```bash
  ./scripts/stop-all.sh
  ```
