# Tài liệu

Tài liệu nằm trong `docs/` và được xuất bản bằng GitBook qua Git Sync, theo cùng cách tổ chức với tài liệu Fomoxa for Unity. `gitbook-docs.yaml` mô tả site và hai space (tiếng Anh, tiếng Việt). `.gitbook.yaml` chỉ ra `README.md`, `SUMMARY.md` và danh sách redirect cho các trang đã chuyển chỗ. Cả hai nằm ở gốc thư mục tài liệu.

## Cấu trúc

| Mục | Thư mục | Mục đích |
|---|---|---|
| Bắt đầu | `getting-started/` | Chuẩn bị máy, rồi chạy cụm giả không cần Kubernetes |
| Triển khai | `deploy/` | Dựng cụm thật trên k3d với Agones, mỗi file ngắn gồm một nhóm bước, kèm bài nhập môn Kubernetes và bài đọc manifest |
| Client Unity | `unity-client.md` | Mở project, dựng scene và bản build |
| Vận hành | `operations/` | Việc hằng ngày: log, cập nhật, drain, TLS, giám sát, scale, sao lưu |
| Kiểm thử và sự cố | `testing.md`, `troubleshooting.md` | Mỗi bài test kiểm gì, và cách sửa lỗi thường gặp |
| Tham khảo | `reference.md` | Cổng, script, biến môi trường, bản đồ file |
| Đóng góp | `contributing/` | Làm việc với các trang này |

`SUMMARY.md` là mục lục và mọi trang phải có trong đó. Thư mục có nhiều phần thì `README.md` làm mục lục của nó. Mỗi file phần nên dài tối đa khoảng 300 dòng; hãy tách trang trước khi vượt mức đó.

## Bố cục và ngôn ngữ

```
docs/
  .gitbook.yaml         readme, summary, redirects (space tiếng Anh)
  gitbook-docs.yaml     site: hai space, EN ở docs/, VI ở docs/vi/
  README.md, SUMMARY.md
  getting-started/ deploy/ operations/ contributing/
  unity-client.md testing.md troubleshooting.md reference.md
  tools/check_docs.py   công cụ kiểm tra
  vi/                   bản tiếng Việt: có .gitbook.yaml, README.md, SUMMARY.md riêng
```

Các trang tiếng Việt nằm trong `vi/` và phản chiếu trang tiếng Anh từng file một, cùng tên file. Tiếng Anh là bản gốc: khi sửa một trang tiếng Anh, sửa trang tiếng Việt cùng đường dẫn trong cùng commit. GitBook chỉ phục vụ file nằm trong space, nên một trang không bao giờ link sang ngôn ngữ kia và không dùng ảnh nằm ngoài space của nó.

## Quy tắc cho mọi trang

- Viết văn xuôi kỹ thuật rõ ràng; tiêu đề tiếng Anh viết hoa chữ đầu câu.
- Mô tả đúng việc mã đang làm. Ghi rõ phần chưa kiểm chứng; kết quả lệnh gắn nhãn (thật) khi đã chạy và (mẫu) khi dựng từ manifest.
- Mỗi bước có mục tiêu, lệnh, ý nghĩa, kết quả mong đợi, cách kiểm tra và lỗi thường gặp.
- Ưu tiên lệnh tường minh hơn script. Khi có script, nói rõ nó chạy những lệnh nào.
- Link trang liên quan bằng đường dẫn tương đối và giữ tiêu đề ổn định, vì trang khác link tới chúng.
- Lệnh đặt trong khối code gắn thẻ `bash`, mỗi khối một lệnh hoặc một chuỗi lệnh liền mạch.
- Chỉ dùng hint của GitBook (`{% hint style="warning" %}`) cho cảnh báo người đọc không được bỏ sót.
- Luồng hoặc chuỗi ngắn dùng khối Mermaid. Không đặt `;` và `#` trong nhãn.

## Chuyển hoặc đổi tên trang

1. Chuyển file ở cả hai ngôn ngữ.
2. Cập nhật `SUMMARY.md` ở cả hai space.
3. Thêm đường dẫn cũ vào `redirects` trong `.gitbook.yaml` và `vi/.gitbook.yaml` (bỏ đuôi `.md`) để link cũ vẫn chạy.
4. Chạy công cụ kiểm tra.

## Kiểm tra các trang

```bash
python3 docs/tools/check_docs.py
```

Công cụ đọc cả hai space và báo:

- link tới file hoặc tiêu đề không tồn tại, và link ra khỏi space (README gốc cùng README của `MyGame` và `tools/MockCluster` cũng được kiểm tra);
- trang mà `SUMMARY.md` không liệt kê;
- trang tiếng Anh thiếu trang tiếng Việt và ngược lại;
- trang tiếng Việt có số tiêu đề khác trang tiếng Anh.

Phải in `0 problems` trước khi commit. CI chạy nó trong job `lint-scripts-and-manifests`.
