# MockCluster - cụm giả để test client Unity

Một lệnh dựng đủ những gì client cần, không cần Docker, K8s, Nakama, Agones hay gateway:

| Thành phần | Cổng | Ghi chú |
|---|---|---|
| Backend giả | TCP `9600` | cùng giao thức fRPC với backend thật: `Players.Login`, `Lobby.ListRooms/JoinRoom/Presence`, tài khoản (đăng ký / khôi phục / đổi mật khẩu), hồ sơ, bạn bè, lời mời, bảng điểm |
| Đầu cuối TLS | TCP `9643` | giống proxy TLS trước backend thật; chứng chỉ tự ký lưu ở `.mock/mock-tls.pfx`, pin SHA-256 được in khi khởi động |
| Directory giả | TCP `9500` | pod đăng ký, giữ room, cấp ticket (`ConnectTokens`) |
| Cổng pod giả | TCP `9602` | pod báo bảng điểm lên đây |
| Pod thật (`MyGame.Server`) × 4 | UDP `7778-7781` | room `arena:1..4`, chạy `--behind-gateway=false` nên client nối thẳng pod |

## Chạy

```bash
./scripts/run-mock-cluster.sh
```

```powershell
powershell -ExecutionPolicy Bypass -File scripts\run-mock-cluster.ps1
```

Cần .NET 8 SDK. Khi thấy `Cum gia san sang`, mở client Unity (mặc định `127.0.0.1:9600`), đăng ký một tài khoản và chọn phòng.
Muốn thử qua TLS: `-backend:127.0.0.1:9643 -tlspin:<PIN in ra khi khởi động>`.

Tuỳ chọn (thêm sau lệnh trên): `--pods=2`, `--match-delay=3` (giả lập thời gian ghép trận, giây), `--strict-version`, `--data=.mock/accounts.json`
(giữ tài khoản giữa các lần chạy; mặc định mất khi tắt), `--no-tls`, `--tls-listen=0.0.0.0:9643`, `--players=0.0.0.0:9600`, `--directory=0.0.0.0:9500`,
`--pods-listen=0.0.0.0:9602`, `--base-port=7778`, `--no-spawn` (tự chạy pod bên ngoài).

## Tự kiểm tra

| Lệnh | Việc |
|---|---|
| `--selftest` | đóng vai client: tài khoản, khôi phục, đổi mật khẩu, phòng, online, bạn bè, lời mời, bảng điểm + kỷ lục, TLS |
| `--bot-test` | bot chạy thật qua UDP mã hoá: xin ticket → nối vào pod → tự leo bục → kiểm bảng điểm và kỷ lục (`BOT_TRACE=1` để xem vị trí) |
| `--probe=host:port` | gọi Login (khách) + ListRooms + Presence vào một backend bất kỳ rồi thoát |
| `--selftest-target=9600,9602` | chạy `--selftest` vào backend thật đang chạy ngoài (cụm giả chỉ làm directory + pod) - xem `scripts/test-real-backend.sh` |

## Khác với cụm thật

- Ghép trận tức thì: không chờ đủ 2 người như Nakama. `auto` chọn luân phiên các phòng chưa đầy.
- Số người mỗi phòng là số người chơi thật mà pod báo lên (sức chứa 16).
- Không kiểm tra key của directory / backend: chỉ dùng trên máy local.
- Không có Agones, gateway hay Nakama. Drain chỉ có phần pod gọi `RequestDrain`.
