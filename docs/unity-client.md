# 04 Unity client: open the project, build the scenes, build the player, run it

This document covers opening the Unity project in `MyGame/`, building the two scenes (Lobby and Game), producing a Windows build, and connecting it to a cluster: the [mock cluster](getting-started/quickstart-mock-cluster.md) or the [Kubernetes cluster](deploy/README.md).

| Item | Value |
|---|---|
| Unity | 6000.5.7f1 (the version in `MyGame/ProjectSettings/ProjectVersion.txt`) |
| Client OS | 64-bit Windows (the `MyGame.exe` build) |
| Git | must be on `PATH` (Unity downloads the Fomoxa packages from GitHub) |
| .NET SDK 8 | only needed to run `fomoxac` from the command line |

## Step 1. Open the project

Goal: Unity opens `MyGame/` and downloads all packages.

1. In Unity Hub choose Add, then Add project from disk, and select the repository's `MyGame` directory. With WSL2 that path looks like `\\wsl$\Ubuntu\home\<you>\fomoxa-infra-demo\MyGame`. If you open it from the Windows Hub, cloning the repository onto a Windows drive is faster.
2. If the Hub says version `6000.5.7f1` is missing, install exactly that version.
3. Open the project and wait for the first import (a few minutes).

`Packages/manifest.json` takes two packages straight from GitHub:

```json
"com.fomoxa.networking": "https://github.com/fomoxa/com.fomoxa.networking.git#0.2.0",
"com.fomoxa.rpc":        "https://github.com/fomoxa/com.fomoxa.rpc.git#0.1.0"
```

Unity calls `git` to clone them into `Library/PackageCache/`, and `packages-lock.json` pins the exact commits.

Check: open Window, then Package Manager. Both packages appear under In Project, and the Console has no red errors.

Common errors:

