# Thêm một phương thức fRPC

Lên đầu tài liệu: [trang chủ tài liệu](../README.md) · Trước: [using-frpc.md](using-frpc.md) · Tiếp: [development.md](development.md)

> **Kết quả:** một lời gọi mới, `Lobby.Echo`, trả về chuỗi bạn gửi và tên người chơi đã đăng nhập, và selftest kiểm nó qua fRPC thật.

`Echo` không có tác dụng gì trong game, nhưng thêm nó phải sửa đúng những file mà một phương thức thật phải sửa. [Dùng fRPC](using-frpc.md) giải thích các phần dùng ở đây.

## Cần có trước

- .NET SDK 8 ([chuẩn bị môi trường](../getting-started/prepare-environment.md#bước-7-cài-net-sdk-8)).
- Riêng bước 6: Unity đã mở project ([client Unity](../unity-client.md)).

Chạy các lệnh từ thư mục gốc của repo, trên một nhánh có thể bỏ đi:

```bash
git switch -c try-frpc
```

## Bước 1. Viết các message

Mục tiêu: lớp request và reply, ở cả hai cây.

Tạo `MyGame.Backend/MyGame.Backend/Models/Echo.cs`:

```csharp
using Fomoxa;

namespace MyGame.Backend.Models
{
    [Network]
    [Codec("rpc")]
    public class EchoRequest
    {
        [Network("string")]
        [Codec("rpc")]
        public string Text { get; set; } = "";
    }

    [Network]
    [Codec("rpc")]
    public class EchoReply
    {
        [Network("string")]
        [Codec("rpc")]
        public string Text { get; set; } = "";

        [Network("string")]
        [Codec("rpc")]
        public string Player { get; set; } = "";
    }
}
```

Chép sang cây Unity:

```bash
cp MyGame.Backend/MyGame.Backend/Models/Echo.cs MyGame/Assets/Network/Models/Backend/Echo.cs
```

Mỗi phương thức cần lớp request riêng. Hai phương thức dùng chung một lớp sẽ không biên dịch được, báo `FRPC001`. Các phương thức có thể dùng chung lớp reply.

## Bước 2. Sinh codec

Mục tiêu: `fomoxac` viết mã encode và decode cho các message mới.

Hãy sinh mã trước khi đụng tới `Services.cs`, vì phương thức của dịch vụ có message chưa có codec sẽ không biên dịch được, báo `FRPC003`.

```bash
FOMOXAC=$PWD/MyGame.Backend/tools/fomoxac/linux-x64/fomoxac
chmod +x "$FOMOXAC"
( cd MyGame.Backend/MyGame.Backend && "$FOMOXAC" generate )
( cd MyGame && "$FOMOXAC" generate )
```

**Kiểm tra:** kết quả liệt kê các file mới của cả hai cây, cùng các file khác:

```
fomoxac: Generated/EchoReplyRpcCodec.cs
fomoxac: Generated/EchoRequestRpcCodec.cs
```

```
fomoxac: Assets/Network/Generated/EchoReplyRpcCodec.cs
fomoxac: Assets/Network/Generated/EchoRequestRpcCodec.cs
```

Các dòng `⚠ EchoReply.rpc` kèm `COMPATIBLE: new message` là bình thường với message mới.

## Bước 3. Khai báo phương thức

Mục tiêu: `Echo` thành một phần của dịch vụ `Lobby`, để source generator thêm `EchoAsync` vào `LobbyClient`.

Thêm các dòng này vào trong `ILobby`, ở cả `MyGame.Backend/MyGame.Backend/Services.cs` và `MyGame/Assets/Network/Rpc/Services.cs`:

```csharp
[Unary(Requires = Roles.Player)]
EchoReply Echo(CallContext context, EchoRequest request);
```

`Requires = Roles.Player` chỉ cho người chơi đã đăng nhập gọi. `CallContext` cho handler biết danh tính của bên gọi.

## Bước 4. Viết handler

Mục tiêu: backend và cụm giả trả lời được lời gọi.

Cả hai đều cài đặt `ILobby` và dùng chung `LobbyCore`, nên logic đặt ở đó. Thêm phương thức này vào `LobbyCore` trong `MyGame.Backend/MyGame.Backend/Accounts/LobbyCore.cs`:

```csharp
public EchoReply Echo(string principal, EchoRequest request)
{
    if (request.Text.Length == 0)
    {
        throw new RpcException(Status.InvalidArgument("Echo needs some text"));
    }

    return new EchoReply { Text = request.Text, Player = principal };
}
```

Rồi thêm dòng này vào lớp `Lobby` trong `MyGame.Backend/MyGame.Backend/Players.cs` và lớp `MockLobby` trong `tools/MockCluster/MockBackend.cs`:

```csharp
public EchoReply Echo(CallContext context, EchoRequest request) => core.Echo(context.Session!.Principal, request);
```

**Kiểm tra:** backend thật biên dịch được:

```bash
dotnet build MyGame.Backend/MyGame.Backend/MyGame.Backend.csproj -c Release -v q -nologo
```

## Bước 5. Gọi từ selftest

Mục tiêu: một client gọi `Echo` qua kết nối thật, với đầu vào hợp lệ và không hợp lệ.

Selftest là một client nói chuyện với backend giả qua fRPC. Trong phương thức `SelfTest` của `tools/MockCluster/Program.cs`, tìm dòng `var lobby = new LobbyClient(connection);` đầu tiên và thêm ngay sau nó:

```csharp
EchoReply echo = await lobby.EchoAsync(new EchoRequest { Text = "hi" }, Within);
Check("Echo", echo.Text == "hi" && echo.Player == me, $"-> {echo.Player}: {echo.Text}");

try
{
    await lobby.EchoAsync(new EchoRequest(), Within);
    Check("Echo without text is refused", false);
}
catch (RpcException failed)
{
    Check("Echo without text is refused", failed.Status.Code == StatusCode.InvalidArgument);
}
```

Chạy:

```bash
./scripts/run-mock-cluster.sh --selftest --pods=2
echo "exit code: $?"
```

**Kiểm tra:** exit code là 0 và kết quả có các dòng này. Tên người chơi khác nhau mỗi lần chạy:

```
[selftest] OK   Echo -> st3f9a1c: hi
[selftest] OK   Echo without text is refused
[selftest] TAT CA DAT
```

## Bước 6. Gọi từ client Unity

Mục tiêu: game gọi `Echo` trên kết nối mà nó đã đăng nhập.

`RoomJoiner` giữ kết nối đó. Thêm phương thức này vào `MyGame/Assets/Network/RoomJoiner.cs`:

```csharp
public async Task<string> Echo(string text)
{
    EchoReply reply = await new LobbyClient(connection).EchoAsync(new EchoRequest { Text = text }, CallOptions.Within(TimeSpan.FromSeconds(5)));
    return reply.Text;
}
```

Để thấy nó chạy, thêm dòng này trong `Authenticate`, sau `await ProfileCore();`:

```csharp
Debug.Log($"[Lobby] echo: {await Echo("hi")}");
```

Khởi động cụm giả bằng `./scripts/run-mock-cluster.sh`, bấm Play trong Unity rồi đăng nhập.

**Kiểm tra:** Unity Console hiện `[Lobby] echo: hi` sau khi đăng nhập.

## Bước 7. Gỡ ví dụ

Mục tiêu: repo trở về như cũ.

Các lệnh này bỏ mọi thay đổi chưa commit trên nhánh:

```bash
git restore .
git clean -fd MyGame.Backend/MyGame.Backend MyGame/Assets/Network
git switch -
git branch -d try-frpc
```

**Kiểm tra:** `git status` báo cây sạch.

## Với một phương thức thật

Giữ các thay đổi và làm nốt [checklist trong hướng dẫn phát triển](development.md#thêm-hoặc-đổi-một-rpc): commit các file sinh ra và file `.meta` mà Unity tạo cho model mới, rồi cập nhật trang [Giao diện RPC](rpc-api.md) ở cả hai ngôn ngữ. Nếu phương thức chạy trên hệ thống đã triển khai, hãy đọc [đổi message an toàn](using-frpc.md#đổi-message-an-toàn) trước.

## Lỗi thường gặp

| Triệu chứng | Nguyên nhân | Cách sửa |
|---|---|---|
| `FRPC003` trên phương thức của bạn | bỏ qua bước 2, hoặc chạy nó trước khi có model | chạy lại bước 2 |
| `CS0535` trên `Lobby` hoặc `MockLobby` | một trong hai lớp chưa có `Echo` | thêm dòng ở bước 4 vào cả hai |
| `CS0246` cho `EchoReply` ở cây Unity | chưa chép `Echo.cs` | chạy lệnh `cp` ở bước 1, rồi bước 2 |
| `the handshake failed: SchemaConflict` | một message khác nhau giữa hai cây | chép lại model, sinh lại cả hai, build lại cả hai |

[Dùng fRPC](using-frpc.md#lỗi-thường-gặp) liệt kê các lỗi áp dụng cho mọi phương thức.

## Bước tiếp theo

- [Hướng dẫn phát triển](development.md): các thay đổi khác hay làm.
- [Kiểm thử](../testing.md#1-selftest-cụm-giả): selftest kiểm những gì.
