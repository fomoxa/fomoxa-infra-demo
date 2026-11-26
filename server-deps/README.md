# server-deps

Bản vendor của phần source 2 package Fomoxa mà `MyGame\Server~` biên dịch.
**Sinh tự động** bằng `scripts\sync-server-deps.ps1` - đừng sửa tay.

| Package | Version | Nguồn |
|---|---|---|
| `com.fomoxa.networking` | 0.5.0 | `MyGame/Library/PackageCache/com.fomoxa.networking@6782a6ad26c9` |
| `com.fomoxa.rpc` | 0.1.8 | `MyGame/Library/PackageCache/com.fomoxa.rpc@c28ea6e57126` |

Đã sao chép: `Runtime/Core`, `Standalone~`, `Runtime/Plugins/*.dll` (networking);
`Runtime/Fomoxa.Rpc`, `Runtime/Directory`, `Runtime/Generators/*.dll` (rpc). Bỏ
`Runtime/Unity`, `Editor`, `Tests` vì console server không dùng UnityEngine.
