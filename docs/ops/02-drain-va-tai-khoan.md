# 05.2 Tắt game server an toàn (drain) và tài khoản người chơi

Lên đầu tài liệu: [05-VAN-HANH](../05-VAN-HANH.md) · Trước: [01-trang-thai-va-cap-nhat.md](01-trang-thai-va-cap-nhat.md) · Tiếp: [03-tls-va-khoa.md](03-tls-va-khoa.md)

## 6. Tắt game server an toàn (drain)

**Mục tiêu:** tắt hoặc thay một game server **mà không cắt người đang chơi**.

**Cách hoạt động (đã cài trong `MyGame/Server~/Program.cs`):** khi pod nhận `SIGTERM` (Kubernetes gửi khi xoá pod), game server gọi `RequestDrain` lên directory - directory **ngừng gán người/phòng mới** cho pod này -
rồi pod **chờ hết người chơi** mới thoát. Tối đa chờ `drain_seconds = 300` giây (directory ép dừng); Kubernetes cho pod `terminationGracePeriodSeconds: 360` giây trước khi `SIGKILL` (360 > 300 là ràng buộc bắt buộc).
Tín hiệu `SIGTERM` lần hai thì thoát ngay.

**Thử:**

```bash
# Lấy tên một GameServer đang có người chơi (STATE=Allocated)
kubectl get gs -n fomoxa
GS=<tên-gameserver>

# Xoá nó và xem log
kubectl delete gs "$GS" -n fomoxa --wait=false
kubectl logs -n fomoxa "$GS" -c fomoxa-server -f --tail=5
```

**Kết quả mong đợi (khuôn dòng thật của chương trình):**

```
[Pod] SIGTERM: xin drain, cho het nguoi choi (gui tin hieu lan nua de thoat ngay)
...(người chơi vẫn chơi bình thường)...
[Pod] drain: het nguoi choi, thoat
```

Với pod **không có người** (`Ready`), dòng cuối xuất hiện gần như ngay. Fleet tạo GameServer mới thay thế.

**Lưu ý:** cơ chế này đã được kiểm chứng ở mức tiến trình (pod thoát khi hết người), **chưa thử** đầy đủ với directory thật và `kubectl delete` trên cụm.

---

## 7. Tài khoản và dữ liệu người chơi

Tài khoản, mật khẩu (băm PBKDF2-SHA256, 120.000 vòng, salt riêng), mã khôi phục (chỉ lưu bản băm), hồ sơ, bạn bè nằm trong **Nakama storage**
(bảng `account`, `profile`, `social`), truy cập qua hai RPC nội bộ `kv_get`/`kv_put` mà chỉ backend (có `http_key`) gọi được.

**Người dùng tự làm trong game:** đăng ký, đổi mật khẩu (thẻ *Tài khoản*), quên mật khẩu (thẻ *Quên mật khẩu* + mã khôi phục), kết bạn, mời vào phòng.

**Quản trị viên kiểm tra một tài khoản có tồn tại không** (không lộ mật khẩu, nhưng trả về bản băm - coi như nhạy cảm):

```bash
BASE=http://127.0.0.1:7350
HTTP_KEY=$(kubectl get secret fomoxa-nakama -n fomoxa -o jsonpath='{.data.http-key}' | base64 -d)
HTTP_CREDS=$(printf '%s:' "$HTTP_KEY" | base64)

curl -s -X POST "$BASE/v2/rpc/kv_get?unwrap" -H "Authorization: Basic $HTTP_CREDS" \
     -H 'Content-Type: application/json' -d '{"collection":"profile","key":"ana"}'; echo
```

**Kết quả mong đợi:** `{"found":false}` nếu chưa có, hoặc `{"found":true,"value":"{\"BestSteps\":5,...}","version":"..."}`. `key` là tên **chữ thường**. Collection: `account` (băm mật khẩu - nhạy cảm), `profile` (kỷ lục, số trận), `social` (bạn bè).

**Chặn dò mật khẩu:** 5 lần sai liên tiếp → khoá tên đó 60 giây. Theo dõi bằng metric `fomoxa_logins_total{result="fail"}` và cảnh báo `HighLoginFailures`.

**Người dùng mất cả mật khẩu lẫn mã khôi phục:** hiện không có công cụ đặt lại thủ công. Cách tạm: xoá bản ghi `account` của họ qua console Nakama (mục *Storage*, bảng `account`) để họ đăng ký lại tên đó (mất tài khoản cũ; hồ sơ `profile` vẫn còn nếu bạn không xoá).

---

## Dưới nắp capô: đồng hồ của một lần drain

Các con số đều lấy từ `deploy/k8s/directory.yaml` và `fleet.yaml`:

```
t = 0      kubectl delete gs ... (hoặc rollout): Kubernetes gửi SIGTERM
t = 0      pod gọi RequestDrain: directory ngừng gán người và phòng mới cho pod
t = 0..300 pod chờ người chơi hiện tại rời đi (drain_seconds = 300)
t = 300    directory ép dừng nếu vẫn còn người
t = 360    Kubernetes gửi SIGKILL (terminationGracePeriodSeconds = 360)
```

Vì sao 360 > 300: nếu hai số bằng nhau, Kubernetes có thể giết pod đúng lúc directory còn đang đóng phòng. Khoảng đệm 60 giây cho pod thời gian báo `Shutdown` với Agones và thoát gọn.

Theo dõi trạng thái trong lúc drain ở một cửa sổ khác:

```bash
kubectl get gs -n fomoxa -w
```

Bạn thấy GameServer cũ chuyển `Allocated` → `Shutdown`, và Fleet tạo cái mới `Starting` → `Ready`. Người chơi mới trong lúc đó được gán vào pod khác, nên số `Ready` tạm giảm một.

### Khi drain không kết thúc

| Dấu hiệu | Nguyên nhân thường gặp | Cách xử lý |
|---|---|---|
| Pod `Terminating` đúng 6 phút rồi biến mất | người chơi không rời; hết hạn bị SIGKILL | bình thường; lần sau giảm `drain_seconds` hoặc báo người chơi |
| Log không có `xin drain` | process không nhận SIGTERM (chạy qua shell bọc) | kiểm tra `ENTRYPOINT` của image gọi thẳng file chạy |
| Pod xoá ngay, người chơi bị rớt | dùng `--grace-period=0` hoặc `--force` | đừng dùng cờ đó với pod có người |

## Dữ liệu tài khoản nằm ở đâu

Ba collection trong Nakama storage, khoá luôn là tên người chơi viết thường:

| Collection | Nội dung | Mức nhạy cảm |
|---|---|---|
| `account` | băm mật khẩu, salt, băm mã khôi phục (số lần sai chỉ nằm trong bộ nhớ backend, mất khi restart) | cao |
| `profile` | `BestSteps`, số trận, thống kê | thấp |
| `social` | danh sách bạn bè, lời mời | thấp |

Để thấy toàn bộ bằng giao diện, mở console Nakama:

```bash
kubectl port-forward svc/nakama-console 7351:7351 -n fomoxa
```

Rồi vào `http://127.0.0.1:7351`, tài khoản `admin`, mật khẩu lấy từ secret:

```bash
kubectl get secret fomoxa-nakama -n fomoxa -o jsonpath='{.data.console-password}' | base64 -d; echo
```

Trong console, mục *Storage* liệt kê ba collection. Chỉ đọc; đừng sửa tay dữ liệu `account`.
