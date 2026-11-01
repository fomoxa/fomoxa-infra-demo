# Kế hoạch cụm Fomoxa + Nakama + Agones

Cập nhật: đã clone repos về `D:\Documents\fomoxa`, đã kiểm tra môi trường máy.
P1–P4 xong (game + directory + gateway + Nakama). P5 (Agones) đã chốt phạm vi, đang ở bước 1.

---

## 0. Trạng thái môi trường (đo thực tế)

| Thành phần | Có gì | Kết luận |
|---|---|---|
| Unity | `6000.4.7f1`, **`6000.5.7f1`** | `com.fomoxa.networking` và `com.fomoxa.rpc` yêu cầu `unity: 6000.5` → **dùng 6000.5.7f1** |
| RedRunner (example game) | `ProjectVersion.txt` = **6000.5.7f1** | Khớp chính xác, mở được ngay |
| .NET | SDK **9.0.320** (không có SDK 8) | Vẫn build được `net8.0` (đã thử với `MyGame.Backend.sln`) → **không cần cài SDK 8** |
| Rust | cargo **1.95.0** | README gateway cần ≥1.88 ✓ |
| Docker | **29.4.1** | OK |
| Git | **2.52.0** | OK |
| GitHub | truy cập được | ✓ |

### 0.1. Đo lại cho P5 (Agones)

| Hạng mục | Đo được | Hệ quả |
|---|---|---|
| `MyGame/Server~` | **không tồn tại** | phải tạo console server từ đầu |
| Unity Linux Build Support | **chưa cài** (chỉ WebGL + windowsstandalonesupport) | không build được Unity Linux player → chọn console server |
| `k3d` / `helm` | **thiếu** | phải cài trước khi làm P5 |
| `kubectl` | có (Windows `Docker\...\kubectl.exe`, và trong Ubuntu) | dùng bản Windows, context `k3d-*` |
| Docker Desktop | 8.1 GB RAM / 20 CPU | đủ cho 2 game server + 5 service phụ |
| WSL2 Ubuntu | 7.7 GB RAM, **engine docker riêng** | **không** dùng; k3d chạy trên engine Docker Desktop |
| Ổ đĩa | C: 62 GB trống, D: 22.6 GB trống | build image trên C: để tránh đầy D: |

**Sửa so với dự định cũ**: P5 **không** chạy k3d trong WSL2 Ubuntu. Máy có 2 Docker engine
(`docker-desktop` và engine trong Ubuntu); dùng lẫn sẽ ra 2 cluster khác nhau. Chốt: k3d +
Agones chạy trên **Docker Desktop engine**, gọi từ PowerShell trên Windows.

---

## 1. Repo & package đang dùng

| Thứ | Vai trò | Trạng thái |
|---|---|---|
| `MyGame` | Project Unity 6000.5.7f1, game demo (P1–P4 xong) | ✅ trong repo git |
| `com.fomoxa.networking` | Netcode Unity + `Standalone~/` (console server cho P2) | ✅ package git `#0.2.0`, không clone |
| `com.fomoxa.rpc` | fRPC phía Unity/pod: `DirectoryPod`, `TransferPlayer` | ✅ package git `#0.1.0`, không clone |
| `gateway` | **`fomoxa-directory` + `fomoxa-gateway`** (Rust, tag `0.2.0`) | ✅ clone, git ignore |
| `unity-docs` | Docs offline, có bản tiếng Việt ở `unity-docs/vi/` | ✅ clone, git ignore |

Đã xóa: `RedRunner`, `networking-rapier`, `unity-codecs`, `fomoxac`, `csharp`,
`attributes-csharp`, `csharp-websocket`, `specification`, `implementation-guide`
và `backend`.

`specification` + `implementation-guide` chỉ là tài liệu khái niệm, không
build/runtime nào đọc — fomoxac và codec sinh sẵn đã là bản thực thi của wire format.

Backend tái tạo bằng template NuGet (đã kiểm chứng `0.1.0` có trên nuget.org):

