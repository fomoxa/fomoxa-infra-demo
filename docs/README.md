# Tài liệu Fomoxa - từ máy trắng đến game chạy được

Bộ tài liệu này hướng dẫn **từng bước, bằng lệnh bash copy-paste được**, để dựng toàn bộ hạ tầng game server nhiều người chơi
(Fomoxa + Nakama + Agones trên Kubernetes) và chạy client Unity kết nối vào. Người chưa biết Kubernetes vẫn làm theo được.

---

## 1. Hệ thống gồm những gì

```
                         MÁY CỦA BẠN (WSL2 / Ubuntu)
 ┌─────────────────────────────────────────────────────────────────────────────┐
 │   Docker                                                                    │
 │   └─ k3d  = k3s (Kubernetes nhẹ) chạy bên trong container Docker            │
 │      └─ cluster "fomoxa"                                                    │
 │         ├─ namespace agones-system   Agones (điều khiển GameServer)         │
 │         └─ namespace fomoxa                                                 │
 │            ├─ postgres      CSDL của Nakama                                 │
 │            ├─ nakama        ghép trận + kho tài khoản/hồ sơ/bạn bè          │
 │            ├─ directory     biết pod nào giữ phòng nào, cấp ticket          │
 │            ├─ gateway       cửa UDP duy nhất của game server                │
 │            ├─ backend       đăng nhập, danh sách phòng, xin vào phòng       │
 │            └─ game-server   Agones Fleet: các pod chơi thật (MyGame.Server) │
 └─────────────────────────────────────────────────────────────────────────────┘
        ▲  TCP 9600  (backend)                    ▲  UDP 7777  (gateway)
        │  đăng nhập, xem phòng, xin ticket       │  chơi game (đã mã hoá bằng ticket)
        └──────────────────────  CLIENT UNITY  ───┘
```

**Client chỉ nhìn thấy đúng hai cổng:**

| Cổng | Giao thức | Tới đâu | Dùng để |
|---|---|---|---|
| `9600` | TCP | `backend` | đăng nhập, danh sách phòng, xin vào phòng (fRPC) |
| `7777` | UDP | `gateway` | chơi game; gateway chuyển tiếp tới đúng `game-server` |

Mọi thứ còn lại (postgres, nakama, directory, game-server) **nằm trong cluster**, client không nối trực tiếp.
Ngoại lệ có chủ đích:

| Cổng | Khi nào mở | Ghi chú |
|---|---|---|
| `9643` TCP | tuỳ chọn | cùng việc với 9600 nhưng **được mã hoá TLS** (proxy `tls-proxy`). Môi trường thật nên dùng cổng này thay 9600. |
| `7350` TCP | chỉ để chạy script test | Nakama API. Client thật **không** dùng. |

### Luồng một lần vào game

```
1. Client ──TCP 9600──► backend      đăng nhập (tên + mật khẩu), lấy danh sách phòng
2. Client ──TCP 9600──► backend      "cho tôi vào phòng arena:1"
3.          backend ──► nakama        ghép đủ 2 người, nhận phòng được gán
4.          backend ──► directory     "cấp ticket vào arena:1"
5.          directory trả ticket + public_address (= địa chỉ GATEWAY, không phải địa chỉ pod)
6. Client ──UDP 7777──► gateway ──► game-server giữ arena:1     (ticket được pod kiểm tra)
```

`public_address` trong `directory.toml` là địa chỉ mà **client thấy**. Chạy local nó là `127.0.0.1:7777` (k3d đưa cổng 7777 của
máy bạn vào gateway). Đặt sai chỗ này, client sẽ cố nối vào địa chỉ nội bộ của pod và treo.

### k3d là gì?

`k3d` chạy **k3s** (một bản Kubernetes gọn) bên trong các container Docker. Cả cluster là vài container trên máy bạn, xoá đi là sạch.
Cổng của máy bạn được đưa vào cluster qua một container "load balancer" của k3d (tham số `--port ...@loadbalancer`).

---

## 2. Chọn đường đi

| Bạn muốn... | Đọc |
|---|---|
| Chỉ thử client Unity trên một máy, **không** cần Kubernetes | [02 - Chạy nhanh bằng cụm giả](02-CHAY-NHANH-CUM-GIA.md) |
| Dựng cụm Kubernetes thật (k3d + Agones) | [01](01-CHUAN-BI-MOI-TRUONG.md) rồi [03 - Triển khai K8s](03-TRIEN-KHAI-K8S.md) |
| Mở project Unity, build client, tham số dòng lệnh | [04 - Client Unity](04-CLIENT-UNITY.md) |
| Logs, scale, cập nhật, TLS, giám sát, autoscaling, sao lưu | [05 - Vận hành](05-VAN-HANH.md) |
| Chạy các bài kiểm thử, hiểu từng test kiểm cái gì | [06 - Kiểm thử](06-KIEM-THU.md) |
| Gặp lỗi | [07 - Xử lý sự cố](07-XU-LY-SU-CO.md) |
| Tra cứu cổng, secrets, biến môi trường, script làm gì | [08 - Tham khảo](08-THAM-KHAO.md) |

