# 03.2 Lấy mã nguồn gateway và build image Docker

Lên đầu tài liệu: [03-TRIEN-KHAI-K8S](README.md) · Trước: [01-chuan-bi-va-khoa.md](01-prepare-and-keys.md) · Tiếp: [03-cluster-va-agones.md](03-cluster-and-agones.md)

## Bước 3. Lấy mã nguồn `gateway` (Rust)

Mục tiêu: có thư mục `gateway/` chứa mã nguồn của `fomoxa-directory` và `fomoxa-gateway` (hai thành phần viết bằng Rust, không nằm trong repo này nhưng cần để build image).

Lệnh:

```bash
[ -d gateway ] || git clone --depth 1 -b 0.2.0 https://github.com/fomoxa/gateway.git gateway
ls gateway | head
```

Ý nghĩa: clone đúng tag `0.2.0` (bản đã dùng cho repo này), `--depth 1` chỉ lấy commit mới nhất cho nhanh. Thư mục `gateway/` được `.gitignore`, không commit.
`[ -d gateway ] ||` bỏ qua nếu thư mục đã có.

Hoặc chạy gọn: `./scripts/build-images.sh` tự clone nếu thiếu (nó là bước đầu của script).

Kết quả mong đợi: thấy `Cargo.toml` và các thư mục crate.

Lỗi thường gặp: `Repository not found` / đòi mật khẩu → repo `fomoxa/gateway` là repo riêng/không truy cập được từ tài khoản của bạn; xin quyền truy cập hoặc đặt sẵn mã vào `gateway/`.

## Bước 4. Build 4 Docker image

Mục tiêu: đóng gói 4 thành phần thành image Docker.

| Image | Dockerfile | Nội dung |
|---|---|---|
| `fomoxa-server` | `deploy/Dockerfile.server` | game server (.NET 8), build từ `MyGame/Server~` + `server-deps` |
| `fomoxa-directory` | `deploy/Dockerfile.directory` | directory (Rust, từ `gateway/`) |
| `fomoxa-gateway` | `deploy/Dockerfile.gateway` | gateway UDP (Rust, từ `gateway/`) |
| `fomoxa-backend` | `MyGame.Backend/Dockerfile` | backend (.NET 8) + công cụ health |

Lệnh:

```bash
docker build -f deploy/Dockerfile.server    -t fomoxa-server:latest    .
docker build -f deploy/Dockerfile.directory -t fomoxa-directory:latest .
docker build -f deploy/Dockerfile.gateway   -t fomoxa-gateway:latest   .
docker build -f MyGame.Backend/Dockerfile   -t fomoxa-backend:latest   MyGame.Backend
```

Ý nghĩa:
- `-f <Dockerfile>` chọn file mô tả; `-t <tên:tag>` đặt tên image; đối số cuối là build context (thư mục được gửi cho Docker làm "gốc" khi `COPY`).
  Ba image đầu dùng context là thư mục gốc repo (`.`); backend dùng context riêng `MyGame.Backend`.
- Mỗi Dockerfile là multi-stage: giai đoạn `build` có SDK/compiler nặng (`dotnet/sdk`, `rust`) để biên dịch; giai đoạn cuối chỉ copy kết quả sang image runtime nhỏ.
- `.dockerignore` loại `MyGame/Library`, `deploy/secrets`, `gateway/target`... khỏi context để build nhanh và không đưa khoá vào image.

Hoặc chạy gọn: `./scripts/build-images.sh` (clone `gateway/` nếu thiếu rồi chạy đúng 4 lệnh trên, in danh sách image ở cuối).

Thời gian: hai image Rust build lâu nhất (10-25 phút lần đầu vì tải và biên dịch crate); các lần sau nhanh nhờ cache.

Kết quả mong đợi (đoạn cuối):

```
 => exporting to image
 => => naming to docker.io/library/fomoxa-server:latest
```

Kiểm tra:

```bash
docker images | grep -E '^fomoxa-(server|directory|gateway|backend)\b'
```

Phải thấy 4 dòng, tag `latest`:

```
fomoxa-backend     latest   3f1c...   2 minutes ago   260MB
fomoxa-gateway     latest   9a77...   6 minutes ago   95MB
fomoxa-directory   latest   c04b...   9 minutes ago   98MB
fomoxa-server      latest   e1d2...   12 minutes ago  235MB
```

Lỗi thường gặp:

| Triệu chứng | Cách xử lý |
|---|---|
| `COPY gateway/ .: not found` | chưa làm Bước 3 |
| `error NU1301 ... api.nuget.org` khi build image .NET | container build không ra được Internet (proxy công ty). Cấu hình proxy cho Docker daemon hoặc build ở mạng khác |
| Build Rust bị `Killed` / hết RAM | tăng RAM cho Docker Desktop/WSL2 ([01, Bước 1](../getting-started/prepare-environment.md#bước-1-kiểm-tra-máy-đủ-sức)); `docker builder prune` giải phóng cache |
| `no space left on device` | `docker system df` rồi `docker image prune -a` |

