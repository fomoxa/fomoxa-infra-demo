# MyGame — demo Fomoxa: sàn + di chuyển + thấy nhau

Demo nhỏ nhất chứng minh tầng mạng Fomoxa chạy được: một sàn, vài cột mốc, mỗi
người chơi là một capsule, di chuyển bằng WASD/mũi tên và **thấy nhau di chuyển**.

- Server authoritative: client chỉ gửi input, server mới là bên di chuyển.
- Input đi qua hệ thống input của Fomoxa (`NetworkInput.Use`), không dự đoán
  (không reconcile) nên client thấy vị trí của chính mình sau một vòng mạng —
  chấp nhận được cho demo, và chính vì thế log chứng minh rõ dữ liệu là từ server.
- Vị trí đồng bộ bằng `NetworkTransform` (position + rotation).
- Không có asset ngoài: sàn/cột/capsule đều là primitive.

## Cấu trúc

```
MyGame/
  Assets/
    Network/
      Models/MoveInput.cs        model input (X, Z) — fomoxac sinh codec
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
  Builds/MyGame.exe              bản build dùng làm pod và client
```

## Package

`Packages/manifest.json` lấy package trực tiếp từ GitHub, không dùng đường dẫn local:

```json
"com.fomoxa.networking": "https://github.com/fomoxa/com.fomoxa.networking.git#0.2.0",
"com.fomoxa.rpc": "https://github.com/fomoxa/com.fomoxa.rpc.git#0.1.0"
```

Unity clone về `Library/PackageCache/` và ghim đúng commit trong
`Packages/packages-lock.json`. Máy cần có `git` trong PATH (máy này có 2.52.0).
Đổi version chỉ cần sửa tag sau dấu `#` rồi để Unity resolve lại.

## Chạy đủ cụm: directory + backend + pod (P2)

Cần 4 thành phần: `fomoxa-directory` (9500), backend (9600), pod (UDP 7778), client.

Lần đầu chạy, sinh key (file key nằm ở `deploy/secrets/`, **không commit**):

```powershell
cd D:\Documents\fomoxa
powershell -NoProfile -File scripts\gen-keys.ps1
```

Build `fomoxa-directory` (chỉ cần lại khi sửa code Rust):

```powershell
cd D:\Documents\fomoxa\gateway
cargo build --release -p fomoxa-directory -p fomoxa-gateway
```

Khởi động cụm (directory + backend + 1 pod, log trong `logs/`):

```powershell
cd D:\Documents\fomoxa
powershell -NoProfile -File scripts\start-cluster.ps1
```

Chạy 2 client — mỗi client tự đăng nhập backend, lấy ticket rồi vào pod:

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
| `MyGame.Backend` | TCP 9600, WS 9601, pod 9602 | đăng nhập, `JoinRoom` → ticket + grant |
| pod (`MyGame.exe -pod`) | UDP 7778 | giữ room `arena:1`, chạy game |
| client | — | login → join → nối pod bằng grant (encrypted UDP) |

Luồng: client → backend (fRPC/TCP) → directory (allocate) → ticket {address, port,
grant} → client nối thẳng pod bằng grant; pod đã `AcquireRoom("arena:1")` nên
directory biết room nằm ở pod nào.

## Chạy nhanh trong Editor (không cần cụm)

1. Mở project bằng Unity **6000.5.7f1**, mở `Assets/Scenes/Game.unity`.
2. Bấm **Play**, rồi bấm **Host (local server + player)** — server + client trong
   cùng tiến trình, tự sinh token, không cần backend/directory.
3. Chạy `Builds\MyGame.exe` **khi cụm đã chạy** (xem trên) để thấy 2 người thật.
4. Trong cụm, nút **Join room arena:1 via backend** làm đúng việc client headless
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
| `-name:<tên>` | tên người chơi gửi lên backend |
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

Menu **Tools > Demo > Rebuild Scene** hoặc chạy batchmode:

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

