# 03.0 Khái niệm Kubernetes dùng trong demo này

Lên đầu tài liệu: [03-TRIEN-KHAI-K8S](README.md) · Tiếp: [01-chuan-bi-va-khoa.md](01-prepare-and-keys.md)

Phần này giải thích ngắn các khái niệm bạn sẽ gặp ở các bước sau. Mỗi khái niệm có một lệnh để tự nhìn thấy nó trong cụm sau khi dựng xong (bước 9 và 10). Nếu bạn đã quen Kubernetes, bỏ qua và sang [01](01-prepare-and-keys.md).

Quy ước: kết quả ghi là ví dụ dựng từ manifest, không phải bản chụp từ một cụm đã chạy; tên pod thật sẽ có đuôi ngẫu nhiên khác.

## Bức tranh chung

```
máy của bạn (Docker)
└── k3d: các container Docker giả làm node Kubernetes
    ├── k3d-fomoxa-serverlb   cổng ra vào: map 7777/udp, 9600, 9643, 7350 từ máy bạn vào cụm
    ├── k3d-fomoxa-server-0   node chạy control plane + pod
    └── (các pod)
        ├── namespace agones-system   bộ điều khiển Agones
        └── namespace fomoxa          postgres, nakama, directory, gateway, backend, tls-proxy, game server
```

k3d = k3s (Kubernetes gọn nhẹ) chạy bên trong Docker. Với bạn, nó cư xử như một cụm Kubernetes thật, nhưng "node" chỉ là container, nên xoá cụm là sạch máy.

## Namespace

Namespace là "ngăn kéo" để gom tài nguyên. Demo dùng hai ngăn: `fomoxa` (của bạn) và `agones-system` (do Helm cài Agones). Lệnh nào cũng cần `-n fomoxa` để chỉ đúng ngăn, hoặc `-A` để xem tất cả.

```bash
kubectl get namespaces
```

Kết quả mong đợi:

```
NAME              STATUS   AGE
default           Active   12m
kube-system       Active   12m
agones-system     Active   9m
fomoxa            Active   6m
```

Mẹo: đặt namespace mặc định cho phiên làm việc để khỏi gõ `-n fomoxa` mãi:

```bash
kubectl config set-context --current --namespace=fomoxa
```

Lệnh này sửa file `~/.kube/config`, chỉ ảnh hưởng context hiện tại. Trả lại bằng `--namespace=default`.

## Pod

Pod là đơn vị chạy nhỏ nhất: một hoặc vài container dùng chung mạng và đĩa. Pod có IP riêng trong cụm, mất là mất IP (IP đổi khi pod được tạo lại), nên không ai gọi thẳng IP pod, mà gọi qua Service.

Pod game server của Agones có 2 container (`2/2`): container game `fomoxa-server` và sidecar `agones-gameserver-sidecar` lo kiểm tra sức khoẻ và báo trạng thái với Agones.

```bash
kubectl get pods -n fomoxa -o wide
```

Cột `READY` dạng `1/1` nghĩa là mọi container trong pod đã sẵn sàng; `RESTARTS` tăng liên tục là dấu hiệu container chết rồi khởi động lại (xem log bằng `kubectl logs ... --previous`).

Các trạng thái hay gặp của cột `STATUS`:

| STATUS | Nghĩa | Việc cần làm |
|---|---|---|
| `Pending` | chưa xếp được lên node (thiếu tài nguyên, chưa có ổ đĩa) | `kubectl describe pod`, đọc mục Events |
| `ContainerCreating` | đang kéo image hoặc gắn volume/secret | đợi; nếu quá vài phút thì xem Events |
| `Running` | đang chạy | kiểm tra cột READY |
| `ImagePullBackOff` / `ErrImagePull` | không kéo được image | với image `fomoxa-*:latest` là quên `k3d image import` (bước 7) |
| `CrashLoopBackOff` | khởi động rồi chết liên tục | `kubectl logs <pod> --previous` |
| `Completed` | chạy xong và thoát bình thường | bình thường với job; lạ với dịch vụ |
| `Terminating` | đang tắt (có thể đợi `terminationGracePeriodSeconds`) | game server chờ tối đa 360 giây để drain |

## Deployment và ReplicaSet

Deployment khai báo "tôi muốn N bản sao của pod này". Bên dưới nó tạo ReplicaSet, ReplicaSet tạo pod và tạo lại khi pod chết. Khi bạn đổi image hoặc cấu hình, Deployment thay pod theo kiểu cuốn chiếu (rolling update): bật pod mới, đợi sẵn sàng, rồi tắt pod cũ.

