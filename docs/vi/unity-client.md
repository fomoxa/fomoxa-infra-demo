# 04 - Client Unity: mở project, dựng scene, build, chạy

Tài liệu này hướng dẫn mở project Unity trong repo (`MyGame/`), dựng hai scene **Lobby** và **Game**, build bản chạy Windows và kết nối vào cụm
([02 - cụm giả](getting-started/quickstart-mock-cluster.md) hoặc [03 - cụm Kubernetes](deploy/README.md)).

> **Chưa kiểm chứng:** môi trường viết tài liệu không có Unity. Mọi thao tác trong Unity (menu, scene, giao diện, camera, nhảy, build) chưa được chạy lại;
> phần **dòng lệnh** (`fomoxac`) và **giao thức** (đăng nhập, phòng, UDP, nhảy/đáp bục) đã kiểm chứng bằng bot và selftest ([06](testing.md)).
> Nếu Unity báo lỗi biên dịch hoặc giao diện lệch so với mô tả, hãy gửi nội dung lỗi để cập nhật.

| Thứ | Giá trị |
|---|---|
| Unity | **6000.5.7f1** (đúng phiên bản trong `MyGame/ProjectSettings/ProjectVersion.txt`) |
| Hệ điều hành client | Windows 64-bit (bản build `MyGame.exe`) |
| Git | phải có trong `PATH` (Unity tải package Fomoxa từ GitHub) |
| .NET SDK 8 | chỉ cần khi chạy `fomoxac` bằng dòng lệnh |

---

## Bước 1. Mở project

**Mục tiêu:** Unity mở được `MyGame/` và tải đủ package.

**Thao tác:**

1. Mở **Unity Hub** → **Add** → **Add project from disk** → chọn thư mục `MyGame` của repo (với WSL2: `\\wsl$\Ubuntu\home\<bạn>\fomoxa-infra-demo\MyGame`; nên **clone repo vào ổ Windows** nếu muốn mở bằng Unity Hub Windows cho nhanh).
2. Hub báo thiếu phiên bản `6000.5.7f1` → **Install** đúng phiên bản đó.
3. Mở project, đợi import lần đầu (vài phút).

**Ý nghĩa:** `Packages/manifest.json` khai báo hai package lấy thẳng từ GitHub:

```json
"com.fomoxa.networking": "https://github.com/fomoxa/com.fomoxa.networking.git#0.2.0",
"com.fomoxa.rpc":        "https://github.com/fomoxa/com.fomoxa.rpc.git#0.1.0"
```

Unity gọi `git` để clone chúng về `Library/PackageCache/`; commit chính xác được ghim trong `packages-lock.json`.

**Kiểm tra:** `Window → Package Manager` → ô *In Project* thấy cả hai package; thanh Console không có lỗi đỏ.

**Lỗi thường gặp:**

