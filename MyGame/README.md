# MyGame - demo Fomoxa: sàn + di chuyển + thấy nhau

Demo nhỏ nhất chứng minh tầng mạng Fomoxa chạy được: một sàn, vài cột mốc, mỗi
người chơi là một capsule, di chuyển bằng WASD/mũi tên và thấy nhau di chuyển.

- Server authoritative: client chỉ gửi input, server mới là bên di chuyển.
- Input đi qua hệ thống input của Fomoxa (`NetworkInput.Use`), không dự đoán
  (không reconcile) nên client thấy vị trí của chính mình sau một vòng mạng -
  chấp nhận được cho demo, và chính vì thế log chứng minh rõ dữ liệu là từ server.
- Vị trí đồng bộ bằng `NetworkTransform` (position + rotation).
- Không có asset ngoài: sàn/cột/capsule đều là primitive.

## Cấu trúc

```
MyGame/
  Assets/
    Network/
      Models/MoveInput.cs        model input (X, Z) - fomoxac sinh codec
      Models/Backend/            bản copy model của backend (rpc) để sinh client
      Rpc/Services.cs            bản copy Services.cs của backend (interface fRPC)
      Generated/                 do fomoxac + fRPC sinh, ĐỪNG sửa tay
      Player.cs                  NetworkBehaviour: Gather input + Apply di chuyển
      Launcher.cs                Host / pod / spawn player khi có peer
      RoomJoiner.cs              login backend → JoinRoom → nối pod bằng grant
      PlayerCamera.cs            camera bám theo player của mình
    Editor/DemoScene.cs          dựng scene + prefab + build player (menu Tools)
    FomoxaSettings.asset         Models = Assets/Network/Models, Generated = Assets/Network/Generated
    Scenes/Game.unity            scene đã dựng sẵn
    Prefabs/Player.prefab        capsule + NetworkObject + NetworkTransform + Player
  Server~/                     (P5) console server .NET 8 - pod không cần Unity
    MyGame.Server.csproj       build từ ../server-deps (source vendor)
    Program.cs                 vòng lặp frame + DirectoryPod + spawn theo peer
    PlayerEntity.cs            StandaloneEntity: PlayerTransform + Player (khớp prefab)
    PodConfig.cs               --key=value → env → mặc định
    PodTransport.cs            listener mã hoá, gắn ticket key sau khi register
  Builds/MyGame.exe              bản build dùng làm pod và client
```

## Hai scene: sảnh và trận đấu

| Scene | Có gì | Khi nào tải |
|---|---|---|
| `Assets/Scenes/Lobby.unity` | camera nền tối, `RoomJoiner` (kết nối backend, giữ phiên đăng nhập), `BootRouter`, giao diện sảnh | scene đầu tiên của bản build |
| `Assets/Scenes/Game.unity` | sàn, cột mốc, camera bám người chơi, `NetworkManager`, `Launcher`, HUD trong trận | chỉ sau khi người chơi bấm vào một phòng và đã có ticket |

Luồng: `Lobby` (đăng nhập → danh sách phòng + người online) → bấm phòng → `RoomJoiner` lấy ticket → `MatchSession.Begin` tải scene `Game` →
`Launcher` lấy ticket ra và nối vào pod → bấm Thoát phòng (hoặc mất kết nối) → quay về `Lobby`, vẫn đăng nhập.
`RoomJoiner` sống xuyên hai scene nên danh sách online vẫn cập nhật khi đang chơi.

Dòng lệnh `-pod`, `-host`, `-client` bỏ qua sảnh và vào thẳng scene `Game` như trước (`BootRouter`). `-autojoin` vẫn đi qua sảnh để đăng nhập và lấy ticket.

## Giao diện (UI Toolkit, dựng từ asset)

```
Assets/UI/Lobby.uxml / Lobby.uss        giao diện sảnh (đăng nhập, phòng, online)
Assets/UI/MatchHud.uxml / MatchHud.uss  HUD trong trận (tên phòng, nút thoát, công cụ dev)
Assets/UI/LobbyTheme.tss                theme mặc định của UI Toolkit
Assets/UI/LobbyPanelSettings.asset      tạo tự động
Assets/Network/LobbyScreen.cs           nối sảnh với RoomJoiner
Assets/Network/MatchHudScreen.cs        nối HUD với Launcher
Assets/Network/MatchSession.cs          chuyển ticket từ sảnh sang scene Game
Assets/Editor/LobbyUiSetup.cs           gắn UI vào scene
```