```
dotnet new install Fomoxa.Backend.Templates
dotnet new fomoxa-backend -n MyGame.Backend
```

Repo khác khi cần: `git clone https://github.com/fomoxa/<tên-repo>`.

Bỏ qua (không cần): `rust`, `go`, `typescript`, `c`, `interop`, `fomoxa-codec-bench`, `fomoxa-example-game`, `attributes-rust`, `fomoxa.github.io`, `.github`, `community`.

---

## 2. Bản đồ port (số thật từ README/config mặc định)

| Port | Thành phần | Ghi chú |
|---|---|---|
| **9500** TCP | `fomoxa-directory` service | backend + pod + gateway nối vào |
| **9501** TCP | `fomoxa-directory` admin | `Drain`, đọc trạng thái pod |
| **7777/udp** | `fomoxa-gateway` listen | client gửi datagram vào đây |
| **7778/udp** | Pod (game) | **tránh 7777** — gateway đã chiếm; pod dùng 7778 |
| 9600 TCP | Fomoxa backend (players) | |
| 9601 WS | Fomoxa backend (WebGL) | bỏ nếu không cần WebGL |
| 9602 TCP | Fomoxa backend (pods) | `TransferPlayer` |
| 7350 / 7351 | Nakama API / console | |
| 5432 | Postgres (Nakama) | |

---

## 3. Config thật (lấy từ `gateway/README.md`)

### `deploy/directory.toml`

```toml
listen = "0.0.0.0:9500"
admin_listen = "0.0.0.0:9501"
grace_seconds = 30
report_interval_seconds = 5
drain_seconds = 300
ticket_seconds = 30

[keys]
backend = { file = "keys/backend" }
pod     = { file = "keys/pod" }
gateway = { file = "keys/gateway" }
admin   = { file = "keys/admin" }
root    = { file = "keys/root" }

[[placement]]
filter = "pod.version == client.version"

[[placement]]
prefer = "pod.region == client.region"

[[placement]]
sort = "packed"

# Bật ở P3 khi đã có gateway:
# public_address = "127.0.0.1:7777"
```

Lưu ý: `root` key phải là **64 ký tự hex**; các role key còn lại ≥16 byte.

### `deploy/gateway.toml`

```toml
listen = "0.0.0.0:7777"
pod_bind = "0.0.0.0:0"
directory = "127.0.0.1:9500"
retry_seconds = 1

[keys]
gateway = { file = "keys/gateway" }
```

### Console server (`MyGame/Server~/MyGame.Server.csproj`)

Theo `unity-docs/console-server/project.md`: biên dịch **source** của `Runtime/Core/**` và `Standalone~/**` + models/generated của Unity project, reference 3 DLL trong `Runtime/Plugins` + `BouncyCastle.Cryptography 2.6.2`.

Cần: `EnableDefaultCompileItems=false`, `TargetFramework=net8.0`, `LangVersion=9.0`.

---

## 4. Các phase

### P1 — Game cơ bản, chưa hạ tầng (1–2 ngày) ⭐

Mục tiêu: **2 client spawn, đi lại, nhìn thấy nhau.**

- Tạo Unity project `MyGame` bằng CLI:
  `Unity.exe -createProject D:\Documents\fomoxa\MyGame -quit -batchmode` (không cần GUI)
- Thêm 3 package qua `Packages/manifest.json` (đường dẫn `file:` tới các repo đã clone):
  `com.fomoxa.networking`, `com.fomoxa.rpc`, `fomoxa.unity` (unity-codecs)
- Viết: `PlayerBehaviour` (di chuyển), `NetworkTransform`, spawn khi connect, camera follow.
- Model đặt ở `Assets/Network/Models/`, chạy `fomoxac generate` → `Assets/Network/Generated/`.
- `Server~/Program.cs` dựng `StandaloneRuntime` (theo `console-server/runtime.md`), pod nghe UDP **7778**.

**DoD**: 2 instance Unity client nối `127.0.0.1:7778`, mỗi client 1 nhân vật, WASD di chuyển, thấy nhau mượt.

