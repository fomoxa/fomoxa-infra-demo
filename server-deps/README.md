# server-deps

Bản vendor của phần source 2 package Fomoxa mà `MyGame\Server~` biên dịch.
**Sinh tự động** bằng `scripts\sync-server-deps.ps1` - đừng sửa tay.

| Package | Version | Nguồn |
|---|---|---|
| `com.fomoxa.networking` | 0.6.0 | `MyGame/Library/PackageCache/com.fomoxa.networking@934ca9bd4aa2` |
| `com.fomoxa.rpc` | 0.1.9 | `MyGame/Library/PackageCache/com.fomoxa.rpc@1f92f7f2452a` |

Đã sao chép: `Runtime/Core`, `Standalone~`, `Runtime/Plugins/*.dll` (networking);
`Runtime/Fomoxa.Rpc`, `Runtime/Directory`, `Runtime/Generators/*.dll` (rpc). Bỏ
`Runtime/Unity`, `Editor`, `Tests` vì console server không dùng UnityEngine.
