# Backend

Lên đầu tài liệu: [trang chủ tài liệu](../README.md) · Trước: [overview.md](overview.md) · Tiếp: [nakama-module.md](nakama-module.md)

Backend (`MyGame.Backend/MyGame.Backend/`) là server duy nhất client nói chuyện cho mọi việc trừ chính trò chơi. Nó nhận kết nối fRPC, xác thực người chơi, áp dụng luật tài khoản và sảnh, nói chuyện với Nakama và directory, và nhận báo cáo từ game server. Cùng mã sảnh này cũng chạy trong cụm giả, nên client cư xử giống nhau trên cả hai.

## Bản đồ file

| File | Việc làm |
|---|---|
| `Program.cs` | đọc cấu hình, khởi động backend, đợi Ctrl+C hoặc SIGTERM |
| `BackendSettings.cs` | cấu hình từ `appsettings.json` và biến môi trường `Fomoxa__...` |
| `Backend.cs` | mở ba listener fRPC (người chơi TCP và WebSocket, pod TCP), endpoint metrics và phép tự kiểm tra Nakama |
| `Services.cs` | các interface dịch vụ: `Players`, `Lobby`, `Pods`, `PodGate` ([giao diện RPC](rpc-api.md)) |
| `Players.cs` | phần cài đặt: `Players.Login`, `Lobby.*`, danh sách phòng, chờ ghép trận, ticket |
| `Pods.cs` | `Pods.Login` và các lệnh `PodGate` mà game server gọi |
| `DirectoryLink.cs` | kết nối client tới directory, dùng để xin ticket |
| `NakamaLink.cs` | HTTP client của Nakama: xác thực, `find_match`, `poll_match`, `verify_ticket`, `list_rooms`, `kv_get`, `kv_put` |
| `OnlinePlayers.cs` | danh sách online lấy từ tín hiệu `Presence` |
| `Accounts/AccountService.cs` | đăng ký, kiểm tra mật khẩu, mã khôi phục, đổi mật khẩu |
| `Accounts/AccountAuth.cs` | ánh xạ `PlayerLogin.Mode` (`register`, `login`, `recover`) vào dịch vụ tài khoản |
| `Accounts/LobbyCore.cs` | hồ sơ, bạn bè, lời mời, bảng điểm, dùng chung giữa backend thật và cụm giả |
| `Accounts/KeyValue.cs` | kho key-value nhỏ phía sau tài khoản, có kiểm soát đồng thời lạc quan |
| `Accounts/Metrics.cs` | xuất văn bản Prometheus mà không cần thư viện ngoài |
| `Models/*.cs` | các message mạng ([liệt kê ở đây](rpc-api.md#các-message)) |
| `Generated/` | mã do `fomoxac` sinh; không sửa tay |

## Đăng nhập

`Players.Login` là bước xác thực của kết nối. Theo `Mode`:

| Mode | Hành vi |
|---|---|
| `register` | tạo tài khoản, lưu băm PBKDF2-SHA256 của mật khẩu (120.000 vòng, salt riêng) và trả mã khôi phục một lần |
| `login` | kiểm tra mật khẩu. Tên không phân biệt hoa thường. Sai năm lần liên tiếp thì khoá tên đó 60 giây |
| `recover` | kiểm tra mã khôi phục, đặt mật khẩu mới và cấp mã mới. Mã cũ hết hiệu lực |
| rỗng | khách. Khi có Nakama thì đăng nhập bằng `DeviceId`; không có Nakama thì chỉ cần `Player` |

Chỉ băm của mã khôi phục được lưu, nên mã đã mất không thể hiện lại. Thư viện RPC không trả phản hồi của lệnh đăng nhập về client, nên mã mới được giữ trong bộ nhớ cho tới khi client gọi `Lobby.TakeRecoveryCode` ngay sau khi đăng nhập.

Phiên mang một principal, là tên người chơi viết thường. Mọi lệnh `Lobby` đọc nó từ `context.Session.Principal`.

## Lưu trữ tài khoản

`KeyValue.cs` cung cấp get và put có kiểm tra version. Put không có version thì ghi đè, `"*"` chỉ ghi khi key chưa tồn tại, và version khác chỉ ghi khi còn khớp. Nhờ vậy hai lần đăng ký đồng thời cùng một tên không thể cùng thành công.

Khi có Nakama, kho này gọi các RPC Lua `kv_get` và `kv_put`, đọc và ghi storage của Nakama dưới user hệ thống. Không có Nakama thì backend dùng kho trong bộ nhớ, có thể lưu ra file JSON (`--data` ở cụm giả).

## Vào phòng

`Lobby.JoinRoom` làm bốn việc:

1. Trả lại ticket trước đó nếu cùng người chơi xin cùng phòng trong 5 giây gần nhất. Việc này làm cho thử lại an toàn.
2. `WaitForMatch`: khi có Nakama, gọi `find_match`, rồi `poll_match` mỗi 300 ms trong tối đa 15 giây. Với `auto`, backend gửi danh sách các phòng mà pod đã báo là chưa đầy, nên Nakama chỉ chọn phòng thật sự tồn tại.
3. Xác minh ticket bằng `verify_ticket`, kiểm tra nó thuộc người chơi và phòng này và chưa hết hạn.
4. Xin directory connect token cho phòng đó (`DirectoryLink.Ticket`) và trả về dưới dạng `RoomTicket`.

Không có Nakama thì bước chờ trả ngay phòng được yêu cầu.

## Pod báo gì

Game server gọi `PodGate.ReportRoom` và `PodGate.ReportScores` khoảng mỗi 2 giây ở cổng 9602.

| Báo cáo | Dùng để |
|---|---|
| `ReportRoom(RoomKey, Players, Capacity)` | danh sách phòng (`ListRooms`) và các metric `rooms_reported`, `room_players` |
| `ReportScores(Room, [Player, Steps])` | bảng điểm của phòng, kỷ lục của từng người và số trận của họ |

`LobbyCore.ReportScores` xử lý báo cáo ở nền: lần đầu thấy một người trong phòng, nó đọc tên hiển thị và kỷ lục rồi tính một trận. Sau đó nó cập nhật số bậc, và lưu kỷ lục mới khi số bậc vượt giá trị đã lưu.

## Danh sách online, bạn bè và lời mời

`Lobby.Presence(Room)` đánh dấu người gọi là online trong một phòng và trả danh sách người đang online. Client gọi mỗi vài giây, kể cả trong trận, vì kết nối backend vẫn mở. Người không được thấy trong 20 giây coi là offline.

Bạn bè và lời mời nằm trong bản ghi `social` của mỗi người. Lời mời kết bạn được lưu là đi ra ở người này và đi vào ở người kia. `Accept` làm cả hai danh sách chứa nhau. Lời mời vào phòng (`Lobby.Invite`) chỉ được gửi giữa bạn bè và hiện trong kết quả `Social` của người nhận cho tới khi họ bỏ qua.

## Số liệu

Backend phục vụ văn bản Prometheus ở `Fomoxa__Metrics__Listen` (cổng 9610 trong cụm).

| Metric | Loại | Ý nghĩa |
|---|---|---|
| `logins_total{mode,result}` | counter | số lần đăng nhập |
| `joins_total{result}` | counter | số lần vào phòng |
| `password_changes_total` | counter | số lần đổi mật khẩu |
| `friend_requests_total` | counter | số lời mời kết bạn |
| `room_invites_total` | counter | số lời mời vào phòng |
| `score_reports_total` | counter | số báo cáo điểm nhận từ pod |
| `online_players` | gauge | người được thấy trong 20 giây gần nhất |
| `rooms_reported` | gauge | số phòng pod đã báo |
| `room_players` | gauge | số người trong các phòng đó |

Mỗi metric được xuất với tiền tố `fomoxa_` (ví dụ `fomoxa_logins_total`). Xem [giám sát](../operations/04-monitoring.md) cho dashboard và luật cảnh báo xây trên chúng.

## Chạy nó

| Cách | Lệnh |
|---|---|
| Với cụm giả (không cần Docker) | `./scripts/run-mock-cluster.sh` |
| Backend thật với Nakama trong docker compose | `./scripts/test-real-backend.sh` (xem [kiểm thử](../testing.md)) |
| Trong cụm | `kubectl apply -f deploy/k8s/backend.yaml` ([đọc manifest](../deploy/manifest-walkthrough.md)) |