| Symptom | Fix |
|---|---|
| `[Package Manager] ... git ... not found` | Install Git, make sure `git --version` works in PowerShell, and reopen Unity Hub |
| UPM reports a JSON error although the file looks fine | `manifest.json` has a BOM. Open it in VS Code; if the bottom right says `UTF-8 with BOM`, use Save with Encoding and pick `UTF-8` |
| `Assembly name collision: Fomoxa.Unity` | A stray `fomoxa.unity` package is in `manifest.json`. Delete that line |
| Red `CS0246` or `CS0103` after `git pull` | The generated code no longer matches the models: [regenerate it](#regenerating-code-after-changing-a-model-or-rpc) |

## Step 2. Build the two scenes (Rebuild Scene)

Goal: create `Assets/Scenes/Lobby.unity` (the lobby) and `Assets/Scenes/Game.unity` (the match), and set Build Settings.

Use the menu Tools, then Demo, then Rebuild Scene. From the command line (PowerShell, in the repository root):

```powershell
$Unity = "C:\Program Files\Unity\Hub\Editor\6000.5.7f1\Editor\Unity.exe"
& $Unity -batchmode -nographics -projectPath "$PWD\MyGame" -executeMethod DemoScene.Build -logFile -
```

`DemoScene.Build` (in `Assets/Editor/DemoScene.cs`) does the following in order:

| # | Task | Result |
|---|---|---|
| 1 | Builds the Game scene: light, floor, 8 pillars, 18 jump platforms (built from `Course`), a camera with `PlayerCamera` | |
| 2 | Builds the `Player` prefab (capsule + `NetworkObject` + `NetworkTransform` + `Player`) | `Assets/Prefabs/Player.prefab` |
| 3 | Builds the `Network Manager` (with the encrypted UDP transport) and `Launcher`, and attaches the in-match HUD (`MatchHud.uxml`) | |
| 4 | Runs the Fomoxa steps that collect prefabs and scenes into the `NetworkManager` lists | |
| 5 | Builds the Lobby scene: a dark camera, a `Backend` object (`RoomJoiner` and `BootRouter`), and the lobby UI (`Lobby.uxml`) | |
| 6 | Sets Build Settings: `Lobby` (index 0), then `Game` | |

Expected output, in the Console or the batch log:

```
[Demo] lobby scene built: Assets/Scenes/Lobby.unity
[Demo] scene built: Assets/Scenes/Game.unity
[Demo] prefab built: Assets/Prefabs/Player.prefab
```

Check:

1. File, then Build Profiles (or Build Settings): the scenes are `Scenes/Lobby` then `Scenes/Game`, both ticked.
2. No `[Demo] prefab:` or `[Demo] scene:` error lines in the Console.
3. Open `Lobby.unity`: the Hierarchy has `Lobby Camera`, `Backend` and `Lobby UI`.
4. Open `Game.unity`: the Hierarchy has `Network Manager`, `Match HUD`, `Main Camera` and `Platform 1` to `Platform 18`.

Why two scenes: the lobby (login, room list, friends) needs no floor, character or game networking. The `Game` scene loads only after the player clicks a room and a ticket has been obtained. See [the two-scene flow](#the-two-scene-flow).

Common errors:

| Symptom | Fix |
|---|---|
| The `Tools, Demo` menu is missing | An Editor script has a compile error. Fix the red errors in the Console first |
| `[Demo] build failed: ...` | Read the exception under that line. Usually the packages have not finished downloading (Step 1) |
| Input fields and buttons show no text in the lobby | `LobbyPanelSettings.asset` has no theme. Run Tools, Demo, Add Lobby UI, then check that the asset's Theme Style Sheet points at `LobbyTheme.tss` |
| Many new `.meta` files appear after the rebuild | Expected. Commit them with the scenes, or the GUIDs will differ between machines |

## Step 3. Try it in the Editor

Goal: get into the game from the Unity Editor itself.

1. Start a backend: the [mock cluster](getting-started/quickstart-mock-cluster.md#step-2-start-the-mock-cluster) or the [Kubernetes cluster](deploy/README.md).
2. Open `Assets/Scenes/Lobby.unity` and press Play.
3. On the Register tab enter a name and a password of at least 8 characters, choose Create account, save the recovery code, and choose "I saved the code".
4. On the Rooms tab click `arena:1`.

Expected result:

- The status line steps through: connecting to the backend, logging in, joining the room, ticket received, loading the match. Then the `Game` scene appears.
- The Console shows `[Join] logged in as <name>`, `[Match] arena:1 -> 127.0.0.1:7778` and `[Player] spawn on client id=... mine=True`.
- The mock cluster lets you in immediately. A real cluster with Nakama needs two players to click the same room, so open a second client.

Development mode with no backend (Host): open `Game.unity` directly and press Play. The HUD shows Dev tools. Choose Host (local server + player) to play alone, without the lobby.

## Step 4. Build the Windows player

Goal: `Builds/MyGame.exe`, used for running several clients and for `test-p5`.

Use the menu Tools, then Demo, then Build Windows Player. From the command line:

```powershell
$Unity = "C:\Program Files\Unity\Hub\Editor\6000.5.7f1\Editor\Unity.exe"
& $Unity -batchmode -nographics -projectPath "$PWD\MyGame" -executeMethod DemoScene.BuildPlayer -logFile -
```

This builds the `Lobby` and `Game` scenes, in that order, for `StandaloneWindows64`, and writes `MyGame/Builds/MyGame.exe`.

Expected output: the log contains `[Demo] player build: Succeeded -> Builds/MyGame.exe`.

Check: `Test-Path MyGame\Builds\MyGame.exe` returns `True`.

If the build fails with "scene not in build", Rebuild Scene (Step 2) has not been run.

## Step 5. Run the build and use the flags

Plain run, with the backend at `127.0.0.1:9600` (the default of both the mock cluster and the k3d cluster):

```powershell
.\MyGame\Builds\MyGame.exe
```

Flags:

| Flag | Meaning |
|---|---|
| `-backend:host:port` | Connect to a different backend, for example `-backend:192.168.1.50:9600` |
| `-tls` | Use TLS to the backend (for a certificate issued by a public CA) |
| `-tlspin:<SHA256-hex>` | Use TLS and trust only the certificate with this fingerprint (self-signed certificates). Get the pin from `scripts/gen-tls-cert.sh` or the mock cluster's banner. Usually paired with `-backend:127.0.0.1:9643` |
| `-autojoin` | Log in automatically (as a guest, through `-device`) and join a room with no UI clicks |
| `-name:<name>` `-room:<room>` `-device:<id>` | Used with `-autojoin` |
| `-batchmode -nographics -logFile <file>` | Standard Unity flags for a hidden run with a log file (used in automated tests) |
| `-bot` | The character runs in circles and jumps now and then (for testing) |
| `-trace` | Print the character's position every two seconds |
| `-host`, `-client`, `-pod` | Older modes that skip the lobby and go straight to the `Game` scene: a local Host, a direct connection, or the Unity build acting as a pod |

Connecting to the mock cluster over TLS:

```powershell
.\MyGame\Builds\MyGame.exe -backend:127.0.0.1:9643 -tlspin:C5B502A6036815A39E6E60AC21398651BFFB9AD4FF19BDD5C6397D9FF74C5167
```

Replace the pin with the one printed when you start the mock cluster or run `gen-tls-cert.sh`.

Two automatic clients, the way `test-p5` does it:

```powershell
.\MyGame\Builds\MyGame.exe -batchmode -nographics -logFile ana.log -autojoin -name:ana -device:dev-ana -room:arena:1
.\MyGame\Builds\MyGame.exe -batchmode -nographics -logFile bo.log  -autojoin -name:bo  -device:dev-bo  -room:arena:1
Select-String -Path ana.log,bo.log -Pattern "mine=True"
```

Each log must contain a `mine=True` line (the client joined a pod and owns a character).

Controls in a match:

| Key | Effect |
|---|---|
| W A S D or the arrow keys | Move, relative to the camera |
| Space | Jump |
| Hold the right mouse button and drag, or Q and E | Rotate the camera |
| Mouse wheel | Zoom |
| Esc or Tab | Open or close the menu (scoreboard, invite friends, leave room). The HUD is hidden while you play so Space never presses a button by accident |

The rules: 18 platforms are arranged in a spiral, each 1.4 m higher than the last. Your score is the highest platform you have stood on, and your personal record is saved in your profile.

## The two-scene flow

```
 Lobby.unity                                         Game.unity
 +--------------------------------------+            +--------------------------------------+
 | RoomJoiner (DontDestroyOnLoad)       |            | Network Manager + Launcher           |
 | LobbyScreen (UI Toolkit: Lobby.uxml) |  click a   | Match HUD (UI Toolkit: MatchHud.uxml)|
 |  login / register / recover          |  room      | PlayerCamera, floor, 18 platforms    |
 |  rooms, friends, account, online     | ---------> |                                      |
 +--------------------------------------+  ticket in +--------------------------------------+
                                          <---------
                                          leave room or lose connection
```

1. `RoomJoiner` survives both scenes and keeps the backend connection: login, the room list, the "still online" heartbeat every 5 seconds, friends, and the scoreboard.
2. Clicking a room makes `RoomJoiner.JoinRoom` request a ticket, then `MatchSession.Begin(room, ticket)` calls `SceneManager.LoadSceneAsync("Game")`. The scene loads only at this point.
3. `Launcher.Start()` in `Game` takes the ticket (`MatchSession.TryTake`) and calls `ClientManager.StartConnection(address, port, ticket)` to connect over UDP through the gateway to the pod.
4. Leaving the room (by button or by losing the connection) makes `MatchSession.ReturnToLobby` reload `Lobby`, still logged in.

The main code is in `MyGame/Assets/Network/`: `RoomJoiner.cs`, `LobbyScreen.cs`, `MatchHudScreen.cs`, `MatchSession.cs`, `Launcher.cs`, `Player.cs`, `PlayerCamera.cs` and `BootRouter.cs`. Two files are shared with the server: `Models/Course.cs` (platform geometry and jump physics) and `Models/MoveInput.cs` (the input sent to the server).

## Editing the UI

The UI is made of UI Toolkit assets, not drawn in code:

| File | Role |
|---|---|
| `Assets/UI/Lobby.uxml`, `Lobby.uss` | Layout and style of the lobby |
| `Assets/UI/MatchHud.uxml`, `MatchHud.uss` | The in-match HUD |
| `Assets/UI/LobbyTheme.tss` | UI Toolkit's default theme |
| `Assets/UI/LobbyPanelSettings.asset` | Panel settings (created automatically) |

Open a `.uxml` file in UI Builder (Window, UI Toolkit, UI Builder), or change colors and sizes in the `.uss` files. If you rename an element's `name="..."`, rename it in `LobbyScreen.cs` or `MatchHudScreen.cs` too, since they look elements up by name. After editing, run Tools, Demo, Add Lobby UI if you need to attach the UI to a scene again.

## Regenerating code after changing a model or RPC

Data exchanged over the network (login, rooms, scores, and `MoveInput`) is described by C# classes marked `[Network]`. The `fomoxac` tool reads them and generates the encode and decode code (`Generated/*.cs`). The backend and Unity each have their own generated tree, and the two must match:

| Tree | Models | Generated code | Settings |
|---|---|---|---|
| Backend | `MyGame.Backend/MyGame.Backend/Models/*.cs` and `Services.cs` | `.../Generated/` | `fomoxa.toml` in the same directory |
| Unity (and the game server) | `MyGame/Assets/Network/Models/**` and `Rpc/Services.cs` | `MyGame/Assets/Network/Generated/` | `Assets/FomoxaSettings.asset` (it writes `MyGame/fomoxa.toml`) |

The Unity-side models and services are copies of the backend's (`Models/Backend/*.cs`, `Rpc/Services.cs`).

To add or change an RPC or model (for example a new field on `RoomInfo`):

```bash
cd ~/fomoxa-infra-demo
FOMOXAC=$PWD/MyGame.Backend/tools/fomoxac/linux-x64/fomoxac
chmod +x "$FOMOXAC"     # git does not keep the executable bit on this file

# 1) Edit the backend models and services (MyGame.Backend/MyGame.Backend/Models/*.cs, Services.cs), then generate the backend code
( cd MyGame.Backend/MyGame.Backend && "$FOMOXAC" generate )

# 2) Copy them to Unity
cp MyGame.Backend/MyGame.Backend/Models/{Players,Pods,Rooms,Social}.cs MyGame/Assets/Network/Models/Backend/
cp MyGame.Backend/MyGame.Backend/Services.cs MyGame/Assets/Network/Rpc/Services.cs

# 3) Generate the Unity and game server code
( cd MyGame && "$FOMOXAC" generate )

# 4) Check the committed generated code matches the models (CI runs this too)
( cd MyGame.Backend/MyGame.Backend && "$FOMOXAC" generate --check )
( cd MyGame && "$FOMOXAC" generate --check )
```

On Windows use `MyGame.Backend\tools\fomoxac\win-x64\fomoxac.exe generate`. Unity also has Tools, Fomoxa, Generate, which runs the same tool for the `MyGame` tree.

`generate` rewrites the `*.cs` files and `.fomoxa/schema.json`. `--check` only compares, and exits non-zero if the generated code is stale (it prints `fomoxac: up to date` when it matches). Adding a field or model changes the protocol fingerprint, so the client, backend and pod must be rebuilt together. An old `MyGame.exe` cannot connect to a new backend.

Expected output:

```
✓ RoomTicket.rpc unchanged
...
fomoxac: up to date
```

Common errors:

| Symptom | Fix |
|---|---|
| `SchemaConflict`, `MessageId conflict`, or the client is dropped right after connecting | The generated code is stale. Delete `MyGame/Assets/Network/Generated/*.cs` and regenerate (step 3) |
| `CS0103: The name '...NetAdapter' does not exist` | The code is not generated yet, or Unity has not finished compiling. Generate, then wait for the compile spinner to stop |
| `FRPC003` when compiling `Services.cs` | The RPC models have no codec yet. Generate (steps 1 and 3) before adding `Services.cs` |
| Unity hangs during Generate | A `fomoxac` process is stuck. Kill it in Task Manager, check the Models and Generated paths in `Assets/FomoxaSettings.asset` (`MyGame/fomoxa.toml` is generated from it, so do not edit that by hand), and run again |
| Dozens of `Generated/*.cs` files change only in the `fomoxac-version` or `generated-at` line | Normal when the `fomoxac` version changes. The protocol content is unchanged |

## Common problems while playing

| Symptom | Cause and fix |
|---|---|
| Login says "sai tên hoặc mật khẩu" (wrong name or password) | The password is wrong, or the name is not registered (use the Register tab). After 5 wrong tries in a row the name is locked for 60 seconds |
| "tên này đã có người dùng" (name already taken) | Names are case-insensitive. Pick another |
| Forgot the password | Use the Forgot password tab: name, recovery code, new password |
| Stuck on "Joining" after clicking a room | A real cluster (Nakama) needs two players to click the same room within 30 seconds. Use "Auto matchmaking" or open a second client |
| "Không có game server chạy phiên bản X" (no game server runs version X) | The client's `Application.version` differs from the pod's. The project sets Player Settings Version to 1.0, matching `FOMOXA_VERSION=1.0`. If you change one, change the other (the mock cluster skips this check unless `--strict-version` is set) |
| You join a room but see nobody | The prefab is not registered with the `NetworkManager`. Run Rebuild Scene (Step 2) and confirm there is no `[Demo] prefab:` error |
| The character moves but cannot jump | Space is being swallowed by the UI. The HUD must be hidden while playing. Rebuild the client from the latest code if it persists |
| The camera does not follow the character | Look in the Console for `[Camera] chưa thấy nhân vật của mình` and `[Player] spawn on client ... mine=True` |
| UDP never connects with WSL2 and Windows | [07](troubleshooting.md#a-windows-client-cannot-reach-a-cluster-in-wsl2) |

## Checklist

- [ ] Unity `6000.5.7f1` opens the project, Package Manager shows both Fomoxa packages, and the Console has no red errors
- [ ] Rebuild Scene is done, and Build Settings lists `Lobby` (0) then `Game` (1)
- [ ] Playing from `Lobby.unity`: register, save the recovery code, enter `arena:1`, and the `Game` scene loads only after the ticket arrives
- [ ] In a match: WASD follows the camera, Space jumps, the HUD is hidden, and Esc or Tab opens the menu with the scoreboard
- [ ] `Builds/MyGame.exe` exists, and two `-autojoin` clients both print `mine=True`
- [ ] `fomoxac generate --check` reports `up to date` in both trees
- [ ] The new `.meta` files are committed

Next: [05 Operations](operations/README.md).