Tools → Demo → Rebuild Scene dựng lại cả hai scene và đặt Build Settings (`Lobby` trước, `Game` sau).
Sửa giao diện: mở `.uxml` bằng UI Builder hoặc chỉnh `.uss`, không đụng code.

## Package

`Packages/manifest.json` lấy package trực tiếp từ GitHub, không dùng đường dẫn local:

```json
"com.fomoxa.networking": "https://github.com/fomoxa/com.fomoxa.networking.git#0.3.4",
"com.fomoxa.rpc": "https://github.com/fomoxa/com.fomoxa.rpc.git#0.1.5"
```

Unity clone về `Library/PackageCache/` và ghim đúng commit trong
`Packages/packages-lock.json`. Máy cần có `git` trong PATH (máy này có 2.52.0).
Đổi version chỉ cần sửa tag sau dấu `#` rồi để Unity resolve lại.

## Chạy đủ cụm: directory + gateway + backend + pod (P3/P4)

Cần 5 thành phần: `fomoxa-directory` (9500), `fomoxa-gateway` (UDP 7777), backend
(9600), pod (UDP 7778), client. Nakama (7350) là tuỳ chọn: bật thì backend bắt buộc
ghép trận trước khi cấp ticket; tắt thì ai cũng vào được room.

Lần đầu chạy, sinh key (file key nằm ở `deploy/secrets/`, không commit):

```powershell
cd D:\Documents\fomoxa
powershell -NoProfile -File scripts\gen-keys.ps1
```

Bật Nakama + Postgres cho matchmaking (P4):

```powershell
cd D:\Documents\fomoxa\deploy
docker compose up -d
cd ..
# kiểm tra module ghép trận (2 user, ticket, user ngoài, ticket bịa)
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\test-nakama.ps1
```

Sau khi cụm đã chạy, kiểm chứng đầu-cuối P4 (2 client + room giả):

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\test-p4.ps1
```

`start-cluster.ps1` tự dò `http://127.0.0.1:7350/healthcheck`: có Nakama thì bật
matchmaking, không có thì in cảnh báo và chạy tiếp.

Build `fomoxa-directory` + `fomoxa-gateway` (chỉ cần lại khi sửa code Rust):

```powershell
cd D:\Documents\fomoxa\gateway
cargo build --release -p fomoxa-directory -p fomoxa-gateway
```

Khởi động cụm (directory + gateway + backend + 1 pod, log trong `logs/`):

```powershell
cd D:\Documents\fomoxa
powershell -NoProfile -File scripts\start-cluster.ps1
```

Chạy 2 client - mỗi client tự đăng nhập backend, lấy ticket rồi vào pod:

```powershell
powershell -NoProfile -File scripts\run-client.ps1 -Name ana
powershell -NoProfile -File scripts\run-client.ps1 -Name bo
```

Mặc định client chạy headless (`-batchmode -nographics`). Muốn thấy hình thì thêm
`-Windowed`. Dừng tất cả bằng `powershell -NoProfile -File scripts\stop-all.ps1`.

Ai chạy ở đâu:

| Thành phần | Cổng | Vai trò |
|---|---|---|
| `fomoxa-directory` | 9500 (admin 9501) | biết pod nào giữ room nào, phát grant |
| `fomoxa-gateway` | UDP 7777 | địa chỉ công khai duy nhất cho mọi pod, route theo session id |
| `MyGame.Backend` | TCP 9600, WS 9601, pod 9602 | đăng nhập (Nakama), `JoinRoom` → ticket + grant |
| Nakama + Postgres | 7350 / 7351, 5432 | ghép 2 người, phát & verify ticket (tuỳ chọn) |
| pod (`MyGame.exe -pod`) | UDP 7778 | giữ room `arena:1`, chạy game |
| pod (`MyGame.Server.exe`) | UDP 7778 | như trên, nhưng không cần Unity (mặc định từ P5) |
| client | - | login → join → nối gateway bằng grant (encrypted UDP) |

