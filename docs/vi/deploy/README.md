# 03: Triển khai cụm Kubernetes (k3d + Agones) từng bước

Tài liệu này dựng cụm thật trên máy bạn: Postgres, Nakama, directory, gateway, backend và Fleet game server của Agones, chạy trong k3d.
Làm tay từng bước để hiểu; cuối tài liệu có đường tắt một lệnh (`./scripts/k8s-up.sh --build`) và bảng cho biết script đó đã làm những bước nào.

> Điều kiện: đã xong [01, Chuẩn bị môi trường](../getting-started/prepare-environment.md) (`docker`, `kubectl`, `k3d`, `helm`, `openssl`, `git`).
> Thời gian: lần đầu 30-60 phút, phần lớn là build image Rust (directory, gateway) và kéo image.

## Nên đọc trước

- [Khái niệm Kubernetes dùng trong dự án](concepts.md): pod, deployment, service, secret, configmap, Agones... và lệnh xem từng thứ trong cụm này.
- [Đọc từng file manifest](manifest-walkthrough.md): mỗi file YAML trong `deploy/k8s/` chứa gì, trường nào quan trọng.

## Bản đồ các bước

Tài liệu này được chia thành các file nhỏ theo nhóm bước. Làm lần lượt từ trên xuống.

| # | Việc | Kết quả | Chi tiết |
|---|---|---|---|
| 1 | Chuẩn bị, kiểm tra cổng trống | máy sẵn sàng | [01-chuan-bi-va-khoa.md](01-prepare-and-keys.md#bước-1-chuẩn-bị-và-kiểm-tra-cổng-trống) |
| 2 | Sinh khoá và chứng chỉ | `deploy/secrets/` | [01-chuan-bi-va-khoa.md](01-prepare-and-keys.md#bước-2-sinh-khoá-và-chứng-chỉ-secrets) |
| 3 | Lấy mã nguồn `gateway` (Rust) | thư mục `gateway/` | [02-build-image.md](02-build-images.md#bước-3-lấy-mã-nguồn-gateway-rust) |
| 4 | Build 4 Docker image | `fomoxa-{server,directory,gateway,backend}:latest` | [02-build-image.md](02-build-images.md#bước-4-build-4-docker-image) |
| 5 | Tạo cluster k3d | cluster `fomoxa` + cổng 7777/9600/9643/7350 | [03-cluster-va-agones.md](03-cluster-and-agones.md#bước-5-tạo-cluster-k3d) |
| 6 | Cài Agones bằng Helm | CRD `Fleet`/`GameServer` | [03-cluster-va-agones.md](03-cluster-and-agones.md#bước-6-cài-agones-bằng-helm) |
| 7 | Import image vào k3d | cluster thấy được image | [03-cluster-va-agones.md](03-cluster-and-agones.md#bước-7-import-image-vào-k3d) |
| 8 | Namespace và Secrets | namespace `fomoxa` + 6 secret | [04-secrets.md](04-secrets.md#bước-8-namespace-và-secrets) |
| 9 | Apply manifest theo thứ tự | các dịch vụ chạy | [05-apply-manifest.md](05-apply-manifests.md#bước-9-apply-manifest-theo-thứ-tự) |
| 10 | Kiểm tra toàn cụm | pod `Running`, GameServer `Ready` | [06-kiem-tra-cum.md](06-verify-cluster.md#bước-10-kiểm-tra-toàn-cụm) |
| 11 | Kiểm tra từ phía "client" | cổng 9600/9643/7777 mở | [06-kiem-tra-cum.md](06-verify-cluster.md#bước-11-kiểm-tra-từ-phía-client) |
| 12 | Test Nakama bằng `curl` | ghép trận đúng | [07-test-nakama.md](07-test-nakama.md#bước-12-test-nakama-bằng-curl) |
| 13 | Test bằng client Unity | GameServer chuyển `Allocated` | [08-test-client-va-don-dep.md](08-test-client-and-cleanup.md#bước-13-test-bằng-client-unity-người-chơi-thật) |
| 14 | Dọn dẹp |  | [08-test-client-va-don-dep.md](08-test-client-and-cleanup.md#bước-14-dọn-dẹp) |

## Đường tắt: một lệnh dựng cả cụm

Sau khi hiểu các bước trên, chạy gọn:

```bash
./scripts/k8s-up.sh --build
```

Script làm đúng những bước nào:

| Phần của `k8s-up.sh` | Tương ứng |
|---|---|
| kiểm `docker info`; tự cài k3d/helm nếu thiếu | [01](../getting-started/prepare-environment.md) |
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
| `AUTOSCALE=1` | bật `FleetAutoscaler` (giữ 2 pod Ready, 4..16 pod) | [05](../operations/05-scaling-security-backup.md#autoscaling) |
| `INSTALL_MONITORING=1` | cài Prometheus/Alertmanager/Grafana/Loki | [05](../operations/04-monitoring.md#giám-sát) |
| `APPLY_NETPOL=1` | bật NetworkPolicy | [05](../operations/05-scaling-security-backup.md#networkpolicy-tuỳ-chọn) |
| `TLS_ONLY=1` | đóng cổng 9600 không mã hoá, chỉ còn TLS 9643 | [05](../operations/03-tls-and-keys.md#tls-giữa-client-và-backend) |
| `APPLY_INGRESS=1` | áp dụng Ingress thử nghiệm | [08](../reference.md) |

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

Tiếp theo: [04: Client Unity](../unity-client.md), [05, Vận hành](../operations/README.md).
