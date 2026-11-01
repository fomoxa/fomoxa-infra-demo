# Game server (pod)

Lên đầu tài liệu: [trang chủ tài liệu](../README.md) · Trước: [nakama-module.md](nakama-module.md) · Tiếp: [client-code.md](client-code.md)

Game server là một chương trình console .NET trong `MyGame/Server~/`. Nó thay cho `MyGame.exe -pod`, nên chạy được trong container mà không cần Unity. Trong cụm, mỗi pod là một `GameServer` của Agones giữ đúng một phòng. Image được build từ `deploy/Dockerfile.server`.

## Bản đồ file

| File | Việc làm |
|---|---|
| `Program.cs` | khởi động, vòng lặp chính, tín hiệu, giữ phòng, metrics |
| `PodConfig.cs` | cấu hình: tham số dòng lệnh, rồi biến môi trường, rồi mặc định |
| `PodTransport.cs` | listener UDP mã hoá, chỉ dựng sau khi directory cấp khoá ticket |
| `PodAuthenticator.cs` | nhớ peer id nào là người chơi tên gì |
| `PlayerEntity.cs` | bản console của prefab `Player`, cùng các behaviour theo cùng thứ tự |
| `AgonesLifecycle.cs` | nói chuyện với sidecar Agones: `Ready`, `Allocated`, `Health`, `Shutdown` |
| `BackendReporter.cs` | gửi số người trong phòng và điểm về backend |

Mã mô phỏng không nằm ở đây. `Player` và `Course` ở `MyGame/Assets/Network/` và được biên dịch vào cả client Unity lẫn server này, nên hai bên chạy cùng luật di chuyển và nhảy.

## Trình tự khởi động

1. Đọc cấu hình ([biến môi trường và cờ](../reference.md#5-biến-môi-trường-và-tham-số)).
2. Kết nối tới sidecar Agones. Ngoài Kubernetes bước này bị bỏ qua và mọi lệnh Agones không làm gì (`[Agones] disabled (local dev no-op)`).
3. Đăng ký với directory bằng khoá `pod`, gửi tên pod, địa chỉ, cổng, phiên bản và vùng. Directory trả về khoá ticket.
4. Mở listener UDP mã hoá ở cổng pod (7778 trong cụm). Nếu khoá ticket đổi về sau, listener khởi động lại với khoá mới.
5. Giữ một phòng. Trong cụm `FOMOXA_CLAIM_ONE=true`, nên pod thử lần lượt các phòng trong `FOMOXA_ROOM` (`arena:1` đến `arena:16`) cho tới khi directory cấp một phòng. Phòng đã có pod khác giữ thì lỗi `FAILED_PRECONDITION`, pod thử phòng kế, rồi đợi 2 giây và thử lại.
6. Chạy các vòng lặp: tick mô phỏng, reporter gửi backend, endpoint metrics và nhịp health của Agones.

Các dòng log cần tìm là `registered`, `listening on udp`, `claimed room from pool`, và `holding room ... (state stays Ready until first player)`.

## Vào phòng và mô phỏng

Client đến với connect token mà backend lấy từ directory. Token mang tên người chơi như dữ liệu ứng dụng. `PodAuthenticator` chấp nhận mọi kết nối mà transport đã xác minh token, và ghi tên theo peer id, nhờ đó điểm được báo theo tên.

Mỗi kết nối tạo một `PlayerEntity`. Behaviour `Player` của nó đọc `MoveInput` của client và đẩy vị trí bằng `Course.Simulate`. Server có thẩm quyền: client dự đoán, server quyết định, và transform được đồng bộ ngược lại. Bục cao nhất người chơi từng đứng lên là `Steps` của họ.

## Trạng thái Agones

| Sự kiện | Đổi trạng thái |
|---|---|
| Pod giữ phòng, chưa ai vào | giữ `Ready` |
| Người đầu tiên kết nối | `Allocated` |
| Người cuối cùng rời | bộ đếm chờ 30 giây bắt đầu, rồi pod tắt |
| Có người quay lại trong thời gian chờ | bộ đếm bị huỷ |
| Sau `Shutdown` | Fleet tạo GameServer `Ready` mới thay thế |

Cách này giữ cho pod không ở trạng thái `Ready` mà vẫn giữ phòng của một phiên cũ. Khi chạy, pod gửi nhịp health cho Agones mỗi 2 giây.

## Drain

Với `SIGTERM` hoặc `SIGINT` lần đầu, pod xin directory drain: directory ngừng gán người chơi hoặc phòng mới cho nó. Pod đợi người chơi rời đi và thoát khi không còn ai. Tín hiệu lần hai, hoặc pod chưa từng đăng ký, thoát ngay. Nếu `drain_seconds` (300) hết trước, directory ép dừng và pod tự gọi `Shutdown`. Kubernetes cho 360 giây trước `SIGKILL`. Dòng thời gian có ở [drain và tài khoản](../operations/02-drain-and-accounts.md).

## Báo cáo và số liệu

| Gì | Chu kỳ | Đi đâu |
|---|---|---|
| Số người trong phòng (`ReportRoom`) | khoảng 2 giây | backend, cổng 9602 |
| Điểm (`ReportScores`) | khoảng 2 giây | backend, cổng 9602 |
| Ảnh chụp điểm lấy từ các entity | mỗi giây | reporter đọc |
| Metrics | khi bị scrape | HTTP ở `FOMOXA_METRICS` (9611 trong cụm) |

Reporter làm hết sức nhưng không bắt buộc. Nếu backend sập nó ghi log lỗi và trò chơi vẫn tiếp tục.

Metric của pod dùng tiền tố `fomoxa_pod_`.

| Metric | Ý nghĩa |
|---|---|
| `players` | số peer đang kết nối vào pod |
| `rooms` | số phòng pod giữ |
| `draining` | 1 khi đang drain |
| `registered` | 1 khi đã đăng ký với directory |
| `best_steps` | số bậc cao nhất trong những người đang ở pod |
| `uptime_seconds` | số giây từ lúc khởi động |

## Chạy tay

```bash
dotnet run --project MyGame/Server~ -- --directory=127.0.0.1:9500 --key=deploy/secrets/directory/pod --room=arena:1 --port=7778 --behind-gateway=false --trace
```

`--trace` in vị trí từng người chơi mỗi hai giây. Cụm giả tự khởi động nhiều pod cho bạn (`--pods=4`), nên hiếm khi cần chạy trực tiếp. Danh sách cờ đầy đủ ở [tham khảo](../reference.md#5-biến-môi-trường-và-tham-số), và [chạy nhanh](../getting-started/quickstart-mock-cluster.md) chỉ cách dùng cụm giả.