Luồng: client → backend (fRPC/TCP) → directory (allocate) → ticket {address, port,
grant} → client nối gateway :7777 bằng grant; gateway bọc datagram trong
envelope 20 byte rồi chuyển tới pod :7778. Pod đã `AcquireRoom("arena:1")` nên
directory biết room nằm ở pod nào, còn gateway học bảng pod từ directory.

## Console server (P5)

Từ P5, pod mặc định là `MyGame/Server~` - bản .NET 8 của server, không cần Unity,
để đóng image Linux cho Agones. Nó khớp prefab `Player.prefab` từng behaviour
(`PlayerTransform` rồi `Player`, `syncPosition/Rotation` bật, `syncScale` tắt,
ngưỡng `0.01 / 0.1 / 0.01`), nên client Unity join vào không cần sửa gì.

Source của 2 package Fomoxa được vendor vào `server-deps/` vì đường dẫn thật
(`Library/PackageCache/com.fomoxa.networking@<hash>`) có hash và bị gitignore.
Sinh lại mỗi khi đổi version trong `Packages/manifest.json`:

```powershell
cd D:\Documents\fomoxa
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\sync-server-deps.ps1
```

Build và chạy:

```powershell
cd MyGame\Server~
dotnet build                       # 0 warning (NoWarn CS8632 cho source vendor)

# chạy 1 pod, dùng chung cụm local
cd D:\Documents\fomoxa
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\run-pod.ps1
```

`start-cluster.ps1` chọn loại pod bằng `-Server`:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\start-cluster.ps1              # console (mặc định)
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\start-cluster.ps1 -Server unity # MyGame.exe -pod
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\start-cluster.ps1 -Pods 2       # pod-1 arena:1, pod-2 arena:2
```

Tham số của `MyGame.Server.exe` (ưu tiên cao hơn biến môi trường):

| Tham số | Biến môi trường | Mặc định |
|---|---|---|
| `--name=` | `FOMOXA_POD_NAME` | `pod-1` |
| `--port=` | `FOMOXA_POD_PORT` | `7778` |
| `--room=` (nhiều room cách `,`) | `FOMOXA_ROOM` | `arena:1` |
| `--directory=host:port` | `FOMOXA_DIRECTORY` | `127.0.0.1:9500` |
| `--address=` | `FOMOXA_POD_ADDRESS` / `POD_IP` | `127.0.0.1` |
| `--version=` | `FOMOXA_VERSION` | `1.0` (phải khớp `bundleVersion` của client) |
| `--region=` | `FOMOXA_REGION` | `local` |
| `--max-players=` | `FOMOXA_MAX_PLAYERS` | `16` |
| `--behind-gateway=` | `FOMOXA_BEHIND_GATEWAY` | `true` |
| `--trace` | `FOMOXA_TRACE` | tắt |
| `--key=` | `POD_KEY_FILE` | (bắt buộc) file key `directory/pod` |

Kiểm chứng lặp lại được (pod giữ room, 2 client thấy nhau, bot di chuyển qua server):

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\test-console-server.ps1
```

## Chạy nhanh trong Editor (không cần cụm)

1. Mở project bằng Unity 6000.5.7f1, mở `Assets/Scenes/Game.unity`.
2. Bấm Play, rồi bấm Host (local server + player) - server + client trong
   cùng tiến trình, tự sinh token, không cần backend/directory.
3. Chạy `Builds\MyGame.exe` khi cụm đã chạy (xem trên) để thấy 2 người thật.
4. Trong cụm, nút Join room arena:1 via backend làm đúng việc client headless
   làm: login → join → connect pod.

## Chạy bằng dòng lệnh (không GUI)

```powershell
cd D:\Documents\fomoxa\MyGame\Builds

# pod (cần set POD_KEY_FILE, xem scripts\run-pod.ps1)
$env:POD_KEY_FILE = "D:\Documents\fomoxa\deploy\secrets\directory\pod"
.\MyGame.exe -batchmode -nographics -logFile pod.log -pod -podname:pod-1 -podport:7778 -room:arena:1

# client: login + join qua backend rồi vào pod
.\MyGame.exe -batchmode -nographics -logFile client.log -autojoin -name:ana -room:arena:1

# host cục bộ, không cần cụm
.\MyGame.exe -host -bot -trace -logFile host.log
```