**P1 — host + client (UDP thường):**

```
HOST    [Player] spawn on server id=1 owner=1
HOST    [Player] spawn on client id=1 owner=1 mine=True
HOST    peer 2: Started
HOST    [Player] spawn on server id=2 owner=2
HOST    [Player] spawn on client id=2 owner=2 mine=False   <- host thấy player của client
HOST    [Pos] id=2 pos=(2.28, 1.00, 12.82)                 <- và thấy nó di chuyển
```

**P2 — directory + backend + pod (encrypted UDP), 2 client headless:**

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

Client không bật prediction, nên vị trí của chính nó chỉ đổi khi server gửi —
đó là bằng chứng dữ liệu đã đi đúng vòng client → server → client.

## Ghi chú kỹ thuật đã gặp

- **Không cài `fomoxa.unity` (unity-codecs)** cùng `com.fomoxa.networking`: cả hai
  có asmdef tên `Fomoxa.Unity.Editor` → Unity báo "Assembly with name ... already
  exists" và **không compile gì cả**. Chỉ cần nó khi model dùng `Vector3`,
  `Quaternion`... Demo này dùng `float` nên bỏ.
- `Packages/manifest.json` phải là UTF-8 **không BOM**; PowerShell 5.1 ghi BOM
  làm Unity báo `JSON parse error ... char 65279`.
- Model dùng `float` (wire type `f32`) để sau này **console server** (P2) compile
  được ngoài Unity — model chứa `UnityEngine.Vector3` sẽ không build được ở đó.
- Thứ tự component trên prefab phải là `NetworkObject` → `NetworkTransform` →
  `Player`; console server sau này phải khai báo behaviour cùng thứ tự.
- `NetworkManager` tự thêm `UdpNetworkTransport` nếu chưa gán. Ở P2 scene gán sẵn
  `EncryptedUdpNetworkTransport` (đặt bằng `SerializedObject` trong `DemoScene.cs`
  vì field `transport` là private).
- **Encrypted UDP là bắt buộc khi có directory**: pod chỉ nhận client có grant.
  Vì vậy host mode phải tự đặt `ConnectTokens` (key random) trước
  `StartConnection`, nếu không `ServerManager` ném lỗi; client cùng máy bỏ qua grant.
- **Thứ tự sinh code khi thêm model rpc**: `FRPC003 ... is not a [Network] model
  with an rpc codec` xuất hiện nếu compile `Services.cs` trước khi có codec. Cách
  làm: copy `Models/` vào project → chạy generate (fomoxac sinh `*RpcCodec.cs`) →
  rồi mới thêm `Services.cs` → compile lại. fomoxac chạy sau compile nên phải 2 lượt.
- `-logFile -` của Unity player **không** ghi ra stdout (bị buffer) → luôn dùng
  `-logFile <đường dẫn>`; muốn xem log thì `Get-Content -Tail`.
- Directory giữ mapping room → pod **cho tới khi nó reap pod đã chết**, nên pod mới
  `AcquireRoom` cùng key sẽ bị `FAILED_PRECONDITION`. Khi test lại: restart
  directory (hoặc đổi `-room:`).
- `Version` của pod và client đều lấy `Application.version` (bundleVersion, đang là
  `1.0`); placement có `filter = "pod.version == client.version"` nên đổi
  bundleVersion phải build lại cả pod lẫn client.
- Key: `root` phải đúng **64 ký tự hex**, các role key ≥ 16 byte. Windows không
  set được biến môi trường rỗng nên `Fomoxa__Directory__Key__File` phải trỏ thẳng
  vào file key (không dùng `= ` để xoá).
- Backend template là `net8.0` mà máy chỉ có .NET 9 → đặt `RollForward=Major`
  trong `MyGame.Backend.csproj`, nếu không app báo "You must install or update .NET".
- Trong script PowerShell, `"$Name:"` bị hiểu là ổ đĩa → viết `"${Name}:"`.