Thứ tự đề xuất cho người mới: **01 → 02 (thấy game chạy) → 03 (dựng cụm thật) → 04 → 05**.

---

## 3. Cách đọc tài liệu này

Mỗi bước trong các tài liệu có cùng khuôn:

> **Mục tiêu** → **Lệnh** → **Ý nghĩa** → **Kết quả mong đợi** → **Kiểm tra** → **Lỗi thường gặp**

Quy ước:

- Lệnh nằm trong khối ` ```bash ` và **copy-paste chạy được**. Chạy từ thư mục gốc repo nếu không ghi khác (`cd ~/fomoxa-infra-demo`).
- **Script chỉ là đường tắt.** Mọi script (`./scripts/xxx.sh`) đều được **bung ra thành các lệnh tay** ngay cạnh. Bạn có thể làm bằng tay để hiểu,
  hoặc chạy script cho gọn: *"Hoặc chạy gọn: `./scripts/xxx.sh`"*. Bảng đối chiếu đầy đủ ở [08](08-THAM-KHAO.md#4-script--lệnh-tay-tương-đương).
- Khối **"Kết quả mong đợi"**: có hai loại. Output ghi chú **(thật)** được chép từ lần chạy thật khi viết tài liệu; output ghi chú **(mẫu)** là minh hoạ
  định dạng, giá trị như tên pod, tuổi, hash, thời gian của bạn sẽ khác. Nếu khác ở những chỗ đó thì bình thường.
- Thuật ngữ Kubernetes giữ nguyên tiếng Anh (pod, deployment, namespace, secret, configmap, service, context...). Giải thích ngay lần đầu xuất hiện.

### Trạng thái kiểm chứng của chính tài liệu này

Để bạn biết nên tin đến đâu:

| Phần | Đã chạy thật khi viết tài liệu? |
|---|---|
| Sinh key / chứng chỉ, cụm giả, selftest, bot-test, backend thật + Nakama thật (docker compose), script test Nakama, `promtool`/`amtool` | **Có** - output đánh dấu *(thật)* |
| Cài Docker/kubectl/k3d/Helm, tạo cluster k3d, cài Agones, build 2 image Rust (directory, gateway), apply manifest lên cluster, tls-proxy, NetworkPolicy, FleetAutoscaler, monitoring trên cluster, UDP qua k3d | **Chưa** - lệnh lấy từ script/manifest của repo, output đánh dấu *(mẫu)* |
| Unity (UI, scene, camera, nhảy, build player) | **Chưa** - môi trường viết tài liệu không có Unity |

Nếu một lệnh "chưa kiểm chứng" cho kết quả khác mô tả, hãy xem [07 - Xử lý sự cố](07-XU-LY-SU-CO.md) rồi báo lại để cập nhật tài liệu.

---

## 4. Cấu trúc thư mục repo (phần cần biết)

```
fomoxa-infra-demo/
├── deploy/
│   ├── k8s/                 manifest Kubernetes (postgres, nakama, directory, gateway, backend, tls-proxy, fleet, ...)
│   ├── monitoring/          Prometheus, Alertmanager, Grafana, Loki, Promtail
│   ├── nakama/modules/      main.lua - module ghép trận + kho key-value cho Nakama
│   ├── Dockerfile.{server,directory,gateway}
│   ├── compose.yaml         Nakama + Postgres cho dev local (không cần K8s)
│   └── secrets/             (tự sinh, KHÔNG commit) key và chứng chỉ
├── MyGame/                  project Unity (client) + Server~/ (game server console .NET 8)
├── MyGame.Backend/          backend .NET (đăng nhập, phòng, tài khoản, bạn bè)
├── server-deps/             mã nguồn thư viện Fomoxa dùng để build game server
├── tools/MockCluster/       cụm giả (backend + directory + pod thật trên một máy)
├── scripts/                 script tiện ích (.sh cho Linux/WSL2, .ps1 cho Windows)
├── gateway/                 (tự clone, KHÔNG commit) mã Rust của directory + gateway
└── docs/                    tài liệu này
```