| Tham số | Ý nghĩa |
|---|---|
| `-pod` | chạy như pod: register với directory, giữ room, nghe UDP 7778 |
| `-podname:<tên>` | tên pod gửi lên directory (mặc định `pod-1`) |
| `-podport:<cổng>` | cổng UDP của pod (mặc định 7778) |
| `-room:<key>` | room pod giữ / room client xin vào (mặc định `arena:1`) |
| `-directory:<ip:cổng>` | địa chỉ directory (mặc định `127.0.0.1:9500`) |
| `-autojoin` | client tự login backend rồi join room |
| `-name:<tên>` | tên người chơi gửi lên backend (mỗi client một tên) |
| `-device:<id>` | device id Nakama (mặc định `fomoxa-device-<tên>`; phải khác nhau giữa 2 client) |
| `-host` | server + client trong cùng tiến trình (không cần cụm) |
| `-bot` | thay bàn phím bằng chuyển động tròn (test tự động) |
| `-trace` | log vị trí mỗi 2 giây |
| `-server` | chỉ chạy server thuần (chỉ dùng khi tự cấp token) |

`POD_KEY_FILE` là biến môi trường trỏ tới file key `directory/pod` (Windows không
set được biến môi trường rỗng nên key phải đọc từ file).

## Đổi địa chỉ server

Trong Editor: chọn `Network Manager`, sửa các ô trên component `Launcher`
(`directoryAddress`, `podAddress`, `podPort`) hoặc trên `RoomJoiner`
(`backendAddress`, `backendPort`).

## Dựng lại scene từ đầu

Menu Tools > Demo > Rebuild Scene hoặc chạy batchmode:

```powershell
& "C:\Program Files\Unity\Hub\Editor\6000.5.7f1\Editor\Unity.exe" `
  -batchmode -nographics -projectPath D:\Documents\fomoxa\MyGame `
  -executeMethod DemoScene.Build -quit -logFile build.log
```

Build player:

```powershell
& "C:\Program Files\Unity\Hub\Editor\6000.5.7f1\Editor\Unity.exe" `
  -batchmode -nographics -projectPath D:\Documents\fomoxa\MyGame `
  -executeMethod DemoScene.BuildPlayer -quit -logFile build.log
```

## Đã kiểm chứng

P1 - host + client (UDP thường):

```
HOST    [Player] spawn on server id=1 owner=1
HOST    [Player] spawn on client id=1 owner=1 mine=True
HOST    peer 2: Started
HOST    [Player] spawn on server id=2 owner=2
HOST    [Player] spawn on client id=2 owner=2 mine=False   <- host thấy player của client
HOST    [Pos] id=2 pos=(2.28, 1.00, 12.82)                 <- và thấy nó di chuyển
```

P2 - directory + backend + pod (encrypted UDP), 2 client headless:

```
ANA   [Join] logged in as ana
ANA   [Join] room arena:1 -> 127.0.0.1:7778, grant 217 bytes
ANA   client: Started
ANA   [Player] spawn on client id=3 owner=3 mine=True
ANA   [Player] spawn on client id=4 owner=4 mine=False
BO    [Join] room arena:1 -> 127.0.0.1:7778, grant 216 bytes
BO    [Pos] id=3 owner=3 pos=(18.88, 1.00, -11.26)         <- thấy ana (bot) di chuyển
POD   [Pod] registered id=26276111 -> [Pod] holds arena:1
POD   peer 3: Started / peer 4: Started
POD   peer 1: Stopped Timeout                              <- thư viện tự despawn object của peer
```

Client không bật prediction, nên vị trí của chính nó chỉ đổi khi server gửi -
đó là bằng chứng dữ liệu đã đi đúng vòng client → server → client.

P3 - thêm gateway, ticket trỏ 7777, pod vẫn 7778:

```
ANA   [Join] room arena:1 -> 127.0.0.1:7777, grant 217 bytes
ANA   client: Started -> [Player] spawn id=1 mine=True
BO    [Pos] id=1 pos=(5.98, 1.00, 6.10)        <- thấy ana, dữ liệu đi qua gateway
POD   peer 1: Started / peer 2: Started
GW    clients on UDP 0.0.0.0:7777, pods from UDP 0.0.0.0:57535, directory at 127.0.0.1:9500
```

