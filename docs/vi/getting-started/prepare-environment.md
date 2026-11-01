# 01: Chuẩn bị môi trường (Ubuntu / WSL2)

Mục tiêu của tài liệu này: máy của bạn có đủ công cụ (`docker`, `kubectl`, `k3d`, `helm`, `openssl`, `git`, `.NET SDK 8`) và đã clone repo.
Xong tài liệu này, bạn chạy được [02, cụm giả](quickstart-mock-cluster.md) và [03, cụm Kubernetes](../deploy/README.md).

## Bước 1. Kiểm tra máy đủ sức

Mục tiêu: chắc chắn máy chịu nổi k3d + Agones + Nakama + Postgres cùng lúc.

| Thứ | Tối thiểu | Ghi chú |
|---|---|---|
| RAM | 8 GB (trống ≥ 4 GB) | cụm đầy đủ ăn khoảng 3-4 GB; thêm giám sát ~1 GB |
| CPU | 4 core | |
| Ổ đĩa | 20 GB trống | các image Docker ~3-4 GB; build Rust thêm vài GB cache |
| OS | Ubuntu 22.04+ | WSL2 hoặc máy Linux thật |
| Mạng | có Internet | kéo image lần đầu |

Lệnh:

```bash
free -h          # RAM
nproc            # số core
df -h ~          # ổ đĩa chứa thư mục home
lsb_release -d   # phiên bản Ubuntu
```

Ý nghĩa: `free -h` cột `available` là RAM thực sự còn dùng được. Nếu dưới ~3 GB, k3d + Agones sẽ chậm hoặc bị OOM-kill.

Kết quả mong đợi:

```
               total        used        free      shared  buff/cache   available
Mem:            15Gi       2.1Gi        10Gi       0.0Ki       3.0Gi        13Gi
8
Filesystem      Size  Used Avail Use% Mounted on
/dev/sdc        251G   40G  199G  17% /
Description:    Ubuntu 22.04.4 LTS
```

Dùng WSL2 mà RAM ít? Tạo/sửa file `C:\Users\<tên bạn>\.wslconfig` trên Windows:

```ini
[wsl2]
memory=8GB
processors=4
```

rồi chạy `wsl --shutdown` trong PowerShell và mở lại Ubuntu. Mặc định WSL2 lấy 50% RAM máy.

## Bước 2. Cài các gói cơ bản

Mục tiêu: có `curl`, `git`, `openssl` (sinh khoá), `ca-certificates`.

Lệnh:

```bash
sudo apt update
sudo apt install -y curl git openssl ca-certificates gnupg
```

Ý nghĩa: `curl` tải file; `git` clone repo; `openssl` sinh khoá ngẫu nhiên và chứng chỉ TLS; `ca-certificates` để HTTPS hợp lệ; `gnupg` để thêm khoá apt của Docker.

Kiểm tra:

```bash
curl --version | head -1
git --version
openssl version
```

Kết quả mong đợi:

```
curl 7.81.0 (x86_64-pc-linux-gnu) ...
git version 2.34.1
OpenSSL 3.0.2 15 Mar 2022 (Library: OpenSSL 3.0.2 15 Mar 2022)
```

## Bước 3. Cài Docker

> WSL2 và Ubuntu thuần là hai trường hợp khác nhau. Chỉ làm MỘT trong hai.
> Không cài Docker Engine bên trong WSL2 nếu bạn đã dùng Docker Desktop (hai engine sẽ giành nhau).

### 3A. Bạn dùng WSL2 (Ubuntu trên Windows) (Docker Desktop)

1. Tải và cài Docker Desktop cho Windows: <https://www.docker.com/products/docker-desktop/> (restart máy nếu được yêu cầu).
2. Mở Docker Desktop → Settings → Resources → WSL Integration → bật công tắc cho distro Ubuntu của bạn → Apply & Restart.
3. Kiểm tra trong terminal Ubuntu:

```bash
docker version --format 'client={{.Client.Version}} server={{.Server.Version}}'
docker ps
```

Ý nghĩa: lệnh đầu in phiên bản client và server; nếu thấy cả hai nghĩa là terminal Ubuntu nói chuyện được với Docker Desktop. `docker ps` liệt kê container đang chạy (rỗng cũng được).

Kết quả mong đợi:

```
client=27.3.1 server=27.3.1
CONTAINER ID   IMAGE   COMMAND   CREATED   STATUS   PORTS   NAMES
```

