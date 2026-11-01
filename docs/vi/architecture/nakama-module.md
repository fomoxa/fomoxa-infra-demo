# Module Nakama

Lên đầu tài liệu: [trang chủ tài liệu](../README.md) · Trước: [backend.md](backend.md) · Tiếp: [game-server.md](game-server.md)

Toàn bộ phía Nakama là một file Lua, `deploy/nakama/modules/main.lua`. Nakama nạp nó như một script và file đăng ký tám RPC. Nó làm hai việc: ghép người chơi vào một phòng rồi phát ticket, và đóng vai kho key-value mà backend dùng cho tài khoản, hồ sơ và bạn bè.

File tồn tại ở hai nơi phải luôn giống hệt: chính file đó, được docker compose mount, và một bản chép trong ConfigMap `nakama-modules` ở `deploy/k8s/nakama.yaml`. CI so sánh chúng (xem [kiểm thử](../testing.md#5-kiểm-tra-tĩnh-mã-sinh-yaml-shell-lua-cấu-hình-giám-sát)).

## Các RPC

| RPC | Ai gọi | Xác thực | Mục đích |
|---|---|---|---|
| `find_match` | backend, thay mặt người chơi | Bearer token của người chơi | vào hàng chờ của một phòng, hoặc ghép với người đang chờ |
| `poll_match` | backend | token của người chơi | đọc kết quả của cùng hàng chờ |
| `verify_ticket` | backend | `http_key` | kiểm tra ticket hợp lệ, chưa hết hạn, và thuộc người chơi và phòng này |
| `reset_match` | backend, script test | `http_key` | xoá một hàng chờ |
| `list_rooms` | backend | `http_key` | nhóm phòng cùng trạng thái của từng phòng |
| `kv_get`, `kv_put` | backend | `http_key` | kho key-value cho tài khoản |
| `health` | bất kỳ ai | không | trả `{ ok, now }` |

RPC chỉ dành cho backend kiểm tra `ctx.user_id`. Gọi bằng `http_key` thì không có user id và được qua. Người chơi đã đăng nhập có user id nên bị từ chối với "chi backend duoc goi RPC nay" (mã 7, permission denied). Vì vậy người chơi không gọi được `kv_get` để đọc tài khoản người khác. [Test Nakama bằng curl](../deploy/07-test-nakama.md) kiểm tra điều này.

## Các collection trong storage

Mọi bản ghi được ghi dưới user hệ thống `00000000-0000-0000-0000-000000000000`.

| Collection | Key | Chứa | Thời gian sống |
|---|---|---|---|
| `match_queue` | `pool:auto` hoặc key phòng | người đang chờ và lúc bắt đầu chờ | 30 giây |
| `match_result` | `<hàng chờ>:<user>`, `<phòng>:<user>`, `user:<user>` | kết quả `matched` kèm phòng, ticket và hạn | đến khi ticket hết hạn |
| `match_ticket` | id của ticket | phòng, hai user, hạn | 120 giây |
| `room_stat` | key phòng | hai user trong phòng và hạn | 120 giây |
| `account`, `profile`, `social` | tên người chơi viết thường | chuỗi JSON do backend ghi (`{ v = ... }`) | vĩnh viễn |

## Ghép cặp hoạt động thế nào

`find_match` nhận `{ room, pool }`. Phòng là `auto`, `pool` hoặc rỗng nghĩa là "phòng nào cũng được". Khi đó key hàng chờ là `pool:auto`, còn lại là key phòng.

1. Xoá kết quả cũ đã xong của user này trong hàng chờ, để `poll_match` không trả về trận trước đó.
2. Đọc bản ghi hàng chờ. Nếu một người khác đang chờ và bắt đầu chưa quá 30 giây, ghép với họ. Nếu chính người đó xin lại, trả `waiting`.
3. Để ghép, làm rỗng bản ghi hàng chờ bằng version của bản ghi, nên chỉ một request thắng. Rồi chọn phòng: phòng được yêu cầu, hoặc với `auto` là phòng kế tiếp trong `pool` (bộ đếm quay vòng ở `pool:counter`). Ghi ticket, trạng thái phòng và kết quả cho cả hai người.
4. Nếu không ai chờ, ghi người này vào hàng chờ và trả `waiting`.
5. Cả hai lần ghi dùng version của bản ghi vừa đọc. Nếu request khác đã đổi nó giữa chừng, lần ghi thất bại và vòng lặp thử lại, tối đa năm lần, rồi RPC trả "match_queue ban, thu lai" (mã 14, unavailable).

Khi hàng chờ chưa có bản ghi, version là `"*"`, nghĩa là chỉ ghi khi key chưa tồn tại. Version rỗng sẽ bỏ qua kiểm tra, và hai người xin cùng lúc sẽ ghi đè nhau rồi không bao giờ được ghép.

`poll_match` chỉ đọc kết quả của cùng hàng chờ, nên người vừa ghép xong ở một phòng rồi xin phòng khác sẽ không nhận kết quả cũ. Kết quả hết hạn bị xoá và câu trả lời quay về `waiting`.

## Nhóm phòng

`ROOM_POOL` gồm `arena:1` đến `arena:4` và là phương án dự phòng. Backend thường gửi `pool`: các phòng mà game server đã báo là tồn tại và chưa đầy. Khi đó Nakama chỉ chọn trong các phòng có pod giữ, nên một cặp ghép không bao giờ trỏ tới phòng không có server.

Mỗi phòng có sức chứa 2 trong module, vì một ticket mang đúng hai user. Phòng có tên như `arena:1` ghép hai người kế tiếp xin phòng đó.

## `list_rooms`

Trả về `auto` cùng từng phòng trong nhóm với `players` (0, 1 hoặc 2) và `state` là `open`, `waiting` hoặc `full`. Các số này đếm người đang chờ hoặc đã ghép, không phải kết nối thật. Backend thay chúng bằng số liệu mà pod báo.

## RPC key-value

`kv_get { collection, key }` trả `{ found, value, version }`. `kv_put { collection, key, value, version }` trả `{ ok }`. Version theo cùng luật với `KeyValue.cs` của backend: chuỗi rỗng thì ghi đè, `"*"` chỉ ghi khi chưa có, còn lại chỉ ghi khi khớp.

Nakama lưu `value` như một chuỗi bên trong `{ v = value }`, nên định dạng JSON của tài khoản, hồ sơ và bạn bè do backend quyết định.

## Sửa module

1. Sửa `deploy/nakama/modules/main.lua`.
2. Chép đoạn mới vào `data.main.lua` của ConfigMap `nakama-modules` (thụt vào bốn dấu cách).
3. Chạy kiểm tra: `./scripts/test-nakama.sh` và `./scripts/test-nakama-pool.sh` với `ALLOW_COMPOSE=1` trên docker compose.
4. Trong cụm: `kubectl apply -f deploy/k8s/nakama.yaml`, rồi `kubectl rollout restart deployment/nakama -n fomoxa`.
