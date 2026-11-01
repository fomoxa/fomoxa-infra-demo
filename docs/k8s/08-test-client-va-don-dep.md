# 03.8 Test bằng client Unity và dọn dẹp

Lên đầu tài liệu: [03-TRIEN-KHAI-K8S](../03-TRIEN-KHAI-K8S.md) · Trước: [07-test-nakama.md](07-test-nakama.md)

## Bước 13. Test bằng client Unity (người chơi thật)

**Mục tiêu:** chứng minh đường đi đầy đủ client → backend → Nakama → directory → gateway → game server, và trạng thái Agones thay đổi theo người chơi.

**13.1. Mở client** (build ở [04](../04-CLIENT-UNITY.md)). Cụm k3d đưa 9600/7777 vào `localhost`, đúng mặc định của client nên **không cần tham số**:

```powershell
.\Builds\MyGame.exe
```

Trong game: **Đăng ký** → lưu mã khôi phục → bấm phòng `arena:1`. (Nakama bật nên phòng cụ thể cần **2 người** cùng bấm vào; mở client thứ hai, đăng ký tên khác, bấm cùng phòng.)

**13.2. Quan sát phía cluster** (terminal WSL2, trong lúc chơi):

```bash
watch -n 2 kubectl get gs,fleet -n fomoxa
```

**Kết quả mong đợi:** một GameServer chuyển `Ready` → **`Allocated`** (cột `ALLOCATED` của Fleet từ 0 lên 1). Log pod đó:

```bash
kubectl logs -n fomoxa -l agones.dev/role=gameserver -c fomoxa-server --prefix --tail=30 | grep -E "Agones|peer"
```

```
[Pod] peer 1 la 'ana'
[Agones] first player joined -> Allocate
[Agones] player 1 connected (active: 1)
...
[Agones] room empty, shutting down in 30s unless a player returns
[Agones] Shutting down...
```

Khi người chơi cuối thoát và phòng trống 30 giây, pod `Shutdown` và Fleet tạo pod mới ở trạng thái `Ready`.

**13.3. Bản tự động (cần `Builds/MyGame.exe`):** `./scripts/test-p5.sh`

**Script này làm gì (bung ra):**
1. `kubectl get gs -n fomoxa` - đảm bảo có GameServer và **0 pod `Allocated`** trước khi bắt đầu.
2. Gọi PowerShell của Windows (WSL2 interop) chạy `scripts/test-p5.ps1`, nó khởi chạy **4 bản `MyGame.exe`** ẩn cửa sổ:
   `-batchmode -nographics -autojoin -name:ana|bo|cai|dan -device:p5-k8s-<tên> -room:arena:1|arena:1|arena:2|arena:2`
   (mỗi phòng cần đúng 2 người nên ghép cặp ana+bo, cai+dan).
3. Đợi mỗi client in `mine=True` trong log (đã vào pod và có nhân vật của mình).
4. Dùng `kubectl` (qua `wsl.exe` nếu chạy từ `test-p5.sh`) đếm pod `Allocated`: **phải đúng 2**, nếu không script thất bại.
5. Tuỳ chọn `TEST_SCALE=1 ./scripts/test-p5.sh`: scale Fleet lên 5 rồi về 4 và kiểm tra số GameServer.

**Kết quả mong đợi (mẫu):**

```
=== Kiem tra DoD P5: Game server chay tren K8s + Agones ===
   ana -> arena:1 ... mine=True (OK)
   bo -> arena:1 ... mine=True (OK)
   cai -> arena:2 ... mine=True (OK)
   dan -> arena:2 ... mine=True (OK)
So pod o trang thai Allocated: 2 (dung ky vong)
=== DoD P5 HOAN TAT THANH CONG! ===
```

**Lỗi thường gặp:** xem [07](../07-XU-LY-SU-CO.md): client không nối được, ở `waiting` mãi (thiếu người thứ hai), GameServer không chuyển `Allocated`.

---

## Bước 14. Dọn dẹp

Chọn một trong ba mức:

```bash
# A) Chỉ xoá ứng dụng, giữ cluster + Agones (dựng lại nhanh)
kubectl delete namespace fomoxa

# B) Xoá toàn bộ cluster k3d (gồm Agones, dữ liệu Postgres)
k3d cluster delete fomoxa

# C) Xoá luôn image Docker đã build
docker rmi fomoxa-server:latest fomoxa-directory:latest fomoxa-gateway:latest fomoxa-backend:latest
```

**Hoặc chạy gọn:** `./scripts/k8s-down.sh --keep-cluster` (mức A) hoặc `./scripts/k8s-down.sh` (mức B).

Khoá trong `deploy/secrets/` không bị xoá bởi các lệnh trên; xoá tay nếu muốn: `rm -rf deploy/secrets`.

---
