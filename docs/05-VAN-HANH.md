# 05 - Vận hành cụm

English version: [en/05-OPERATIONS.md](en/05-OPERATIONS.md)

Các việc hằng ngày và các tính năng vận hành: xem log, scale, cập nhật, tắt an toàn (drain), tài khoản, TLS, khoá Nakama, giám sát, autoscaling, NetworkPolicy, sao lưu.
Giả định bạn đã có cụm chạy theo [03](03-TRIEN-KHAI-K8S.md) và đang đứng ở `~/fomoxa-infra-demo` với `kubectl config use-context k3d-fomoxa`.

> **Đã chạy thật khi viết tài liệu:** lệnh liên quan Nakama/RPC (`kv_get`), `openssl s_client` kiểm tra chứng chỉ, `/metrics`, `promtool`/`amtool`/`loki`/`promtail` kiểm cấu hình.
> **Chưa chạy trên cụm Kubernetes thật:** các lệnh `kubectl` bên dưới (output *(mẫu)*), manifest `tls-proxy`, `networkpolicy`, `fleet-autoscaler`, `monitoring/`.

[Restart](ops/01-trang-thai-va-cap-nhat.md#4-khởi-động-lại-một-dịch-vụ) · [Cập nhật image](ops/01-trang-thai-va-cap-nhat.md#cập-nhật-sau-khi-build-lại-image) · [Drain](ops/02-drain-va-tai-khoan.md#6-tắt-game-server-an-toàn-drain) · [Tài khoản](ops/02-drain-va-tai-khoan.md#7-tài-khoản-và-dữ-liệu-người-chơi) ·
[TLS](ops/03-tls-va-khoa.md#tls-giữa-client-và-backend) · [Khoá Nakama](ops/03-tls-va-khoa.md#9-khoá-và-mật-khẩu-nakama) · [Giám sát](ops/04-giam-sat.md#giám-sát) · [Autoscaling](ops/05-scale-bao-mat-sao-luu.md#autoscaling) · [NetworkPolicy](ops/05-scale-bao-mat-sao-luu.md#networkpolicy-tuỳ-chọn) ·
[Sao lưu](ops/05-scale-bao-mat-sao-luu.md#13-sao-lưu-và-khôi-phục-postgres) · [Đổi địa chỉ công khai](ops/05-scale-bao-mat-sao-luu.md#14-đổi-địa-chỉ-công-khai-public_address)

---

## Các phần

- [01-trang-thai-va-cap-nhat.md](ops/01-trang-thai-va-cap-nhat.md): Trạng thái và log, truy cập dịch vụ nội bộ, scale tay, restart, cập nhật image
- [02-drain-va-tai-khoan.md](ops/02-drain-va-tai-khoan.md): Tắt game server an toàn (drain), tài khoản và dữ liệu người chơi
- [03-tls-va-khoa.md](ops/03-tls-va-khoa.md): TLS giữa client và backend, khoá và mật khẩu Nakama
- [04-giam-sat.md](ops/04-giam-sat.md): Prometheus, Grafana, Alertmanager, Loki: cài đặt, chỉ số, cảnh báo
- [05-scale-bao-mat-sao-luu.md](ops/05-scale-bao-mat-sao-luu.md): Autoscaling, NetworkPolicy, sao lưu Postgres, đổi `public_address`

## Ai nên đọc phần nào

- Người trực cụm hằng ngày: phần 1 (trạng thái, log, cập nhật) và phần 4 (giám sát).
- Người phụ trách bảo mật: phần 3 (TLS, khoá Nakama) và phần 2 (tài khoản người chơi).
- Người lo hiệu năng và chi phí: phần 4 (giám sát) và phần 5 (autoscaling).
- Trước khi mở ra ngoài mạng nội bộ: phần 3 và phần 5 (NetworkPolicy, `public_address`).

## Checklist vận hành

- [ ] Biết xem log từng dịch vụ và log mọi game server cùng lúc
- [ ] Biết cập nhật image: build → `k3d image import` → `rollout restart` (backend) / xoá + apply Fleet (game server)
- [ ] Thử drain: `kubectl delete gs ...` trên pod `Allocated` thấy `xin drain`, rồi `het nguoi choi, thoat`
- [ ] Đã quyết định TLS: chứng chỉ, pin trong client, `TLS_ONLY` hay không
- [ ] Đã sao lưu `deploy/secrets/` và có lịch `pg_dump`
- [ ] Giám sát: Prometheus Targets đều `UP`; Alertmanager đã có nơi nhận thông báo
- [ ] Autoscaler bật nếu cần; hiểu trần 16 pod/16 phòng
- [ ] `public_address` đúng với địa chỉ client thấy gateway
