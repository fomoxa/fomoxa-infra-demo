# Fomoxa Game Server Architecture

Hạ tầng game server nhiều người chơi phân tán (Distributed Multiplayer Game Server Infrastructure) kết hợp **Fomoxa Networking**, **Nakama Matchmaking** và **Agones GameServer Orchestration** trên **Kubernetes**.

---

## Tài liệu hướng dẫn & Kế hoạch

- 📖 **[Tài liệu từng bước (docs/vi/README.md)](docs/vi/README.md)** (English: [docs/README.md](docs/README.md)): hướng dẫn chi tiết bằng lệnh bash copy-paste được, từ máy trắng đến game chạy: [chuẩn bị môi trường](docs/vi/getting-started/prepare-environment.md) → [cụm giả](docs/vi/getting-started/quickstart-mock-cluster.md) → [triển khai Kubernetes](docs/vi/deploy/README.md) → [client Unity](docs/vi/unity-client.md) → [vận hành](docs/vi/operations/README.md); kèm [kiểm thử](docs/vi/testing.md), [xử lý sự cố](docs/vi/troubleshooting.md), [tham khảo](docs/vi/reference.md).
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