---

### P2 — Directory + Backend (1–2 ngày) ✅ XONG

- `cargo build --release -p fomoxa-directory -p fomoxa-gateway` trong `gateway/` (20 s, `target/release/*.exe`).
- Sinh keys (`scripts/gen-keys.ps1`), chạy directory với `deploy/directory.toml`.
- Pod: `DirectoryPodSettings { DirectoryAddress, DirectoryPort=9500, Key, Address, Port=7778, Version, Region, MaxPlayers }` → pod tự register rồi `AcquireRoom("arena:1")`.
- Backend: `MyGame.Backend` (từ `dotnet new fomoxa-backend`), `Fomoxa__Directory__Address` + 2 key file.

**DoD**: `JoinRoom("arena:1","1.0.0")` trả ticket có `Address:Port` = pod thật; client nối pod bằng grant; 2 client vào cùng room. ✅

**Kết quả đo được** (`logs/`):

```
[Join] logged in as ana
[Join] room arena:1 -> 127.0.0.1:7778, grant 217 bytes
client: Started
[Player] spawn on client id=3 owner=3 mine=True
```

- Pod: `[Pod] registered id=26276111` → `[Pod] holds arena:1` → `peer 3: Started`.
- Client 2 thấy cả 2 object (`mine=True` / `mine=False`), vị trí khớp khi một client chạy `-bot`.
- Client tắt → pod `peer 1: Stopped Timeout` và thư viện **tự despawn** object của peer (không cần code).

**Quyết định & bẫy của P2**

- Pod P2 = **chính Unity build** (`MyGame.exe -pod`), chưa phải console server. Lý do: tái dùng đúng code Unity, không phải nhân đôi `Player`/`NetworkTransform`. Console server (Linux, container) để dành cho P5 (Agones).
- Vì pod dùng encrypted UDP, **host mode** phải tự đặt `ConnectTokens` (key random) trước `StartConnection`, nếu không `ServerManager` ném lỗi. Client localhost bỏ qua grant.
- `Version` của pod và client đều là `Application.version` = `1.0` (bundleVersion mặc định), nên `filter = "pod.version == client.version"` luôn khớp. Đổi `bundleVersion` phải đổi cả hai đầu.
- Directory giữ mapping room → pod **cho tới khi reap pod chết**; pod mới `AcquireRoom` cùng key sẽ nhận `FAILED_PRECONDITION`. Khi test lại: restart directory hoặc đổi roomKey.
- Model rpc phải có codec **trước khi** compile `Services.cs`, nếu không `FRPC003`. Vì fomoxac chạy sau compile: copy `Models/` → generate → rồi mới thêm `Services.cs`.
- `-logFile -` của Unity player **không** ghi ra stdout (buffer) → luôn `-logFile <đường dẫn>`.
- Backend net8.0 trên máy chỉ có .NET 9 → cần `RollForward=Major` (đã đặt trong csproj).

---

### P3 — Gateway (1–2 ngày) ✅ XONG

- Chạy gateway với `deploy/gateway.toml` (UDP 7777, `pod_bind = 0.0.0.0:0`).
- Directory thêm `public_address = "127.0.0.1:7777"` → ticket trỏ gateway.
- Pod chuyển sang **encrypted UDP + `encryptedTransport.BehindGateway = true`**.

**DoD**: ticket trỏ 7777; 2 client qua gateway vào cùng pod; datagram có envelope 20 byte; gateway không giữ state per-session. ✅

**Kết quả đo được**

```
ANA [Join] room arena:1 -> 127.0.0.1:7777, grant 217 bytes     <- trước P3 là :7778
ANA client: Started -> [Player] spawn id=1 mine=True
BO  [Pos] id=1 pos=(5.98, 1.00, 6.10)                          <- thấy ana qua gateway
POD peer 1: Started / peer 2: Started
GW  (stderr) clients on UDP 0.0.0.0:7777, pods from UDP 0.0.0.0:57535, directory at 127.0.0.1:9500
```

