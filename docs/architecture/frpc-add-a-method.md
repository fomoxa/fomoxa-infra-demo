# Add an fRPC method

Top of this document: [documentation home](../README.md) · Previous: [using-frpc.md](using-frpc.md) · Next: [development.md](development.md)

> **Outcome:** a new call, `Lobby.Echo`, returns your text and the name of the logged-in player, and the selftest checks it over real fRPC.

`Echo` has no use in the game, but it touches every file a real method touches. [Using fRPC](using-frpc.md) explains the parts used here.

## Prerequisites

- The .NET 8 SDK ([prepare the environment](../getting-started/prepare-environment.md#step-7-install-the-net-8-sdk)).
- For step 6 only: Unity with the project open ([Unity client](../unity-client.md)).

Run the commands from the repository root, on a branch you can throw away:

```bash
git switch -c try-frpc
```

## Step 1. Write the messages

Goal: the request and reply classes, in both trees.

Create `MyGame.Backend/MyGame.Backend/Models/Echo.cs`:

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

Copy it to the Unity tree:

```bash
cp MyGame.Backend/MyGame.Backend/Models/Echo.cs MyGame/Assets/Network/Models/Backend/Echo.cs
```

Each method needs its own request class. Two methods that share one fail to compile with `FRPC001`. Methods can share a reply class.

## Step 2. Generate the codecs

Goal: `fomoxac` writes the encode and decode code for the new messages.

Generate before you touch `Services.cs`, because a service method whose message has no codec fails to compile with `FRPC003`.

```bash
FOMOXAC=$PWD/MyGame.Backend/tools/fomoxac/linux-x64/fomoxac
chmod +x "$FOMOXAC"
( cd MyGame.Backend/MyGame.Backend && "$FOMOXAC" generate )
( cd MyGame && "$FOMOXAC" generate )
```

**Check:** the output lists the new files for both trees, among others:

```
fomoxac: Generated/EchoReplyRpcCodec.cs
fomoxac: Generated/EchoRequestRpcCodec.cs
```

```
fomoxac: Assets/Network/Generated/EchoReplyRpcCodec.cs
fomoxac: Assets/Network/Generated/EchoRequestRpcCodec.cs
```

The `⚠ EchoReply.rpc` lines with `COMPATIBLE: new message` are normal for a new message.

## Step 3. Declare the method

Goal: `Echo` becomes part of the `Lobby` service, so the source generator adds `EchoAsync` to `LobbyClient`.

Add these lines inside `ILobby`, in both `MyGame.Backend/MyGame.Backend/Services.cs` and `MyGame/Assets/Network/Rpc/Services.cs`:

```csharp
[Unary(Requires = Roles.Player)]
EchoReply Echo(CallContext context, EchoRequest request);
```

`Requires = Roles.Player` lets only a logged-in player call it. `CallContext` gives the handler the caller's identity.

## Step 4. Write the handler

Goal: the backend and the mock cluster answer the call.

Both implement `ILobby` and both share `LobbyCore`, so the logic goes there. Add this method to `LobbyCore` in `MyGame.Backend/MyGame.Backend/Accounts/LobbyCore.cs`:

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

Then add this line to the `Lobby` class in `MyGame.Backend/MyGame.Backend/Players.cs` and to the `MockLobby` class in `tools/MockCluster/MockBackend.cs`:

```csharp
public EchoReply Echo(CallContext context, EchoRequest request) => core.Echo(context.Session!.Principal, request);
```

**Check:** the real backend compiles:

```bash
dotnet build MyGame.Backend/MyGame.Backend/MyGame.Backend.csproj -c Release -v q -nologo
```

## Step 5. Call it from the selftest

Goal: a client calls `Echo` over a real connection, with and without valid input.

The selftest is a client that talks to the mock backend over fRPC. In the `SelfTest` method of `tools/MockCluster/Program.cs`, find the first `var lobby = new LobbyClient(connection);` and add this right after it:

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

Run it:

```bash
./scripts/run-mock-cluster.sh --selftest --pods=2
echo "exit code: $?"
```

**Check:** the exit code is 0 and the output contains these lines. The player name differs on every run:

```
[selftest] OK   Echo -> st3f9a1c: hi
[selftest] OK   Echo without text is refused
[selftest] TAT CA DAT
```

## Step 6. Call it from the Unity client

Goal: the game calls `Echo` on the connection it logged in with.

`RoomJoiner` holds that connection. Add this method to `MyGame/Assets/Network/RoomJoiner.cs`:

```csharp
public async Task<string> Echo(string text)
{
    EchoReply reply = await new LobbyClient(connection).EchoAsync(new EchoRequest { Text = text }, CallOptions.Within(TimeSpan.FromSeconds(5)));
    return reply.Text;
}
```

To see it run, add this line in `Authenticate`, after `await ProfileCore();`:

```csharp
Debug.Log($"[Lobby] echo: {await Echo("hi")}");
```

Start the mock cluster with `./scripts/run-mock-cluster.sh`, press Play in Unity and log in.

**Check:** the Unity Console shows `[Lobby] echo: hi` after the login.

## Step 7. Remove the example

Goal: the repository is back as it was.

These commands discard every uncommitted change on the branch:

```bash
git restore .
git clean -fd MyGame.Backend/MyGame.Backend MyGame/Assets/Network
git switch -
git branch -d try-frpc
```

**Check:** `git status` reports a clean tree.

## For a real method

Keep your changes and work through the rest of the [development guide checklist](development.md#add-or-change-an-rpc): commit the generated files and the `.meta` file Unity creates for a new model, and update the [RPC interface](rpc-api.md) page in both languages. If the method runs on a deployed system, read [change a message safely](using-frpc.md#change-a-message-safely) first.

## Common mistakes

| Symptom | Cause | Fix |
|---|---|---|
| `FRPC003` on your method | step 2 was skipped, or it ran before the model existed | run step 2 again |
| `CS0535` on `Lobby` or `MockLobby` | one of the two classes has no `Echo` | add the line from step 4 to both |
| `CS0246` for `EchoReply` in the Unity tree | `Echo.cs` was not copied | run the `cp` command from step 1, then step 2 |
| `the handshake failed: SchemaConflict` | a message differs between the trees | copy the model again, regenerate both, rebuild both |

[Using fRPC](using-frpc.md#common-mistakes) lists the mistakes that apply to any method.

## Next steps

- [Development guide](development.md): the other changes people make often.
- [Testing](../testing.md#1-selftest-of-the-mock-cluster): what the selftest checks.
