# MockCluster - cụm giả để test client Unity

Một lệnh dựng đủ những gì client cần, không cần Docker, K8s, Nakama, Agones hay gateway:

| Thành phần | Cổng | Ghi chú |
|---|---|---|
| Backend giả | TCP `9600` | `Players.Login`, `Lobby.ListRooms`, `Lobby.JoinRoom` (cùng giao thức fRPC với backend thật) |
| Directory giả | TCP `9500` | pod đăng ký, giữ room, cấp ticket (`ConnectTokens`) |
| Pod thật (`MyGame.Server`) × 4 | UDP `7778-7781` | room `arena:1..4`, chạy `--behind-gateway=false` nên client nối thẳng pod |

## Chạy

```bash
./scripts/run-mock-cluster.sh
```

```powershell
powershell -ExecutionPolicy Bypass -File scripts\run-mock-cluster.ps1
```

Cần .NET 8 SDK. Khi thấy `Cum gia san sang`, mở client Unity (backend mặc định `127.0.0.1:9600`), đăng nhập và chọn phòng.

Tuỳ chọn (thêm sau lệnh trên): `--pods=2`, `--match-delay=3` (giả lập thời gian ghép trận, giây), `--strict-version` (bắt phiên bản client trùng pod, mặc định bỏ qua), `--players=0.0.0.0:9600`, `--directory=0.0.0.0:9500`, `--base-port=7778`, `--no-spawn` (tự chạy pod bên ngoài), `--selftest` (tự đóng vai client rồi thoát).

## Khác với cụm thật

- Đăng nhập: tên 3-24 ký tự. Có mật khẩu (≥ 8 ký tự) thì lần đầu tạo tài khoản, lần sau phải đúng; tài khoản chỉ nằm trong bộ nhớ, tắt mock là mất.
- Ghép trận tức thì: không chờ đủ 2 người như Nakama. `auto` chọn luân phiên các phòng chưa đầy.
- Số người mỗi phòng là số người chơi thật mà pod báo lên (sức chứa 16).
- Không kiểm tra key của directory/backend: **chỉ dùng trên máy local**.
- Không có drain đầy đủ, Agones, gateway hay Nakama.
