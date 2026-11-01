# Kế hoạch cụm Fomoxa + Nakama + Agones

Cập nhật: đã clone repos về `D:\Documents\fomoxa`, đã kiểm tra môi trường máy.

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

---

## 1. Repo & package đang dùng

| Thứ | Vai trò | Trạng thái |
|---|---|---|
| `MyGame` | Project Unity 6000.5.7f1, game demo (P1 xong) | ✅ trong repo git |
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

### P2 — Directory + Backend (1–2 ngày)

- `cargo build --release -p fomoxa-directory -p fomoxa-gateway` trong `gateway/`.
- Sinh keys (`scripts/gen-keys.ps1`), chạy directory với `deploy/directory.toml`.
- Pod: `DirectoryPodSettings { DirectoryAddress, DirectoryPort=9500, Key, Address, Port=7778, Version="1.0.0", Region, MaxPlayers }` → pod tự register.
- Backend: chạy `MyGame.Backend` với `Fomoxa__Directory__Address=127.0.0.1:9500` + key file.

**DoD**: `JoinRoom("arena:1","1.0.0")` trả ticket có `Address:Port` = pod thật; client nối pod bằng grant; 2 client vào cùng room.

---

### P3 — Gateway (1–2 ngày)

- Chạy gateway với `deploy/gateway.toml` (UDP 7777).
- Directory thêm `public_address = "127.0.0.1:7777"` → ticket trỏ gateway.
- Pod chuyển sang **encrypted UDP + `behindGateway = true`**
  (`EncryptedUdpNetworkTransport.behindGateway`, `EncryptedUdpTransportFactory(..., behindGateway)`).

**DoD**: ticket trỏ 7777; 2 client qua gateway vào cùng pod; datagram có envelope 20 byte; gateway không giữ state per-session.

---

### P4 — Nakama (1–2 ngày)

- Postgres + Nakama (compose).
- Module: device auth → matchmaker 2 người → storage `match:xxx → [userIds]` → ký `proof = HMAC(hmac, userId|roomKey|exp)`.
- Backend: `Players.Authenticate` verify token Nakama + verify `proof` trong `JoinRoom`.

**DoD**: 2 client match → cùng roomKey → cùng pod; roomKey giả/hết hạn bị `FAILED_PRECONDITION`.

---

### P5 — Agones (3–5 ngày) ⚠️

- **WSL2 + k3d** (không dùng Docker Desktop + kind trên Windows).
- Agones helm + Fleet 2 replicas (image = console server).
- Directory/gateway/backend/nakama = Deployments; gateway **trong cluster** để tới pod IP.
- Pod đọc env Agones → register với pod IP.

**DoD**: `kubectl scale fleet` → pod mới tự register; room mới đặt lên pod mới; drain 1 pod → relocate.

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
├─ gateway\                    (clone) → target/release/*.exe
├─ com.fomoxa.networking\      (clone) — package
├─ com.fomoxa.rpc\             (clone) — package
├─ unity-codecs\               (clone) — package
├─ RedRunner\                  (clone) — tham chiếu gameplay
├─ unity-docs\                 (clone) — docs offline + bản vi/
├─ backend\                    (clone) — template gốc
├─ MyGame\                     ← Unity project (tạo ở P1)
│  ├─ Assets\Network\Models\
│  ├─ Assets\Network\Generated\
│  └─ Server~\MyGame.Server.csproj   ← pod
├─ deploy\
│  ├─ directory.toml
│  ├─ gateway.toml
│  └─ compose.yaml             (P4: + postgres + nakama)
├─ keys\                       ← sinh bởi script, KHÔNG commit
└─ scripts\
   ├─ gen-keys.ps1
   ├─ up.ps1  / down.ps1
   └─ smoke.ps1
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
| Agones + Directory cùng placement | Agones chỉ giữ pod sống, không gọi `GameServerAllocation` |
| Windows/Docker Desktop + UDP + kind | P5 dùng WSL2 + k3d |

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

1. `scripts/gen-keys.ps1` — 5 role key + root (64 hex) + pod key backend + hmac.
2. Tạo `MyGame` bằng Unity CLI + sửa `manifest.json`.
3. Viết `PlayerBehaviour`, spawn, input, camera.
4. Viết `Server~/Program.cs` + csproj.
5. `deploy/directory.toml`, `deploy/gateway.toml`.
