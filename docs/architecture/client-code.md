# Unity client code

Top of this document: [documentation home](../README.md) · Previous: [game-server.md](game-server.md) · Next: [rpc-api.md](rpc-api.md)

This page maps the client scripts in `MyGame/Assets/`. For opening the project, building the scenes and the player, see [Unity client](../unity-client.md).

## Scenes

| Scene | Contains | Loaded when |
|---|---|---|
| `Lobby` | a dark background camera, `RoomJoiner`, `LobbyScreen` and `BootRouter` | first scene of the build |
| `Game` | `NetworkManager`, the floor and platforms, `Launcher`, `MatchHudScreen`, the player prefab | only after a ticket has been obtained |

The `Game` scene is loaded after the ticket arrives, so no networking objects exist while a player sits in the lobby. Leaving a match loads `Lobby` again.

The scenes are built by the editor scripts, not by hand: **Tools > Demo > Rebuild Scene** (`DemoScene.cs`) creates both scenes, and `LobbyUiSetup.cs` attaches the UI Toolkit documents. The menus are listed at the end of this page.

## Scripts (`MyGame/Assets/Network/`)

| Script | Role |
|---|---|
| `RoomJoiner` | the heart of the client. Lives across scenes (`DontDestroyOnLoad`). Opens the fRPC connection to the backend, logs in, lists rooms, joins, pings `Presence`, handles friends, invitations, password change and recovery. Reads the command-line flags |
| `LobbyScreen` | the lobby UI built from `Assets/UI/Lobby.uxml` and `Lobby.uss`. It only draws and forwards clicks; every action calls `RoomJoiner` |
| `BootRouter` | in the `Lobby` scene. Command-line modes that need no lobby (`-pod`, `-host`, `-client`) skip the lobby and load `Game` directly |
| `MatchSession` | the hand-over between scenes: the lobby calls `Begin(room, ticket)`, which loads `Game`; `Launcher` takes the ticket with `TryTake` and `ReturnToLobby` goes back |
| `Launcher` | in `Game`. Starts the client, host or server from the ticket, and returns to the lobby when the connection ends |
| `MatchHudScreen` | the in-match HUD from `MatchHud.uxml`: room name, connection state, leave button. It hides while you play so Space cannot press a button, and returns when you press Escape or Tab |
| `Player` | the networked player behaviour: reads input, runs `Course.Simulate`, tracks `Steps`. The same code runs on the server |
| `PlayerCamera` | the chase camera behind your own character. Right mouse or Q and E rotate, the wheel zooms, platforms in the way pull the camera closer. Movement is relative to the camera yaw |
| `PinnedCertificateCheck` | the TLS pin check: compares the SHA-256 of the certificate with `-tlspin:` |
| `Models/Course.cs` | the platform layout and the movement and jump rules, shared with the server ([constants](../reference.md#8-gameplay-constants)) |
| `Models/MoveInput.cs` | the input message, including `Jump` |
| `Models/Backend/*.cs`, `Rpc/Services.cs` | the network messages and service interfaces; `Generated/` holds the produced codecs |

## Command-line flags

Read in `RoomJoiner.ApplyCommandLine` and the autojoin code.

| Flag | Effect |
|---|---|
| `-backend:host:port` | backend address (9600 plain, 9643 TLS) |
| `-tls` | connect over TLS using the system roots |
| `-tlspin:<sha256>` | connect over TLS and accept only the certificate with this fingerprint |
| `-autojoin` | log in and join without clicking, for tests |
| `-room:<key>` | the room for `-autojoin` |
| `-name:<name>` | the player name for `-autojoin` |
| `-device:<id>` | the device id for guest login |
| `-pod`, `-host`, `-client` | run the game directly without the lobby |

## The login and join flow in the client

1. `LobbyScreen` collects the name and password and asks `RoomJoiner` to log in with the mode `register`, `login` or `recover`.
2. `RoomJoiner` connects to the backend with `RpcConnection`, sends `PlayerLogin`, and keeps the connection open for the whole session.
3. It lists rooms with `Lobby.ListRooms` and pings `Lobby.Presence` every few seconds. Because the connection stays open, you still show as online during a match.
4. A click on a room calls `Lobby.JoinRoom`. The reply is a `RoomTicket`: address, port and the connect token.
5. `MatchSession.Begin` loads `Game`. `Launcher` takes the ticket and connects over encrypted UDP to the address in the ticket.
6. When the connection ends, `Launcher` calls `MatchSession.ReturnToLobby`, and the lobby shows the reason.

## Editor tools

| Menu | Script | What it does |
|---|---|---|
| Tools > Fomoxa > Generate | the package | runs `fomoxac` for the `MyGame` tree |
| Tools > Demo > Rebuild Scene | `DemoScene.cs` | rebuilds the `Lobby` and `Game` scenes, the platforms and the prefabs |
| Tools > Demo > Add Lobby UI | `LobbyUiSetup.cs` | attaches `Lobby.uxml` to the lobby scene and `MatchHud.uxml` to the game scene |
| Tools > Demo > Build Windows Player | `DemoScene.cs` | builds `MyGame/Builds/MyGame.exe` |
| (called by scripts) | `DemoGenerate.cs` | runs Tools > Fomoxa > Generate in batch mode |
