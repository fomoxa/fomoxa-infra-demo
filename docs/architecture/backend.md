# Backend

Top of this document: [documentation home](../README.md) · Previous: [overview.md](overview.md) · Next: [nakama-module.md](nakama-module.md)

The backend (`MyGame.Backend/MyGame.Backend/`) is the only server a client talks to for everything except the game itself. It accepts fRPC connections, authenticates players, applies the account and lobby rules, talks to Nakama and the directory, and receives reports from game servers. The same lobby code also runs inside the mock cluster, so the client behaves the same against both.

## File map

| File | What it does |
|---|---|
| `Program.cs` | reads settings, starts the backend, waits for Ctrl+C or SIGTERM |
| `BackendSettings.cs` | configuration from `appsettings.json` and `Fomoxa__...` environment variables |
| `Backend.cs` | starts the three fRPC listeners (players TCP and WebSocket, pods TCP), the metrics endpoint and the Nakama self test |
| `Services.cs` | the service interfaces: `Players`, `Lobby`, `Pods`, `PodGate` ([RPC interface](rpc-api.md)) |
| `Players.cs` | the implementations: `Players.Login`, `Lobby.*`, room list, matchmaking wait, tickets |
| `Pods.cs` | `Pods.Login` and the `PodGate` calls that game servers make |
| `DirectoryLink.cs` | the client connection to the directory, used to request tickets |
| `NakamaLink.cs` | the HTTP client for Nakama: authentication, `find_match`, `poll_match`, `verify_ticket`, `list_rooms`, `kv_get`, `kv_put` |
| `OnlinePlayers.cs` | the online list kept from `Presence` pings |
| `Accounts/AccountService.cs` | registration, password check, recovery codes, change password |
| `Accounts/AccountAuth.cs` | maps `PlayerLogin.Mode` (`register`, `login`, `recover`) onto the account service |
| `Accounts/LobbyCore.cs` | profile, friends, invitations, scoreboard, shared by the real backend and the mock cluster |
| `Accounts/KeyValue.cs` | the small key-value store behind accounts, with optimistic concurrency |
| `Accounts/Metrics.cs` | Prometheus text output without an external library |
| `Models/*.cs` | the network messages ([listed here](rpc-api.md#messages)) |
| `Generated/` | code produced by `fomoxac`; do not edit by hand |

## Login

`Players.Login` is the authentication step of the connection. By `Mode`:

| Mode | Behaviour |
|---|---|
| `register` | creates the account, stores a PBKDF2-SHA256 hash of the password (120,000 rounds, its own salt) and returns a recovery code once |
| `login` | checks the password. Names are case-insensitive. Five wrong attempts in a row lock that name for 60 seconds |
| `recover` | checks the recovery code, sets the new password and issues a new code. The old code stops working |
| empty | a guest. With Nakama configured it signs in with `DeviceId`; without Nakama it only needs `Player` |

Only the hash of the recovery code is stored, so a lost code cannot be shown again. The RPC library does not return the login response to the client, so the new code waits in memory until the client calls `Lobby.TakeRecoveryCode` right after logging in.

The session carries a principal, which is the lowercase player name. Every `Lobby` call reads it from `context.Session.Principal`.

## Accounts storage

`KeyValue.cs` offers get and put with a version check. A put with no version overwrites, `"*"` writes only if the key does not exist yet, and any other version writes only if it still matches. This is how two concurrent registrations of the same name cannot both succeed.

When Nakama is configured, the store calls the Lua RPCs `kv_get` and `kv_put`, which read and write Nakama storage under the system user. Without Nakama the backend uses an in-memory store, optionally saved to a JSON file (`--data` in the mock cluster).

## Joining a room

`Lobby.JoinRoom` does four things:

1. Return the previous ticket if the same player asked for the same room in the last 5 seconds. This makes retries safe.
2. `WaitForMatch`: with Nakama, call `find_match`, then `poll_match` every 300 ms for up to 15 seconds. For `auto`, the backend sends the list of rooms that pods have reported as not full, so Nakama only chooses a room that really exists.
3. Verify the ticket with `verify_ticket`, which checks it belongs to this player and room and has not expired.
4. Ask the directory for the connect token for that room (`DirectoryLink.Ticket`) and return it as a `RoomTicket`.

Without Nakama the wait step returns the requested room at once.

## What pods report

Game servers call `PodGate.ReportRoom` and `PodGate.ReportScores` about every 2 seconds on port 9602.

| Report | Used for |
|---|---|
| `ReportRoom(RoomKey, Players, Capacity)` | the room list (`ListRooms`) and the `rooms_reported` and `room_players` metrics |
| `ReportScores(Room, [Player, Steps])` | the room scoreboard, each player's best record and their match count |

`LobbyCore.ReportScores` handles a report in the background: the first time it sees a player in a room it reads the display name and record and counts one match. After that it updates the steps, and it saves a new best when the steps exceed the stored one.

## Online list, friends and invitations

`Lobby.Presence(Room)` marks the caller as online in a given room and returns the list of online players. Clients call it every few seconds, including during a match, because their backend connection stays open. A player not seen for 20 seconds counts as offline.

Friends and invitations live in the `social` record of each player. A friend request is stored as outgoing for one player and incoming for the other. `Accept` makes both lists contain the other. An invitation (`Lobby.Invite`) is allowed only between friends and appears in the recipient's `Social` result until they dismiss it.

## Metrics

The backend serves Prometheus text on `Fomoxa__Metrics__Listen` (port 9610 in the cluster).

| Metric | Type | Meaning |
|---|---|---|
| `logins_total{mode,result}` | counter | login attempts |
| `joins_total{result}` | counter | rooms joined |
| `password_changes_total` | counter | password changes |
| `friend_requests_total` | counter | friend requests sent |
| `room_invites_total` | counter | invitations sent |
| `score_reports_total` | counter | score reports received from pods |
| `online_players` | gauge | players seen in the last 20 seconds |
| `rooms_reported` | gauge | rooms pods have reported |
| `room_players` | gauge | players in those rooms |

Each metric is exported with the prefix `fomoxa_` (for example `fomoxa_logins_total`). See [monitoring](../operations/04-monitoring.md) for the dashboard and the alert rules built on them.

## Running it

| Way | Command |
|---|---|
| With the mock cluster (no Docker) | `./scripts/run-mock-cluster.sh` |
| The real backend against Nakama in docker compose | `./scripts/test-real-backend.sh` (see [testing](../testing.md)) |
| In the cluster | `kubectl apply -f deploy/k8s/backend.yaml` ([manifest walkthrough](../deploy/manifest-walkthrough.md)) |
