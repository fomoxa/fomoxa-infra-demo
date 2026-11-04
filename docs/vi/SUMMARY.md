# Mục lục

* [Tài liệu Fomoxa - từ máy trắng đến game chạy được](README.md)

## Bắt đầu

* [Chuẩn bị môi trường (Ubuntu / WSL2)](getting-started/prepare-environment.md)
* [Chạy nhanh bằng cụm giả (không cần Kubernetes)](getting-started/quickstart-mock-cluster.md)

## Triển khai

* [Triển khai cụm Kubernetes (k3d + Agones) từng bước](deploy/README.md)
  * [Khái niệm Kubernetes dùng trong demo này](deploy/concepts.md)
  * [Chuẩn bị, cổng trống, khoá và chứng chỉ](deploy/01-prepare-and-keys.md)
  * [Lấy mã nguồn gateway và build image Docker](deploy/02-build-images.md)
  * [Tạo cluster k3d, cài Agones, nạp image](deploy/03-cluster-and-agones.md)
  * [Namespace và Secrets](deploy/04-secrets.md)
  * [Apply manifest theo thứ tự](deploy/05-apply-manifests.md)
  * [Kiểm tra toàn cụm và kiểm tra từ phía client](deploy/06-verify-cluster.md)
  * [Test Nakama bằng curl](deploy/07-test-nakama.md)
  * [Test bằng client Unity và dọn dẹp](deploy/08-test-client-and-cleanup.md)
  * [Đọc các file manifest trong `deploy/k8s/`](deploy/manifest-walkthrough.md)
  * [Đường đi của lưu lượng và nơi cấu hình TLS](deploy/traffic-and-tls.md)

## Client Unity

* [Client Unity: mở project, dựng scene, build, chạy](unity-client.md)

## Vận hành

* [Vận hành cụm](operations/README.md)
  * [Trạng thái, log, scale, restart, cập nhật image](operations/01-status-and-updates.md)
  * [Tắt game server an toàn (drain) và tài khoản người chơi](operations/02-drain-and-accounts.md)
  * [TLS và khoá Nakama](operations/03-tls-and-keys.md)
  * [Giám sát](operations/04-monitoring.md)
  * [Autoscaling, NetworkPolicy, sao lưu, địa chỉ công khai](operations/05-scaling-security-backup.md)

## Kiến trúc

* [Tổng quan kiến trúc](architecture/overview.md)
* [Backend](architecture/backend.md)
* [Module Nakama](architecture/nakama-module.md)
* [Game server (pod)](architecture/game-server.md)
* [Mã client Unity](architecture/client-code.md)
* [Giao diện RPC](architecture/rpc-api.md)
* [Dùng fRPC](architecture/using-frpc.md)
* [Thêm một phương thức fRPC](architecture/frpc-add-a-method.md)
* [Hướng dẫn phát triển](architecture/development.md)

## Kiểm thử và sự cố

* [Kiểm thử](testing.md)
* [Xử lý sự cố](troubleshooting.md)

## Tham khảo

* [Tham khảo](reference.md)

## Đóng góp

* [Documentation](contributing/documentation.md)
