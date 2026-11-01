# 05.4 Giám sát

Lên đầu tài liệu: [05-VAN-HANH](README.md) · Trước: [03-tls-va-khoa.md](03-tls-and-keys.md) · Tiếp: [05-scale-bao-mat-sao-luu.md](05-scaling-security-backup.md)

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