- `pod_bind` lấy cổng ngẫu nhiên (57535) — đây là cổng gateway dùng để nói chuyện với pod.
- Pod chạy `BehindGateway = true` mà client vẫn vào được ⇒ datagram đi qua gateway có envelope
  20 byte; nếu thiếu envelope, listener của pod từ chối ngay.
- Gateway không cần biết trước pod: nó nhận bảng pod từ directory rồi route theo 4 byte id
  trong session id.

**Bẫy**: `BehindGateway` là cấu hình của **listener phía server**. Host mode (không gateway)
phải đặt `false`, nếu không client local không vào được — `Launcher.StartHost` làm việc này.

---

### P4 — Nakama (1–2 ngày) ✅ XONG

- `deploy/compose.yaml`: Postgres 15 + Nakama 3.41.0, cổng 7350/7351, console `admin/admin`.
- Module Lua `deploy/nakama/modules/main.lua` (mount vào `/nakama/data/modules`):
  - `find_match` / `poll_match`: ghép 2 người theo room, state trong storage
    (`match_queue`, `match_result`), phát ticket UUID TTL **120 s** (`match_ticket`).
  - `verify_ticket`: chỉ backend gọi được (http_key), kiểm tra ticket tồn tại, chưa hết hạn,
    đúng room và đúng user.
  - `reset_match`, `health` để test.
- Backend (`NakamaLink` + `Players` + `Lobby`):
  - `Players.Authenticate` bắt buộc `DeviceId` khi bật Nakama → `POST /v2/account/authenticate/device`,
    giữ session token theo user id (`PlayerSessions`).
  - `Lobby.JoinRoom` gọi `find_match`/`poll_match` (chờ tối đa 15 s) rồi `verify_ticket`
    **trước khi** xin ticket từ directory. Không ghép được hoặc ticket sai/không thuộc user
    → `FAILED_PRECONDITION`; Nakama không trả lời → `UNAVAILABLE`.
- Client Unity chỉ gửi `-device:<id>` (mặc định `fomoxa-device-<tên>`); toàn bộ gọi Nakama
  nằm ở backend nên client không cần biết http_key và không lộ server key.

**Khác PLAN ban đầu**: không dùng `proof = HMAC(...)`. Ticket UUID lưu server-side rồi verify
bằng http_key chặt hơn (có state, chống replay, thu hồi được bằng TTL) và không phải chia sẻ
secret HMAC cho client. Cũng vì vậy client **không** gọi Nakama trực tiếp như mục 4 cũ.

**DoD**: 2 client match → cùng roomKey → cùng pod; roomKey giả/hết hạn bị `FAILED_PRECONDITION`. ✅

**Kết quả đo được** (`logs/`):

```
ANA  [Join] logged in as ana -> room arena:1 -> 127.0.0.1:7777, grant 250 bytes
BO   [Join] logged in as bo  -> room arena:1 -> 127.0.0.1:7777, grant 250 bytes
ANA  [Player] spawn id=3 mine=False / id=4 mine=True
BO   [Player] spawn id=3 mine=True  / id=4 mine=False
FAKE [Join] failed: không ghép được trận cho fake:1 trong 15 giây (FAILED_PRECONDITION)
BE   [Nakama] authenticated fomoxa-device-ana as 761d6e7a... / ...-bo as b9a707e5...
```

`scripts/test-nakama.ps1` kiểm tra riêng module: 2 user nhận cùng ticket, user ngoài và
ticket bịa bị từ chối.

**Bẫy của P4**

- Deadline mặc định của fRPC là **5 s**, trong khi match chờ tới 15 s → client phải gọi
  `JoinRoomAsync(request, CallOptions.Within(30s))`, nếu không sẽ thấy `DEADLINE_EXCEEDED`
  thay vì `FAILED_PRECONDITION`.
- Hai client cùng `-name:` dùng chung device id → cùng user Nakama → `find_match` coi là
  "đang chờ" và không bao giờ ghép; mỗi client phải một tên (hoặc `-device:` riêng).
