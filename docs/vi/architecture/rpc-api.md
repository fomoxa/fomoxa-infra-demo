# Giao diện RPC

Lên đầu tài liệu: [trang chủ tài liệu](../README.md) · Trước: [client-code.md](client-code.md) · Tiếp: [development.md](development.md)

Client, backend và game server nói chuyện với nhau qua fRPC. Các dịch vụ được khai báo trong `MyGame.Backend/MyGame.Backend/Services.cs` và các message trong `MyGame.Backend/MyGame.Backend/Models/*.cs`. `fomoxac` sinh codec và client có kiểu từ chúng, và project Unity có bản khai báo riêng giống hệt dưới `MyGame/Assets/Network/`.

## Dịch vụ

| Dịch vụ | Ai gọi | Cần role |
|---|---|---|
| `Players` | client, như bước đăng nhập của kết nối | không (lệnh này xác thực) |
| `Lobby` | client đã đăng nhập | `player` |
| `Pods` | game server, như đăng nhập của nó | không (lệnh này xác thực) |
| `PodGate` | game server | `pod` |

### `Players`

| Phương thức | Request | Response | Ghi chú |
|---|---|---|---|
| `Login` | `PlayerLogin` | `PlayerWelcome` | trường `Mode` chọn đăng ký, đăng nhập hay khôi phục ([backend](backend.md#đăng-nhập)) |

### `Lobby`

| Phương thức | Request | Response | Mục đích |
|---|---|---|---|
| `JoinRoom` | `JoinRoomRequest` | `RoomTicket` | ghép cặp, xác minh ticket, trả connect token. Có thể lỗi `NoRoomForVersion` |
| `ListRooms` | `ListRoomsRequest` | `RoomList` | các phòng kèm số người và trạng thái |
| `Presence` | `PresenceRequest` | `OnlineList` | đánh dấu bạn online trong một phòng, trả mọi người đang online |
| `ChangePassword` | `ChangePasswordRequest` | `ActionResult` | cần mật khẩu cũ |
| `TakeRecoveryCode` | `RecoveryCodeRequest` | `ActionResult` | `Message` chứa mã khôi phục mới một lần, sau khi đăng ký hoặc khôi phục |
| `GetProfile` | `ProfileRequest` | `PlayerProfile` | bậc cao nhất và số trận của một người |
| `Social` | `SocialRequest` | `SocialState` | bạn bè, lời mời kết bạn và lời mời vào phòng |
| `FriendAction` | `FriendActionRequest` | `ActionResult` | `Action`: `request`, `accept`, `decline`, `remove` hoặc `dismiss-invite` |
| `Invite` | `InviteRequest` | `ActionResult` | mời một người bạn vào phòng |
| `Scoreboard` | `ScoreboardRequest` | `ScoreboardState` | bảng xếp hạng của một phòng |

### `Pods`

| Phương thức | Request | Response |
|---|---|---|
| `Login` | `PodLogin` | không |

### `PodGate`

| Phương thức | Request | Response | Mục đích |
|---|---|---|---|
| `ReportRoom` | `RoomReport` | không | số người và sức chứa của một phòng |
| `ReportScores` | `ScoreReport` | không | số bậc hiện tại của từng người trong phòng |
| `TransferPlayer` | `TransferPlayerRequest` | `RoomTicket` | chuyển một người sang phòng khác. Có khai báo nhưng chưa dùng |

Các dịch vụ giữa backend, pod và directory (`Directory`, `DirectoryAdmin`) thuộc runtime Fomoxa và được liệt kê ở [tham khảo](../reference.md#7-giao-diện-rpc-của-backend).

## Các message

Kiểu của trường là kiểu mạng: `string`, `bytes`, `bool`, `u16`, `u32`, `i64` và mảng.

| Message | Trường |
|---|---|
| `PlayerLogin` | `Player` string, `DeviceId` string, `Secret` bytes, `Mode` string, `Recovery` bytes |
| `PlayerWelcome` | `Player` string, `RecoveryCode` string |
| `JoinRoomRequest` | `RoomKey` string, `Version` string |
| `RoomTicket` | `Address` string, `Port` u16, `Connection` bytes, `WebSocketUrl` string |
| `NoRoomForVersion` | `Version` string |
| `ListRoomsRequest` | không có |
| `RoomInfo` | `RoomKey` string, `Players` u32, `Capacity` u32, `State` string (`open`, `waiting`, `full`) |
| `RoomList` | `Rooms` mảng `RoomInfo` |
| `PresenceRequest` | `Room` string |
| `OnlinePlayer` | `Name` string, `Room` string |
| `OnlineList` | `Players` mảng `OnlinePlayer` |
| `ActionResult` | `Ok` bool, `Message` string |
| `ChangePasswordRequest` | `OldPassword` string, `NewPassword` string |
| `RecoveryCodeRequest` | không có |
| `ProfileRequest` | `Player` string |
| `PlayerProfile` | `Name` string, `BestSteps` u32, `Matches` u32, `CreatedUnix` i64 |
| `FriendInfo` | `Name` string, `Online` bool, `Room` string, `BestSteps` u32 |
| `InviteInfo` | `From` string, `Room` string |
| `SocialRequest` | không có |
| `SocialState` | `Friends` mảng `FriendInfo`, `Incoming` mảng string, `Outgoing` mảng string, `Invites` mảng `InviteInfo` |
| `FriendActionRequest` | `Action` string, `Target` string |
| `InviteRequest` | `To` string, `Room` string |
| `ScoreEntry` | `Name` string, `Steps` u32, `Best` u32 |
| `ScoreboardRequest` | `Room` string |
| `ScoreboardState` | `Entries` mảng `ScoreEntry` |
| `PodLogin` | `Key` bytes |
| `RoomReport` | `RoomKey` string, `Players` u32, `Capacity` u32 |
| `PodScore` | `Player` string, `Steps` u32 |
| `ScoreReport` | `Room` string, `Entries` mảng `PodScore` |
| `TransferPlayerRequest` | `Player` string, `RoomKey` string, `Version` string |

Bản thân lưu lượng game không phải RPC. Message duy nhất là `MoveInput` (di chuyển và `Jump`) cùng transform được đồng bộ và trạng thái người chơi, gửi qua UDP mã hoá.

## Lỗi

Một lệnh thất bại mang mã trạng thái và thông báo. Những mã client hay gặp:

| Mã | Khi nào |
|---|---|
| `InvalidArgument` | tên không hợp lệ, mật khẩu yếu, hoặc trường bắt buộc bị rỗng |
| `Unauthenticated` | sai mật khẩu hoặc mã khôi phục, hoặc phiên Nakama đã mất và bạn phải đăng nhập lại |
| `ResourceExhausted` | tên bị khoá 60 giây sau năm lần sai mật khẩu |
| `FailedPrecondition` | không ghép được trong 15 giây, ticket bị từ chối, hoặc phòng đã có người giữ |
| `Unavailable` | không tới được Nakama hoặc directory |

`LobbyCore.Translate` ánh xạ các lỗi tài khoản sang các mã này. Mọi lỗi tài khoản khác, như tên đã có người dùng, thành `FailedPrecondition`.
