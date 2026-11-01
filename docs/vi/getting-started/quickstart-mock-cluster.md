# 02 - Chạy nhanh bằng cụm giả (không cần Kubernetes)

**Cụm giả** (`tools/MockCluster`) dựng trên **một máy, không Docker, không k3d, không Agones, không Nakama** đủ những gì client Unity cần:
backend, directory và các game server thật. Dùng để: thử client, phát triển tính năng, chạy kiểm thử nhanh, hoặc xem hệ thống hoạt động trước khi dựng cụm thật.

```
 Client Unity ──TCP 9600──► Backend giả ─┐        (đăng nhập, phòng, bạn bè, bảng điểm - giao thức fRPC thật)
              ──TCP 9643──► đầu cuối TLS ─┘        (tuỳ chọn, giống proxy TLS của cụm thật)
              ──UDP 7778..7781──► Pod thật ×4      (MyGame.Server, vật lý nhảy/đáp bục thật)
                                   ▲
                       Directory giả (9500) cấp ticket; pod báo bảng điểm về cổng 9602
```

Khác cụm thật: ghép trận tức thì (không chờ đủ 2 người như Nakama), tài khoản nằm trong bộ nhớ, không có Agones/gateway, key không được kiểm tra. **Chỉ dùng trên máy local.**

