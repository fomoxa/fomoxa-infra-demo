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
      Generated/                 do fomoxac sinh, ĐỪNG sửa tay
      Player.cs                  NetworkBehaviour: Gather input + Apply di chuyển
      Launcher.cs                nút Host/Server/Client, spawn player khi có peer
      PlayerCamera.cs            camera bám theo player của mình
    Editor/DemoScene.cs          dựng scene + prefab + build player (menu Tools)
    FomoxaSettings.asset         Models = Assets/Network/Models, Generated = Assets/Network/Generated
    Scenes/Game.unity            scene đã dựng sẵn
    Prefabs/Player.prefab        capsule + NetworkObject + NetworkTransform + Player
  Builds/MyGame.exe              bản build Windows dùng để test 2 người
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

## Chạy 2 người (cách nhanh nhất)

1. Mở project bằng Unity **6000.5.7f1**, mở `Assets/Scenes/Game.unity`.
2. Bấm **Play**, rồi bấm **Host (server + local player)**.
3. Chạy `Builds\MyGame.exe` (double-click), bấm **Client**.
4. Cửa sổ Editor và cửa sổ build sẽ thấy 2 capsule; bấm **Client** xong thì
   dùng WASD/mũi tên để di chuyển — bên kia thấy di chuyển theo.

Muốn 3+ người: chạy thêm `MyGame.exe` và bấm **Client**.

## Chạy bằng dòng lệnh (không GUI)

```powershell
cd D:\Documents\fomoxa\MyGame\Builds

# máy chủ
.\MyGame.exe -server -logFile server.log

# máy khác (hoặc cùng máy): client
.\MyGame.exe -client -logFile client.log
```

Tham số hỗ trợ:

| Tham số | Ý nghĩa |
|---|---|
| `-server` | chỉ chạy server, cổng 7777 |
| `-client` | tự kết nối `127.0.0.1:7777` sau 1 giây |
| `-host` | server + client trong cùng tiến trình |
| `-bot` | thay input bàn phím bằng chuyển động tròn (để test tự động) |
| `-trace` | log vị trí mỗi 2 giây |

Server headless:

```powershell
.\MyGame.exe -batchmode -nographics -server -logFile server.log
```

## Đổi địa chỉ server

Trong Editor: chọn `Network Manager`, sửa ô **Address** trên component `Launcher`
(hoặc sửa biến `address` trong `Launcher.cs`). Port mặc định 7777; đổi ở ô **Port**.

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

Chạy 1 host + 1 client headless (`-host -trace` và `-client -bot -trace`):

```
HOST    [Player] spawn on server id=1 owner=1
HOST    [Player] spawn on client id=1 owner=1 mine=True
HOST    peer 2: Started
HOST    [Player] spawn on server id=2 owner=2
HOST    [Player] spawn on client id=2 owner=2 mine=False   <- host thấy player của client
HOST    [Pos] id=2 pos=(2.28, 1.00, 12.82)                 <- và thấy nó di chuyển

CLIENT  [Player] spawn on client id=1 owner=1 mine=False    <- client thấy player của host
CLIENT  [Player] spawn on client id=2 owner=2 mine=True
CLIENT  [Pos] id=2 pos=(0.03, 1.00, 15.52)                 <- vị trí do server gửi về
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
- `NetworkManager` tự thêm `UdpNetworkTransport` nếu chưa gán.
