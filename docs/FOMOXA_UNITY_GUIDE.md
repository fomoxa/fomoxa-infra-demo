# Hướng dẫn Onboarding: Fomoxa Networking trong Unity

> **Đối tượng**: Lập trình viên Unity đã biết C# cơ bản, chưa từng dùng Fomoxa.  
> **Unity**: 6000.x (Unity 6), Windows.  
> **Thời gian hoàn thành**: ~2–3 tiếng nếu làm lần đầu.

---

## Mục lục

1. [Giới thiệu - Game demo sẽ làm gì](#1-giới-thiệu)
2. [Yêu cầu môi trường](#2-yêu-cầu-môi-trường)
3. [Cài đặt package Fomoxa](#3-cài-đặt-package-fomoxa)
4. [Tạo project và cấu hình FomoxaSettings](#4-tạo-project-và-cấu-hình-fomoxasettings)
5. [Tạo Model và Generate code](#5-tạo-model-và-generate-code)
6. [Tạo NetworkManager và scene cơ bản](#6-tạo-networkmanager-và-scene-cơ-bản)
7. [Tạo Player Prefab](#7-tạo-player-prefab)
8. [Viết logic Spawn + Di chuyển](#8-viết-logic-spawn--di-chuyển)
9. [Chạy Server + 2 Client](#9-chạy-server--2-client)
10. [Kiểm tra kết quả thành công](#10-kiểm-tra-kết-quả-thành-công)
11. [Troubleshooting thường gặp](#11-troubleshooting-thường-gặp)
12. [Bài tập nhỏ: Tự thêm RPC](#12-bài-tập-nhỏ-tự-thêm-rpc)
13. [Checklist tự xác nhận](#13-checklist-tự-xác-nhận)

---

## 1. Giới thiệu

Game demo bạn sẽ tạo là một **arena multiplayer 3D tối giản**:

- Mỗi người chơi điều khiển một **Capsule** màu xanh.
- Dùng **WASD** (hoặc phím mũi tên) để di chuyển.
- Server quản lý vật lý + đồng bộ vị trí đến tất cả client.
- Khi 2 client kết nối, mỗi người **thấy nhau di chuyển theo thời gian thực**.

Sau khi xong bài này, bạn sẽ hiểu luồng cốt lõi của Fomoxa:

```
Model (C#) → fomoxac (codegen) → NetAdapter → NetworkBehaviour → NetworkTransform → đồng bộ
```

---

## 2. Yêu cầu môi trường

| Thứ | Phiên bản / Ghi chú |
|-----|---------------------|
| Unity | **6000.x** (Unity 6), Windows 64-bit |
| .NET SDK | **8.0+** - cần cho `fomoxac` codegen |
| Git | Trong PATH (để UPM tải package từ GitHub) |
| Rust / Cargo | **Không cần** (chỉ dùng nếu build console server) |

### Kiểm tra nhanh

Mở PowerShell, chạy:

```powershell
git --version
# git version 2.x.x

dotnet --version
# 8.x.x
```

Nếu `git` chưa có trong PATH, tải tại https://git-scm.com/download/win và cài đặt (chọn "Add to PATH").

> **Lưu ý**: Không cần cài `fomoxa.unity` package từ Asset Store. Package đó gây xung đột tên assembly với `com.fomoxa.networking` từ GitHub.

---

## 3. Cài đặt package Fomoxa

### Mục tiêu bước này
Thêm 2 package Fomoxa vào project Unity qua UPM (Unity Package Manager) bằng cách chỉnh file `manifest.json`.

### Bước 3.1 - Tạo Unity project mới

1. Mở **Unity Hub**.
2. Click **New project**.
3. Chọn template **3D (URP)** (Universal Render Pipeline).
4. Đặt tên: `MyGame`.
5. Chọn Unity **6000.x**.
6. Click **Create project** - đợi Unity khởi tạo xong.

### Bước 3.2 - Chỉnh manifest.json

Đóng Unity lại trước khi sửa file này.

Mở file `MyGame/Packages/manifest.json` bằng bất kỳ text editor nào (Notepad, VS Code, ...).

Thêm 2 dòng vào đầu block `"dependencies"`:

```json
{
  "dependencies": {
    "com.fomoxa.networking": "https://github.com/fomoxa/com.fomoxa.networking.git#0.2.0",
    "com.fomoxa.rpc": "https://github.com/fomoxa/com.fomoxa.rpc.git#0.1.0",

    "com.unity.multiplayer.center": "1.0.1",
    "com.unity.modules.physics": "1.0.0",
    ...
  }
}
```

> **Quan trọng**: File `manifest.json` phải được lưu dưới dạng **UTF-8 không có BOM**. VS Code mặc định lưu đúng. Nếu dùng Notepad, kiểm tra encoding ở góc dưới phải khi lưu.

### Bước 3.3 - Mở lại Unity, đợi resolve

Mở lại Unity. Ở góc dưới phải sẽ thấy:

```
Resolving packages...
```

Đợi khoảng 1–3 phút (Unity tải từ GitHub). Khi xong, Console sẽ không có lỗi đỏ liên quan đến package.

### Kết quả mong đợi

Vào **Window > Package Manager**, filter "In Project" - thấy:
- `Fomoxa Networking` (0.2.0)
- `Fomoxa Rpc` (0.1.0)

### Lỗi thường gặp

| Lỗi | Nguyên nhân | Cách sửa |
|-----|-------------|----------|
| `No 'git' executable was found` | Git chưa trong PATH | Cài Git, thêm vào PATH, restart Unity Hub |
| `JSON parse error in manifest.json` | Thừa/thiếu dấu phẩy | Mở manifest.json, kiểm tra syntax JSON |
| `Assembly name collision: Fomoxa.Unity` | Package `fomoxa.unity` từ Asset Store đã cài | Xóa package đó khỏi manifest |
| Tải mãi không xong | Tường lửa/proxy block GitHub | Thử VPN hoặc dùng hotspot điện thoại |

---

## 4. Tạo project và cấu hình FomoxaSettings

### Mục tiêu bước này
Tạo cấu trúc thư mục chuẩn và cấu hình `FomoxaSettings` - file config trung tâm báo cho `fomoxac` biết Models ở đâu và output Generated ở đâu.

### Bước 4.1 - Tạo cấu trúc thư mục

Trong Unity Editor, mở **Project** panel. Tạo các folder sau trong `Assets/`:

```
Assets/
├── Materials/         ← vật liệu màu sắc
├── Network/
│   ├── Models/        ← đặt file C# model tại đây
│   └── Generated/     ← fomoxac sẽ ghi code ra đây
├── Prefabs/           ← Player.prefab
└── Scenes/            ← Game.unity
```

Cách tạo: Trong Project panel, chuột phải vào `Assets` → **Create > Folder**, đặt tên lần lượt.

### Bước 4.2 - Tạo FomoxaSettings asset

1. Trong menu Unity: **Tools > Fomoxa > Create Settings**.
2. Một file `FomoxaSettings.asset` xuất hiện ở `Assets/`.
3. Click chọn file đó - nhìn vào **Inspector**.

Điền các trường sau:

| Trường | Giá trị |
|--------|---------|
| **Models Path** | `Assets/Network/Models` |
| **Generated Path** | `Assets/Network/Generated` |
| **Namespace** | `Generated` |

> Nếu không thấy menu **Tools > Fomoxa**, có thể package chưa compile xong. Đợi thanh tiến trình phía dưới Unity chạy xong, hoặc nhấn **Ctrl+R** để refresh.

### Kết quả mong đợi

Inspector của `FomoxaSettings.asset` hiển thị đúng đường dẫn đã điền, không có lỗi đỏ.

---

## 5. Tạo Model và Generate code

### Mục tiêu bước này
Định nghĩa kiểu dữ liệu `MoveInput` (input di chuyển của player), sau đó chạy `fomoxac` để sinh ra `MoveInputNetAdapter.cs` và `MoveInputNetCodec.cs`.

### Bước 5.1 - Tạo file Model

Trong Project panel, chuột phải vào `Assets/Network/Models/` → **Create > C# Script**. Đặt tên: `MoveInput`.

Mở file `MoveInput.cs`, xóa toàn bộ nội dung, paste code sau:

```csharp
using Fomoxa;

namespace MyGame.Network.Models
{
    [Network]
    [Codec("net")]
    public class MoveInput
    {
        [Network("f32")]
        [Codec("net")]
        public float X { get; set; }

        [Network("f32")]
        [Codec("net")]
        public float Z { get; set; }
    }
}
```

**Lưu ý quan trọng**:
- Dùng `float`, **không** dùng `Vector3` hay `Vector2`. Lý do: console server (C# thuần, không có Unity) không biết `Vector3`.
- `[Network("f32")]` = kiểu truyền qua mạng là float 32-bit.
- `[Codec("net")]` = sinh codec cho namespace "net".
- Chỉ dùng `X` và `Z` (mặt phẳng ngang), `Y` cố định = 1f ở phía Apply.

### Bước 5.2 - Đợi Unity compile

Sau khi lưu file, nhìn góc dưới phải Unity - thấy vòng xoay nhỏ ("Compiling..."). Đợi hết.

### Bước 5.3 - Chạy Generate (fomoxac)

Vào menu: **Tools > Fomoxa > Generate**

Hoặc nếu không thấy menu đó, mở Terminal (hoặc PowerShell) tại thư mục project:

```powershell
# Windows PowerShell - chạy từ thư mục MyGame/
dotnet tool run fomoxac --models Assets/Network/Models --output Assets/Network/Generated --namespace Generated
```

> **fomoxac** là .NET global/local tool được cài kèm package. Nếu chạy lệnh trên báo lỗi "not found", thử:
> ```powershell
> dotnet tool restore
> ```

### Bước 5.4 - Kiểm tra file được sinh ra

Sau khi Generate xong, trong `Assets/Network/Generated/` phải có tối thiểu 2 file mới:

```
Assets/Network/Generated/
├── MoveInputNetAdapter.cs    ← adapter bridge giữa Model và network layer
└── MoveInputNetCodec.cs      ← encode/decode binary
```

Click vào `MoveInputNetAdapter.cs` - nội dung đầu file sẽ có comment:

```
// Generated by com.fomoxa.networking from a fomoxac codec. Edit the model, not this file.
```

Đợi Unity compile lại lần nữa.

### Kết quả mong đợi

Console không có lỗi. Trong `Generated/` có file `MoveInputNetAdapter.cs` với class `MoveInputNetAdapter` chứa `public static readonly MoveInputNetAdapter Instance`.

### Lỗi thường gặp

| Lỗi | Nguyên nhân | Cách sửa |
|-----|-------------|----------|
| `[Network] is not defined` | Package chưa load xong | Đợi resolve xong, refresh Assets |
| `fomoxac: command not found` | Tool chưa restore | `dotnet tool restore` trong thư mục project |
| `CS0246: type or namespace 'Generated' not found` | Generated chưa chạy hoặc file bị thiếu | Chạy Generate lại |
| Không thấy `MoveInputNetAdapter.cs` | Generate thất bại | Xem Output panel → Log của fomoxac để tìm lỗi |

---

## 6. Tạo NetworkManager và scene cơ bản

### Mục tiêu bước này
Tạo scene `Game.unity` với đầy đủ cấu trúc: sàn, ánh sáng, camera, GameObject `Network Manager` có gắn `NetworkManager` + `EncryptedUdpNetworkTransport`.

### Cách nhanh nhất - Dùng DemoScene builder (nếu project là clone từ repo)

Nếu bạn đang dùng project MyGame từ repo Fomoxa, chạy:

**Tools > Demo > Rebuild Scene**

Lệnh này tự động:
1. Tạo scene `Assets/Scenes/Game.unity`.
2. Tạo prefab `Assets/Prefabs/Player.prefab`.
3. Tạo `Network Manager` GameObject với `NetworkManager` + `EncryptedUdpNetworkTransport`.
4. Tạo `Backend` GameObject với `TcpNetworkTransport` + `RoomJoiner`.
5. Thêm `Launcher` component và wire tất cả references.

**Kết quả mong đợi**: Console in `[Demo] scene built: Assets/Scenes/Game.unity`.

→ **Bỏ qua bước 6.1–6.6 bên dưới và chuyển thẳng đến [Bước 7](#7-tạo-player-prefab)**.

---

### Cách thủ công - Tạo từ đầu (nếu project mới hoàn toàn)

### Bước 6.1 - Tạo scene mới

**File > New Scene** → chọn **Empty** → Save As `Assets/Scenes/Game.unity`.

### Bước 6.2 - Tạo sàn (Floor)

1. **GameObject > 3D Object > Plane**.
2. Đổi tên: `Floor`.
3. Trong Inspector, Transform:
   - Scale: `(5, 1, 5)` - tạo sàn 50×50 đơn vị.

### Bước 6.3 - Tạo ánh sáng

1. **GameObject > Light > Directional Light**.
2. Transform Rotation: `(50, -30, 0)`.
3. Intensity: `1.2`.

### Bước 6.4 - Cấu hình Camera

1. Chọn `Main Camera` trong Hierarchy.
2. Transform Position: `(0, 14, -14)`.
3. Transform Rotation: `(35, 0, 0)`.
4. Thêm component `PlayerCamera` (nếu đã có script này - xem Bước 7.4).

### Bước 6.5 - Tạo Network Manager GameObject

1. **GameObject > Create Empty** → đặt tên: `Network Manager`.
2. Thêm các component theo thứ tự:
   - Click **Add Component** → tìm `Network Manager` → chọn.
   - Click **Add Component** → tìm `Encrypted Udp Network Transport` → chọn.

### Bước 6.6 - Gán Transport vào NetworkManager

> **Quan trọng**: `NetworkManager` có trường `Transport` (SerializedField riêng). Phải gán thủ công.

1. Chọn `Network Manager` GameObject.
2. Trong Inspector, tìm component `Network Manager`.
3. Kéo component `Encrypted Udp Network Transport` từ cùng GameObject vào trường `Transport` của `Network Manager`.

> Nếu không thấy trường `Transport`, thử click nút ⋮ trên component và chọn **Debug** mode.

### Kết quả mong đợi bước 6

Hierarchy trông như sau:

```
Game (scene)
├── Directional Light
├── Floor
├── Main Camera
└── Network Manager
    ├── [NetworkManager]
    └── [EncryptedUdpNetworkTransport]
```

Không có lỗi đỏ trong Console.

---

## 7. Tạo Player Prefab

### Mục tiêu bước này
Tạo Prefab `Player.prefab` với đúng thứ tự component: `NetworkObject` → `NetworkTransform` → `Player`. Thứ tự **rất quan trọng**, sai thứ tự sẽ gây lỗi lúc runtime.

### Bước 7.1 - Tạo Capsule trong scene

1. **GameObject > 3D Object > Capsule**.
2. Đổi tên: `Player`.
3. Transform Position: `(0, 1, 0)`.

### Bước 7.2 - Thêm component theo đúng thứ tự

Chọn `Player` GameObject → Inspector:

**Add Component 1**: `Network Object`  
**Add Component 2**: `Network Transform`  
**Add Component 3**: `Player` (script bạn sẽ tạo ở Bước 8)

> **Lưu ý**: Nếu thêm sai thứ tự, dùng nút ⋮ (three dots) trên component → **Move Up / Move Down** để sắp xếp lại.

### Bước 7.3 - Lưu thành Prefab

1. Kéo GameObject `Player` từ Hierarchy vào folder `Assets/Prefabs/` trong Project panel.
2. Unity hỏi **"Create Prefab Variant?" hay "Original Prefab?"** → chọn **Original Prefab**.
3. Một file `Player.prefab` xuất hiện trong `Assets/Prefabs/`.
4. Xóa GameObject `Player` khỏi scene (chỉ giữ lại Prefab).

### Bước 7.4 - Tạo script PlayerCamera (theo dõi player chủ)

Tạo file `Assets/Network/PlayerCamera.cs`:

```csharp
using Fomoxa.Networking;
using Fomoxa.Unity;
using UnityEngine;

namespace MyGame.Network
{
    public sealed class PlayerCamera : MonoBehaviour
    {
        private NetworkManager manager;
        private Transform target;

        private void Awake()
        {
            manager = FindAnyObjectByType<NetworkManager>();
        }

        private void LateUpdate()
        {
            if (target == null)
            {
                FindMyPlayer();
            }

            if (target != null)
            {
                Vector3 follow = target.position + new Vector3(0f, 14f, -14f);
                transform.position = Vector3.Lerp(transform.position, follow, Time.deltaTime * 5f);
            }
        }

        private void FindMyPlayer()
        {
            foreach (NetworkObject obj in FindObjectsByType<NetworkObject>(FindObjectsSortMode.None))
            {
                if (obj.IsOwner)
                {
                    target = obj.transform;
                    return;
                }
            }
        }
    }
}
```

Gán script này vào `Main Camera` trong scene.

### Kết quả mong đợi

File `Assets/Prefabs/Player.prefab` tồn tại. Mở prefab, thấy 3 component đúng thứ tự:
1. `Network Object`
2. `Network Transform`
3. `Player`

---

## 8. Viết logic Spawn + Di chuyển

### Mục tiêu bước này
Tạo 2 script: `Player.cs` (xử lý input + movement) và `Launcher.cs` (khởi động server/client, spawn player).

### Bước 8.1 - Tạo script Player.cs

Tạo file `Assets/Network/Player.cs`:

```csharp
using System;
using Fomoxa.Networking;
using Fomoxa.Unity;
using Generated;
using MyGame.Network.Models;
using UnityEngine;

namespace MyGame.Network
{
    public sealed class Player : NetworkBehaviour
    {
        [SerializeField] private float speed = 6f;
        [SerializeField] private float bounds = 23f;

        private NetworkManager manager;

        private void Awake()
        {
            manager = FindAnyObjectByType<NetworkManager>();
        }

        // Gọi khi object spawn TRÊN SERVER
        public override void OnStartServer()
        {
            Debug.Log($"[Player] spawn on server id={NetworkObject.ObjectId} owner={NetworkObject.OwnerId}");
        }

        // Gọi khi object spawn TRÊN CLIENT
        public override void OnStartClient()
        {
            Debug.Log($"[Player] spawn on client id={NetworkObject.ObjectId} owner={NetworkObject.OwnerId} mine={NetworkObject.IsOwner} pos={transform.position}");
        }

        // Đăng ký input type cho object này
        protected override void OnRegisterInput(NetworkInput input)
        {
            // MoveInputNetAdapter.Instance là class được sinh ra bởi fomoxac
            // Gather = hàm lấy input từ bàn phím (chạy trên client)
            // Apply  = hàm áp dụng input vào vị trí (chạy trên server VÀ client local)
            input.Use(MoveInputNetAdapter.Instance, Gather, Apply);
        }

        // Chạy trên client - đọc bàn phím, ghi vào input
        private void Gather(MoveInput input)
        {
            input.X = Input.GetAxisRaw("Horizontal"); // A/D hoặc ←/→
            input.Z = Input.GetAxisRaw("Vertical");   // W/S hoặc ↑/↓
        }

        // Chạy trên cả server lẫn client - tính vị trí mới
        private void Apply(MoveInput input, InputContext context)
        {
            if (manager == null) return;

            Vector3 direction = Vector3.ClampMagnitude(new Vector3(input.X, 0f, input.Z), 1f);

            // Quay mặt theo hướng di chuyển
            if (direction.sqrMagnitude > 0.0001f)
            {
                transform.rotation = Quaternion.LookRotation(direction, Vector3.up);
            }

            // Tính vị trí mới - dùng TickDelta thay vì Time.deltaTime
            // TickDelta = thời gian 1 network tick (cố định, không phụ thuộc framerate)
            Vector3 next = transform.position + direction * speed * (float)manager.TimeManager.TickDelta;
            next.x = Mathf.Clamp(next.x, -bounds, bounds);
            next.z = Mathf.Clamp(next.z, -bounds, bounds);
            next.y = 1f; // cố định chiều cao
            transform.position = next;
        }
    }
}
```

**Giải thích quan trọng**:

| Điều | Chi tiết |
|------|----------|
| `NetworkBehaviour` | Base class thay vì `MonoBehaviour`, cung cấp `OnStartServer`, `OnStartClient`, `OnRegisterInput` |
| `OnRegisterInput` | Override bắt buộc để đăng ký input type - Fomoxa tự gọi |
| `MoveInputNetAdapter.Instance` | Singleton sinh ra bởi fomoxac - mã hóa/giải mã `MoveInput` qua mạng |
| `manager.TimeManager.TickDelta` | `double` - thời gian một tick network. Dùng cái này thay `Time.deltaTime` để movement sync đúng |
| `Gather` → chỉ client | Đọc keyboard |
| `Apply` → server + client | Tính toán vị trí, cả hai bên đều chạy (client-side prediction) |

### Bước 8.2 - Tạo script Launcher.cs

Tạo file `Assets/Network/Launcher.cs`:

```csharp
using System;
using Fomoxa.Networking;
using Fomoxa.Networking.Sessions;
using Fomoxa.Unity;
using UnityEngine;

namespace MyGame.Network
{
    public sealed class Launcher : MonoBehaviour
    {
        [Header("Network")]
        public NetworkManager manager;
        public NetworkObject playerPrefab;
        public ushort port = 7777;

        [Header("Transport")]
        public EncryptedUdpNetworkTransport encryptedTransport;

        private int slot;

        private void Start()
        {
            // Đăng ký event
            manager.ServerManager.OnRemoteConnectionState += OnRemoteConnectionState;
            manager.ClientManager.OnClientConnectionState += OnClientConnectionState;

            // Đọc command line args - cho phép chạy server/client từ terminal
            string[] args = Environment.GetCommandLineArgs();

            if (Array.IndexOf(args, "-host") >= 0)
            {
                StartHost();
            }
            else if (Array.IndexOf(args, "-client") >= 0)
            {
                // Delay 1 giây để server khởi động trước
                Invoke(nameof(ConnectClient), 1f);
            }
        }

        private void OnDestroy()
        {
            if (manager == null || manager.ServerManager == null) return;
            manager.ServerManager.OnRemoteConnectionState -= OnRemoteConnectionState;
            manager.ClientManager.OnClientConnectionState -= OnClientConnectionState;
        }

        private void OnGUI()
        {
            GUILayout.BeginArea(new Rect(10f, 10f, 380f, 190f));

            bool serverRunning = manager.ServerManager.State != ServerState.Stopped;
            bool clientRunning = manager.ClientManager.State != ConnectionState.Stopped;

            if (serverRunning || clientRunning)
            {
                GUILayout.Label($"server: {manager.ServerManager.State}  peers: {manager.ServerManager.PeerCount}");
                GUILayout.Label($"client: {manager.ClientManager.State}");
                GUILayout.Label("move: WASD / arrow keys");

                if (GUILayout.Button("Stop"))
                {
                    manager.ClientManager.StopConnection();
                    manager.ServerManager.StopConnection();
                }
            }
            else
            {
                if (GUILayout.Button("Host (Server + Player trong 1)"))
                {
                    StartHost();
                }

                if (GUILayout.Button("Join (Client chỉ)"))
                {
                    ConnectClient();
                }
            }

            GUILayout.EndArea();
        }

        // Host mode: chạy server VÀ client cùng một process
        private void StartHost()
        {
            if (encryptedTransport != null)
            {
                // Host local không đi qua gateway
                encryptedTransport.BehindGateway = false;

                // Tạo key ngẫu nhiên cho session (host mode không cần key từ directory)
                var key = new byte[ConnectTokens.KeyLength];
                new System.Random().NextBytes(key);
                encryptedTransport.Tokens = new ConnectTokens(FomoxaRegistry.Default, key);
                encryptedTransport.Target = "host";
            }

            manager.ServerManager.StartConnection(port);
            // Client kết nối vào port mà server vừa bind
            manager.ClientManager.StartConnection("127.0.0.1", manager.ServerManager.Port);
            Debug.Log($"[Host] listening on port {manager.ServerManager.Port}");
        }

        // Client mode: chỉ kết nối tới server
        private void ConnectClient()
        {
            manager.ClientManager.StartConnection("127.0.0.1", port);
            Debug.Log($"[Client] connecting to 127.0.0.1:{port}");
        }

        // SERVER EVENT: có peer mới kết nối → spawn player cho họ
        private void OnRemoteConnectionState(ConnectionStateArgs args)
        {
            Debug.Log($"peer {args.PeerId}: {args.State} {args.Reason}");

            if (args.State != ConnectionState.Started)
            {
                return; // bỏ qua disconnect event
            }

            // Tính vị trí spawn phân tán trên vòng tròn
            Vector3 position = new Vector3(0f, 1f, 0f) + SpawnOffset();

            // Instantiate prefab trên server
            NetworkObject player = UnityEngine.Object.Instantiate(playerPrefab, position, Quaternion.identity);

            // Spawn lên mạng - gán owner = peer vừa kết nối
            manager.ServerManager.Spawn(player, args.PeerId);
        }

        // Phân phối vị trí spawn theo vòng tròn 8 điểm
        private Vector3 SpawnOffset()
        {
            slot = (slot + 1) % 8;
            float angle = slot * Mathf.PI * 2f / 8f;
            return new Vector3(Mathf.Sin(angle) * 3f, 0f, Mathf.Cos(angle) * 3f);
        }

        // CLIENT EVENT: trạng thái kết nối thay đổi
        private void OnClientConnectionState(ConnectionStateArgs args)
        {
            Debug.Log($"[Client] {args.State} {args.Reason}");
        }
    }
}
```

### Bước 8.3 - Gắn Launcher vào scene

1. Chọn GameObject `Network Manager` trong Hierarchy.
2. **Add Component** → `Launcher`.
3. Trong Inspector của `Launcher`:
   - `Manager` → kéo `Network Manager` GameObject vào.
   - `Player Prefab` → kéo `Assets/Prefabs/Player.prefab` vào.
   - `Port` → `7777` (mặc định).
   - `Encrypted Transport` → kéo component `Encrypted Udp Network Transport` từ `Network Manager` vào.

### Kết quả mong đợi

Console không có lỗi đỏ. Nhìn `Network Manager` trong Inspector - Launcher có đủ references.

---

## 9. Chạy Server + 2 Client

### Mục tiêu bước này
Chạy 3 process: 1 Host (server + client 1) và 1 Client độc lập - thấy 2 capsule xuất hiện và di chuyển theo nhau.

### Cách A - Host mode trong Editor (đơn giản nhất, chỉ 1 client)

1. Nhấn **Play** trong Unity Editor.
2. Click nút **"Host (Server + Player trong 1)"** trên màn hình.
3. Thấy log: `[Host] listening on port 7777`.
4. Thấy log: `[Player] spawn on server id=1 owner=1`.
5. Thấy log: `[Player] spawn on client id=1 owner=1 mine=True`.
6. Dùng **WASD** di chuyển capsule.

Với cách này chỉ có 1 player. Để test 2 player, cần Cách B.

---

### Cách B - Build player và chạy 2 process riêng

#### Bước 9.1 - Build Windows Player

Vào menu: **Tools > Demo > Build Windows Player**

Hoặc dùng **File > Build Settings** → chọn scene `Game` → **Build** → lưu vào `Builds/MyGame.exe`.

Đợi build xong (1–5 phút). Thấy log: `[Demo] player build: Succeeded`.

#### Bước 9.2 - Chạy Server (Host)

Mở PowerShell:

```powershell
cd D:\Documents\MyGame\Builds

# Chạy host (server + client 1)
.\MyGame.exe -host -logFile logs\host.log
```

> **Tại sao dùng `-logFile logs\host.log`?**  
> Nếu dùng `-logFile -` (stdout), output Unity bị buffer → không thấy log realtime.  
> Luôn ghi ra file thay thế.

Tạo thư mục log trước:

```powershell
New-Item -ItemType Directory -Force -Path Builds\logs
```

Rồi:

```powershell
.\MyGame.exe -host -logFile logs\host.log
```

Một cửa sổ game hiện ra. Capsule xanh xuất hiện ở giữa sân.

#### Bước 9.3 - Theo dõi log server

Mở PowerShell thứ 2:

```powershell
Get-Content D:\Documents\MyGame\Builds\logs\host.log -Wait
```

Thấy dòng:

```
[Host] listening on port 7777
[Player] spawn on server id=1 owner=1
[Player] spawn on client id=1 owner=1 mine=True pos=(0.0, 1.0, 3.0)
```

#### Bước 9.4 - Chạy Client 2

Mở PowerShell thứ 3:

```powershell
cd D:\Documents\MyGame\Builds

.\MyGame.exe -client -logFile logs\client2.log
```

Ngay lập tức server sẽ log:

```
peer 2: Started
[Player] spawn on server id=2 owner=2
```

Và trong log client 2:

```
[Client] Started
[Player] spawn on client id=1 owner=1 mine=False pos=(0.0, 1.0, 3.0)   ← player của host
[Player] spawn on client id=2 owner=2 mine=True pos=(0.0, 1.0, -3.0)   ← player của mình
```

### Chế độ Host vs Server riêng

| Chế độ | Command | Khi nào dùng |
|--------|---------|--------------|
| **Host** | `-host` | Test nhanh, server + 1 client cùng process |
| **Server thuần** | `-batchmode -nographics -host` | Server không cần hiển thị |
| **Client** | `-client` | Thêm client vào server đang chạy |

Ví dụ server không màn hình (headless):

```powershell
.\MyGame.exe -batchmode -nographics -host -logFile logs\server.log
```

---

## 10. Kiểm tra kết quả thành công

### Danh sách log cần thấy

**Phía Server (host.log)**:

```
[Host] listening on port 7777
peer 1: Started
[Player] spawn on server id=1 owner=1
peer 2: Started
[Player] spawn on server id=2 owner=2
```

**Phía Client 1 (trong cửa sổ host)**:

```
[Client] Started
[Player] spawn on client id=1 owner=1 mine=True pos=(0.0, 1.0, 3.0)
[Player] spawn on client id=2 owner=2 mine=False pos=(0.0, 1.0, -3.0)
```

**Phía Client 2 (client2.log)**:

```
[Client] Started
[Player] spawn on client id=1 owner=1 mine=False pos=(0.0, 1.0, 3.0)
[Player] spawn on client id=2 owner=2 mine=True pos=(0.0, 1.0, -3.0)
```

### Kiểm tra trực quan

1. Mở cả 2 cửa sổ game cạnh nhau.
2. Dùng WASD trong cửa sổ Host - capsule di chuyển ở CẢ HAI cửa sổ.
3. Click vào cửa sổ Client 2, dùng WASD - capsule kia cũng di chuyển.
4. Hai capsule có màu giống nhau nhưng vị trí khác nhau.

---

## 11. Troubleshooting thường gặp

### 11.1 - `SchemaConflict` hoặc `MessageId conflict`

**Triệu chứng**: Console in `SchemaConflict` hoặc client disconnect ngay khi kết nối.

**Nguyên nhân**: File Generated cũ không khớp với Model hiện tại.

**Cách sửa**:
```powershell
# Xóa toàn bộ Generated, chạy lại
Remove-Item Assets/Network/Generated/*.cs
# Sau đó: Tools > Fomoxa > Generate
```

---

### 11.2 - `Assembly name collision: Fomoxa.Unity`

**Triệu chứng**: Lỗi compile `Multiple assemblies with the same name: Fomoxa.Unity`.

**Nguyên nhân**: Package `fomoxa.unity` từ Asset Store đã được cài, xung đột với `com.fomoxa.networking`.

**Cách sửa**: Mở `Packages/manifest.json`, xóa dòng có `fomoxa.unity` khỏi dependencies.

---

### 11.3 - Client kết nối được nhưng không thấy player của nhau

**Triệu chứng**: 2 client chạy nhưng mỗi cửa sổ chỉ thấy capsule của mình.

**Nguyên nhân phổ biến**:
- Prefab chưa được đăng ký với `NetworkManager` (Fomoxa cần chạy `FomoxaPrefabStep`).
- Component `Player` thiếu hoặc sai thứ tự.

**Cách sửa**:
1. Chạy **Tools > Demo > Rebuild Scene** để trigger `FomoxaPrefabStep.RunForProject()`.
2. Kiểm tra console sau đó - không được có lỗi `[Demo] prefab:`.

---

### 11.4 - `peer X: TimedOut` - client disconnect ngay lập tức

**Nguyên nhân**: Encryption key không khớp giữa server và client.

**Trong Host mode**: Không xảy ra vì cùng process.

**Trong Pod mode** (dùng `EncryptedUdpNetworkTransport`): Key phải được cấp từ `DirectoryPod` sau khi register. Đây là flow backend phức tạp hơn, xem `docs/UBUNTU_GUIDE.md`.

**Cách sửa tạm thời khi test local**: Không gán `EncryptedUdpNetworkTransport` vào `Launcher.encryptedTransport` - khi trường đó là null, `EnsureTokens()` trả về `true` mà không cần token thực.

---

### 11.5 - `CS0103: The name 'MoveInputNetAdapter' does not exist`

**Nguyên nhân**: File `MoveInputNetAdapter.cs` chưa được generate hoặc chưa compile.

**Cách sửa**:
1. Kiểm tra `Assets/Network/Generated/MoveInputNetAdapter.cs` có tồn tại không.
2. Nếu không: chạy **Tools > Fomoxa > Generate**.
3. Đợi Unity compile (vòng xoay phía dưới hết).

---

### 11.6 - Unity freeze khi chạy Generate

**Nguyên nhân**: `fomoxac` process bị treo hoặc đường dẫn trong `FomoxaSettings` sai.

**Cách sửa**:
1. Kiểm tra `FomoxaSettings.asset` - Models Path và Generated Path đúng chưa.
2. Mở Task Manager → tìm process `fomoxac` → Kill nếu còn chạy.
3. Chạy lại Generate.

---

### 11.7 - BOM trong manifest.json

**Triệu chứng**: UPM báo lỗi JSON parse dù nhìn file trông bình thường.

**Cách kiểm tra**: Mở file trong VS Code → nhìn góc dưới phải → nếu thấy `UTF-8 with BOM` → có BOM.

**Cách sửa**: Trong VS Code: click `UTF-8 with BOM` → Save with Encoding → `UTF-8`.

---

## 12. Bài tập nhỏ: Tự thêm RPC

### Mục tiêu
Thêm một **teleport button** - khi nhấn phím `T`, player của bạn nhảy về vị trí trung tâm sân (0, 1, 0). Dùng RPC để gửi lệnh từ client lên server.

### Bước 12.1 - Tạo model TeleportRequest

Tạo file `Assets/Network/Models/TeleportRequest.cs`:

```csharp
using Fomoxa;

namespace MyGame.Network.Models
{
    [Network]
    [Codec("net")]
    public class TeleportRequest
    {
        // Không có field - chỉ là signal "hãy teleport tôi về center"
    }
}
```

### Bước 12.2 - Generate lại

**Tools > Fomoxa > Generate** → đợi compile.

Kiểm tra: `Assets/Network/Generated/TeleportRequestNetAdapter.cs` xuất hiện.

### Bước 12.3 - Thêm RPC vào Player.cs

Mở `Player.cs`, thêm vào cuối class (trước dấu `}`):

```csharp
        // Gọi từ client - client nhấn T
        // Server nhận, xử lý teleport
        protected override void OnRegisterRpc(NetworkRpc rpc)
        {
            rpc.Client<TeleportRequest>(HandleTeleport);
        }

        private void Update()
        {
            // Chỉ owner mới được gửi RPC
            if (NetworkObject.IsOwner && Input.GetKeyDown(KeyCode.T))
            {
                Debug.Log("[Player] sending teleport RPC");
                NetworkObject.InvokeRpc(new TeleportRequest());
            }
        }

        // Chạy trên SERVER - xử lý lệnh teleport
        private void HandleTeleport(TeleportRequest request, RpcContext context)
        {
            Debug.Log($"[Player] teleporting id={NetworkObject.ObjectId} to center");
            transform.position = new Vector3(0f, 1f, 0f);
        }
```

> **Lưu ý**: `rpc.Client<T>(handler)` nghĩa là "RPC gửi từ client, server xử lý". Fomoxa có cả `rpc.Server<T>()` cho chiều ngược lại.

### Bước 12.4 - Thêm `using` cần thiết

Đầu file `Player.cs`, thêm:

```csharp
using MyGame.Network.Models;
```

(nếu chưa có)

### Bước 12.5 - Test

1. Build lại: **Tools > Demo > Build Windows Player**.
2. Chạy Host + Client như Bước 9.
3. Click vào cửa sổ game của bạn, nhấn `T`.
4. Thấy log server: `[Player] teleporting id=2 to center`.
5. Cả 2 cửa sổ đều thấy capsule nhảy về giữa sân.

### Mở rộng thêm

Sau khi teleport RPC hoạt động, thử:
- Đổi teleport đến vị trí random thay vì center.
- Thêm cooldown 3 giây (không cho phép nhấn T liên tục).
- Thêm RPC từ server xuống client: server thông báo "player X vừa teleport" → client in thông báo lên màn hình.

---

## 13. Checklist tự xác nhận

Tôi đã chạy thành công khi:

- [ ] Package `com.fomoxa.networking` và `com.fomoxa.rpc` hiện trong Package Manager với version đúng.
- [ ] File `MoveInputNetAdapter.cs` tồn tại trong `Assets/Network/Generated/` và không có lỗi compile.
- [ ] `FomoxaSettings.asset` có Models Path và Generated Path đúng.
- [ ] Prefab `Player.prefab` có đúng 3 component theo thứ tự: `NetworkObject` → `NetworkTransform` → `Player`.
- [ ] Nhấn Play trong Editor → click "Host" → thấy log `[Host] listening on port 7777`.
- [ ] Thấy log `[Player] spawn on client ... mine=True`.
- [ ] Dùng WASD di chuyển được capsule.
- [ ] Chạy build `.exe` với `-client` → thấy log `[Client] Started`.
- [ ] Log server in `peer X: Started` khi client 2 kết nối.
- [ ] Cả 2 cửa sổ game thấy **2 capsule** - mỗi cái điều khiển được của mình, thấy cái kia di chuyển.
- [ ] *(Nâng cao)* Nhấn `T` → thấy capsule teleport về center trong cả 2 cửa sổ.

---

## Tham khảo thêm

| Tài liệu | Nội dung |
|----------|---------|
| [`docs/UBUNTU_GUIDE.md`](./UBUNTU_GUIDE.md) | Cài Docker, k3d, Nakama, triển khai lên cụm K8s |
| [`MyGame/README.md`](../MyGame/README.md) | Tổng quan kiến trúc MyGame, cách build server C# |
| [`PLAN.md`](../PLAN.md) | Roadmap phát triển, các phase đã hoàn thành |
| `Assets/Network/Launcher.cs` | Toàn bộ code Launcher thực tế (bao gồm Pod mode, Directory) |
| `Assets/Network/Player.cs` | Script Player hoàn chỉnh với bot mode và trace |

---

*Tài liệu này được viết dựa trên codebase thực tế tại commit `ccb59ca`. API có thể thay đổi - luôn kiểm tra `Assets/Network/` để xem code mẫu mới nhất.*

