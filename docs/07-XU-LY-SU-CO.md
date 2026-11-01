# 07 - Xử lý sự cố

English version: [en/07-TROUBLESHOOTING.md](en/07-TROUBLESHOOTING.md)

Cách dùng: tìm **triệu chứng** của bạn → đọc **nguyên nhân** → chạy **cách xử lý**. Nếu chưa biết lỗi nằm ở đâu, chạy [bộ lệnh chẩn đoán nhanh](#bộ-lệnh-chẩn-đoán-nhanh) trước.

Mục lục: [Chẩn đoán nhanh](#bộ-lệnh-chẩn-đoán-nhanh) · [Docker, k3d, kubectl](#1-docker-k3d-kubectl) · [Pod và image](#2-pod-và-image) · [Agones và game server](#3-agones-và-game-server) ·
[Nakama](#4-nakama) · [Backend và tài khoản](#5-backend-và-tài-khoản) · [Kết nối của client](#6-kết-nối-của-client) · [TLS](#7-tls) · [Giám sát, NetworkPolicy](#8-giám-sát-và-networkpolicy) · [Cụm giả và kiểm thử](#9-cụm-giả-và-kiểm-thử)

---

## Bộ lệnh chẩn đoán nhanh

```bash
kubectl config current-context                       # phải là k3d-fomoxa
kubectl get nodes
kubectl get pods -A | grep -vE 'Running|Completed'   # mọi pod KHÔNG ở trạng thái tốt (ở mọi namespace)
kubectl get pods,svc -n fomoxa
kubectl get fleet,gs -n fomoxa
kubectl get events -n fomoxa --sort-by=.lastTimestamp | tail -15
docker ps --format '{{.Names}}\t{{.Status}}\t{{.Ports}}'
docker port k3d-fomoxa-serverlb
free -h
```

**Cách đọc:** dòng đầu phải ra `k3d-fomoxa`; lệnh thứ ba nên rỗng (hoặc chỉ có pod đang khởi động); `docker port` phải có `7777/udp`, `9600`, `9643`, `7350`; `free -h` cột `available` dưới ~2 GB là nguy hiểm.

---

## 1. Docker, k3d, kubectl

### `Cannot connect to the Docker daemon`
- **Nguyên nhân:** WSL2: Docker Desktop chưa chạy hoặc chưa bật WSL Integration. Linux: dịch vụ docker chưa chạy.
- **Xử lý:** WSL2: mở Docker Desktop → *Settings → Resources → WSL Integration* → bật cho Ubuntu → *Apply & Restart*. Linux: `sudo systemctl start docker`.
- **Kiểm tra:** `docker info --format '{{.ServerVersion}}'` in được phiên bản.

### `permission denied while trying to connect to the Docker daemon socket`
- **Xử lý:** `sudo usermod -aG docker "$USER" && newgrp docker` (hoặc đăng xuất/đăng nhập lại).

### `port is already allocated` / không tạo được cluster
- **Nguyên nhân:** 7777/9600/9643/7350 đã bị giữ (compose Nakama, cụm cũ, cụm giả, ứng dụng khác).
- **Xử lý:**

```bash
docker ps --format '{{.Names}}\t{{.Ports}}' | grep -E ':(7777|9600|9643|7350)->'      # container nào giữ?
ss -ltnup | grep -E ':(7777|9600|9643|7350)\b'                                          # tiến trình nào giữ?
docker compose -f deploy/compose.yaml down        # nếu là Nakama của compose
k3d cluster delete fomoxa                         # nếu là cụm cũ
```

### `kubectl` báo sai/không thấy tài nguyên
- **Nguyên nhân:** `kubectl` đang trỏ cluster khác (rất hay gặp khi có nhiều cluster).
- **Xử lý:** `kubectl config get-contexts` rồi `kubectl config use-context k3d-fomoxa`. Mọi lệnh nên kèm `-n fomoxa` (hoặc đặt mặc định: `kubectl config set-context --current --namespace=fomoxa`).

### `k3d image import` chạy rất lâu
- Bình thường với image vài trăm MB; đợi. Kiểm tra: `docker exec k3d-fomoxa-server-0 crictl images | grep fomoxa`.

### Cluster đã tạo mà thiếu cổng (client không nối được 9600/7777)
- **Nguyên nhân:** k3d chỉ nhận `--port` lúc **tạo** cluster.
- **Xử lý:** xoá và tạo lại: `k3d cluster delete fomoxa` rồi lặp [03, Bước 5-9](03-TRIEN-KHAI-K8S.md#bước-5-tạo-cluster-k3d).

---

## 2. Pod và image

| Trạng thái pod | Nguyên nhân thường gặp | Xử lý |
|---|---|---|
| `ImagePullBackOff` / `ErrImagePull` | image `fomoxa-*:latest` chưa có trong k3d (chúng không có trên Docker Hub) | `k3d image import fomoxa-<tên>:latest -c fomoxa`, rồi `kubectl rollout restart deployment/<tên> -n fomoxa` |
| `CreateContainerConfigError` | thiếu secret hoặc thiếu khoá trong secret | `kubectl describe pod <pod> -n fomoxa` (dòng `Error:` ghi rõ secret/khoá thiếu); làm lại [03, Bước 8](03-TRIEN-KHAI-K8S.md#bước-8-namespace-và-secrets) |
| `CrashLoopBackOff` | ứng dụng thoát ngay | `kubectl logs <pod> -n fomoxa --previous` đọc lý do |
| `Pending` rất lâu | thiếu CPU/RAM trong node | `kubectl describe pod <pod> -n fomoxa` (mục *Events*: `Insufficient memory`); giảm số pod (`kubectl scale fleet ... --replicas=2`) hoặc tăng RAM Docker/WSL2 |
| `Init:Error` (nakama) | initContainer `migrate` không nối được Postgres | xem mục [Nakama](#4-nakama) |
| `Evicted` / `OOMKilled` | hết bộ nhớ, bị giới hạn `limits` | `kubectl describe pod ...` mục *Last State*; tăng RAM máy hoặc nới `limits` trong manifest |

**Sau mỗi lần build lại image:** nhớ import + restart. Với Fleet phải xoá và apply lại ([05](05-VAN-HANH.md#cập-nhật-sau-khi-build-lại-image)). Dấu hiệu quên: sửa mã xong mà hành vi không đổi.

---

## 3. Agones và game server

### `helm repo add` báo 404
- Dùng nhầm URL cũ. Đúng: `helm repo add agones https://agones.dev/chart/stable --force-update`.

### `helm install` hết giờ (`timed out waiting for the condition`)
- **Nguyên nhân:** pod Agones chưa lên (kéo image chậm), hoặc allocator tranh cổng 443 với Traefik (đã tắt bằng `agones.allocator.install=false`).
- **Xử lý:**

```bash
kubectl get pods -n agones-system
kubectl describe pod <pod> -n agones-system | tail -20
helm uninstall agones -n agones-system && helm install agones agones/agones --namespace agones-system \
    --set "agones.allocator.install=false" --set "agones.ping.install=false" --wait --timeout 10m
```

### GameServer mãi ở `Starting`, không lên `Ready`
- **Nguyên nhân:** container game không gọi được sidecar Agones hoặc không nối được directory.
- **Xử lý:**

```bash
GS=$(kubectl get gs -n fomoxa -o name | head -1 | cut -d/ -f2)
kubectl logs "$GS" -n fomoxa -c fomoxa-server --tail=30
kubectl logs "$GS" -n fomoxa -c agones-gameserver-sidecar --tail=30
```

  Tìm: `Connection refused` tới `directory...:9500` (directory chưa lên / khoá `pod` lệch giữa `fomoxa-pod-key` và `fomoxa-directory-keys`); `ReadyAsync attempt ...` (sidecar chưa sẵn sàng - đợi 30 giây).

### Game server `Ready` nhưng không bao giờ `Allocated`
- **Nguyên nhân:** chưa có người chơi nào thật sự **nối UDP vào pod**. `Allocated` chỉ xảy ra khi người chơi đầu tiên kết nối (không phải khi có người xin phòng).
- **Xử lý:** xem [client không nối được](#6-kết-nối-của-client). Log pod phải có `[Pod] peer N la '<tên>'` rồi `[Agones] first player joined -> Allocate`.

### Pod `Shutdown` sau ~30 giây khi mọi người rời
- **Không phải lỗi.** Phòng trống 30 giây thì pod tự tắt (`[Agones] room empty, shutting down`) và Fleet tạo pod mới `Ready`. Người quay lại trong 30 giây thì việc tắt bị huỷ.

### Nhiều pod nhưng chỉ có vài phòng
- Mỗi pod giữ **một** phòng trong `arena:1..16`. Quá 16 pod thì pod thừa không có phòng (không dùng được). Đừng đặt `maxReplicas` > 16 nếu không mở rộng `FOMOXA_ROOM` trong `fleet.yaml`.

---

## 4. Nakama

### `lua runtime error` / RPC trả lỗi lạ
```bash
kubectl logs deploy/nakama -n fomoxa | grep -iE 'lua|error' | tail
```
- **Nguyên nhân thường gặp:** sửa `main.lua` nhưng quên cập nhật ConfigMap trong `nakama.yaml` (hoặc ngược lại), hoặc lỗi cú pháp Lua.
- **Xử lý:** bắt lỗi lệch bằng [kiểm tra tĩnh](06-KIEM-THU.md#5-kiểm-tra-tĩnh-mã-sinh-yaml-shell-lua-cấu-hình-giám-sát); sau khi sửa `kubectl apply -f deploy/k8s/nakama.yaml && kubectl rollout restart deployment/nakama -n fomoxa`.

### Ghép trận: một người `waiting` mãi
- **Nguyên nhân:** phòng cụ thể (ví dụ `arena:1`) cần **đúng 2 người cùng xin phòng đó** trong vòng 30 giây; hoặc hai người xin hai phòng khác nhau; hoặc hàng chờ cũ chưa xoá.
- **Xử lý:** dùng "Tự động ghép trận" (`auto`); hoặc xoá hàng chờ bằng `reset_match` (cần `http_key`) - xem [03, Bước 12.2](03-TRIEN-KHAI-K8S.md#bước-12-test-nakama-bằng-curl).

### Người vừa chơi xong xin phòng khác lại bị đưa vào phòng cũ
- Đã sửa trong `main.lua` (poll không còn trả kết quả của trận cũ). Nếu còn gặp, kiểm tra Nakama đang chạy đúng bản `main.lua` mới (so ConfigMap, restart Nakama).

### `401 Unauthorized` khi gọi Nakama
- Sai `server-key`/`http-key`. Cụm K8s dùng khoá **riêng** trong secret `fomoxa-nakama`; `docker compose` dùng `defaultkey`/`defaulthttpkey`. Xem [03, Bước 12](03-TRIEN-KHAI-K8S.md#bước-12-test-nakama-bằng-curl).

### `{"code":7,...,"message":"chi backend duoc goi RPC nay"}`
- **Không phải lỗi.** Người chơi (token Bearer) không được gọi các RPC nội bộ; chỉ backend (có `http_key`) gọi được.

### Đổi mật khẩu DB làm Nakama không khởi động
- **Triệu chứng:** pod `nakama` `Init:Error`/`CrashLoopBackOff`, log `password authentication failed for user "postgres"`.
- **Nguyên nhân:** mật khẩu `postgres` được lưu **trong volume dữ liệu** ngay lần khởi tạo đầu; đổi secret `db-password` sau đó không đổi mật khẩu thật trong CSDL.
- **Xử lý (mất dữ liệu):**

```bash
kubectl delete deployment nakama postgres -n fomoxa
kubectl delete pvc postgres-data -n fomoxa
kubectl apply -f deploy/k8s/postgres.yaml -f deploy/k8s/nakama.yaml
```

  **Hoặc giữ dữ liệu:** đặt lại secret về giá trị **cũ** (file `deploy/secrets/nakama/db-password` cũ), hoặc vào Postgres đổi mật khẩu trước: `kubectl exec -n fomoxa deploy/postgres -- psql -U postgres -c "ALTER USER postgres PASSWORD '<mật-khẩu-mới>';"` rồi mới cập nhật secret.

### Console Nakama (7351) không vào được
- Console không mở ra ngoài. Dùng `kubectl port-forward -n fomoxa svc/nakama-console 7351:7351`, mật khẩu trong secret (xem [05, mục 2](05-VAN-HANH.md#2-truy-cập-dịch-vụ-nội-bộ)).

---

## 5. Backend và tài khoản

### Đăng nhập báo "kho tài khoản không trả lời" / "Nakama không trả lời"
- Backend không nói chuyện được với Nakama (tài khoản lưu trong Nakama storage).
- **Xử lý:** `kubectl get pods -n fomoxa | grep nakama`; `kubectl logs deploy/backend -n fomoxa | grep -i nakama`; kiểm `Fomoxa__Nakama__HttpKey` của backend trùng `http-key` trong secret (backend lấy từ cùng secret `fomoxa-nakama` nên chỉ lệch khi bạn sửa tay).

### "Nakama đang bật: hãy đăng nhập bằng tài khoản"
- Backend có Nakama từ chối đăng nhập chỉ bằng tên. Dùng thẻ **Đăng ký/Đăng nhập** (mật khẩu), hoặc kiểu khách `-autojoin ... -device:<id>`.

### "tên này đã có người dùng" / "sai tên hoặc mật khẩu" / "thử sai quá nhiều lần"
- Tên đã đăng ký (không phân biệt hoa thường) / sai mật khẩu / 5 lần sai liên tiếp → khoá tên đó 60 giây (đợi một phút).

### Quên mật khẩu **và** mất mã khôi phục
- Không có công cụ đặt lại thủ công. Cách tạm: xoá bản ghi `account` của tên đó trong console Nakama (*Storage*) để đăng ký lại ([05, mục 7](05-VAN-HANH.md#7-tài-khoản-và-dữ-liệu-người-chơi)).

### Mã khôi phục không hiện sau khi đăng ký
- Mã chỉ lưu tạm 10 phút trong bộ nhớ backend và client lấy **một lần** ngay sau đăng ký. Nếu client đóng ngay hoặc backend restart đúng lúc đó thì mất. Dùng đổi mật khẩu (khi còn đăng nhập) hoặc đăng ký tài khoản khác.

### Bảng điểm trống / kỷ lục không lưu
- Pod phải nối được backend ở cổng 9602 (log pod phải có `[Backend] da ket noi`). Không có → kiểm secret `fomoxa-backend-pod-key` và Service `backend-pods`: `kubectl get svc backend-pods -n fomoxa`.
- Người chơi **khách** (không tài khoản) vẫn lên bảng điểm nhưng kỷ lục không được lưu.

---

## 6. Kết nối của client

### Đăng nhập được nhưng vào phòng thì treo ở "Đang kết nối game server"
- **Nguyên nhân thường gặp nhất:** `public_address` sai. Directory trả về địa chỉ này cho client; chạy local phải là `127.0.0.1:7777`.
- **Xử lý:**

```bash
kubectl get configmap directory-config -n fomoxa -o jsonpath='{.data.directory\.toml}' | grep public_address
docker port k3d-fomoxa-serverlb | grep 7777
```

  Phải có `public_address = "127.0.0.1:7777"` và `7777/udp -> 0.0.0.0:7777`. Client ở máy khác: xem [05, mục 14](05-VAN-HANH.md#14-đổi-địa-chỉ-công-khai-public_address).

### Client Windows không nối được cụm trong WSL2
- **Triệu chứng:** TCP 9600 đăng nhập được nhưng UDP 7777 không tới.
- **Nguyên nhân:** chuyển tiếp `localhost` của WSL2 không đáng tin với UDP. **Docker Desktop** (WSL Integration) publish cổng container ra thẳng `localhost` của Windows, kể cả UDP.
- **Xử lý:** dùng Docker Desktop thay vì Docker Engine tự cài trong WSL2 ([01, Bước 3A](01-CHUAN-BI-MOI-TRUONG.md#3a-bạn-dùng-wsl2-ubuntu-trên-windows--docker-desktop)); kiểm tra từ PowerShell Windows `Test-NetConnection 127.0.0.1 -Port 9600`. Cách khác: chạy client **trong** WSL2/Linux hoặc dùng cụm giả ([02](02-CHAY-NHANH-CUM-GIA.md)).

### "Không có game server chạy phiên bản X"
- Phiên bản client (`Player Settings → Version`, mặc định `1.0`) khác `FOMOXA_VERSION` của pod (`fleet.yaml`, mặc định `1.0`). Directory chỉ ghép pod cùng phiên bản (`filter = "pod.version == client.version"`). Cụm giả mặc định bỏ qua kiểm tra này.

### Lỗi bắt tay giao thức ("handshake failed" / "schema"), bản `MyGame.exe` cũ không vào được
- Bản build client cũ lệch "vân tay giao thức" với backend/pod mới. Build lại **cả ba** cùng lúc ([04, Sinh lại mã](04-CLIENT-UNITY.md#sinh-lại-mã-khi-đổi-model-hoặc-rpc)).

### Vào được phòng nhưng không thấy người khác / không có nhân vật
- Prefab chưa đăng ký với `NetworkManager`: chạy lại **Tools → Demo → Rebuild Scene**; Console không được có `[Demo] prefab:` lỗi ([04](04-CLIENT-UNITY.md#bước-2-dựng-hai-scene-rebuild-scene)).

### Space làm thoát phòng
- HUD đang hiện và nút giữ focus. Bản hiện tại **ẩn HUD khi đang chơi** và các nút không nhận focus bàn phím; nếu vẫn gặp, build lại client từ mã mới nhất.

### Camera không đi theo nhân vật
- Console: có `[Player] spawn on client ... mine=True` không? Nếu không thì nhân vật của bạn chưa spawn (kết nối chưa vào pod). Dòng `[Camera] chưa thấy nhân vật của mình sau 8 giây` xác nhận điều đó.

---

## 7. TLS

| Triệu chứng | Nguyên nhân | Xử lý |
|---|---|---|
| "không kết nối được backend: TlsRejected (vân tay chứng chỉ ... không khớp)" | pin trong client khác chứng chỉ server đang trình | lấy pin đúng: `openssl s_client -connect 127.0.0.1:9643 </dev/null 2>/dev/null \| openssl x509 -noout -fingerprint -sha256`, bỏ dấu `:`; hoặc `./scripts/gen-tls-cert.sh` in pin của file hiện có. Chứng chỉ đã tạo lại thì phải cập nhật pin ở mọi client |
| "không kết nối được backend: Failed" khi dùng `-backend:...:9643` | proxy TLS không chạy hoặc cổng 9643 chưa được map | `kubectl get pods -n fomoxa \| grep tls-proxy`; `docker port k3d-fomoxa-serverlb \| grep 9643` (cluster tạo trước khi có cổng 9643 thì phải tạo lại) |
| `tls-proxy` `CrashLoopBackOff` | thiếu/sai secret `fomoxa-tls` | `kubectl get secret fomoxa-tls -n fomoxa`; tạo lại ([03, Bước 8](03-TRIEN-KHAI-K8S.md#bước-8-namespace-và-secrets)) |
| Muốn dùng chứng chỉ CA thật nhưng client vẫn đòi pin | để trống pin, truyền `-tls` | [05, mục 8.4](05-VAN-HANH.md#84-môi-trường-thật-có-tên-miền) |

---

## 8. Giám sát và NetworkPolicy

### Prometheus target `DOWN` hoặc không có target
- **Nguyên nhân:** pod thiếu annotation `prometheus.io/scrape: "true"`; hoặc NetworkPolicy chặn namespace `monitoring`; hoặc pod chưa mở cổng metrics.
- **Xử lý:** `kubectl get pod <pod> -n fomoxa -o jsonpath='{.metadata.annotations}'`; trong pod: `kubectl exec -n fomoxa deploy/backend -- ...` (backend: cổng 9610), game server (cổng 9611). Nếu đã bật NetworkPolicy, policy `metrics-from-monitoring` phải có (xem `deploy/k8s/networkpolicy.yaml`).

### Sau khi bật NetworkPolicy có thứ không nối được
- Gỡ ngay để xác nhận nguyên nhân: `kubectl delete -f deploy/k8s/networkpolicy.yaml`. Nếu hết lỗi thì policy thiếu một luồng; xem bảng luồng ở đầu file đó. (Chưa kiểm chứng trên cụm thật.)

### Grafana không có dữ liệu
- *Connections → Data sources → Prometheus → Save & test*; kiểm tra Prometheus `Status → Targets` trước.

---

## 9. Cụm giả và kiểm thử

| Triệu chứng | Xử lý |
|---|---|
| `Address already in use` khi chạy cụm giả | `ss -ltnp \| grep -E '9600\|9500\|9643\|9602'`; tắt cụm giả/ cụm k3d đang chạy; hoặc đổi cổng bằng `--players=`, `--directory=`... |
| `--selftest` thất bại ở "Dang ky trung ten" khi chạy vào **backend thật** | tài khoản được lưu lâu dài; selftest đã dùng tên ngẫu nhiên, nếu vẫn gặp thì chạy lại |
| `test-nakama.sh` dừng: "cổng 7350 đang trỏ tới container ... compose" | bạn đang test nhầm Nakama của compose. Tắt compose, hoặc cố ý: `ALLOW_COMPOSE=1 ./scripts/test-nakama.sh` |
| `test-p5`: "Truoc khi join da co N pod Allocated" | còn người chơi cũ; đợi pod trống 30 giây tự tắt, hoặc `kubectl delete gs --all -n fomoxa` |
| `test-p5` báo "Ky vong 2 pod Allocated nhung thuc te la 0" | client không vào được pod: xem [mục 6](#6-kết-nối-của-client); đọc `logs/p5-k8s-*.log` |
| `test-real-backend.sh`: `Connection refused` ngay đầu selftest | backend chưa kịp lên (tăng `sleep 6` trong script) hoặc cổng 9600/9500 đang bị chiếm |
| `dotnet publish` lỗi NuGet | máy không ra được `api.nuget.org`; kiểm tra mạng/proxy |
| Build image .NET báo `NU1301` | container build không ra được Internet (proxy). Cấu hình proxy cho Docker daemon |

---

## Khi vẫn chưa tìm ra

Thu thập và gửi kèm khi báo lỗi:

```bash
kubectl get pods,svc,fleet,gs -n fomoxa -o wide
kubectl describe pod <pod-lỗi> -n fomoxa | tail -40
kubectl logs <pod-lỗi> -n fomoxa --tail=100
git rev-parse --short HEAD
```

Với client: file log của Unity (`%USERPROFILE%\AppData\LocalLow\<Company>\MyGame\Player.log` trên Windows) hoặc `-logFile` bạn đã truyền.