> **Đã chạy thật khi viết tài liệu:** toàn bộ tài liệu này (output ghi *(thật)*), trừ phần Unity.
> **Cần:** .NET SDK 8 ([01, Bước 7](prepare-environment.md#bước-7-cài-net-sdk-8)).

---

## Bước 1. Build game server và cụm giả

**Mục tiêu:** có hai chương trình đã biên dịch: `MyGame.Server` (pod) và `MockCluster`.

**Lệnh (tay):**

```bash
cd ~/fomoxa-infra-demo

dotnet publish "MyGame/Server~/MyGame.Server.csproj" -c Release -o .mock/server
dotnet publish tools/MockCluster/MockCluster.csproj   -c Release -o .mock/cluster
```

**Ý nghĩa:**
- `dotnet publish ... -c Release -o <thư mục>` biên dịch (chế độ Release) và gom mọi file cần để chạy vào `<thư mục>`.
- Lần đầu mất vài phút vì `dotnet` tải gói NuGet (AgonesSDK, BouncyCastle).
- `.mock/` nằm trong `.gitignore`, không bị commit.

**Hoặc chạy gọn:** `./scripts/run-mock-cluster.sh` (nó chạy đúng hai lệnh `publish` trên rồi chạy luôn cụm giả ở Bước 2).

**Kết quả mong đợi (mẫu):**

```
  MyGame.Server -> /home/you/fomoxa-infra-demo/MyGame/Server~/bin/Release/net8.0/MyGame.Server.dll
  MyGame.Server -> /home/you/fomoxa-infra-demo/.mock/server/
  MockCluster -> /home/you/fomoxa-infra-demo/tools/MockCluster/bin/Release/net8.0/MockCluster.dll
  MockCluster -> /home/you/fomoxa-infra-demo/.mock/cluster/
```

**Kiểm tra:**

```bash
ls .mock/server/MyGame.Server.dll .mock/cluster/MockCluster.dll
```

**Lỗi thường gặp:**

| Triệu chứng | Cách xử lý |
|---|---|
| `dotnet: command not found` | [01, Bước 7](prepare-environment.md#bước-7-cài-net-sdk-8) |
| `error NU1301: Unable to load the service index for source https://api.nuget.org` | máy không ra được Internet/NuGet; kiểm tra mạng/proxy rồi chạy lại |
| `The type or namespace name ... could not be found` ngay sau khi `git pull` | file sinh mã lệch model; xem [04, mục "Sinh lại mã"](../unity-client.md#sinh-lại-mã-khi-đổi-model-hoặc-rpc) |

---

## Bước 2. Chạy cụm giả

**Mục tiêu:** khởi động backend (9600), directory (9500), đầu cuối TLS (9643) và 4 pod thật (UDP 7778-7781).

**Lệnh:**

```bash
dotnet .mock/cluster/MockCluster.dll --server=.mock/server/MyGame.Server.dll
```

**Ý nghĩa:** `MockCluster.dll` mở các dịch vụ giả rồi **tự khởi chạy 4 tiến trình `MyGame.Server`** (mỗi pod một cổng UDP, mỗi pod giữ một phòng `arena:N`).
`--server=` chỉ cho nó biết file pod ở đâu. Để chạy tiếp, giữ cửa sổ này mở; tắt bằng **Ctrl+C**.

**Kết quả mong đợi (thật):**

```
[Mock] directory giả  : 0.0.0.0:9500
[Mock] backend giả    : 0.0.0.0:9600  (client Unity: 127.0.0.1:9600)
[Mock] cổng pod giả   : 0.0.0.0:9602
[Mock] backend TLS    : cổng 9643  pin SHA-256 = C5B502A6036815A39E6E60AC21398651BFFB9AD4FF19BDD5C6397D9FF74C5167
[Mock]   Unity: -backend:127.0.0.1:9643 -tlspin:C5B502A6036815A39E6E60AC21398651BFFB9AD4FF19BDD5C6397D9FF74C5167
[Mock] tài khoản: trong bộ nhớ (mất khi tắt; dùng --data=file.json để giữ lại)
[pod-4] [Backend] bao so nguoi choi ve 127.0.0.1:9602
[pod-1] [Pod] registering mock-pod-1 (127.0.0.1:7778, version 1.0) at 127.0.0.1:9500
[Directory] pod #1 'mock-pod-4' 127.0.0.1:7781 v1.0 dang ky
[pod-4] [Pod] registered id=1 changed=False
[pod-4] [Pod] listening on udp 7781
[Directory] pod #1 giu room arena:1
[pod-4] [Pod] claimed room from pool: arena:1
...
[Mock] 4 pod da dang ky. Cum gia san sang. Ctrl+C de tat.
```

(Giá trị `pin SHA-256` của bạn sẽ khác - nó được sinh một lần và lưu ở `.mock/mock-tls.pfx`.)

**Kiểm tra** (mở một terminal khác):

```bash
timeout 3 bash -c '</dev/tcp/127.0.0.1/9600' && echo "9600 mo"
timeout 3 bash -c '</dev/tcp/127.0.0.1/9643' && echo "9643 mo"
timeout 3 bash -c '</dev/tcp/127.0.0.1/9500' && echo "9500 mo (directory giả)"
```

**Ý nghĩa:** `/dev/tcp/host/port` là cách của bash để thử mở kết nối TCP; nếu cổng mở thì in dòng `... mo`.

**Lỗi thường gặp:**

| Triệu chứng | Cách xử lý |
|---|---|
| `Address already in use` (9600/9500/9643/9602) | có tiến trình khác giữ cổng: `ss -ltnp \| grep -E '9600\|9500\|9643\|9602'`. Đổi cổng bằng `--players=0.0.0.0:19600` v.v. hoặc tắt tiến trình đó. Nhớ cụm k3d thật cũng map 9600/9643 - đừng chạy hai cụm cùng lúc |
| `khong thay MyGame.Server.dll` | sai đường dẫn `--server=`; kiểm tra `ls .mock/server/MyGame.Server.dll` |
| `pod chua dang ky du sau 30 giay` | xem log `[pod-N]` ngay phía trên; thường là pod chưa kết nối được `9500` |

---

## Bước 3. Cho client Unity kết nối

**Mục tiêu:** đăng ký tài khoản, xem phòng, vào phòng và nhảy lên bục trong client.

Client Unity mặc định nối `127.0.0.1:9600`, trùng với cụm giả nên **không cần tham số gì**. Chạy bản build (Windows):

```powershell
.\Builds\MyGame.exe
```

hoặc nhấn Play trong Unity Editor (mở `Assets/Scenes/Lobby.unity`). Cách build client: [04 - Client Unity](../unity-client.md).

Thao tác trong game:

1. Thẻ **Đăng ký** → nhập tên (3-24 ký tự chữ/số/`_`/`-`), mật khẩu ≥ 8 ký tự → **Tạo tài khoản**.
2. **Lưu mã khôi phục** hiện ra (chỉ hiện một lần) → **Tôi đã lưu mã**.
3. Thẻ **Phòng** → bấm `arena:1` (hoặc "Tự động ghép trận").
4. Trong trận: WASD di chuyển (theo hướng camera), **Space** nhảy, chuột phải/Q/E xoay camera, **Esc/Tab** mở menu và bảng điểm.

Muốn thử qua TLS: chạy client với `-backend:127.0.0.1:9643 -tlspin:<PIN in ra ở Bước 2>`.

Muốn giữ tài khoản giữa các lần chạy: thêm `--data=.mock/accounts.json` vào lệnh ở Bước 2.

---

## Bước 4. Tự kiểm tra bằng "client giả lập" (không cần Unity)

Cụm giả có sẵn hai bài kiểm tra tự động. Chạy khi cụm giả **đã tắt** (chúng tự dựng cụm riêng của chúng).

### 4.1. `--selftest` - tài khoản, phòng, bạn bè, bảng điểm, TLS

```bash
dotnet .mock/cluster/MockCluster.dll --server=.mock/server/MyGame.Server.dll --pods=2 --selftest
```

**Ý nghĩa:** một "client" nối vào backend giả qua giao thức fRPC thật, đăng ký/khôi phục/đổi mật khẩu, ghép 2 người vào phòng, kết bạn, mời, báo bảng điểm từ một "pod", và kết nối thử qua TLS (đúng pin vào được, sai pin bị từ chối). Thoát với mã 0 nếu mọi thứ đạt.

**Hoặc chạy gọn:** `./scripts/run-mock-cluster.sh --selftest --pods=2`

**Kết quả mong đợi (thật, đã rút gọn: bỏ bớt dòng, giá trị mã/cổng của bạn sẽ khác):**

```
[selftest] OK   Dang ky trung ten bi tu choi
[selftest] OK   Dang ky mat khau yeu bi tu choi
[selftest] OK   Sai mat khau bi tu choi
[selftest] OK   Dang ky tra ve ma khoi phuc (CXNZ-UN6J-UVGD-4JGS)
[selftest] OK   Khoi phuc bang ma dung
[selftest] OK   Ma khoi phuc cu het hieu luc
[selftest] OK   ListRooms (auto[0/2] open, arena:1[0/16] open, arena:2[0/16] open)
[selftest] OK   JoinRoom auto (2 nguoi) -> 127.0.0.1:7778 / 127.0.0.1:7778
[selftest] OK   Presence (st8822e8@arena:1)
[selftest] OK   Doi mat khau
[selftest] OK   Bang diem xep hang (st8822e8:3/3, guestx:1/0)
[selftest] OK   Ky luc luu lai la bac cao nhat (best=5, matches=1)
[selftest] OK   Thanh ban be hai chieu
[selftest] OK   Moi ban vao phong
[selftest] TAT CA DAT
[selftest] OK   Ket noi qua TLS (dung pin) va goi RPC (3 phong)
[selftest] TLS DAT
```

**Kiểm tra:** `echo $?` ngay sau lệnh → `0`. Có dòng `FAIL` hoặc `CO LOI` là có phần hỏng.

### 4.2. `--bot-test` - bot leo bục qua UDP thật

```bash
dotnet .mock/cluster/MockCluster.dll --server=.mock/server/MyGame.Server.dll --pods=2 --bot-test
```

**Ý nghĩa:** một bot đăng ký, xin ticket, **nối vào pod bằng UDP mã hoá thật**, tự đi và nhảy lên các bục theo vị trí server gửi về trong ~25 giây,
rồi kiểm tra bảng điểm và kỷ lục đã lưu. Đây là bài kiểm tra gần nhất với một người chơi thật mà không cần Unity.

**Hoặc chạy gọn:** `./scripts/run-mock-cluster.sh --bot-test --pods=2`

**Kết quả mong đợi (thật, đã rút gọn):**

```
[bot] OK   Co ticket vao arena:1 -> 127.0.0.1:7779
[bot] ket noi: Starting None
[bot] ket noi: Authenticating None
[bot] ket noi: Started None
[bot] OK   Da vao pod (client Started)
[bot] OK   Bang diem co ten bot (botjumper:18/18)
[bot] OK   Bot leo duoc it nhat 3 bac (steps=18)
[bot] OK   Ky luc ca nhan duoc luu (best=18, matches=1)
[bot] TAT CA DAT
```

Thêm biến môi trường `BOT_TRACE=1` phía trước lệnh để xem vị trí bot theo thời gian.

---

## Bảng tuỳ chọn của MockCluster

| Tuỳ chọn | Mặc định | Ý nghĩa |
|---|---|---|
| `--server=<đường dẫn>` | (bắt buộc nếu không có `--no-spawn`) | file `MyGame.Server.dll` để khởi chạy pod |
| `--pods=N` | 4 | số pod (phòng `arena:1..N`) |
| `--base-port=P` | 7778 | cổng UDP của pod đầu tiên (pod sau tăng dần) |
| `--players=host:port` | `0.0.0.0:9600` | cổng backend giả |
| `--directory=host:port` | `0.0.0.0:9500` | cổng directory giả |
| `--pods-listen=host:port` | `0.0.0.0:9602` | cổng pod báo bảng điểm |
| `--tls-listen=host:port` / `--no-tls` | `0.0.0.0:9643` | đầu cuối TLS / tắt nó |
| `--data=<file.json>` | (bộ nhớ) | lưu tài khoản ra file |
| `--match-delay=<giây>` | 0 | giả lập thời gian chờ ghép trận |
| `--strict-version` | tắt | bắt phiên bản client trùng phiên bản pod |
| `--no-spawn` | tắt | không tự chạy pod (bạn tự chạy pod bên ngoài) |
| `--selftest`, `--bot-test`, `--probe=host:port`, `--selftest-target=9600,9602` | | các chế độ kiểm thử, xem [06](../testing.md) |

---

## Checklist cuối

- [ ] Hai lệnh `dotnet publish` chạy xong, có `.mock/server/MyGame.Server.dll` và `.mock/cluster/MockCluster.dll`
- [ ] Chạy cụm giả thấy dòng `4 pod da dang ky. Cum gia san sang`
- [ ] `--selftest` kết thúc bằng `TAT CA DAT` và `TLS DAT`
- [ ] `--bot-test` kết thúc bằng `TAT CA DAT`
- [ ] (Có Unity) đăng ký → vào `arena:1` → nhảy được

Tiếp theo: [03 - Triển khai Kubernetes](../deploy/README.md) để dựng cụm thật, hoặc [04 - Client Unity](../unity-client.md).