Vì sao nên dùng Docker Desktop khi client Unity chạy trên Windows? Docker Desktop đưa cổng của container ra thẳng `localhost` của Windows, kể cả UDP.
Nhờ vậy client Unity trên Windows nối được `127.0.0.1:7777` (UDP) của cụm trong WSL2.

### 3B. Bạn dùng Ubuntu thuần (máy Linux / VM) (Docker Engine)

```bash
# 1) Khoá GPG chính thức của Docker
sudo install -m 0755 -d /etc/apt/keyrings
curl -fsSL https://download.docker.com/linux/ubuntu/gpg \
    | sudo gpg --dearmor -o /etc/apt/keyrings/docker.gpg
sudo chmod a+r /etc/apt/keyrings/docker.gpg

# 2) Thêm kho apt của Docker
echo "deb [arch=$(dpkg --print-architecture) signed-by=/etc/apt/keyrings/docker.gpg] \
https://download.docker.com/linux/ubuntu $(. /etc/os-release && echo "$VERSION_CODENAME") stable" \
    | sudo tee /etc/apt/sources.list.d/docker.list > /dev/null

# 3) Cài Docker Engine
sudo apt update
sudo apt install -y docker-ce docker-ce-cli containerd.io docker-buildx-plugin docker-compose-plugin

# 4) Cho phép user hiện tại chạy docker không cần sudo, và bật dịch vụ
sudo usermod -aG docker "$USER"
sudo systemctl enable --now docker
newgrp docker        # áp dụng group ngay trong terminal này (hoặc đăng xuất rồi đăng nhập lại)
```

Ý nghĩa: bước 1-2 khai báo kho chính thức để `apt` cài đúng bản Docker; bước 3 cài engine, CLI, buildx và `docker compose`; bước 4 thêm bạn vào group `docker`
(nếu không sẽ phải gõ `sudo` trước mọi lệnh docker) và bật dịch vụ khởi động cùng máy.

Kiểm tra:

```bash
docker run --rm hello-world
```

Kết quả mong đợi: có dòng `Hello from Docker!`.

### Lỗi thường gặp (Docker)

| Triệu chứng | Nguyên nhân | Cách xử lý |
|---|---|---|
| `Cannot connect to the Docker daemon at unix:///var/run/docker.sock` | WSL2: Docker Desktop chưa chạy / chưa bật WSL Integration. Linux: dịch vụ chưa chạy | WSL2: mở Docker Desktop và làm lại mục 3A bước 2. Linux: `sudo systemctl start docker` |
| `permission denied while trying to connect to the Docker daemon socket` | user chưa trong group `docker` | `sudo usermod -aG docker "$USER" && newgrp docker` |
| `Got permission denied` sau khi đã `usermod` | group chưa áp dụng | đăng xuất/đăng nhập lại, hoặc `newgrp docker` |

## Bước 4. Cài kubectl

Mục tiêu: `kubectl`: công cụ dòng lệnh để làm việc với Kubernetes (mọi lệnh `kubectl get/apply/logs...` đều dùng nó).

Lệnh:

```bash
KUBECTL_VERSION=$(curl -L -s https://dl.k8s.io/release/stable.txt)
echo "Sẽ cài kubectl ${KUBECTL_VERSION}"
curl -LO "https://dl.k8s.io/release/${KUBECTL_VERSION}/bin/linux/amd64/kubectl"
sudo install -o root -g root -m 0755 kubectl /usr/local/bin/kubectl
rm kubectl
kubectl version --client
```

Ý nghĩa: lấy số phiên bản ổn định mới nhất, tải file chạy, cài vào `/usr/local/bin` (có trong `PATH`), xoá file tải về, in phiên bản.

Kết quả mong đợi:

```
Client Version: v1.31.2
Kustomize Version: v5.4.2
```

Lỗi thường gặp: `kubectl: command not found` → `/usr/local/bin` chưa có trong `PATH` (`echo $PATH`), hoặc lệnh `install` bị lỗi quyền (chạy lại với `sudo`).

## Bước 5. Cài k3d

Mục tiêu: `k3d`: tạo cụm Kubernetes (k3s) chạy trong container Docker.

Lệnh:

```bash
curl -s https://raw.githubusercontent.com/k3d-io/k3d/main/install.sh | TAG=v5.7.4 bash
k3d version
```

Ý nghĩa: script cài đặt chính thức của k3d, ghim phiên bản `v5.7.4` (phiên bản mà repo này dùng; bản khác có thể đổi cú pháp `--port`).

Kết quả mong đợi:

```
k3d version v5.7.4
k3s version v1.30.4-k3s1 (default)
```

