# 08: Tham khảo

Tra cứu nhanh: cổng và dịch vụ, cấu hình `directory`/`gateway`, secrets, biến môi trường, giao diện RPC, hằng số gameplay, và bảng đối chiếu từng script với lệnh tay tương đương.

Mục lục: [1. Cổng và dịch vụ](#1-cổng-và-dịch-vụ) · [2. Cấu hình directory và gateway](#2-cấu-hình-directory-và-gateway) · [3. Secrets](#3-secrets) ·
[4. Script ↔ lệnh tay tương đương](#4-script--lệnh-tay-tương-đương) · [5. Biến môi trường và tham số](#5-biến-môi-trường-và-tham-số) · [6. Manifest](#6-manifest-trong-deployk8s) ·
[7. Giao diện RPC](#7-giao-diện-rpc-của-backend) · [8. Hằng số gameplay](#8-hằng-số-gameplay)

## 1. Cổng và dịch vụ

Phía client thấy:

| Cổng máy bạn | Giao thức | Vào | Mục đích |
|---|---|---|---|
| `9600` | TCP | `backend` | đăng nhập, phòng, bạn bè, bảng điểm (fRPC) |
| `7777` | UDP | `gateway` | chơi game |
| `9643` | TCP | `tls-proxy` → `backend:9600` | cùng như 9600 nhưng mã hoá TLS (tuỳ chọn, khuyến nghị cho môi trường thật) |
| `7350` | TCP | `nakama` | chỉ để chạy script test, client không dùng |

Trong cluster (namespace `fomoxa`), client không nối trực tiếp:

| Thành phần | Kiểu Service | Cổng | Ai nối vào | Image / nguồn |
|---|---|---|---|---|
| `postgres` | ClusterIP | 5432 | nakama | `postgres:15` |
| `nakama` | LoadBalancer | 7350 (API) | backend, script test | `registry.heroiclabs.com/heroiclabs/nakama:3.41.0` |
| `nakama-console` | ClusterIP | 7351 | bạn, qua `port-forward` | (cùng pod nakama) |
| `directory` | ClusterIP | 9500 (pod/backend/gateway), 9501 (admin) | game server, backend, gateway | `fomoxa-directory` (Rust) |
| `gateway` | LoadBalancer | 7777/UDP | client | `fomoxa-gateway` (Rust) |
| `backend` | LoadBalancer | 9600 (người chơi), 9601 (WebSocket) | client, tls-proxy | `fomoxa-backend` (.NET) |
| `backend-pods` | ClusterIP | 9602 | game server báo số người/bảng điểm | (cùng pod backend) |
| `tls-proxy` | LoadBalancer | 9643 | client | `nginxinc/nginx-unprivileged:1.27-alpine` |
| game server (Agones Fleet) | (pod, cổng 7778/UDP) | 7778 | gateway | `fomoxa-server` (.NET) |

Cổng metrics (chỉ Prometheus): backend `9610`, game server `9611`, Nakama `9100`.

Namespace khác: `agones-system` (Agones), `monitoring` (tuỳ chọn).

## 2. Cấu hình directory và gateway

### `directory.toml` (trong ConfigMap `directory-config`, file `deploy/k8s/directory.yaml`)

```toml
listen = "0.0.0.0:9500"            # pod, backend, gateway nối vào đây
admin_listen = "0.0.0.0:9501"      # quản trị (drain, đọc danh sách pod)

public_address = "127.0.0.1:7777"  # địa chỉ GATEWAY mà CLIENT thấy

grace_seconds = 30                 # pod mất tín hiệu bao lâu thì coi là chết
report_interval_seconds = 5        # chu kỳ pod báo tải
drain_seconds = 300                # thời gian tối đa một pod được drain
ticket_seconds = 30                # ticket vào phòng sống bao lâu

[keys]                             # khoá theo vai trò, đọc từ secret mount tại /secrets/directory/
backend = { file = "/secrets/directory/backend" }
pod     = { file = "/secrets/directory/pod" }
gateway = { file = "/secrets/directory/gateway" }
admin   = { file = "/secrets/directory/admin" }
root    = { file = "/secrets/directory/root" }

[[placement]]
filter = "pod.version == client.version"     # chỉ ghép pod cùng phiên bản với client

[[placement]]
prefer = "pod.region == client.region"       # ưu tiên cùng vùng
weight = 10

[[placement]]
sort = "packed"                              # dồn người vào pod đã có người
```

| Khoá | Ý nghĩa | Ràng buộc |
|---|---|---|
| `public_address` | Khi có gateway phía trước, `Allocate` trả địa chỉ này thay cho địa chỉ thật của pod. Phải là địa chỉ UDP mà *client* tới được | local: `127.0.0.1:7777`; xem [05, mục 14](operations/05-scaling-security-backup.md#14-đổi-địa-chỉ-công-khai-public_address) |
| `drain_seconds` | Thời gian tối đa pod được chờ hết người khi drain | `terminationGracePeriodSeconds` của Fleet (360) phải lớn hơn |
| `[keys].root` | khoá gốc | đúng 64 ký tự hex (32 byte); các khoá còn lại ≥ 16 byte |

### `gateway.toml` (ConfigMap `gateway-config`, file `deploy/k8s/gateway.yaml`)

```toml
listen = "0.0.0.0:7777"                                 # UDP, cửa duy nhất của game server
pod_bind = "0.0.0.0:0"                                  # cổng nguồn khi gateway nói chuyện với pod
directory = "directory.fomoxa.svc.cluster.local:9500"   # tên DNS trong cluster
retry_seconds = 1

[keys]
gateway = { file = "/secrets/directory/gateway" }       # gateway chỉ cần khoá của chính nó
```

Sửa ConfigMap xong phải `kubectl rollout restart deployment/<directory|gateway> -n fomoxa` mới đọc lại.

## 3. Secrets

Sinh bằng `scripts/gen-keys.sh` + `scripts/gen-tls-cert.sh` (bung ra ở [03, Bước 2](deploy/01-prepare-and-keys.md#bước-2-sinh-khoá-và-chứng-chỉ-secrets)); nạp vào cluster bằng `scripts/gen-k8s-secrets.sh` ([Bước 8](deploy/04-secrets.md#bước-8-namespace-và-secrets)).

File trong `deploy/secrets/` (không commit):

| File | Độ dài hex | Dùng cho |
|---|---|---|
| `directory/root` | 64 | khoá gốc của directory |
| `directory/admin` | 48 | quản trị directory |
| `directory/backend` | 48 | backend ↔ directory |
| `directory/gateway` | 48 | gateway ↔ directory |
| `directory/pod` | 48 | game server ↔ directory |
| `backend-pod` | 48 | game server ↔ backend (cổng 9602) |
| `nakama/server-key` | 32 | đăng nhập ứng dụng vào Nakama |
| `nakama/http-key` | 48 | backend gọi RPC nội bộ của Nakama |
| `nakama/console-password` | 32 | console Nakama |
| `nakama/session-key`, `nakama/refresh-key` | 48 | mã hoá token Nakama |
| `nakama/db-password` | 32 | mật khẩu Postgres |
| `tls/tls.crt`, `tls/tls.key` | (PEM) | chứng chỉ TLS của `tls-proxy` |

Secret trong cluster (namespace `fomoxa`), pod nào được thấy gì:

| Secret | Chứa | Mount/đọc bởi |
|---|---|---|
| `fomoxa-directory-keys` | backend, pod, gateway, admin, root | `directory` (đủ 5); `gateway` chỉ mount khoá `gateway` |
| `fomoxa-backend-keys` | backend, backend-pod | `backend` |
| `fomoxa-pod-key` | pod | game server |
| `fomoxa-backend-pod-key` | backend-pod | game server (tách riêng, không kèm khoá backend→directory) |
| `fomoxa-nakama` | 6 giá trị Nakama/Postgres | `nakama`, `postgres`, `backend` |
| `fomoxa-tls` | `tls.crt`, `tls.key` | `tls-proxy` |
| `grafana-admin` (ns `monitoring`) | password | `grafana` |

## 4. Script ↔ lệnh tay tương đương

Mọi script chỉ là đường tắt. Bảng dưới cho biết mỗi script thực sự chạy những lệnh gì và đoạn tài liệu có phiên bản bung ra.

| Script | Việc script làm (bung ra) | Lệnh tay ở |
|---|---|---|
| `scripts/gen-keys.sh [-f]` | với mỗi file: `openssl rand -hex N \| tr -d '\n' > file` (12 khoá); bỏ qua file đã có trừ khi `-f` | [03 Bước 2](deploy/01-prepare-and-keys.md#bước-2-sinh-khoá-và-chứng-chỉ-secrets) |
| `scripts/gen-tls-cert.sh [-f]` | `openssl req -x509 -newkey rsa:2048 -nodes -days 730 -keyout tls.key -out tls.crt -subj "/CN=$TLS_HOST" -addext "subjectAltName=..."`, rồi in `openssl x509 -outform DER \| openssl dgst -sha256 -hex` (pin) | [03 Bước 2](deploy/01-prepare-and-keys.md#bước-2-sinh-khoá-và-chứng-chỉ-secrets) |
| `scripts/gen-k8s-secrets.sh [ns]` | gọi `gen-keys.sh` nếu thiếu; 6 lệnh `kubectl create secret ... --dry-run=client -o yaml \| kubectl apply -f -` | [03 Bước 8](deploy/04-secrets.md#bước-8-namespace-và-secrets) |
| `scripts/build-images.sh [tag]` | `git clone -b 0.2.0 .../gateway` nếu thiếu; 4 lệnh `docker build -f ... -t fomoxa-*:tag` | [03 Bước 3-4](deploy/02-build-images.md#bước-3-lấy-mã-nguồn-gateway-rust) |
| `scripts/k8s-up.sh [--build]` | kiểm `docker info`, tự cài k3d/helm nếu thiếu → kiểm cổng trống → `k3d cluster create fomoxa --port ...@loadbalancer` → `helm repo add` + `helm install agones ...` → (`--build`: `build-images.sh`) → `k3d image import` → `kubectl apply namespace` + `gen-k8s-secrets.sh` → `kubectl apply` 7 manifest → `kubectl rollout status` | [03, toàn bộ](deploy/README.md#đường-tắt-một-lệnh-dựng-cả-cụm) |
| `scripts/k8s-down.sh [--keep-cluster]` | `kubectl delete namespace fomoxa` (có `--keep-cluster`) hoặc `k3d cluster delete fomoxa` | [03 Bước 14](deploy/08-test-client-and-cleanup.md#bước-14-dọn-dẹp) |
| `scripts/test-nakama.sh`, `test-nakama-pool.sh` | `curl` tới Nakama: đăng nhập device → `reset_match` → `find_match` ×2 → `poll_match` → `verify_ticket`; tự đọc khoá qua `lib-nakama-keys.sh` | [03 Bước 12](deploy/07-test-nakama.md#bước-12-test-nakama-bằng-curl), [06 §3](testing.md#3-module-lua-của-nakama) |
| `scripts/lib-nakama-keys.sh` | (được `source`) nếu cổng 7350 do container `k3d-*` giữ → đọc `server-key`/`http-key` từ secret `fomoxa-nakama`; ngược lại dùng khoá mặc định của compose | [05 mục 9](operations/03-tls-and-keys.md#9-khoá-và-mật-khẩu-nakama) |
| `scripts/test-p5.sh [phòng1 phòng2]` | `kubectl get gs` → gọi `powershell.exe test-p5.ps1` (4 client `MyGame.exe -autojoin`) với `FOMOXA_KUBECTL_WSL=1` → kiểm đúng 2 pod `Allocated` | [03 Bước 13](deploy/08-test-client-and-cleanup.md#bước-13-test-bằng-client-unity-người-chơi-thật) |
| `scripts/run-mock-cluster.sh [tuỳ chọn]` | `dotnet publish` game server + MockCluster vào `.mock/` → `dotnet .mock/cluster/MockCluster.dll --server=... [tuỳ chọn]` | [02 Bước 1-2](getting-started/quickstart-mock-cluster.md) |
| `scripts/test-real-backend.sh` | `docker compose up -d` → 3× `dotnet publish` → chạy backend thật với biến `Fomoxa__*` → `MockCluster --selftest-target=9600,9602 --selftest` → `curl /metrics` → dọn | [06 §4](testing.md#4-backend-thật--nakama-thật) |
| `scripts/stop-all.sh` | `pkill -9 -f` các tiến trình tên `fomoxa-directory`, `fomoxa-gateway`, `MyGame.Server`, `MyGame.Backend`, `MyGame` (và `taskkill.exe` trên Windows); dùng cho cách chạy không K8s | (không có) |

Bản PowerShell (`.ps1`) cho Windows: `gen-keys.ps1`, `gen-tls-cert.ps1`, `gen-k8s-secrets.ps1`, `build-images.ps1`, `k8s-up.ps1`, `k8s-down.ps1`, `test-nakama.ps1`, `test-nakama-pool.ps1`, `test-p5.ps1`, `run-mock-cluster.ps1` làm cùng việc với bản `.sh`.

Cách chạy cũ trên Windows, không dùng K8s (directory/gateway Rust build ra `.exe` và chạy trực tiếp): `start-cluster.ps1` (dựng cả cụm cục bộ), `run-directory.ps1`, `run-backend.ps1`, `run-pod.ps1`, `run-client.ps1`, `stop-all.ps1`,
kiểm thử `test-p4.ps1`, `test-console-server.ps1`. Chúng không được dùng trong tài liệu này vì cụm giả ([02](getting-started/quickstart-mock-cluster.md)) thay thế được mà không cần build Rust.

`scripts/sync-server-deps.ps1` (Windows): chép phần mã nguồn của hai package Fomoxa từ `MyGame/Library/PackageCache/` vào `server-deps/` để game server console build không phụ thuộc Unity.
Chạy lại mỗi khi đổi phiên bản package trong `MyGame/Packages/manifest.json` (sau khi Unity đã tải package).

## 5. Biến môi trường và tham số

### Game server `MyGame.Server`

Thứ tự ưu tiên: tham số dòng lệnh `--tên=giá-trị` → biến môi trường → mặc định.

| Tham số | Biến môi trường | Mặc định | Ý nghĩa |
|---|---|---|---|
| `--directory` | `FOMOXA_DIRECTORY` | `127.0.0.1:9500` | địa chỉ directory |
| `--name` | `FOMOXA_POD_NAME` | `pod-1` | tên pod (đăng ký với directory) |
| `--address` | `FOMOXA_POD_ADDRESS` (hoặc `POD_IP`) | `127.0.0.1` | IP pod báo cho directory |
| `--port` | `FOMOXA_POD_PORT` | 7778 | cổng UDP |
| `--version` | `FOMOXA_VERSION` | `1.0` | phiên bản; chỉ ghép client cùng phiên bản |
| `--region` | `FOMOXA_REGION` | `local` | vùng |
| `--max-players` | `FOMOXA_MAX_PLAYERS` | 16 | sức chứa một pod |
| `--room` | `FOMOXA_ROOM` | `arena:1` | danh sách phòng, cách nhau dấu phẩy |
| `--claim-one` | `FOMOXA_CLAIM_ONE` | false | mỗi pod chỉ giữ một phòng trống trong danh sách (dùng với Fleet) |
| `--behind-gateway` | `FOMOXA_BEHIND_GATEWAY` | true | pod đứng sau gateway (false: client nối thẳng pod, dùng trong cụm giả) |
| `--agones` | `FOMOXA_AGONES` | tự bật nếu có `AGONES_SDK_GRPC_PORT` | báo trạng thái cho sidecar Agones |
| `--key` | `POD_KEY_FILE` | (bắt buộc) | file khoá `pod` |
| `--backend` | `FOMOXA_BACKEND` | (rỗng = tắt) | `host:9602` của backend để báo số người + bảng điểm |
| `--backend-key` | `POD_BACKEND_KEY_FILE` | | file khoá `backend-pod` (bắt buộc nếu có `--backend`) |
| `--metrics` | `FOMOXA_METRICS` | (rỗng = tắt) | `host:port` HTTP `/metrics` (K8s: `0.0.0.0:9611`) |
| `--trace` | `FOMOXA_TRACE` | false | in vị trí người chơi 2 giây một lần |

### Backend (`MyGame.Backend`)

Cấu hình .NET dạng `Fomoxa:Mục:Khoá`; qua biến môi trường thay `:` bằng `__`.

| Biến | Mặc định | Ý nghĩa |
|---|---|---|
| `Fomoxa__Directory__Address` | `127.0.0.1:9500` | directory |
| `Fomoxa__Directory__Key__File` | (bắt buộc) | file khoá `backend` |
| `Fomoxa__Players__Tcp` / `Fomoxa__Players__WebSocket` | `0.0.0.0:9600` / `0.0.0.0:9601` | cổng người chơi (WebSocket để rỗng `""` để tắt) |
| `Fomoxa__Pods__Tcp` / `Fomoxa__Pods__Key__File` | `0.0.0.0:9602` / (bắt buộc) | cổng pod + file khoá `backend-pod` (≥ 16 byte) |
| `Fomoxa__Nakama__Url` | (rỗng) | URL Nakama. Rỗng = không Nakama: ghép trận tức thì, tài khoản trong bộ nhớ |
| `Fomoxa__Nakama__ServerKey` / `HttpKey` | `defaultkey` / `defaulthttpkey` | khoá Nakama (K8s lấy từ secret) |
| `Fomoxa__Data__File` | (rỗng) | file JSON lưu tài khoản khi không có Nakama |
| `Fomoxa__Metrics__Listen` | `0.0.0.0:9610` | HTTP `/metrics` (rỗng = tắt) |

### Client Unity

Tham số dòng lệnh: xem bảng ở [04, Bước 5](unity-client.md#bước-5-chạy-bản-build-và-tham-số-dòng-lệnh).

## 6. Manifest trong `deploy/k8s/`

| File | Nội dung |
|---|---|
| `namespace.yaml` | namespace `fomoxa` |
| `postgres.yaml` | PVC 1 GiB, Deployment, Service `postgres` |
| `nakama.yaml` | ConfigMap `nakama-modules` (= `deploy/nakama/modules/main.lua`), Deployment (+ initContainer `migrate`), Service `nakama`, Service `nakama-console` |
| `directory.yaml` | ConfigMap `directory-config`, Deployment, Service `directory` |
| `gateway.yaml` | ConfigMap `gateway-config`, Deployment, Service `gateway` (UDP) |
| `backend.yaml` | Deployment, Service `backend` (LB 9600/9601), Service `backend-pods` (ClusterIP 9602) |
| `tls-proxy.yaml` | nginx stream TLS 9643 → backend 9600 |
| `fleet.yaml` | Agones `Fleet` `fomoxa-server`: 4 replicas, cổng 7778/UDP, 16 phòng, `terminationGracePeriodSeconds: 360` |
| `fleet-autoscaler.yaml` | `FleetAutoscaler` Buffer 2, 4..16 (tuỳ chọn) |
| `networkpolicy.yaml` | chặn mặc định + các luồng cần thiết (tuỳ chọn) |
| `ingress.yaml` | thử nghiệm, chỉ route WebSocket `/ws`, không TLS (`APPLY_INGRESS=1`) |

`deploy/monitoring/`: `00-namespace`, `10-prometheus` (+ luật cảnh báo), `20-alertmanager`, `30-grafana` (+ dashboard), `40-loki` (+ promtail).

## 7. Giao diện RPC của backend

fRPC qua TCP 9600. Định nghĩa gốc: `MyGame.Backend/MyGame.Backend/Services.cs`; model: `Models/*.cs`.

`Players.Login(PlayerLogin) → PlayerWelcome`: dùng làm bước xác thực khi kết nối. `PlayerLogin.Mode`:

| `Mode` | Việc | Trường dùng |
|---|---|---|
| `register` | tạo tài khoản | `Player`, `Secret` = mật khẩu |
| `login` | đăng nhập | `Player`, `Secret` |
| `recover` | đặt lại mật khẩu | `Player`, `Recovery` = mã khôi phục, `Secret` = mật khẩu mới |
| (rỗng, không `Secret`) | khách / kiểu cũ | `DeviceId` (qua Nakama) hoặc chỉ `Player` (backend không Nakama) |

`Lobby` (cần đăng nhập): `ListRooms`, `JoinRoom(RoomKey, Version) → RoomTicket`, `Presence(Room) → OnlineList`, `TakeRecoveryCode`, `ChangePassword`, `GetProfile`, `Social`, `FriendAction(request|accept|decline|remove|dismiss-invite)`, `Invite`, `Scoreboard(Room)`.

`PodGate` (khoá `backend-pod`): `ReportRoom(RoomKey, Players, Capacity)`, `ReportScores(Room, [Player, Steps])`, `TransferPlayer` (chưa dùng).

`Directory` (backend/pod/gateway ↔ directory, cổng 9500): `Allocate`, `RegisterPod`, `AcquireRoom`, `ReleaseRoom`, `ReportLoad`, `WatchDrain`, `RequestDrain`, `WatchPods`; `DirectoryAdmin` (khoá `admin`, cổng 9501): `Drain`, `ReadPods`.

## 8. Hằng số gameplay

Định nghĩa duy nhất trong `MyGame/Assets/Network/Models/Course.cs`, dùng chung bởi client Unity và server.

| Hằng số | Giá trị | Ý nghĩa |
|---|---|---|
| `Count` | 18 | số bục |
| `StepHeight` | 1,4 m | bục thứ *n* có mặt trên ở độ cao *n* × 1,4 m |
| `PlatformHalf` | 1,75 m | nửa cạnh bục (bục vuông 3,5 m) |
| `Radius`, `AngleStepDegrees` | 7 m, 40° | bục xếp xoắn ốc quanh tâm sàn |
| `Speed` | 6 m/s | tốc độ chạy |
| `Gravity`, `JumpSpeed` | 25 m/s², 9,5 m/s | nhảy cao tối đa ≈ 1,8 m (đủ vượt 1,4 m của một bậc) |
| `Bounds` | 23 m | giới hạn khu chơi |
| `PlayerRadius`, `PlayerHeight` | 0,5 m, 2 m | kích thước nhân vật |

Đổi hằng số: sửa file này (một nơi), build lại cả client và game server; scene tự dựng lại bục khi chạy Rebuild Scene.
