# Fomoxa Game Server Architecture

Hạ tầng game server nhiều người chơi phân tán (Distributed Multiplayer Game Server Infrastructure) kết hợp **Fomoxa Networking**, **Nakama Matchmaking** và **Agones GameServer Orchestration** trên **Kubernetes**.

---

## Tài liệu hướng dẫn & Kế hoạch

- 📖 **[Tài liệu từng bước (docs/README.md)](docs/README.md)** (English: [docs/en/README.md](docs/en/README.md)): hướng dẫn chi tiết bằng lệnh bash copy-paste được, từ máy trắng đến game chạy: [chuẩn bị môi trường](docs/01-CHUAN-BI-MOI-TRUONG.md) → [cụm giả](docs/02-CHAY-NHANH-CUM-GIA.md) → [triển khai Kubernetes](docs/03-TRIEN-KHAI-K8S.md) → [client Unity](docs/04-CLIENT-UNITY.md) → [vận hành](docs/05-VAN-HANH.md); kèm [kiểm thử](docs/06-KIEM-THU.md), [xử lý sự cố](docs/07-XU-LY-SU-CO.md), [tham khảo](docs/08-THAM-KHAO.md).
- 📋 **[Kế hoạch phát triển (PLAN.md)](PLAN.md)**: Toàn bộ lộ trình từ P1 đến P6 (kiến trúc, quyết định kỹ thuật, DoD, và nhật ký các bước).
- 🧪 **[Cụm giả (tools/MockCluster)](tools/MockCluster/README.md)**: chạy thử cả hệ thống trên một máy, không cần K8s: `./scripts/run-mock-cluster.sh`.
- 🎮 **[Dự án Unity Game Client (MyGame/README.md)](MyGame/README.md)**: Hướng dẫn về scene Unity, cấu trúc prefab, package dependencies và chạy client.

---

## Bắt đầu nhanh (Quick Start)

### Trên Ubuntu / WSL2:
```bash
# 1. Sinh keys
./scripts/gen-keys.sh

# 2. Đóng gói images
./scripts/build-images.sh

# 3. Khởi động cụm Kubernetes + Agones
./scripts/k8s-up.sh

# 4. Kiểm thử matchmaking & GameServer
./scripts/test-nakama.sh
./scripts/test-nakama-pool.sh
./scripts/test-p5.sh
```

### Trên Windows PowerShell:
```powershell
# 1. Sinh keys
powershell -ExecutionPolicy Bypass -File scripts\gen-keys.ps1

# 2. Đóng gói images
powershell -ExecutionPolicy Bypass -File scripts\build-images.ps1

# 3. Khởi động cụm Kubernetes + Agones
powershell -ExecutionPolicy Bypass -File scripts\k8s-up.ps1

# 4. Kiểm thử
powershell -ExecutionPolicy Bypass -File scripts\test-p5.ps1
```