Lưu ý phiên bản k3s: k3d 5.7.4 mặc định dùng k3s v1.30. Agones chỉ chạy với các phiên bản Kubernetes mà bản Agones đó hỗ trợ.
Nếu cài Agones mới nhất mà báo lỗi tương thích, ghim `AGONES_VERSION` (xem [03, Bước 6](../deploy/03-cluster-and-agones.md#bước-6-cài-agones-bằng-helm)).

## Bước 6. Cài Helm

Mục tiêu: `helm`: "trình cài gói" của Kubernetes, dùng để cài Agones bằng một lệnh thay vì apply hàng chục file YAML.

Lệnh:

```bash
curl -fsSL https://raw.githubusercontent.com/helm/helm/main/scripts/get-helm-3 | bash
helm version --short
```

Kết quả mong đợi:

```
v3.16.2+g13654a5
```

## Bước 7. Cài .NET SDK 8

Mục tiêu: `dotnet`: cần để chạy cụm giả, chạy các bài kiểm thử và sinh mã (`fomoxac`). Không cần nếu bạn chỉ build image bằng Docker (Dockerfile đã có SDK bên trong), nhưng nên cài.

Lệnh (Ubuntu 22.04 / 24.04):

```bash
sudo apt install -y dotnet-sdk-8.0
dotnet --version
```

Nếu `apt` không tìm thấy gói (một số bản Ubuntu), dùng script chính thức:

```bash
curl -fsSL https://dot.net/v1/dotnet-install.sh -o dotnet-install.sh
bash dotnet-install.sh --channel 8.0 --install-dir "$HOME/.dotnet"
echo 'export DOTNET_ROOT="$HOME/.dotnet"; export PATH="$PATH:$HOME/.dotnet"' >> ~/.bashrc
source ~/.bashrc
dotnet --version
```

Kết quả mong đợi:

```
8.0.427
```

Bất kỳ `8.0.x` đều được.

## Bước 8. Kiểm tra tất cả công cụ một lượt

Lệnh:

```bash
echo "docker : $(docker version --format '{{.Client.Version}}')"
echo "kubectl: $(kubectl version --client -o json | grep -m1 gitVersion | tr -d ' ,\"')"
echo "k3d    : $(k3d version | head -1)"
echo "helm   : $(helm version --short)"
echo "dotnet : $(dotnet --version)"
echo "openssl: $(openssl version | cut -d' ' -f1-2)"
echo "git    : $(git --version)"
```

Kết quả mong đợi:

```
docker : 27.3.1
kubectl: gitVersion:v1.31.2
k3d    : k3d version v5.7.4
helm   : v3.16.2+g13654a5
dotnet : 8.0.427
openssl: OpenSSL 3.0.2
git    : git version 2.34.1
```

Dòng nào báo `command not found` thì quay lại bước cài tương ứng.

## Bước 9. Clone repo

Mục tiêu: có mã nguồn trên máy, đứng đúng thư mục.

> WSL2: clone vào ổ Linux (`~`), đừng clone vào `/mnt/c/...`. Truy cập file qua `/mnt/c` chậm hơn rất nhiều và làm Docker build/`dotnet` chậm theo.

Lệnh:

```bash
cd ~
git clone https://github.com/fomoxa/fomoxa-infra-demo.git
cd fomoxa-infra-demo
git branch --show-current
ls
```

Kết quả mong đợi:

```
main
MyGame  MyGame.Backend  PLAN.md  README.md  deploy  docs  scripts  server-deps  tools
```

Đang làm việc trên một nhánh khác (ví dụ `claude/vibrant-bardeen-g301qh`)? Chuyển sang nhánh đó: `git checkout <tên-nhánh>`.

Từ giờ, mọi lệnh trong tài liệu chạy ở thư mục này: `cd ~/fomoxa-infra-demo`.

Làm cho script chạy được (nếu clone qua đường làm mất quyền thực thi):

```bash
chmod +x scripts/*.sh
```

## Checklist cuối

- [ ] `free -h` cho thấy ≥ 4 GB RAM trống
- [ ] `docker run --rm hello-world` in `Hello from Docker!`
- [ ] `kubectl version --client`, `k3d version`, `helm version --short`, `dotnet --version` đều in được phiên bản
- [ ] Đang đứng trong `~/fomoxa-infra-demo` và `ls` thấy `deploy`, `scripts`, `MyGame`

Tiếp theo: [02: Chạy nhanh bằng cụm giả](quickstart-mock-cluster.md) (thấy game chạy sau vài phút) hoặc [03, Triển khai Kubernetes](../deploy/README.md).
