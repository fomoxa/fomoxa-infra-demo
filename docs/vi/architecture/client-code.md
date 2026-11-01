# Mã client Unity

Lên đầu tài liệu: [trang chủ tài liệu](../README.md) · Trước: [game-server.md](game-server.md) · Tiếp: [rpc-api.md](rpc-api.md)

Trang này vẽ bản đồ các script client trong `MyGame/Assets/`. Muốn mở project, dựng scene và build bản chạy thì xem [client Unity](../unity-client.md).

## Scene

| Scene | Chứa | Được tải khi |
|---|---|---|
| `Lobby` | camera nền tối, `RoomJoiner`, `LobbyScreen` và `BootRouter` | scene đầu tiên của bản build |
| `Game` | `NetworkManager`, sàn và các bục, `Launcher`, `MatchHudScreen`, prefab người chơi | chỉ sau khi đã có ticket |

Scene `Game` được tải sau khi ticket đến, nên không có đối tượng mạng nào tồn tại khi người chơi ở sảnh. Rời trận sẽ tải lại `Lobby`.

Các scene được dựng bằng script editor, không dựng tay: **Tools > Demo > Rebuild Scene** (`DemoScene.cs`) tạo cả hai scene, và `LobbyUiSetup.cs` gắn các tài liệu UI Toolkit. Các menu được liệt kê ở cuối trang.

## Script (`MyGame/Assets/Network/`)

| Script | Vai trò |
|---|---|
| `RoomJoiner` | trái tim của client. Sống xuyên các scene (`DontDestroyOnLoad`). Mở kết nối fRPC tới backend, đăng nhập, liệt kê phòng, vào phòng, gửi `Presence`, xử lý bạn bè, lời mời, đổi mật khẩu và khôi phục. Đọc các cờ dòng lệnh |
| `LobbyScreen` | giao diện sảnh dựng từ `Assets/UI/Lobby.uxml` và `Lobby.uss`. Nó chỉ vẽ và chuyển tiếp lượt bấm; mọi hành động gọi `RoomJoiner` |
| `BootRouter` | nằm trong scene `Lobby`. Các chế độ dòng lệnh không cần sảnh (`-pod`, `-host`, `-client`) bỏ qua sảnh và tải thẳng `Game` |
| `MatchSession` | cầu nối giữa các scene: sảnh gọi `Begin(room, ticket)` để tải `Game`; `Launcher` lấy ticket bằng `TryTake` và `ReturnToLobby` quay về |
| `Launcher` | nằm trong `Game`. Khởi động client, host hoặc server từ ticket, và quay về sảnh khi kết nối kết thúc |
| `MatchHudScreen` | HUD trong trận từ `MatchHud.uxml`: tên phòng, trạng thái kết nối, nút thoát. Nó ẩn khi bạn đang chơi để phím Space không bấm nhầm nút, và hiện lại khi bấm Escape hoặc Tab |
| `Player` | behaviour người chơi trên mạng: đọc input, chạy `Course.Simulate`, theo dõi `Steps`. Cùng mã chạy trên server |
| `PlayerCamera` | camera đuổi sau lưng nhân vật của bạn. Chuột phải hoặc Q và E để xoay, con lăn để zoom, bục chắn đường thì kéo camera lại gần. Di chuyển tính theo hướng camera |
| `PinnedCertificateCheck` | kiểm tra pin TLS: so SHA-256 của chứng chỉ với `-tlspin:` |
| `Models/Course.cs` | cách bố trí bục và luật di chuyển, nhảy, dùng chung với server ([hằng số](../reference.md#8-hằng-số-gameplay)) |
| `Models/MoveInput.cs` | message input, gồm `Jump` |
| `Models/Backend/*.cs`, `Rpc/Services.cs` | các message mạng và interface dịch vụ; `Generated/` chứa codec đã sinh |

## Cờ dòng lệnh

Được đọc trong `RoomJoiner.ApplyCommandLine` và mã autojoin.

| Cờ | Tác dụng |
|---|---|
| `-backend:host:port` | địa chỉ backend (9600 thường, 9643 TLS) |
| `-tls` | kết nối qua TLS với bộ gốc của hệ thống |
| `-tlspin:<sha256>` | kết nối qua TLS và chỉ chấp nhận chứng chỉ có vân tay này |
| `-autojoin` | đăng nhập và vào phòng không cần bấm, dùng cho test |
| `-room:<key>` | phòng cho `-autojoin` |
| `-name:<tên>` | tên người chơi cho `-autojoin` |
| `-device:<id>` | device id cho đăng nhập khách |
| `-pod`, `-host`, `-client` | chạy game trực tiếp không qua sảnh |

## Luồng đăng nhập và vào phòng ở client

1. `LobbyScreen` thu tên và mật khẩu rồi nhờ `RoomJoiner` đăng nhập với mode `register`, `login` hoặc `recover`.
2. `RoomJoiner` kết nối tới backend bằng `RpcConnection`, gửi `PlayerLogin`, và giữ kết nối mở suốt phiên.
3. Nó liệt kê phòng bằng `Lobby.ListRooms` và gửi `Lobby.Presence` mỗi vài giây. Vì kết nối vẫn mở nên bạn vẫn hiện online trong trận.
4. Bấm vào một phòng gọi `Lobby.JoinRoom`. Phản hồi là một `RoomTicket`: địa chỉ, cổng và connect token.
5. `MatchSession.Begin` tải `Game`. `Launcher` lấy ticket và kết nối UDP mã hoá tới địa chỉ trong ticket.
6. Khi kết nối kết thúc, `Launcher` gọi `MatchSession.ReturnToLobby`, và sảnh hiện lý do.

## Công cụ editor

| Menu | Script | Làm gì |
|---|---|---|
| Tools > Fomoxa > Generate | package | chạy `fomoxac` cho cây `MyGame` |
| Tools > Demo > Rebuild Scene | `DemoScene.cs` | dựng lại hai scene `Lobby` và `Game`, các bục và prefab |
| Tools > Demo > Add Lobby UI | `LobbyUiSetup.cs` | gắn `Lobby.uxml` vào scene sảnh và `MatchHud.uxml` vào scene game |
| Tools > Demo > Build Windows Player | `DemoScene.cs` | build `MyGame/Builds/MyGame.exe` |
| (script gọi) | `DemoGenerate.cs` | chạy Tools > Fomoxa > Generate ở chế độ batch |
