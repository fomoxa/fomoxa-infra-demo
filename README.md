# Fomoxa Game Server Architecture

Hạ tầng game server nhiều người chơi phân tán (Distributed Multiplayer Game Server Infrastructure) kết hợp Fomoxa Networking, Nakama Matchmaking và Agones GameServer Orchestration trên Kubernetes.

## Tài liệu

Hướng dẫn đầy đủ, từ chuẩn bị máy tới vận hành cụm, nằm trong tài liệu Fomoxa for Unity: [tiếng Anh](https://github.com/fomoxa/unity-docs/blob/main/examples/infra-demo/README.md), [tiếng Việt](https://github.com/fomoxa/unity-docs/blob/main/vi/examples/infra-demo/README.md).

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