Pod chạy `BehindGateway = true` mà client vẫn vào được, nghĩa là datagram tới pod
đúng là có envelope 20 byte do gateway thêm vào (thiếu envelope thì listener từ chối).

P4 - thêm Nakama matchmaking (device auth + ticket):

```
ANA   [Join] logged in as ana -> room arena:1 -> 127.0.0.1:7777, grant 250 bytes
BO    [Join] logged in as bo  -> room arena:1 -> 127.0.0.1:7777, grant 250 bytes
ANA   [Player] spawn id=3 mine=False / id=4 mine=True
BO    [Player] spawn id=3 mine=True  / id=4 mine=False
FAKE  [Join] failed: không ghép được trận cho fake:1 trong 15 giây (FAILED_PRECONDITION)
BE    [Nakama] authenticated fomoxa-device-ana as 761d6e7a...
```

`test-nakama.ps1` xác nhận: 2 user nhận cùng ticket, user ngoài và ticket bịa bị từ chối.

P5 bước 1 - pod là console server (`MyGame.Server.exe`), client vẫn là Unity:

```
POD   [Pod] registering pod-1 (127.0.0.1:7778, version 1.0) at 127.0.0.1:9500
POD   [Pod] registered id=26286610 changed=False -> listening on udp 7778 -> holds arena:1
ANA   [Join] room arena:1 -> 127.0.0.1:7777, grant 250 bytes
ANA   [Player] spawn on client id=3 mine=False / id=4 mine=True
BO    [Pos] id=3 x: 3.00 -> 3.15                       <- bot di chuyển qua server console
```

`dotnet build` 0 warning/0 error; `test-console-server.ps1` lặp lại được; `test-p4.ps1`
vẫn pass với pod console.

## Chạy cụm Kubernetes + Agones (P5)

Đưa toàn bộ game server stack lên Kubernetes với Agones:
- Game server chạy bằng Fleet Agones (`fomoxa-server`), quản lý vòng đời Ready/Allocated/Shutdown.
- Directory, Gateway, Backend, Nakama, Postgres chạy dạng Deployment/Service trong namespace `fomoxa`.
- Client Unity trên Windows kết nối vào cluster qua k3d LoadBalancer ports: UDP `7777` (Gateway) và TCP `9600` (Backend).

### Công cụ cần chuẩn bị

- Docker Desktop (đang chạy engine Windows)
- `kubectl`, `k3d`, `helm`:
  ```powershell
  winget install Kubernetes.kubectl
  winget install k3d-io.k3d
  winget install Helm.Helm
  ```

### Đóng gói Docker Images

```powershell
powershell -ExecutionPolicy Bypass -File scripts\build-images.ps1
```
Đóng gói 4 images: `fomoxa-server`, `fomoxa-directory`, `fomoxa-gateway`, `fomoxa-backend`.

### Khởi động cụm K8s

```powershell
powershell -ExecutionPolicy Bypass -File scripts\k8s-up.ps1
```
Script tự động:
1. Tạo cụm k3d `fomoxa` map ports 7777/udp và 9600/tcp ra host.
2. Cài đặt Agones via Helm chart vào namespace `agones-system`.
3. Import images vào cluster k3d.
4. Sinh Kubernetes Secrets từ `deploy/secrets/` qua `scripts\gen-k8s-secrets.ps1`.
5. Apply toàn bộ manifests `deploy/k8s/` và đợi các dịch vụ Ready.

### Chạy kiểm chứng DoD P5

```powershell
powershell -ExecutionPolicy Bypass -File scripts\test-p5.ps1
```
Kiểm chứng:
- 2 client Unity (`ana` và `bo`) join 2 room khác nhau (`arena:1`, `arena:2`) qua Gateway `127.0.0.1:7777`.
- Cả hai nhận spawn (`mine=True`).
- Cả hai GameServer pod chuyển sang trạng thái `Allocated`.
- Tuỳ chọn thêm `-TestScale` để kiểm tra scale Fleet 2 → 3 pods.

### Tắt cụm K8s

