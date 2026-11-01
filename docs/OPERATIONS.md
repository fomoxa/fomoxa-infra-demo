# Vận hành: tài khoản, gameplay, TLS, key, giám sát, autoscaling

Tài liệu này gom các tính năng thêm sau P6. Phần "Đã kiểm chứng như thế nào" ở cuối nói rõ cái gì đã chạy thật, cái gì chưa.

## 1. Gameplay: leo bục

- 18 bục xếp xoắn ốc quanh tâm sàn, mỗi bục cao hơn bục trước 1,4 m (xem `MyGame/Assets/Network/Models/Course.cs`).
- Điều khiển: WASD / mũi tên, **Space** để nhảy. Bục là "một chiều": nhảy xuyên từ dưới lên được và đáp lên mặt trên, nhưng đi ngang từ bên cạnh thì bị chặn.
- Điểm = chỉ số bục cao nhất đã đứng lên (số bậc). Bảng điểm xếp hạng theo phòng hiện trong HUD; **kỷ lục cá nhân** (số bậc cao nhất từ trước tới nay) và **số trận** lưu trong hồ sơ.
- Server là bên tính vật lý. `Course.Simulate` dùng chung cho `Player.cs` (Unity, chế độ Host) và `PlayerEntity.cs` (server console) nên hai bên cho cùng kết quả.
- Luồng điểm: pod → `PodGate.ReportScores` (cổng 9602 của backend) → backend lưu kỷ lục → client hỏi `Lobby.Scoreboard`.

## 2. Tài khoản

| Việc | Cách làm |
|---|---|
| Đăng ký | thẻ **Đăng ký** trong sảnh: tên 3-24 ký tự (chữ, số, `_`, `-`), mật khẩu ≥ 8 ký tự. Tên không phân biệt hoa thường và chỉ đăng ký được một lần. |
| Mã khôi phục | hiện **một lần** sau khi đăng ký / khôi phục (dạng `XXXX-XXXX-XXXX-XXXX`). Server chỉ lưu bản băm. |
| Quên mật khẩu | thẻ **Quên mật khẩu**: tên + mã khôi phục + mật khẩu mới. Mã cũ hết hiệu lực, hiện mã mới. |
| Đổi mật khẩu | sảnh → thẻ **Tài khoản**. |
| Chống dò mật khẩu | 5 lần sai liên tiếp thì khoá tên đó 60 giây. |

Mật khẩu băm bằng PBKDF2-SHA256 (120.000 vòng, salt riêng). Dữ liệu (tài khoản, hồ sơ, bạn bè) nằm trong **Nakama storage** qua hai RPC Lua `kv_get` / `kv_put`
(chỉ backend gọi được bằng `http_key`; người chơi gọi sẽ bị `PERMISSION_DENIED`). Không có Nakama thì dùng bộ nhớ, hoặc file nếu đặt `Fomoxa:Data:File`.

Thư viện RPC không trả nội dung phản hồi của lệnh đăng nhập cho client, nên mã khôi phục được lấy bằng `Lobby.TakeRecoveryCode` ngay sau đăng ký (một lần, tồn tại 10 phút trong bộ nhớ backend).

## 3. Bạn bè và lời mời

- Thẻ **Bạn bè**: gửi lời mời theo tên, chấp nhận / từ chối, xoá bạn. Hiện bạn nào đang online, đang ở phòng nào, kỷ lục của bạn; bấm **Tham gia** để vào phòng của bạn.
- Trong trận, HUD có mục **Mời bạn vào phòng** (chỉ mời được bạn bè). Lời mời sống 2 phút, hiện ở thẻ Bạn bè của người nhận.

## 4. TLS giữa client và backend

fRPC của backend không tự mã hoá, mà mật khẩu đi qua đường này, nên đặt backend sau một **proxy TLS** (nginx stream, cổng `9643`).

```bash
./scripts/gen-tls-cert.sh            # tạo chứng chỉ tự ký + in vân tay SHA-256 (pin)
./scripts/k8s-up.sh                  # tạo secret fomoxa-tls, bật deployment tls-proxy, map cổng 9643
TLS_ONLY=1 ./scripts/k8s-up.sh       # đóng luôn cổng 9600 không mã hoá (chỉ còn đường TLS)
```