- `start-cluster.ps1` / `run-backend.ps1` tự dò `http://127.0.0.1:7350/healthcheck`; Nakama
  không chạy thì backend tắt matchmaking (vẫn chơi được, nhưng ai cũng vào được room).
- Nakama ghi `deploy/nakama/.cookie` → đã ignore, đừng commit.


---

### P5 — Agones (4–5 ngày) 🚧

Mục tiêu: game server chạy như `GameServer` của Agones, tự register với directory, giữ room,
`Allocated` khi có room, và rời fleet sau khi drain.

#### Phạm vi đã chốt

| # | Quyết định | Chốt | Lý do |
|---|---|---|---|
| D1 | Game server | **Console server** `MyGame/Server~` (.NET 8) | không có Unity Linux module; image nhỏ, không cần GPU/X |
| D2 | Gateway | **Trong cluster**, 1 replica, `portPolicy: None` | chỉ mở 1 cổng UDP ra host; pod không cần host port động |
| D3 | Nakama + Postgres | **Trong cluster** | giữ nguyên luồng P4 end-to-end |
| D4 | Drain | **`DirectoryAdmin.Drain`** (RPC qua port-forward 9501) | `kubectl drain` không cho thời gian relocate room |
| D5 | Transfer người chơi | **Không làm** | P5 chỉ drain room rỗng; client join lại sau drain |

#### Kiến trúc đích

```
Unity client (Windows host)
   │ TCP 9600 (login + join)
   ▼
k3d: 9600 ──► backend pod ──► nakama pod (7350) ──► postgres
                  │ Allocate(roomKey)
                  ▼
             directory pod (9500)
                  ▲ register / AcquireRoom / report_load / WatchDrain
                  │
k3d: 7777/udp ──► gateway pod ──► game-server pod-1 / pod-2  (:7778)
                                       ▲
                                       └── Agones sidecar (9357)
```

Client chỉ thấy 2 cổng: TCP **9600** (backend) và UDP **7777** (gateway).
Directory đặt `public_address = "127.0.0.1:7777"` để ticket trỏ gateway, không trỏ pod.

#### Room-key cho nhiều pod

Cơ chế hiện tại có một lỗ hổng: `allocate` gán room cho pod theo `placement`, nhưng pod **chỉ
báo** room mình giữ nếu đã gọi `AcquireRoom`; `report_load` → `reconcile` **xoá** room mà pod
không báo. Vì vậy room gán cho pod không biết key sẽ biến mất sau ≤5 s.

Phương án chọn cho P5 (không sửa Rust): **pool cố định** `arena:1 … arena:4`; pod thứ *i* gọi
`AcquireRoom("arena:<i>")` ngay trong `OnRegistered`. `allocate` thấy key đã có chủ → trả đúng
pod đó. Nakama vẫn trả `roomKey` client yêu cầu nên test bằng 2 key tường minh.

**Nợ kỹ thuật ghi lại**: module Lua tự chọn room trống trong pool (để nhiều client cùng lúc
không dồn vào 1 room) — làm ở P5.5, kèm test ≥4 client.

#### Các bước (thứ tự commit)

| # | Việc | Commit |
|---|---|---|
| 1 | Vendor `com.fomoxa.networking` vào repo; tạo `MyGame/Server~` (csproj, `Program.cs`, `PlayerEntity.cs`) | `P5: console server chay local` |
| 2 | Wrapper Agones SDK (`Ready/Health/Allocate/Shutdown`) + `OnDrain`/`OnDrainDeadline`, no-op ngoài cluster | `P5: Agones lifecycle` |
| 3 | 3 Dockerfile: game server, directory, gateway | `P5: dong goi image` |
| 4 | `deploy/k8s/`: manifest + `directory.toml`/`gateway.toml` bản K8s + script sinh Secret | `P5: manifest k8s + agones fleet` |
| 5 | `scripts/k8s-up.ps1`, `k8s-down.ps1`, `test-p5.ps1` | `P5: script cum k8s` |
| 6 | Cập nhật `PLAN.md` + `MyGame/README.md` | `P5: tai lieu` |

