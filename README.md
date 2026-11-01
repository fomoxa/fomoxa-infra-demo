# Fomoxa Game Server Architecture

Hạ tầng game server nhiều người chơi phân tán (Distributed Multiplayer Game Server Infrastructure) kết hợp **Fomoxa Networking**, **Nakama Matchmaking** và **Agones GameServer Orchestration** trên **Kubernetes**.

---

## Tài liệu hướng dẫn & Kế hoạch

- 📖 **[Hướng dẫn từng bước chạy trên Ubuntu / WSL2 (Zero to Running)](docs/UBUNTU_GUIDE.md)**: Hướng dẫn chi tiết bằng lệnh Bash cho người mới clone repo từ A đến Z (cài phụ thuộc, build, chạy K8s, test).
- 📋 **[Kế hoạch phát triển (PLAN.md)](PLAN.md)**: Toàn bộ lộ trình từ P1 đến P6 (kiến trúc, quyết định kỹ thuật, DoD, và nhật ký các bước).
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

