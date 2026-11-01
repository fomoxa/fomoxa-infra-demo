# 05 - Vận hành cụm

Các việc hằng ngày và các tính năng vận hành: xem log, scale, cập nhật, tắt an toàn (drain), tài khoản, TLS, khoá Nakama, giám sát, autoscaling, NetworkPolicy, sao lưu.
Giả định bạn đã có cụm chạy theo [03](03-TRIEN-KHAI-K8S.md) và đang đứng ở `~/fomoxa-infra-demo` với `kubectl config use-context k3d-fomoxa`.

> **Đã chạy thật khi viết tài liệu:** lệnh liên quan Nakama/RPC (`kv_get`), `openssl s_client` kiểm tra chứng chỉ, `/metrics`, `promtool`/`amtool`/`loki`/`promtail` kiểm cấu hình.
> **Chưa chạy trên cụm Kubernetes thật:** các lệnh `kubectl` bên dưới (output *(mẫu)*), manifest `tls-proxy`, `networkpolicy`, `fleet-autoscaler`, `monitoring/`.

Mục lục: [Xem trạng thái và log](#1-xem-trạng-thái-và-log) · [Truy cập dịch vụ nội bộ](#2-truy-cập-dịch-vụ-nội-bộ) · [Scale](#3-scale-fleet-thủ-công) ·
[Restart](#4-khởi-động-lại-một-dịch-vụ) · [Cập nhật image](#cập-nhật-sau-khi-build-lại-image) · [Drain](#6-tắt-game-server-an-toàn-drain) · [Tài khoản](#7-tài-khoản-và-dữ-liệu-người-chơi) ·
[TLS](#tls-giữa-client-và-backend) · [Khoá Nakama](#9-khoá-và-mật-khẩu-nakama) · [Giám sát](#giám-sát) · [Autoscaling](#autoscaling) · [NetworkPolicy](#networkpolicy-tuỳ-chọn) ·
[Sao lưu](#13-sao-lưu-và-khôi-phục-postgres) · [Đổi địa chỉ công khai](#14-đổi-địa-chỉ-công-khai-public_address)

---

## 1. Xem trạng thái và log

**Mục tiêu:** biết cụm đang ra sao và vì sao một thứ không chạy.

```bash
# Tổng quan
kubectl get pods,svc -n fomoxa
kubectl get fleet,gs -n fomoxa

# Log một dịch vụ (theo dõi trực tiếp: thêm -f)
kubectl logs deploy/backend   -n fomoxa --tail=50
kubectl logs deploy/nakama    -n fomoxa --tail=50
kubectl logs deploy/directory -n fomoxa --tail=50
kubectl logs deploy/gateway   -n fomoxa --tail=50

# Log MỌI game server cùng lúc (mỗi dòng có tiền tố tên pod)
kubectl logs -n fomoxa -l agones.dev/role=gameserver -c fomoxa-server --prefix --tail=20

# Log của lần chạy TRƯỚC khi pod bị crash
kubectl logs <tên-pod> -n fomoxa --previous

# Vì sao pod không lên? (sự kiện + trạng thái chi tiết)
kubectl describe pod <tên-pod> -n fomoxa
kubectl get events -n fomoxa --sort-by=.lastTimestamp | tail -20

# Dùng CPU/RAM (k3s có sẵn metrics-server)
kubectl top pods -n fomoxa
```

**Ý nghĩa:** `deploy/<tên>` cho `kubectl logs` chọn một pod bất kỳ của deployment đó. Game server có **2 container**, nên phải chỉ rõ `-c fomoxa-server` (container còn lại là sidecar của Agones).

**Kết quả mong đợi `kubectl top` (mẫu):**

```
NAME                        CPU(cores)   MEMORY(bytes)
backend-7f9d8c6b5-4hq2l     12m          95Mi
fomoxa-server-hl9fz-2xkpq   6m           48Mi
nakama-79c8d6b5f4-9rt6w     25m          180Mi
```

---

## 2. Truy cập dịch vụ nội bộ

Nhiều dịch vụ chỉ nằm trong cluster (`ClusterIP`). Dùng `kubectl port-forward` để mở tạm ra máy bạn (giữ terminal chạy; Ctrl+C để đóng):

```bash
# Console quản trị Nakama  -> http://127.0.0.1:7351   (user: admin)
kubectl port-forward -n fomoxa svc/nakama-console 7351:7351
kubectl get secret fomoxa-nakama -n fomoxa -o jsonpath='{.data.console-password}' | base64 -d; echo   # mật khẩu

# Postgres  -> psql
kubectl exec -it -n fomoxa deploy/postgres -- psql -U postgres -d nakama -c '\dt'

# Metrics trực tiếp của backend
kubectl port-forward -n fomoxa deploy/backend 9610:9610 &
curl -s http://127.0.0.1:9610/metrics | head -20
kill %1
```

**Kết quả `/metrics` (thật, từ backend):**

```
# HELP fomoxa_online_players Số người chơi online (báo có mặt trong 20 giây gần nhất)
# TYPE fomoxa_online_players gauge
fomoxa_online_players 2
fomoxa_logins_total{mode="login",result="ok"} 5
fomoxa_logins_total{mode="login",result="fail"} 4
fomoxa_joins_total{result="ok"} 2
fomoxa_score_reports_total 3
```

---

## 3. Scale Fleet thủ công

**Mục tiêu:** thêm/bớt game server bằng tay (khi **chưa** bật autoscaler).

```bash
kubectl scale fleet fomoxa-server -n fomoxa --replicas=6
kubectl get gs -n fomoxa                    # đợi các GameServer mới lên Ready
kubectl scale fleet fomoxa-server -n fomoxa --replicas=4
```

**Ý nghĩa:** Agones tạo/xoá GameServer cho khớp `replicas`. Khi giảm, nó **ưu tiên xoá pod `Ready` (chưa có người)**, giữ lại pod `Allocated`.
Mỗi pod giữ một phòng trong `arena:1..16` nên tối đa 16 pod hữu ích (xem `FOMOXA_ROOM` trong `deploy/k8s/fleet.yaml`).

**Lưu ý:** nếu `FleetAutoscaler` đang chạy, nó sẽ ghi đè `replicas` bạn đặt. Muốn scale tay thì tắt autoscaler trước: `kubectl delete -f deploy/k8s/fleet-autoscaler.yaml`.

---

## 4. Khởi động lại một dịch vụ

```bash
kubectl rollout restart deployment/backend   -n fomoxa
kubectl rollout restart deployment/directory -n fomoxa
kubectl rollout status  deployment/backend   -n fomoxa
```

**Ý nghĩa:** `rollout restart` thay pod lần lượt (không gián đoạn nếu có nhiều bản sao; ở đây mỗi dịch vụ 1 bản nên có khoảng gián đoạn ngắn).
**Thứ tự khi phải restart nhiều thứ:** `postgres` → `nakama` → `directory` → `backend` → `gateway`. Khi `directory` restart, các game server tự đăng ký lại.

---

## Cập nhật sau khi build lại image

**Mục tiêu:** đưa mã mới (backend, server, directory, gateway) vào cụm đang chạy.

```bash
# 1) Build lại image đã đổi (ví dụ backend và game server)
docker build -f MyGame.Backend/Dockerfile  -t fomoxa-backend:latest MyGame.Backend
docker build -f deploy/Dockerfile.server   -t fomoxa-server:latest  .

# 2) Nạp vào k3d
k3d image import fomoxa-backend:latest fomoxa-server:latest -c fomoxa

# 3) Backend / directory / gateway: khởi động lại để dùng image mới
kubectl rollout restart deployment/backend -n fomoxa

# 4) Game server (Fleet): tag không đổi (:latest) nên Agones không thấy gì thay đổi -> xoá Fleet rồi apply lại
kubectl delete fleet fomoxa-server -n fomoxa
kubectl apply -f deploy/k8s/fleet.yaml
kubectl wait --for=jsonpath='{.status.state}'=Ready gs -l agones.dev/fleet=fomoxa-server -n fomoxa --timeout=300s
```

**Vì sao bước 4 khác bước 3:** `Deployment` có lệnh `rollout restart`; `Fleet` thì chỉ tự thay pod khi *spec* đổi. Cùng tag `:latest` nghĩa là spec không đổi, nên phải xoá và tạo lại. **Người đang chơi sẽ bị ngắt** khi xoá Fleet;
để cập nhật nhẹ nhàng hơn, dùng tag phiên bản (`fomoxa-server:1.1`), sửa `image:` và `FOMOXA_VERSION` trong `fleet.yaml` rồi `kubectl apply` - Agones sẽ cuốn chiếu (rolling) thay pod.

**Quan trọng khi đổi giao thức mạng** (model/RPC): client (`MyGame.exe`), backend và game server phải build **cùng lúc** ([04, Sinh lại mã](04-CLIENT-UNITY.md#sinh-lại-mã-khi-đổi-model-hoặc-rpc)).

---

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

## TLS giữa client và backend

fRPC của backend **không tự mã hoá**, mà mật khẩu đăng nhập đi qua đường này, nên môi trường thật phải đi qua **proxy TLS** (`tls-proxy`, nginx, cổng 9643). Đường UDP game đã được mã hoá bằng ticket của Fomoxa, không cần thêm.

### 8.1. Kiểm tra chứng chỉ proxy đang trình ra

```bash
# Vân tay chứng chỉ mà server TRÌNH RA (so với "pin" trong client)
openssl s_client -connect 127.0.0.1:9643 -servername fomoxa.local </dev/null 2>/dev/null \
    | openssl x509 -noout -fingerprint -sha256 -subject
```

**Kết quả mong đợi (thật, chạy với đầu cuối TLS của cụm giả):**

```
sha256 Fingerprint=C5:B5:02:A6:03:68:15:A3:9E:6E:60:AC:21:39:86:51:BF:FB:9A:D4:FF:19:BD:D5:C6:39:7D:9F:F7:4C:51:67
subject=CN = fomoxa-mock
```

Bỏ dấu `:` là ra chính xác chuỗi `-tlspin:` của client (`C5B502A6...5167`).

### 8.2. Đổi chứng chỉ (tạo mới, nạp vào cluster, restart proxy)

```bash
./scripts/gen-tls-cert.sh -f                     # tạo lại tls.crt/tls.key và in pin MỚI
kubectl create secret tls fomoxa-tls -n fomoxa \
    --cert=deploy/secrets/tls/tls.crt --key=deploy/secrets/tls/tls.key \
    --dry-run=client -o yaml | kubectl apply -f -
kubectl rollout restart deployment/tls-proxy -n fomoxa
```

Sau đó **cập nhật pin trong mọi client** (`-tlspin:` hoặc ô *Tls Pin Sha256* của `RoomJoiner`), nếu không client sẽ từ chối với "vân tay chứng chỉ không khớp".

### 8.3. Chỉ cho phép đường mã hoá

```bash
kubectl patch svc backend -n fomoxa -p '{"spec":{"type":"ClusterIP"}}'     # đóng 9600 ra ngoài
# (hoặc chạy lần đầu: TLS_ONLY=1 ./scripts/k8s-up.sh)
```

Sau đó client **bắt buộc** dùng `-backend:127.0.0.1:9643 -tlspin:...`. Nhớ rằng cụm tạo bằng k3d vẫn map cổng 9600 vào máy (Service không còn LoadBalancer nên không có gì sau cổng đó).
Hoàn tác: `kubectl apply -f deploy/k8s/backend.yaml`.

### 8.4. Môi trường thật (có tên miền)

Dùng chứng chỉ do CA công cộng cấp (ví dụ **cert-manager** + Let's Encrypt) cho secret `fomoxa-tls`; khi đó **không cần pin** (client dùng bộ gốc Mozilla có sẵn: chỉ truyền `-tls`).
Đặt tên miền trỏ vào IP của Service `tls-proxy`. Chứng chỉ phải có đúng tên miền trong SAN.

---

## 9. Khoá và mật khẩu Nakama

Cụm K8s **không còn dùng** `defaultkey`, `defaulthttpkey`, `admin:admin`, mật khẩu DB `localdb`. Chúng nằm trong secret `fomoxa-nakama`; `nakama`, `backend` và `postgres` đọc từ đó.
(`deploy/compose.yaml` — chỉ để dev local — vẫn giữ giá trị mặc định.)

**Xem khoá hiện tại:**

```bash
for k in server-key http-key console-password; do
  printf '%-18s ' "$k"; kubectl get secret fomoxa-nakama -n fomoxa -o jsonpath="{.data.$k}" | base64 -d; echo
done
```

**Xoay vòng (đổi) khoá Nakama**, **không** gồm mật khẩu DB:

```bash
openssl rand -hex 24 | tr -d '\n' > deploy/secrets/nakama/http-key       # ví dụ đổi http-key
kubectl create secret generic fomoxa-nakama -n fomoxa \
    --from-file=server-key=deploy/secrets/nakama/server-key \
    --from-file=http-key=deploy/secrets/nakama/http-key \
    --from-file=console-password=deploy/secrets/nakama/console-password \
    --from-file=session-key=deploy/secrets/nakama/session-key \
    --from-file=refresh-key=deploy/secrets/nakama/refresh-key \
    --from-file=db-password=deploy/secrets/nakama/db-password \
    --dry-run=client -o yaml | kubectl apply -f -
kubectl rollout restart deployment/nakama deployment/backend -n fomoxa
```

**Lưu ý quan trọng:**
- Đổi `session-key`/`refresh-key` làm mọi phiên đăng nhập hiện có mất hiệu lực (người chơi đăng nhập lại).
- **Đổi `db-password` làm Nakama không khởi động được** trên volume Postgres đã có dữ liệu (mật khẩu được lưu trong chính dữ liệu Postgres). Muốn đổi, hoặc `ALTER USER postgres PASSWORD '...'` trong Postgres trước rồi mới đổi secret, hoặc chấp nhận xoá dữ liệu (`kubectl delete pvc postgres-data -n fomoxa`).
- Các script `test-nakama*.sh` tự đọc khoá từ secret. Muốn ép khoá mặc định (khi test Nakama của `docker compose`): `NAKAMA_USE_DEFAULT_KEYS=1 ./scripts/test-nakama.sh`.

---

## Giám sát

**Mục tiêu:** có số liệu (Prometheus), biểu đồ (Grafana), cảnh báo (Alertmanager) và log tập trung (Loki).

### 10.1. Cài đặt

```bash
kubectl apply -f deploy/monitoring/00-namespace.yaml
kubectl create secret generic grafana-admin -n monitoring --from-literal=password="$(openssl rand -hex 12)"   # chỉ lần đầu
kubectl apply -f deploy/monitoring/
kubectl rollout status deployment/prometheus   -n monitoring
kubectl rollout status deployment/grafana      -n monitoring
```

**Hoặc chạy gọn:** `INSTALL_MONITORING=1 ./scripts/k8s-up.sh` (làm đúng các lệnh trên).

**Ý nghĩa:** namespace `monitoring` riêng; secret chứa mật khẩu admin của Grafana (ngẫu nhiên); `apply -f deploy/monitoring/` cài Prometheus, Alertmanager, Grafana, Loki và Promtail.

**Kiểm tra:** `kubectl get pods -n monitoring` → prometheus, alertmanager, grafana, loki `Running`; một pod `promtail` mỗi node.

### 10.2. Mở giao diện

```bash
kubectl port-forward -n monitoring svc/grafana 3000:3000        # http://127.0.0.1:3000  (user: admin)
kubectl get secret grafana-admin -n monitoring -o jsonpath='{.data.password}' | base64 -d; echo
kubectl port-forward -n monitoring svc/prometheus 9090:9090     # http://127.0.0.1:9090/targets
kubectl port-forward -n monitoring svc/alertmanager 9093:9093   # http://127.0.0.1:9093
```

Trong Grafana: thư mục **Fomoxa → "Fomoxa - tổng quan"** (người chơi online, GameServer Ready, đăng nhập/giây, người trong từng pod, số bậc cao nhất...). Xem log: *Explore* → nguồn **Loki** → `{namespace="fomoxa", app="backend"}` hoặc `{pod=~"fomoxa-server-.*"}`.

Trong Prometheus, trang **Status → Targets** phải thấy các target `UP`: backend (`:9610`), các GameServer (`:9611`), nakama (`:9100`) và controller Agones.

### 10.3. Chỉ số của Fomoxa

| Metric | Loại | Nguồn | Ý nghĩa |
|---|---|---|---|
| `fomoxa_online_players` | gauge | backend | người chơi báo "còn online" trong 20 giây gần nhất |
| `fomoxa_logins_total{mode,result}` | counter | backend | đăng nhập/đăng ký/khôi phục, ok hoặc fail |
| `fomoxa_joins_total{result}` | counter | backend | lượt xin vào phòng thành công |
| `fomoxa_rooms_reported`, `fomoxa_room_players` | gauge | backend | số phòng và tổng người chơi theo báo cáo của pod |
| `fomoxa_score_reports_total`, `fomoxa_password_changes_total`, `fomoxa_friend_requests_total`, `fomoxa_room_invites_total` | counter | backend | hoạt động xã hội/điểm |
| `fomoxa_pod_players`, `fomoxa_pod_rooms`, `fomoxa_pod_registered`, `fomoxa_pod_draining`, `fomoxa_pod_best_steps`, `fomoxa_pod_uptime_seconds` | gauge | từng game server | tình trạng một pod |
| `agones_fleets_replicas_count{type}` | gauge | Agones | Fleet: `total`, `allocated`, `ready`, `desired` |

### 10.4. Cảnh báo có sẵn

| Cảnh báo | Điều kiện | Mức |
|---|---|---|
| `BackendDown` | không scrape được backend 1 phút | critical |
| `NakamaDown` | không scrape được Nakama 1 phút | critical |
| `NoReadyGameServer` | hết GameServer `Ready` 2 phút | critical |
| `GameServerNotRegistered` | pod chạy nhưng chưa đăng ký với directory 2 phút | warning |
| `HighLoginFailures` | > 1 lần đăng nhập thất bại/giây trong 5 phút (dò mật khẩu) | warning |
| `FleetAtMaxReplicas` | Fleet chạm trần 16 pod 10 phút | warning |
| `PodNearlyFull` | ≥ 14 người trên một pod 5 phút | info |

Mặc định Alertmanager **chỉ gom nhóm, chưa gửi đi đâu**. Để nhận thông báo, sửa `receivers` trong `deploy/monitoring/20-alertmanager.yaml` (đã có mẫu Slack/webhook, đang để comment), rồi:

```bash
kubectl apply -f deploy/monitoring/20-alertmanager.yaml
kubectl rollout restart deployment/alertmanager -n monitoring
```

**Kiểm tra cấu hình trước khi áp dụng** (đã chạy thật khi viết tài liệu; cần Docker):

```bash
# trích cấu hình ra file rồi kiểm bằng công cụ chính thức
python3 - <<'PY'
import yaml
d = list(yaml.safe_load_all(open("deploy/monitoring/10-prometheus.yaml")))
pick = lambda n: [x for x in d if x.get("metadata", {}).get("name") == n][0]["data"]
import os; os.makedirs("/tmp/prom/rules", exist_ok=True)
open("/tmp/prom/prometheus.yml", "w").write(pick("prometheus-config")["prometheus.yml"].replace("/etc/prometheus/rules/*.yml", "/tmp/prom/rules/*.yml"))
open("/tmp/prom/rules/fomoxa.yml", "w").write(pick("prometheus-rules")["fomoxa.yml"])
PY
docker run --rm --entrypoint promtool -v /tmp/prom:/tmp/prom prom/prometheus:v2.55.1 check config /tmp/prom/prometheus.yml
```

**Kết quả mong đợi (thật):**

```
Checking /tmp/prom/prometheus.yml
  SUCCESS: 1 rule files found
 SUCCESS: /tmp/prom/prometheus.yml is valid prometheus config file syntax
```

**Lưu ý:** Prometheus/Loki/Grafana dùng `emptyDir` - dữ liệu mất khi pod khởi động lại. Đủ cho dev/demo; production cần PVC.

---

## Autoscaling

**Mục tiêu:** tự thêm/bớt game server theo số phòng trống.

```bash
kubectl apply -f deploy/k8s/fleet-autoscaler.yaml
kubectl get fleetautoscaler -n fomoxa
```

**Hoặc chạy gọn:** `AUTOSCALE=1 ./scripts/k8s-up.sh`.

**Ý nghĩa:** `FleetAutoscaler` kiểu **Buffer**: luôn giữ **2 pod `Ready`** (phòng trống); số pod tổng nằm trong **[4, 16]**; kiểm tra mỗi 15 giây.
Khi người chơi làm pod chuyển `Allocated`, số `Ready` tụt dưới 2 → Agones thêm pod. Mỗi pod giữ một trong 16 phòng nên 16 là trần hợp lý.
"Tự động ghép trận" chỉ chọn trong các phòng **có pod** (backend gửi danh sách phòng thật cho Nakama).

**Kết quả mong đợi (mẫu):**

```
NAME                       STRATEGY   BUFFER   MIN   MAX   REPLICAS (mẫu)
fomoxa-server-autoscaler   Buffer     2        4     16    4
```

**Quan sát khi có người vào:** `watch -n 2 kubectl get fleet,gs -n fomoxa` - `ALLOCATED` tăng thì `DESIRED` tăng theo để còn 2 `Ready`.

**Tắt:** `kubectl delete -f deploy/k8s/fleet-autoscaler.yaml` (Fleet giữ nguyên số pod hiện tại, sau đó `kubectl scale` tay được).

**Đừng** sửa `replicas` trong `fleet.yaml` rồi `kubectl apply` lại khi autoscaler đang chạy - sẽ tranh quyền điều khiển.

---

## NetworkPolicy (tuỳ chọn)

**Mục tiêu:** mặc định chặn mọi luồng vào trong namespace `fomoxa`, chỉ mở các luồng cần thiết (client→gateway/backend, backend→directory/nakama, nakama→postgres, pod→directory/backend, Prometheus→metrics). Console Nakama (7351) không mở.

```bash
kubectl apply -f deploy/k8s/networkpolicy.yaml          # hoặc: APPLY_NETPOL=1 ./scripts/k8s-up.sh
kubectl get networkpolicy -n fomoxa
```

**Hoàn tác nếu có gì không nối được:**

```bash
kubectl delete -f deploy/k8s/networkpolicy.yaml
```

**Chưa kiểm chứng trên cụm thật.** Sau khi bật, chạy lại [Bước 11-13 của tài liệu 03](03-TRIEN-KHAI-K8S.md#bước-11-kiểm-tra-từ-phía-client) để chắc không có luồng nào bị chặn nhầm.

---

## 13. Sao lưu và khôi phục Postgres

Toàn bộ dữ liệu bền (tài khoản, hồ sơ, bạn bè, trạng thái Nakama) nằm trong Postgres.

```bash
# Sao lưu
mkdir -p backups
kubectl exec -n fomoxa deploy/postgres -- pg_dump -U postgres nakama > "backups/nakama-$(date +%Y%m%d-%H%M).sql"
ls -lh backups/

# Khôi phục vào một CSDL trống (CẢNH BÁO: ghi đè dữ liệu; dừng Nakama/backend trước)
kubectl scale deployment/nakama deployment/backend -n fomoxa --replicas=0
kubectl exec -i -n fomoxa deploy/postgres -- psql -U postgres -c 'DROP DATABASE IF EXISTS nakama;' -c 'CREATE DATABASE nakama;'
kubectl exec -i -n fomoxa deploy/postgres -- psql -U postgres -d nakama < backups/nakama-XXXX.sql
kubectl scale deployment/nakama deployment/backend -n fomoxa --replicas=1
```

**Ý nghĩa:** `pg_dump` xuất toàn bộ CSDL `nakama` ra file SQL ngay trong container qua kết nối cục bộ. Giữ file sao lưu **ngoài** cluster (dữ liệu Postgres nằm trong volume của k3d, xoá cluster là mất).
`deploy/secrets/` cũng cần sao lưu riêng (không nằm trong git): thiếu khoá, dữ liệu cũ không giải mã/đăng nhập được.

---

## 14. Đổi địa chỉ công khai (`public_address`)

Chạy local, `public_address = "127.0.0.1:7777"`: máy bạn chính là nơi k3d publish cổng UDP 7777 vào gateway. Khi client ở **máy khác** (LAN/Internet), directory phải trả về địa chỉ mà *client* tới được:

```bash
# 1) sửa public_address trong ConfigMap directory-config
kubectl edit configmap directory-config -n fomoxa      # đổi dòng public_address = "203.0.113.42:7777"

# (hoặc sửa deploy/k8s/directory.yaml rồi: kubectl apply -f deploy/k8s/directory.yaml)

# 2) khởi động lại directory để đọc cấu hình mới
kubectl rollout restart deployment/directory -n fomoxa
kubectl rollout status  deployment/directory -n fomoxa
```

**Điều kiện:** UDP 7777 của địa chỉ đó phải đi được tới Service `gateway` (mở cổng ở tường lửa/router; trên cloud là IP của Service LoadBalancer). Client cũng cần `-backend:<ip>:9600` (hoặc `9643` + pin/tên miền).
Sai chỗ này, triệu chứng là: đăng nhập và xin phòng thành công nhưng **kết nối UDP treo** (client vẫn đòi nối `127.0.0.1:7777` trên máy của họ).

---

## Checklist vận hành

- [ ] Biết xem log từng dịch vụ và log mọi game server cùng lúc
- [ ] Biết cập nhật image: build → `k3d image import` → `rollout restart` (backend) / xoá + apply Fleet (game server)
- [ ] Thử drain: `kubectl delete gs ...` trên pod `Allocated` thấy `xin drain`, rồi `het nguoi choi, thoat`
- [ ] Đã quyết định TLS: chứng chỉ, pin trong client, `TLS_ONLY` hay không
- [ ] Đã sao lưu `deploy/secrets/` và có lịch `pg_dump`
- [ ] Giám sát: Prometheus Targets đều `UP`; Alertmanager đã có nơi nhận thông báo
- [ ] Autoscaler bật nếu cần; hiểu trần 16 pod/16 phòng
- [ ] `public_address` đúng với địa chỉ client thấy gateway