**Bước 1 là cửa ải**: console server chạy local mà 2 client Unity không thấy nhau di chuyển thì
dừng lại sửa, không đi tiếp. Phần Agones/K8s phía sau chỉ là đóng gói.

#### DoD

```powershell
kubectl get gs
#   pod-1   Ready      10.42.0.x
#   pod-2   Ready      10.42.0.y

kubectl get gs -o custom-columns=NAME:.metadata.name,STATE:.status.state
#   pod-1   Allocated
#   pod-2   Allocated

powershell -File scripts\test-p5.ps1
#   ana -> arena:1 -> 127.0.0.1:7777 ... mine=True
#   bo  -> arena:2 -> 127.0.0.1:7777 ... mine=True
```

Thêm: scale Fleet 2 → 3 thì pod-3 `Ready` và register; drain pod-2 (room rỗng) → room relocate →
pod-2 `Shutdown` → Fleet tạo pod thay thế.

#### Bẫy dự kiến

- **Hai Docker engine**: `docker` trên Windows = Docker Desktop; `wsl -d Ubuntu -- docker` là
  engine khác. Luôn kiểm `kubectl config current-context` = `k3d-fomoxa`.
- **Grace period < drain**: `drain_seconds = 300` > mặc định 30 → đặt
  `terminationGracePeriodSeconds: 360`, nếu không pod bị SIGKILL giữa drain.
- **`Ready` trước register**: sidecar Agones khởi động chậm hơn container vài trăm ms → retry
  60 × 500 ms rồi mới `DirectoryPod.Start()`.
- **Đổi pod id sau reconnect**: `DirectoryPod` cấp key ticket mới → stop listener, set
  `ConnectTokens`, start lại (xử lý trong `OnRegistered` khi `ChangedPodId`).
- **Package path có hash**: `Library/PackageCache/com.fomoxa.networking@<hash>` không commit
  được → phải vendor trước khi viết Dockerfile.
- **Behaviour + threshold**: console phải là `NetworkTransform` rồi `Player`, với
  `(true, true, false, 0.01, 0.1, 0.01)`; sai là `SchemaConflict` (trông như lỗi mạng).
- **Version `1.0`**: placement `filter = "pod.version == client.version"`; console hardcode sai
  version thì `Allocate` trả `NoPodAvailable` dù Fleet đầy pod `Ready`.
- **`public_address`**: phải là địa chỉ client thấy (`127.0.0.1:7777`), không phải DNS trong
  cluster.
- **k3d + UDP**: phải map cổng lúc tạo cluster (`--port "7777:7777/udp@loadbalancer"`,
  `--port "9600:9600@loadbalancer"`), không thêm sau được.

#### Ước lượng

| Việc | Ngày |
|---|---|
| Bước 1: vendor + `Server~` + test local 2 client | 1.5 |
| Bước 2: Agones lifecycle | 0.5 |
| Bước 3: Dockerfile | 0.5 |
| Bước 4: manifest + config K8s | 1 |
| Bước 5: script + debug placement/reconcile | 1 |
| Buffer | 0.5 |
| **Tổng** | **~5** |

---

### P6 — Hardening (tuỳ chọn)

- TLS ingress trước 9600/9601, `limit_conn`.
- Probe bằng `fomoxa-rpc-health` (exec, không HTTP).
- Vá 5 điểm backend: try/catch `Backend.Start`, log `Status.Internal`, giữ `Details`, `Idempotent` cho `JoinRoom`, `FormatException → Unavailable`.

---

## 5. Layout sau khi hoàn thành

