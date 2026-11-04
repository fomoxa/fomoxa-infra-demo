# RPC interface

Top of this document: [documentation home](../README.md) · Previous: [client-code.md](client-code.md) · Next: [using-frpc.md](using-frpc.md)

Clients, the backend and the game servers talk over fRPC. The services are declared in `MyGame.Backend/MyGame.Backend/Services.cs` and the messages in `MyGame.Backend/MyGame.Backend/Models/*.cs`. `fomoxac` generates the codecs and the typed clients from them, and the Unity project has its own copy of the same declarations under `MyGame/Assets/Network/`. To call them or add your own, see [Using fRPC](using-frpc.md).

## Services

| Service | Who calls it | Needs role |
|---|---|---|
| `Players` | a client, as the connection login | none (this call authenticates) |
| `Lobby` | a logged-in client | `player` |
| `Pods` | a game server, as its login | none (this call authenticates) |
| `PodGate` | a game server | `pod` |

### `Players`

| Method | Request | Response | Notes |
|---|---|---|---|
| `Login` | `PlayerLogin` | `PlayerWelcome` | the `Mode` field selects register, login or recover ([backend](backend.md#login)) |

### `Lobby`

| Method | Request | Response | Purpose |
|---|---|---|---|
| `JoinRoom` | `JoinRoomRequest` | `RoomTicket` | pair, verify the ticket, return the connect token. Can fail with `NoRoomForVersion` |
| `ListRooms` | `ListRoomsRequest` | `RoomList` | rooms with player counts and a state |
| `Presence` | `PresenceRequest` | `OnlineList` | mark yourself online in a room, return everyone online |
| `ChangePassword` | `ChangePasswordRequest` | `ActionResult` | needs the old password |
| `TakeRecoveryCode` | `RecoveryCodeRequest` | `ActionResult` | `Message` holds the new recovery code once, after register or recover |
| `GetProfile` | `ProfileRequest` | `PlayerProfile` | best steps and matches of one player |
| `Social` | `SocialRequest` | `SocialState` | friends, requests and invitations |
| `FriendAction` | `FriendActionRequest` | `ActionResult` | `Action`: `request`, `accept`, `decline`, `remove` or `dismiss-invite` |
| `Invite` | `InviteRequest` | `ActionResult` | invite a friend into a room |
| `Scoreboard` | `ScoreboardRequest` | `ScoreboardState` | ranking of one room |

### `Pods`

| Method | Request | Response |
|---|---|---|
| `Login` | `PodLogin` | none |

### `PodGate`

| Method | Request | Response | Purpose |
|---|---|---|---|
| `ReportRoom` | `RoomReport` | none | players and capacity of a room |
| `ReportScores` | `ScoreReport` | none | current steps of each player in a room |
| `TransferPlayer` | `TransferPlayerRequest` | `RoomTicket` | hand a player to another room. Declared but unused |

The services between the backend, the pods and the directory (`Directory`, `DirectoryAdmin`) belong to the Fomoxa runtime and are listed in the [reference](../reference.md#7-backend-rpc-interface).

## Messages

Field types are the network types: `string`, `bytes`, `bool`, `u16`, `u32`, `i64` and arrays.

| Message | Fields |
|---|---|
| `PlayerLogin` | `Player` string, `DeviceId` string, `Secret` bytes, `Mode` string, `Recovery` bytes |
| `PlayerWelcome` | `Player` string, `RecoveryCode` string |
| `JoinRoomRequest` | `RoomKey` string, `Version` string |
| `RoomTicket` | `Address` string, `Port` u16, `Connection` bytes, `WebSocketUrl` string |
| `NoRoomForVersion` | `Version` string |
| `ListRoomsRequest` | none |
| `RoomInfo` | `RoomKey` string, `Players` u32, `Capacity` u32, `State` string (`open`, `waiting`, `full`) |
| `RoomList` | `Rooms` array of `RoomInfo` |
| `PresenceRequest` | `Room` string |
| `OnlinePlayer` | `Name` string, `Room` string |
| `OnlineList` | `Players` array of `OnlinePlayer` |
| `ActionResult` | `Ok` bool, `Message` string |
| `ChangePasswordRequest` | `OldPassword` string, `NewPassword` string |
| `RecoveryCodeRequest` | none |
| `ProfileRequest` | `Player` string |
| `PlayerProfile` | `Name` string, `BestSteps` u32, `Matches` u32, `CreatedUnix` i64 |
| `FriendInfo` | `Name` string, `Online` bool, `Room` string, `BestSteps` u32 |
| `InviteInfo` | `From` string, `Room` string |
| `SocialRequest` | none |
| `SocialState` | `Friends` array of `FriendInfo`, `Incoming` array of string, `Outgoing` array of string, `Invites` array of `InviteInfo` |
| `FriendActionRequest` | `Action` string, `Target` string |
| `InviteRequest` | `To` string, `Room` string |
| `ScoreEntry` | `Name` string, `Steps` u32, `Best` u32 |
| `ScoreboardRequest` | `Room` string |
| `ScoreboardState` | `Entries` array of `ScoreEntry` |
| `PodLogin` | `Key` bytes |
| `RoomReport` | `RoomKey` string, `Players` u32, `Capacity` u32 |
| `PodScore` | `Player` string, `Steps` u32 |
| `ScoreReport` | `Room` string, `Entries` array of `PodScore` |
| `TransferPlayerRequest` | `Player` string, `RoomKey` string, `Version` string |

The game traffic itself is not RPC. The only message is `MoveInput` (movement and `Jump`) plus the replicated transform and the player state, sent over encrypted UDP.

## Errors

A failed call carries a status code and a message. The ones clients meet:

| Code | When |
|---|---|
| `InvalidArgument` | an invalid name, a weak password, or a required field that is empty |
| `Unauthenticated` | a wrong password or recovery code, or the Nakama session is gone and you must log in again |
| `ResourceExhausted` | the name is locked for 60 seconds after five wrong passwords |
| `FailedPrecondition` | no match found in 15 seconds, a ticket was refused, or a room is already held |
| `Unavailable` | Nakama or the directory could not be reached |

`LobbyCore.Translate` maps the account errors to these codes. Any other account error, such as a name that is already taken, becomes `FailedPrecondition`.
