# 05.1 Trạng thái, log, scale, restart, cập nhật image

Lên đầu tài liệu: [05-VAN-HANH](README.md) · Tiếp: [02-drain-va-tai-khoan.md](02-drain-and-accounts.md)

## 1. Xem trạng thái và log

Mục tiêu: biết cụm đang ra sao và vì sao một thứ không chạy.

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

Ý nghĩa: `deploy/<tên>` cho `kubectl logs` chọn một pod bất kỳ của deployment đó. Game server có 2 container, nên phải chỉ rõ `-c fomoxa-server` (container còn lại là sidecar của Agones).

Kết quả mong đợi `kubectl top` (mẫu):

```
NAME                        CPU(cores)   MEMORY(bytes)
backend-7f9d8c6b5-4hq2l     12m          95Mi
fomoxa-server-hl9fz-2xkpq   6m           48Mi
nakama-79c8d6b5f4-9rt6w     25m          180Mi
```

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

Kết quả `/metrics` (thật, từ backend):

```
# HELP fomoxa_online_players Số người chơi online (báo có mặt trong 20 giây gần nhất)
# TYPE fomoxa_online_players gauge
fomoxa_online_players 2
fomoxa_logins_total{mode="login",result="ok"} 5
fomoxa_logins_total{mode="login",result="fail"} 4
fomoxa_joins_total{result="ok"} 2
fomoxa_score_reports_total 3
```

## 3. Scale Fleet thủ công

Mục tiêu: thêm/bớt game server bằng tay (khi chưa bật autoscaler).

```bash
kubectl scale fleet fomoxa-server -n fomoxa --replicas=6
kubectl get gs -n fomoxa                    # đợi các GameServer mới lên Ready
kubectl scale fleet fomoxa-server -n fomoxa --replicas=4
```

Ý nghĩa: Agones tạo/xoá GameServer cho khớp `replicas`. Khi giảm, nó ưu tiên xoá pod `Ready` (chưa có người), giữ lại pod `Allocated`.
Mỗi pod giữ một phòng trong `arena:1..16` nên tối đa 16 pod hữu ích (xem `FOMOXA_ROOM` trong `deploy/k8s/fleet.yaml`).

Lưu ý: nếu `FleetAutoscaler` đang chạy, nó sẽ ghi đè `replicas` bạn đặt. Muốn scale tay thì tắt autoscaler trước: `kubectl delete -f deploy/k8s/fleet-autoscaler.yaml`.

## 4. Khởi động lại một dịch vụ

```bash
kubectl rollout restart deployment/backend   -n fomoxa
kubectl rollout restart deployment/directory -n fomoxa
kubectl rollout status  deployment/backend   -n fomoxa
```

Ý nghĩa: `rollout restart` thay pod lần lượt (không gián đoạn nếu có nhiều bản sao; ở đây mỗi dịch vụ 1 bản nên có khoảng gián đoạn ngắn).
Thứ tự khi phải restart nhiều thứ: `postgres` → `nakama` → `directory` → `backend` → `gateway`. Khi `directory` restart, các game server tự đăng ký lại.

## Cập nhật sau khi build lại image

Mục tiêu: đưa mã mới (backend, server, directory, gateway) vào cụm đang chạy.

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

Vì sao bước 4 khác bước 3: `Deployment` có lệnh `rollout restart`; `Fleet` thì chỉ tự thay pod khi *spec* đổi. Cùng tag `:latest` nghĩa là spec không đổi, nên phải xoá và tạo lại. Người đang chơi sẽ bị ngắt khi xoá Fleet;
để cập nhật nhẹ nhàng hơn, dùng tag phiên bản (`fomoxa-server:1.1`), sửa `image:` và `FOMOXA_VERSION` trong `fleet.yaml` rồi `kubectl apply`, Agones sẽ cuốn chiếu (rolling) thay pod.

Quan trọng khi đổi giao thức mạng (model/RPC): client (`MyGame.exe`), backend và game server phải build cùng lúc ([04, Sinh lại mã](../unity-client.md#sinh-lại-mã-khi-đổi-model-hoặc-rpc)).