| Triệu chứng | Cách xử lý |
|---|---|
| `[Package Manager] ... git ... not found` | cài Git và đảm bảo `git --version` chạy được trong PowerShell; mở lại Unity Hub |
| UPM báo lỗi JSON dù file nhìn bình thường | `manifest.json` bị **BOM**. Mở bằng VS Code, góc dưới phải thấy `UTF-8 with BOM` → *Save with Encoding* → `UTF-8` |
| `Assembly name collision: Fomoxa.Unity` | có package `fomoxa.unity` cài thừa trong `manifest.json`; xoá dòng đó |
| Lỗi đỏ `CS0246/CS0103` sau khi `git pull` | mã sinh tự động lệch model → [Sinh lại mã](#sinh-lại-mã-khi-đổi-model-hoặc-rpc) |

---

## Bước 2. Dựng hai scene (Rebuild Scene)

**Mục tiêu:** tạo `Assets/Scenes/Lobby.unity` (sảnh) và `Assets/Scenes/Game.unity` (trận đấu) và đặt Build Settings.

**Thao tác:** menu **Tools → Demo → Rebuild Scene**.

**Hoặc dòng lệnh (PowerShell, chạy từ thư mục gốc repo):**

```powershell
$Unity = "C:\Program Files\Unity\Hub\Editor\6000.5.7f1\Editor\Unity.exe"
& $Unity -batchmode -nographics -projectPath "$PWD\MyGame" -executeMethod DemoScene.Build -logFile -
```

**Ý nghĩa:** `DemoScene.Build` (trong `Assets/Editor/DemoScene.cs`) làm lần lượt:

| # | Việc | Kết quả |
|---|---|---|
| 1 | tạo scene **Game**: đèn, sàn, 8 cột mốc, **18 bục nhảy** (dựng theo `Course`), camera `PlayerCamera` | |
| 2 | tạo prefab `Player` (capsule + `NetworkObject` + `NetworkTransform` + `Player`) | `Assets/Prefabs/Player.prefab` |
| 3 | tạo `Network Manager` (+ transport UDP mã hoá) và `Launcher`; gắn HUD trong trận (`MatchHud.uxml`) | |
| 4 | chạy các bước của Fomoxa: gom prefab/scene vào danh sách của `NetworkManager` | |
| 5 | tạo scene **Lobby**: camera nền tối, object `Backend` (`RoomJoiner` + `BootRouter`), giao diện sảnh (`Lobby.uxml`) | |
| 6 | đặt **Build Settings**: `Lobby` (index 0) rồi `Game` | |

**Kết quả mong đợi** - Console (hoặc log của lệnh batch) có:

```
[Demo] lobby scene built: Assets/Scenes/Lobby.unity
[Demo] scene built: Assets/Scenes/Game.unity
[Demo] prefab built: Assets/Prefabs/Player.prefab
```

**Kiểm tra:**

1. `File → Build Profiles` (hoặc *Build Settings*): danh sách scene đúng thứ tự `Scenes/Lobby` rồi `Scenes/Game`, cả hai được tích.
2. Không có dòng lỗi `[Demo] prefab:` hoặc `[Demo] scene:` trong Console.
3. Mở `Lobby.unity`: Hierarchy có `Lobby Camera`, `Backend`, `Lobby UI`.
4. Mở `Game.unity`: Hierarchy có `Network Manager`, `Match HUD`, `Main Camera`, `Platform 1`...`Platform 18`.

**Vì sao hai scene?** Sảnh (đăng nhập, danh sách phòng, bạn bè) **không cần** sàn/nhân vật/mạng game, và chỉ khi người chơi **bấm vào phòng** và đã có ticket thì scene `Game` mới được tải. Xem [Luồng hai scene](#luồng-hai-scene).

**Lỗi thường gặp:**

| Triệu chứng | Cách xử lý |
|---|---|
| Thiếu menu `Tools → Demo` | script Editor lỗi biên dịch - xem Console, sửa lỗi đỏ trước |
| `[Demo] build failed: ...` | đọc exception ngay dưới dòng đó; thường là package chưa tải xong (Bước 1) |
| Ô nhập/nút không hiện chữ trong sảnh | `LobbyPanelSettings.asset` chưa có theme: chạy menu **Tools → Demo → Add Lobby UI** rồi kiểm tra ô *Theme Style Sheet* của asset đó trỏ tới `LobbyTheme.tss` |
| Sau khi Rebuild thấy nhiều file `.meta` mới | bình thường - **commit chúng cùng scene**, nếu không GUID đổi giữa các máy |

---

## Bước 3. Chạy thử trong Editor

**Mục tiêu:** vào được game từ chính Unity Editor.

1. Bật backend: [cụm giả (02)](getting-started/quickstart-mock-cluster.md#bước-2-chạy-cụm-giả) **hoặc** [cụm K8s (03)](deploy/README.md).
2. Mở `Assets/Scenes/Lobby.unity` → bấm **Play**.
3. Thẻ **Đăng ký** → tên + mật khẩu (≥ 8 ký tự) → **Tạo tài khoản** → lưu **mã khôi phục** → **Tôi đã lưu mã**.
4. Thẻ **Phòng** → bấm `arena:1`.

**Kết quả mong đợi:**
- Trạng thái chạy qua: *Đang kết nối backend → Đang đăng nhập → Đang vào phòng → Đã nhận ticket → Đang tải màn chơi...* rồi scene `Game` hiện ra.
- Console: `[Join] logged in as <tên>`, `[Match] arena:1 -> 127.0.0.1:7778`, `[Player] spawn on client id=... mine=True`.
- Với **cụm giả** vào ngay; với **cụm thật (Nakama)** cần **2 người** cùng bấm một phòng (mở thêm một client).

**Chế độ dev không cần backend (Host):** mở `Game.unity` trực tiếp → Play → HUD hiện *Công cụ dev* → **Host (server cục bộ + người chơi)** để chơi một mình, không qua sảnh.

---

## Bước 4. Build bản chạy Windows

**Mục tiêu:** `Builds/MyGame.exe` (dùng để chạy nhiều client và cho `test-p5`).

**Thao tác:** menu **Tools → Demo → Build Windows Player**.

**Hoặc dòng lệnh:**

```powershell
$Unity = "C:\Program Files\Unity\Hub\Editor\6000.5.7f1\Editor\Unity.exe"
& $Unity -batchmode -nographics -projectPath "$PWD\MyGame" -executeMethod DemoScene.BuildPlayer -logFile -
```

**Ý nghĩa:** build hai scene `Lobby` và `Game` (theo thứ tự đó) cho `StandaloneWindows64`, ghi vào `MyGame/Builds/MyGame.exe`.

**Kết quả mong đợi:** log có `[Demo] player build: Succeeded -> Builds/MyGame.exe`.

**Kiểm tra:** `Test-Path MyGame\Builds\MyGame.exe` → `True`.

**Lỗi thường gặp:** build thất bại với "scene not in build" → chưa chạy Rebuild Scene (Bước 2).

---

## Bước 5. Chạy bản build và tham số dòng lệnh

**Chạy bình thường** (backend ở `127.0.0.1:9600`, mặc định của cụm giả và cụm k3d):

```powershell
.\MyGame\Builds\MyGame.exe
```

**Tham số:**

| Tham số | Ý nghĩa |
|---|---|
| `-backend:host:port` | nối backend khác mặc định `127.0.0.1:9600`, ví dụ `-backend:192.168.1.50:9600` |
| `-tls` | bật TLS khi nối backend (dùng chứng chỉ do CA công cộng cấp) |
| `-tlspin:<SHA256-hex>` | bật TLS **và** chỉ tin chứng chỉ có đúng vân tay này (chứng chỉ tự ký). Lấy pin từ `scripts/gen-tls-cert.sh` hoặc từ banner của cụm giả. Thường đi với `-backend:127.0.0.1:9643` |
| `-autojoin` | tự đăng nhập (kiểu khách qua `-device`) và vào phòng, không cần bấm UI |
| `-name:<tên>` `-room:<phòng>` `-device:<id>` | đi kèm `-autojoin` |
| `-batchmode -nographics -logFile <file>` | tham số chuẩn của Unity để chạy ẩn và ghi log (dùng cho test tự động) |
| `-bot` | nhân vật tự chạy theo vòng tròn và nhảy định kỳ (kiểm thử) |
| `-trace` | in vị trí nhân vật 2 giây một lần |
| `-host`, `-client`, `-pod` | chế độ cũ **bỏ qua sảnh**, vào thẳng scene `Game`: Host cục bộ, nối trực tiếp, hoặc chạy bản Unity làm pod |

Ví dụ nối cụm giả qua TLS:

```powershell
.\MyGame\Builds\MyGame.exe -backend:127.0.0.1:9643 -tlspin:C5B502A6036815A39E6E60AC21398651BFFB9AD4FF19BDD5C6397D9FF74C5167
```

(Thay bằng pin in ra khi bạn khởi động cụm giả/chạy `gen-tls-cert.sh`.)

Ví dụ hai client tự động (giống `test-p5`):

```powershell
.\MyGame\Builds\MyGame.exe -batchmode -nographics -logFile ana.log -autojoin -name:ana -device:dev-ana -room:arena:1
.\MyGame\Builds\MyGame.exe -batchmode -nographics -logFile bo.log  -autojoin -name:bo  -device:dev-bo  -room:arena:1
Select-String -Path ana.log,bo.log -Pattern "mine=True"
```

Mỗi log phải có dòng `mine=True` (đã vào pod, có nhân vật của mình).

**Điều khiển trong trận:**

| Phím | Tác dụng |
|---|---|
| W A S D / mũi tên | di chuyển **theo hướng camera** |
| Space | nhảy |
| Giữ chuột phải + kéo, hoặc Q / E | xoay camera |
| Lăn chuột | zoom |
| Esc hoặc Tab | mở/đóng menu (bảng điểm, mời bạn, thoát phòng). HUD **ẩn** khi đang chơi để Space không bấm nhầm nút |

**Luật chơi:** 18 bục xếp xoắn ốc, mỗi bục cao hơn bục trước 1,4 m. Điểm = số bậc cao nhất đã đứng lên; kỷ lục cá nhân được lưu vào hồ sơ.

---

## Luồng hai scene

```
 Lobby.unity                                         Game.unity
 ┌──────────────────────────────────────┐            ┌──────────────────────────────────────┐
 │ RoomJoiner (DontDestroyOnLoad)       │            │ Network Manager + Launcher           │
 │ LobbyScreen (UI Toolkit: Lobby.uxml) │  bấm phòng │ Match HUD (UI Toolkit: MatchHud.uxml)│
 │  đăng nhập/đăng ký/khôi phục         │ ─────────► │ PlayerCamera, sàn, 18 bục            │
 │  phòng · bạn bè · tài khoản · online │  có ticket │                                      │
 └──────────────────────────────────────┘ ◄───────── └──────────────────────────────────────┘
                                          thoát phòng / mất kết nối
```

1. `RoomJoiner` (tồn tại xuyên suốt hai scene) giữ kết nối tới backend: đăng nhập, danh sách phòng, báo "còn online" mỗi 5 giây, bạn bè, bảng điểm.
2. Bấm phòng → `RoomJoiner.JoinRoom` xin ticket → `MatchSession.Begin(phòng, ticket)` → **chỉ lúc này** `SceneManager.LoadSceneAsync("Game")`.
3. `Launcher.Start()` trong `Game` lấy ticket ra (`MatchSession.TryTake`) và `ClientManager.StartConnection(address, port, ticket)` nối UDP vào gateway/pod.
4. Thoát phòng (nút, hoặc mất kết nối) → `MatchSession.ReturnToLobby` tải lại `Lobby`, **vẫn đăng nhập**.

Mã chính (đều trong `MyGame/Assets/Network/`): `RoomJoiner.cs`, `LobbyScreen.cs`, `MatchHudScreen.cs`, `MatchSession.cs`, `Launcher.cs`, `Player.cs`, `PlayerCamera.cs`, `BootRouter.cs`;
dùng chung với server: `Models/Course.cs` (hình học bục + vật lý nhảy), `Models/MoveInput.cs` (đầu vào gửi lên server).

## Chỉnh giao diện

Giao diện là **asset UI Toolkit**, không vẽ bằng code:

| File | Vai trò |
|---|---|
| `Assets/UI/Lobby.uxml`, `Lobby.uss` | bố cục và kiểu dáng sảnh |
| `Assets/UI/MatchHud.uxml`, `MatchHud.uss` | HUD trong trận |
| `Assets/UI/LobbyTheme.tss` | theme mặc định của UI Toolkit |
| `Assets/UI/LobbyPanelSettings.asset` | cài đặt panel (tự tạo) |

Mở `.uxml` bằng **UI Builder** (`Window → UI Toolkit → UI Builder`) hoặc sửa màu/kích thước trong `.uss`. Nếu đổi `name="..."` của phần tử thì phải đổi theo trong `LobbyScreen.cs`/`MatchHudScreen.cs` (chúng tìm phần tử theo tên).
Sau khi sửa, chạy **Tools → Demo → Add Lobby UI** nếu cần gắn lại UI vào scene.

---

## Sinh lại mã khi đổi model hoặc RPC

Dữ liệu trao đổi qua mạng (đăng nhập, phòng, bảng điểm... và `MoveInput`) được mô tả bằng các lớp C# có thuộc tính `[Network]`. Công cụ **`fomoxac`**
đọc chúng và **sinh mã mã hoá/giải mã** (`Generated/*.cs`). Backend và Unity có **hai cây mã sinh riêng** cần khớp nhau:

| Cây | Model | Mã sinh | Cấu hình |
|---|---|---|---|
| Backend | `MyGame.Backend/MyGame.Backend/Models/*.cs` + `Services.cs` | `.../Generated/` | `fomoxa.toml` cùng thư mục |
| Unity (và server game) | `MyGame/Assets/Network/Models/**` + `Rpc/Services.cs` | `MyGame/Assets/Network/Generated/` | `Assets/FomoxaSettings.asset` (sinh ra `MyGame/fomoxa.toml`) |

Model/dịch vụ phía Unity là **bản sao** của backend (`Models/Backend/*.cs`, `Rpc/Services.cs`).

**Quy trình thêm hoặc sửa một RPC/model (ví dụ thêm trường vào `RoomInfo`):**

```bash
cd ~/fomoxa-infra-demo
FOMOXAC=$PWD/MyGame.Backend/tools/fomoxac/linux-x64/fomoxac
chmod +x "$FOMOXAC"     # file trong git không giữ quyền thực thi

# 1) Sửa model + dịch vụ ở backend (MyGame.Backend/MyGame.Backend/Models/*.cs, Services.cs), rồi sinh mã backend
( cd MyGame.Backend/MyGame.Backend && "$FOMOXAC" generate )

# 2) Chép bản sao sang Unity
cp MyGame.Backend/MyGame.Backend/Models/{Players,Pods,Rooms,Social}.cs MyGame/Assets/Network/Models/Backend/
cp MyGame.Backend/MyGame.Backend/Services.cs MyGame/Assets/Network/Rpc/Services.cs

# 3) Sinh mã phía Unity/server
( cd MyGame && "$FOMOXAC" generate )

# 4) Kiểm tra mã sinh sẵn đã khớp model (đây cũng là bước CI chạy)
( cd MyGame.Backend/MyGame.Backend && "$FOMOXAC" generate --check )
( cd MyGame && "$FOMOXAC" generate --check )
```

Trên Windows dùng `MyGame.Backend\tools\fomoxac\win-x64\fomoxac.exe generate`. Trong Unity cũng có menu **Tools → Fomoxa → Generate** (chạy cùng công cụ cho cây `MyGame`).

**Ý nghĩa:** `generate` ghi lại `*.cs` và `.fomoxa/schema.json`; `--check` chỉ so sánh và thoát mã khác 0 nếu mã sinh đã cũ (in `fomoxac: up to date` khi khớp).
Thêm trường hoặc model mới làm **đổi "vân tay" giao thức** → client, backend và pod phải được build lại **cùng lúc** (bản `MyGame.exe` cũ sẽ không bắt tay được với backend mới).

**Kết quả mong đợi (thật):**

```
✓ RoomTicket.rpc unchanged
...
fomoxac: up to date
```

**Lỗi thường gặp:**

| Triệu chứng | Cách xử lý |
|---|---|
| `SchemaConflict` / `MessageId conflict` / client bị ngắt ngay khi kết nối | mã sinh cũ không khớp model: xoá `MyGame/Assets/Network/Generated/*.cs` rồi sinh lại (bước 3) |
| `CS0103: The name '...NetAdapter' does not exist` | chưa sinh mã hoặc Unity chưa biên dịch xong; sinh lại, đợi vòng xoay biên dịch hết |
| `FRPC003` khi biên dịch `Services.cs` | model RPC chưa có codec: phải sinh mã (bước 1/3) **trước** khi thêm `Services.cs` |
| Unity treo khi Generate | process `fomoxac` kẹt: tắt nó trong Task Manager, kiểm tra đường dẫn Models/Generated trong asset `Assets/FomoxaSettings.asset` (file `MyGame/fomoxa.toml` được sinh từ asset này, đừng sửa tay), chạy lại |
| Thấy hàng chục file `Generated/*.cs` đổi chỉ ở dòng `fomoxac-version`/`generated-at` | bình thường khi đổi phiên bản `fomoxac`; nội dung giao thức không đổi |

---

## Lỗi thường gặp khi chơi

| Triệu chứng | Nguyên nhân / cách xử lý |
|---|---|
| Đăng nhập báo "sai tên hoặc mật khẩu" | đúng tên nhưng sai mật khẩu, hoặc tên chưa đăng ký (dùng thẻ **Đăng ký**). 5 lần sai liên tiếp bị khoá tên đó 60 giây |
| "tên này đã có người dùng" | tên đã đăng ký (không phân biệt hoa thường). Đổi tên khác |
| Quên mật khẩu | thẻ **Quên mật khẩu**: tên + mã khôi phục + mật khẩu mới |
| Bấm phòng rồi chờ mãi ở "Đang ghép trận" | cụm thật (Nakama) cần **2 người** cùng bấm một phòng, trong 30 giây. Dùng "Tự động ghép trận" hoặc mở client thứ hai |
| "Không có game server chạy phiên bản X" | client có `Application.version` khác phiên bản pod (`FOMOXA_VERSION` trong `fleet.yaml`, mặc định `1.0`). Project đang đặt `Player Settings → Version = 1.0` khớp `FOMOXA_VERSION=1.0`; nếu bạn đổi một bên thì đổi cả bên kia (cụm giả mặc định không kiểm phiên bản, trừ khi có `--strict-version`) |
| Vào được phòng nhưng không thấy người khác | prefab chưa đăng ký với `NetworkManager`: chạy **Rebuild Scene** (Bước 2) và kiểm tra không có lỗi `[Demo] prefab:` |
| Nhân vật đi nhưng không nhảy | Space bị UI nuốt: HUD phải **ẩn** khi đang chơi (Esc/Tab chỉ mở khi bạn muốn). Nếu vẫn lỗi, đảm bảo bản build mới nhất |
| Camera đứng yên/không thấy nhân vật | Console tìm dòng `[Camera] chưa thấy nhân vật của mình` và `[Player] spawn on client ... mine=True` |
| Kết nối UDP mãi không vào được trong WSL2 + Windows | [07](troubleshooting.md#client-windows-không-nối-được-cụm-trong-wsl2) |

---

## Checklist cuối

- [ ] Unity `6000.5.7f1` mở project, Package Manager thấy 2 package Fomoxa, Console không lỗi đỏ
- [ ] **Rebuild Scene** xong; Build Settings có `Lobby` (0) rồi `Game` (1)
- [ ] Play từ `Lobby.unity`: đăng ký, lưu mã khôi phục, vào `arena:1`, scene `Game` tải sau khi có ticket
- [ ] Trong trận: WASD theo camera, Space nhảy, HUD ẩn, Esc/Tab mở menu có bảng điểm
- [ ] `Builds/MyGame.exe` tồn tại; hai client `-autojoin` cùng in `mine=True`
- [ ] `fomoxac generate --check` ở cả hai cây đều `up to date`
- [ ] Đã commit các file `.meta` mới

Tiếp theo: [05 - Vận hành](operations/README.md).