```powershell
powershell -ExecutionPolicy Bypass -File scripts\k8s-down.ps1
```

## Ghi chú kỹ thuật đã gặp

- Không cài `fomoxa.unity` (unity-codecs) cùng `com.fomoxa.networking`: cả hai
  có asmdef tên `Fomoxa.Unity.Editor` → Unity báo "Assembly with name ... already
  exists" và không compile gì cả. Chỉ cần nó khi model dùng `Vector3`,
  `Quaternion`... Demo này dùng `float` nên bỏ.
- `Packages/manifest.json` phải là UTF-8 không BOM; PowerShell 5.1 ghi BOM
  làm Unity báo `JSON parse error ... char 65279`.
- Model dùng `float` (wire type `f32`) để sau này console server (P2) compile
  được ngoài Unity - model chứa `UnityEngine.Vector3` sẽ không build được ở đó.
- Thứ tự component trên prefab phải là `NetworkObject` → `NetworkTransform` →
  `Player`; console server sau này phải khai báo behaviour cùng thứ tự.
- `NetworkManager` tự thêm `UdpNetworkTransport` nếu chưa gán. Ở P2 scene gán sẵn
  `EncryptedUdpNetworkTransport` (đặt bằng `SerializedObject` trong `DemoScene.cs`
  vì field `transport` là private).
- Encrypted UDP là bắt buộc khi có directory: pod chỉ nhận client có grant.
  Vì vậy host mode phải tự đặt `ConnectTokens` (key random) trước
  `StartConnection`, nếu không `ServerManager` ném lỗi; client cùng máy bỏ qua grant.
- Thứ tự sinh code khi thêm model rpc: `FRPC003 ... is not a [Network] model
  with an rpc codec` xuất hiện nếu compile `Services.cs` trước khi có codec. Cách
  làm: copy `Models/` vào project → chạy generate (fomoxac sinh `*RpcCodec.cs`) →
  rồi mới thêm `Services.cs` → compile lại. fomoxac chạy sau compile nên phải 2 lượt.
- `-logFile -` của Unity player không ghi ra stdout (bị buffer) → luôn dùng
  `-logFile <đường dẫn>`; muốn xem log thì `Get-Content -Tail`.
- Directory giữ mapping room → pod cho tới khi nó reap pod đã chết, nên pod mới
  `AcquireRoom` cùng key sẽ bị `FAILED_PRECONDITION`. Khi test lại: restart
  directory (hoặc đổi `-room:`).
- `Version` của pod và client đều lấy `Application.version` (bundleVersion, đang là
  `1.0`); placement có `filter = "pod.version == client.version"` nên đổi
  bundleVersion phải build lại cả pod lẫn client.
- Key: `root` phải đúng 64 ký tự hex, các role key ≥ 16 byte. Windows không
  set được biến môi trường rỗng nên `Fomoxa__Directory__Key__File` phải trỏ thẳng
  vào file key (không dùng `= ` để xoá).
- Backend template là `net8.0` mà máy chỉ có .NET 9 → đặt `RollForward=Major`
  trong `MyGame.Backend.csproj`, nếu không app báo "You must install or update .NET".
- `BehindGateway` là cấu hình của listener phía server, không phải của client: pod
  qua gateway đặt `true`, host mode (không gateway) phải đặt `false`, nếu không
  client local không vào được.
- Gateway không cần biết trước pod: nó lấy bảng pod từ directory và route theo 4 byte
  id trong session id. `pod_bind = 0.0.0.0:0` để OS chọn cổng nói chuyện với pod.
- Trong script PowerShell, `"$Name:"` bị hiểu là ổ đĩa → viết `"${Name}:"`.
- Deadline mặc định của fRPC là 5 s; match Nakama chờ tới 15 s nên `JoinRoomAsync` phải
  truyền `CallOptions.Within(30s)`, nếu không client nhận `DEADLINE_EXCEEDED` thay vì
  `FAILED_PRECONDITION`.
- Mỗi client phải một `-name:` (hoặc `-device:`); hai client cùng device id là cùng user
  Nakama nên `find_match` không bao giờ ghép được.
- Nakama chạy bằng `docker compose` trong `deploy/`; backend tự dò `:7350/healthcheck`,
  không có Nakama thì tắt matchmaking.