Demo dùng Deployment cho `postgres`, `nakama`, `directory`, `gateway`, `backend`, `tls-proxy`, mỗi cái `replicas: 1`.

```bash
kubectl get deployments,replicasets -n fomoxa
```

Tên pod có dạng `<deployment>-<hash replicaset>-<hash pod>`, ví dụ `backend-7c9d8b6f5-abcde`. Hash giữa đổi mỗi khi template pod đổi. Vì thế `kubectl rollout restart` tạo ReplicaSet mới và bạn thấy tên pod khác.

Vì sao `replicas: 1` hết? Postgres cần một ổ đĩa `ReadWriteOnce`; directory giữ trạng thái trong bộ nhớ; backend giữ danh sách online trong bộ nhớ. Nhân bản chúng khi chưa thiết kế lại sẽ cho dữ liệu lệch nhau, nên demo giữ một bản.

## Service

Service là địa chỉ ổn định đặt trước một nhóm pod (chọn theo nhãn `selector`). Trong cụm, DNS dạng:

```
<tên-service>.<namespace>.svc.cluster.local
```

Ví dụ backend gọi Nakama bằng `http://nakama.fomoxa.svc.cluster.local:7350`. Cùng namespace thì gọi tắt `nakama:7350` cũng được.

| Loại | Truy cập từ đâu | Dùng ở demo |
|---|---|---|
| `ClusterIP` (mặc định) | chỉ trong cụm | `postgres`, `directory`, `backend-pods`, `nakama-console` |
| `LoadBalancer` | từ ngoài cụm; k3d gắn vào cổng của máy bạn | `gateway` (UDP 7777), `backend` (TCP 9600, 9601), `tls-proxy` (9643), `nakama` (7350) |

Lý do cổng 9602 (pod báo cáo về backend) nằm ở Service `backend-pods` kiểu `ClusterIP` riêng: nó không được lộ ra ngoài, chỉ pod trong cụm gọi được.

```bash
kubectl get svc -n fomoxa
```

Kết quả mong đợi:

```
NAME             TYPE           CLUSTER-IP     EXTERNAL-IP   PORT(S)
backend          LoadBalancer   10.43.12.7     172.18.0.2    9600:30960/TCP,9601:31001/TCP
backend-pods     ClusterIP      10.43.88.20    <none>        9602/TCP
directory        ClusterIP      10.43.5.61     <none>        9500/TCP,9501/TCP
gateway          LoadBalancer   10.43.201.9    172.18.0.2    7777:30777/UDP
nakama           LoadBalancer   10.43.77.3     172.18.0.2    7350:30735/TCP
nakama-console   ClusterIP      10.43.140.2    <none>        7351/TCP
postgres         ClusterIP      10.43.31.4     <none>        5432/TCP
tls-proxy        LoadBalancer   10.43.64.18    172.18.0.2    9643:30643/TCP
```

Xem pod nào đứng sau một Service (rỗng nghĩa là selector sai hoặc pod chưa sẵn sàng):

```bash
kubectl get endpoints backend -n fomoxa
```

## ConfigMap

ConfigMap lưu cấu hình dạng chữ không bí mật: `directory.toml`, `gateway.toml`, `nginx.conf`, `main.lua`. Nó được gắn vào pod như một thư mục chứa file.

Điểm hay quên: sửa ConfigMap không làm pod tự đọc lại. Sau khi sửa, restart workload đang dùng nó:

```bash
kubectl rollout restart deployment/directory -n fomoxa
```

Xem nội dung ConfigMap đang chạy:

```bash
kubectl get configmap directory-config -n fomoxa -o jsonpath='{.data.directory\.toml}'
```

## Secret

Secret giống ConfigMap nhưng dành cho dữ liệu nhạy cảm: khoá, mật khẩu, chứng chỉ. Giá trị được mã hoá base64 (chỉ là cách biểu diễn, không phải mã hoá an toàn). Ai có quyền đọc secret là đọc được.

