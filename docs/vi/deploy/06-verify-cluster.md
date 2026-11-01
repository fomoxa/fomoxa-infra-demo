# 03.6 Kiểm tra toàn cụm và kiểm tra từ phía client

Lên đầu tài liệu: [03-TRIEN-KHAI-K8S](README.md) · Trước: [05-apply-manifest.md](05-apply-manifests.md) · Tiếp: [07-test-nakama.md](07-test-nakama.md)

## Bước 10. Kiểm tra toàn cụm

Mục tiêu: xác nhận mọi thành phần chạy và game server đã đăng ký với directory.

Lệnh:

```bash
kubectl get pods -n fomoxa
kubectl get svc -n fomoxa
kubectl get fleet,gs -n fomoxa
```

Kết quả mong đợi:

```
NAME                         READY   STATUS    RESTARTS   AGE
backend-7f9d8c6b5-4hq2l      1/1     Running   0          3m
directory-5c7b9d8f6-m8x2p    1/1     Running   0          4m
fomoxa-server-hl9fz-2xkpq    2/2     Running   0          2m
fomoxa-server-hl9fz-8mnrd    2/2     Running   0          2m
fomoxa-server-hl9fz-c4v7t    2/2     Running   0          2m
fomoxa-server-hl9fz-wp5xs    2/2     Running   0          2m
gateway-6d5f7b8c9-z7k3q      1/1     Running   0          4m
nakama-79c8d6b5f4-9rt6w      1/1     Running   0          5m
postgres-6d8f9c7b5-x2k4m     1/1     Running   0          6m
tls-proxy-5b8f6d7c4-j2n9v    1/1     Running   0          3m

NAME                TYPE           PORT(S)
backend             LoadBalancer   9600:3xxxx/TCP,9601:3xxxx/TCP
backend-pods        ClusterIP      9602/TCP
directory           ClusterIP      9500/TCP,9501/TCP
gateway             LoadBalancer   7777:3xxxx/UDP
nakama              LoadBalancer   7350:3xxxx/TCP
nakama-console      ClusterIP      7351/TCP
postgres            ClusterIP      5432/TCP
tls-proxy           LoadBalancer   9643:3xxxx/TCP

NAME                                 SCHEDULING   DESIRED   CURRENT   ALLOCATED   READY   AGE
fleet.agones.dev/fomoxa-server       Packed       4         4         0           4       2m

NAME                                      STATE   ADDRESS      PORT   NODE                  AGE
gameserver.agones.dev/fomoxa-server-...   Ready   10.42.0.x    7778   k3d-fomoxa-server-0   2m
```

Ý nghĩa các cột/trạng thái quan trọng:
- `READY 2/2` ở pod game server: 2 container (game + sidecar Agones) đều sẵn sàng.
- `STATE` của GameServer: `Starting` → `Ready` (đã đăng ký, chờ người chơi) → `Allocated` (đã có người chơi đầu tiên) → `Shutdown` (phòng trống 30 giây thì tự tắt, Fleet tạo pod mới).
- `ALLOCATED 0 / READY 4`: 4 phòng trống đang chờ người chơi.

Xem log xác nhận game server đã đăng ký phòng:

```bash
kubectl logs -n fomoxa -l agones.dev/role=gameserver -c fomoxa-server --tail=20 --prefix | grep -E "registered|claimed|Ready|Backend"
```

Kết quả mong đợi (khuôn dòng thật của chương trình, tên pod là mẫu):

```
[pod/fomoxa-server-hl9fz-2xkpq/fomoxa-server] [Pod] registered id=1 changed=False
[pod/fomoxa-server-hl9fz-2xkpq/fomoxa-server] [Pod] listening on udp 7778
[pod/fomoxa-server-hl9fz-2xkpq/fomoxa-server] [Pod] claimed room from pool: arena:1
[pod/fomoxa-server-hl9fz-2xkpq/fomoxa-server] [Agones] Ready
[pod/fomoxa-server-hl9fz-2xkpq/fomoxa-server] [Backend] da ket noi
```

`[Backend] da ket noi` nghĩa là pod đã nối được backend (cổng 9602) để báo số người/bảng điểm.

Xem log backend:

```bash
kubectl logs deploy/backend -n fomoxa | head -5
```

```
[Nakama] cấu hình http://nakama.fomoxa.svc.cluster.local:7350
[Metrics] http://0.0.0.0:9610/metrics
backend: players on tcp 0.0.0.0:9600, websocket 0.0.0.0:9601; pods on tcp 0.0.0.0:9602; directory at directory.fomoxa.svc.cluster.local:9500
[Nakama] self test ok <uuid>
```

Directory và gateway (chương trình Rust) in log theo định dạng riêng; dấu hiệu tốt là không có dòng lỗi lặp lại: `kubectl logs deploy/directory -n fomoxa --tail=20`, `kubectl logs deploy/gateway -n fomoxa --tail=20`.

Lỗi thường gặp: pod game server không có dòng `registered` → xem `kubectl logs ... -c fomoxa-server` có `Connection refused` tới `directory...:9500` không (directory chưa lên hoặc khoá `pod` sai giữa secret `fomoxa-pod-key` và `fomoxa-directory-keys`).

## Bước 11. Kiểm tra từ phía "client"

Mục tiêu: xác nhận máy bạn nối được các cổng mà client sẽ dùng.

Lệnh:

```bash
# TCP 9600 (backend) và 9643 (TLS) và 7350 (Nakama, chỉ để test)
for p in 9600 9643 7350; do
  timeout 3 bash -c "</dev/tcp/127.0.0.1/$p" && echo "TCP $p: mở" || echo "TCP $p: KHÔNG nối được"
done

# UDP 7777: không thể "thử kết nối" UDP; kiểm tra cổng đã được publish
docker port k3d-fomoxa-serverlb | grep 7777

# Backend tự báo sức khoẻ (cùng lệnh mà Kubernetes dùng làm probe)
kubectl exec -n fomoxa deploy/backend -- dotnet /app/health/fomoxa-rpc-health.dll 127.0.0.1:9600; echo "exit=$?"

# Nakama
curl -s -o /dev/null -w "Nakama healthcheck: HTTP %{http_code}\n" http://127.0.0.1:7350/healthcheck
```

Ý nghĩa: UDP không có "bắt tay" nên không thể kiểm tra bằng cách nối thử; ta kiểm tra cổng đã được k3d publish, còn việc gateway nhận gói tin thật sự được chứng minh ở Bước 13 (client chơi được).
Công cụ `fomoxa-rpc-health` in `SERVING` và thoát mã 0 khi backend khoẻ.

Kết quả mong đợi:

```
TCP 9600: mở
TCP 9643: mở
TCP 7350: mở
7777/udp -> 0.0.0.0:7777
SERVING
exit=0
Nakama healthcheck: HTTP 200
```

Lỗi thường gặp: `TCP 9600: KHÔNG nối được` → cluster tạo thiếu `--port` (Bước 5) hoặc Service backend chưa có `EXTERNAL-IP`: `kubectl get svc backend -n fomoxa`. Nếu client Unity ở Windows mà cluster ở WSL2: cần Docker Desktop (cổng ra `localhost` Windows); xem [07](../troubleshooting.md#client-windows-không-nối-được-cụm-trong-wsl2).

