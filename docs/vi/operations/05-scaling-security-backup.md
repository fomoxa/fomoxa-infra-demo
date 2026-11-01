# 05.5 Autoscaling, NetworkPolicy, sao lưu, địa chỉ công khai

Lên đầu tài liệu: [05-VAN-HANH](README.md) · Trước: [04-giam-sat.md](04-monitoring.md)

## Autoscaling

Mục tiêu: tự thêm/bớt game server theo số phòng trống.

```bash
kubectl apply -f deploy/k8s/fleet-autoscaler.yaml
kubectl get fleetautoscaler -n fomoxa
```

Hoặc chạy gọn: `AUTOSCALE=1 ./scripts/k8s-up.sh`.

Ý nghĩa: `FleetAutoscaler` kiểu Buffer: luôn giữ 2 pod `Ready` (phòng trống); số pod tổng nằm trong [4, 16]; kiểm tra mỗi 15 giây.
Khi người chơi làm pod chuyển `Allocated`, số `Ready` tụt dưới 2 → Agones thêm pod. Mỗi pod giữ một trong 16 phòng nên 16 là trần hợp lý.
"Tự động ghép trận" chỉ chọn trong các phòng có pod (backend gửi danh sách phòng thật cho Nakama).

Kết quả mong đợi (mẫu):

```
NAME                       STRATEGY   BUFFER   MIN   MAX   REPLICAS (mẫu)
fomoxa-server-autoscaler   Buffer     2        4     16    4
```

Quan sát khi có người vào: `watch -n 2 kubectl get fleet,gs -n fomoxa`, `ALLOCATED` tăng thì `DESIRED` tăng theo để còn 2 `Ready`.

Tắt: `kubectl delete -f deploy/k8s/fleet-autoscaler.yaml` (Fleet giữ nguyên số pod hiện tại, sau đó `kubectl scale` tay được).

Đừng sửa `replicas` trong `fleet.yaml` rồi `kubectl apply` lại khi autoscaler đang chạy, sẽ tranh quyền điều khiển.

## NetworkPolicy (tuỳ chọn)

Mục tiêu: mặc định chặn mọi luồng vào trong namespace `fomoxa`, chỉ mở các luồng cần thiết (client→gateway/backend, backend→directory/nakama, nakama→postgres, pod→directory/backend, Prometheus→metrics). Console Nakama (7351) không mở.

```bash
kubectl apply -f deploy/k8s/networkpolicy.yaml          # hoặc: APPLY_NETPOL=1 ./scripts/k8s-up.sh
kubectl get networkpolicy -n fomoxa
```

Hoàn tác nếu có gì không nối được:

```bash
kubectl delete -f deploy/k8s/networkpolicy.yaml
```

Chưa kiểm chứng trên cụm thật. Sau khi bật, chạy lại [Bước 11-13 của tài liệu 03](../deploy/06-verify-cluster.md#bước-11-kiểm-tra-từ-phía-client) để chắc không có luồng nào bị chặn nhầm.

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

Ý nghĩa: `pg_dump` xuất toàn bộ CSDL `nakama` ra file SQL ngay trong container qua kết nối cục bộ. Giữ file sao lưu ngoài cluster (dữ liệu Postgres nằm trong volume của k3d, xoá cluster là mất).
`deploy/secrets/` cũng cần sao lưu riêng (không nằm trong git): thiếu khoá, dữ liệu cũ không giải mã/đăng nhập được.

## 14. Đổi địa chỉ công khai (`public_address`)

Chạy local, `public_address = "127.0.0.1:7777"`: máy bạn chính là nơi k3d publish cổng UDP 7777 vào gateway. Khi client ở máy khác (LAN/Internet), directory phải trả về địa chỉ mà *client* tới được:

```bash
# 1) sửa public_address trong ConfigMap directory-config
kubectl edit configmap directory-config -n fomoxa      # đổi dòng public_address = "203.0.113.42:7777"

# (hoặc sửa deploy/k8s/directory.yaml rồi: kubectl apply -f deploy/k8s/directory.yaml)

# 2) khởi động lại directory để đọc cấu hình mới
kubectl rollout restart deployment/directory -n fomoxa
kubectl rollout status  deployment/directory -n fomoxa
```

Điều kiện: UDP 7777 của địa chỉ đó phải đi được tới Service `gateway` (mở cổng ở tường lửa/router; trên cloud là IP của Service LoadBalancer). Client cũng cần `-backend:<ip>:9600` (hoặc `9643` + pin/tên miền).
Sai chỗ này, triệu chứng là: đăng nhập và xin phòng thành công nhưng kết nối UDP treo (client vẫn đòi nối `127.0.0.1:7777` trên máy của họ).