Demo có sáu secret (tạo ở [bước 8](04-secrets.md#bước-8-namespace-và-secrets)). Mỗi dịch vụ chỉ nhận secret nó cần; gateway còn bị giới hạn chỉ thấy khoá `gateway` bằng `items`.

```bash
kubectl get secrets -n fomoxa
kubectl get secret fomoxa-nakama -n fomoxa -o jsonpath='{.data.server-key}' | base64 -d; echo
```

Dòng thứ hai in giá trị thật của một khoá. Đừng dán kết quả vào chat hay commit.

## Volume, PersistentVolumeClaim

Container mất dữ liệu khi pod bị xoá. Muốn giữ (cơ sở dữ liệu), pod gắn một PersistentVolumeClaim (PVC) yêu cầu ổ đĩa. k3d tự cấp ổ bằng StorageClass `local-path` (một thư mục trên node).

```bash
kubectl get pvc -n fomoxa
```

Kết quả mong đợi:

```
NAME            STATUS   VOLUME                                     CAPACITY   ACCESS MODES   STORAGECLASS
postgres-data   Bound    pvc-0e1f...                                1Gi        RWO            local-path
```

`Bound` là đã có ổ. Xoá cụm k3d thì ổ mất theo; muốn giữ dữ liệu hãy `pg_dump` trước ([05 phần 5](../operations/05-scaling-security-backup.md)).

## Probe: readiness và liveness

- readinessProbe: "pod đã sẵn sàng nhận việc chưa?". Chưa sẵn sàng thì Service không gửi lưu lượng vào, và rollout đợi.
- livenessProbe: "pod còn sống không?". Thất bại nhiều lần thì kubelet khởi động lại container.

Demo dùng ba kiểu: `exec` (chạy lệnh, như `pg_isready`), `tcpSocket` (mở được cổng là xong), và probe riêng của Agones cho game server.

Khi một pod mãi không `Ready`, đọc mục Events:

```bash
kubectl describe pod -l app=backend -n fomoxa | sed -n '/Events:/,$p'
```

## Agones: GameServer, Fleet, FleetAutoscaler

Agones thêm vào Kubernetes ba loại tài nguyên cho game server:

| Loại | Vai trò | Tương tự |
|---|---|---|
| `GameServer` | một pod game server có vòng đời riêng | một pod, nhưng có trạng thái game |
| `Fleet` | nhóm GameServer, giữ đủ số bản | như Deployment cho game server |
| `FleetAutoscaler` | tự tăng giảm số bản của Fleet | như HorizontalPodAutoscaler |

Vòng đời một GameServer:

```
Starting → Scheduled → RequestReady → Ready → Allocated → Shutdown
```

- `Ready`: sẵn sàng nhận người chơi, chưa có ai.
- `Allocated`: đang có người chơi, Agones không tự tắt nó khi thu nhỏ Fleet.
- `Shutdown`: đang tắt. Với `terminationGracePeriodSeconds: 360`, process có 6 phút để drain.

Trong demo, process game server tự gọi Agones SDK để báo `Ready`, và `Allocated` khi có người vào phòng (xem [05 phần 2](../operations/02-drain-and-accounts.md)).

```bash
kubectl get fleet,gs -n fomoxa
kubectl get crd | grep agones
```

Dòng thứ hai liệt kê các CRD (loại tài nguyên mở rộng) mà Agones đăng ký, ví dụ `gameservers.agones.dev`, `fleets.agones.dev`.

## Các lệnh kubectl bạn sẽ dùng nhiều

| Việc | Lệnh |
|---|---|
| Xem tình trạng | `kubectl get pods -n fomoxa` |
| Xem chi tiết và lỗi | `kubectl describe pod <tên> -n fomoxa` |
| Xem log | `kubectl logs deploy/backend -n fomoxa --tail=50` |
| Log container trước khi chết | `kubectl logs <pod> -n fomoxa --previous` |
| Theo dõi log trực tiếp | `kubectl logs -f deploy/backend -n fomoxa` |
| Vào trong pod | `kubectl exec -it deploy/backend -n fomoxa -- sh` |
| Chuyển cổng ra máy bạn | `kubectl port-forward svc/nakama-console 7351:7351 -n fomoxa` |
| Xem tài nguyên đã khai báo | `kubectl get deploy backend -n fomoxa -o yaml` |
| Sự kiện gần đây | `kubectl get events -n fomoxa --sort-by=.lastTimestamp` |

Nếu chưa biết một trường YAML nghĩa là gì, hỏi chính Kubernetes:

```bash
kubectl explain deployment.spec.strategy
```

Tiếp theo: [01 Chuẩn bị và khoá](01-prepare-and-keys.md). Khi đến bước 9, mở thêm [09 Đọc manifest](manifest-walkthrough.md) để hiểu từng file YAML.