```
D:\Documents\fomoxa\
├─ PLAN.md                     ← file này
├─ gateway\                    (clone, ignore) → target/release/fomoxa-directory.exe
├─ unity-docs\                 (clone, ignore) — docs offline
├─ MyGame\                     ← Unity project (P1, P2)
│  ├─ Assets\Network\Models\        model net (MoveInput) + Backend (rpc)
│  ├─ Assets\Network\Rpc\Services.cs  bản copy từ backend (để sinh fRPC client)
│  ├─ Assets\Network\Generated\     codec fomoxac + codec rpc
│  ├─ Server~\                      (P5) console server .NET 8 — image game server
│  └─ Builds\MyGame.exe             build dùng làm client
├─ MyGame.Backend\             ← backend (template fomoxa-backend, P2)
├─ deploy\
│  ├─ directory.toml
│  ├─ gateway.toml             (P3)
│  ├─ compose.yaml             (P4 — Postgres + Nakama)
│  ├─ nakama\modules\main.lua  (P4 — matchmaking 2 người)
│  ├─ k8s\                     (P5 — manifest + config bản K8s)
│  └─ secrets\                 ← sinh bởi gen-keys.ps1, KHÔNG commit
└─ scripts\
   ├─ gen-keys.ps1
   ├─ start-cluster.ps1        directory + backend + pod
   ├─ stop-all.ps1
   ├─ k8s-up.ps1 / k8s-down.ps1  (P5)
   ├─ test-nakama.ps1 / test-p4.ps1
   └─ run-directory.ps1 / run-backend.ps1 / run-pod.ps1 / run-client.ps1
```

---

## 6. Rủi ro

| Rủi ro | Giảm thiểu |
|---|---|
| UDP 7777 vs pod 7778 trùng port | Cố định: gateway 7777, pod 7778 |
| `root` key sai định dạng (phải 64 hex) | `gen-keys.ps1` sinh đúng |
| Lệch model giữa Unity và console server → `SchemaConflict` | Luôn generate lại + build lại cả hai; ghi vào `up.ps1` |
| Pod phải `behindGateway=true` mới đi qua gateway | Chỉ bật ở P3, đừng bật sớm |
| Nakama matchmaker async | Log match id; fallback set roomKey tay để test P2/P3 |
| Deadline fRPC mặc định 5 s < match timeout 15 s | `JoinRoomAsync` phải truyền `CallOptions.Within(30s)` |
| Hai client trùng device id → không ghép được | Mỗi client một `-name:` / `-device:` riêng |
| Agones + Directory cùng placement | Agones chỉ giữ pod sống, không gọi `GameServerAllocation` |
| Windows/Docker Desktop + UDP + kind | P5 dùng **k3d trên engine Docker Desktop** (không dùng engine trong WSL Ubuntu) |
| Hai Docker engine (Desktop vs Ubuntu) → 2 cluster | Luôn kiểm `kubectl config current-context` = `k3d-fomoxa` |
| `reconcile` xoá room pod không báo | Pod phải `AcquireRoom` lại sau **mỗi** lần register (pool `arena:i`) |
| `terminationGracePeriodSeconds` 30 < `drain_seconds` 300 | Đặt 360 trong Fleet |
| Cổng UDP không map được sau khi tạo k3d cluster | Map ngay lúc `k3d cluster create` |

---

## 7. Lịch

| Mốc | Full-time | Buổi tối |
|---|---|---|
| P1 (demo thấy nhau) | 1–2 ngày | ~1 tuần |
| + P2, P3 (đủ Fomoxa) | ~5 ngày | 3–4 tuần |
| + P4 (Nakama) | ~7 ngày | 5–6 tuần |
| + P5 (Agones) | ~12 ngày | 8–10 tuần |

---

## 8. Việc tiếp theo

P5 đã chốt phạm vi (xem mục 4). Bắt đầu ở **bước 1**:

1. Vendor `com.fomoxa.networking` vào repo (bỏ phụ thuộc `Library/PackageCache/<hash>`).
2. Tạo `MyGame/Server~`: csproj + `Program.cs` + `PlayerEntity.cs`, chạy `dotnet run`.
3. 2 client Unity join `arena:1` vào console server, thấy nhau di chuyển → mới đi tiếp.

Sau bước 1 mới tới Agones lifecycle (bước 2) và phần K8s (bước 3–5).