Client Unity: tham số dòng lệnh `-backend:127.0.0.1:9643 -tlspin:<PIN>` (hoặc bật `Use Tls` + `Tls Pin Sha256` trên `RoomJoiner`).
Chứng chỉ do CA công cộng cấp (cert-manager / Let's Encrypt) thì **bỏ pin**: client kiểm theo bộ gốc Mozilla có sẵn trong thư viện.
Cụm giả có sẵn đầu cuối TLS ở cổng 9643 (in pin khi khởi động).

> Đường UDP game (client ↔ pod) đã được mã hoá bằng ticket do thư viện Fomoxa cấp, không cần thêm.

## 5. Key Nakama

`defaultkey` / `defaulthttpkey` / `admin:admin` / mật khẩu DB `localdb` **không còn dùng trong cụm K8s**. `scripts/gen-keys.*` sinh ngẫu nhiên vào
`deploy/secrets/nakama/`, `gen-k8s-secrets.*` tạo secret `fomoxa-nakama`, và manifest nạp từ secret. Console Nakama (7351) không còn mở ra ngoài:

```bash
kubectl port-forward -n fomoxa svc/nakama-console 7351:7351
```

Các script `test-nakama*.sh|ps1` tự đọc key từ secret khi Nakama đang chạy trong k3d (`NAKAMA_USE_DEFAULT_KEYS=1` để ép key mặc định của docker compose).

> **Đổi mật khẩu DB làm hỏng volume Postgres cũ** (mật khẩu nằm trong dữ liệu): với cụm đã có dữ liệu, chạy `./scripts/k8s-down.sh` (xoá PVC) rồi dựng lại, hoặc `ALTER USER postgres PASSWORD '...'` thủ công.
> `deploy/compose.yaml` (chỉ dùng cho dev local) vẫn giữ key mặc định.

## 6. Giám sát

```bash
INSTALL_MONITORING=1 ./scripts/k8s-up.sh
kubectl port-forward -n monitoring svc/grafana 3000:3000
kubectl get secret grafana-admin -n monitoring -o jsonpath='{.data.password}' | base64 -d   # mật khẩu admin
```

| Thành phần | Việc |
|---|---|
| Backend `:9610/metrics`, pod `:9611/metrics` | `fomoxa_logins_total`, `fomoxa_joins_total`, `fomoxa_online_players`, `fomoxa_room_players`, `fomoxa_pod_players`, `fomoxa_pod_best_steps`, ... |
| Prometheus | tự tìm pod có annotation `prometheus.io/scrape` (backend, GameServer, Nakama `:9100`, controller Agones) |
| Luật cảnh báo | `BackendDown`, `NakamaDown`, `NoReadyGameServer`, `GameServerNotRegistered`, `HighLoginFailures`, `PodNearlyFull`, `FleetAtMaxReplicas` |
| Alertmanager | mặc định không gửi đi đâu; sửa `deploy/monitoring/20-alertmanager.yaml` (mẫu Slack / webhook) |
| Grafana | dashboard "Fomoxa - tổng quan" nạp sẵn; datasource Prometheus + Loki |
| Loki + Promtail | log tập trung: `{namespace="fomoxa", app="backend"}` trong Explore |

Mặc định lưu bằng `emptyDir` (mất khi pod khởi động lại): đủ cho dev / demo, production cần PVC.

## 7. Autoscaling

```bash
AUTOSCALE=1 ./scripts/k8s-up.sh      # hoặc: kubectl apply -f deploy/k8s/fleet-autoscaler.yaml
```

`FleetAutoscaler` (policy Buffer) luôn giữ **2 pod Ready**, từ **4 đến 16 pod**. Mỗi pod giữ một trong 16 phòng `arena:1..16` (fleet.yaml). Khi autoscaler đang chạy,
đừng sửa `replicas` trong `fleet.yaml` rồi apply lại. Ghép trận "auto" chỉ chọn trong các phòng thật sự có pod (backend gửi danh sách phòng kèm `find_match`).

## 8. Đã kiểm chứng như thế nào

| Phần | Cách kiểm | Kết quả |
|---|---|---|
| Tài khoản, khôi phục, đổi mật khẩu, hồ sơ, bạn bè, lời mời, bảng điểm, kỷ lục | `./scripts/run-mock-cluster.sh --selftest` (cụm giả) và `./scripts/test-real-backend.sh` (**backend thật + Nakama thật**) | đạt |
| Ticket → UDP mã hoá → pod biết tên người chơi → nhảy/đáp bục → bảng điểm → kỷ lục | `./scripts/run-mock-cluster.sh --bot-test` (bot qua UDP thật) | đạt |
| TLS (đúng pin vào được, sai pin bị từ chối) | selftest của cụm giả, dùng chính client TLS của thư viện Fomoxa | đạt |
| Module Lua Nakama (`kv_*`, `list_rooms`, ghép cặp, chặn RPC nội bộ) | `test-nakama.sh`, `test-nakama-pool.sh` với Nakama thật | đạt |
| Cấu hình Prometheus, luật cảnh báo, Alertmanager, Loki, Promtail | `promtool`, `amtool`, `loki -verify-config`, `promtail -check-syntax` | hợp lệ |
| **Chưa chạy trên cụm K8s thật**: tls-proxy (nginx), NetworkPolicy, FleetAutoscaler, các manifest giám sát, secret Nakama, tính năng drain | chỉ kiểm YAML | cần thử trên máy có k3d + Agones |
| **Chưa chạy trong Unity**: giao diện UI Toolkit mới, `Player.cs` (Unity), scene Lobby/Game, nhập liệu Space | chỉ kiểm cú pháp bằng bộ giả lập API Unity | cần mở Unity thử |
