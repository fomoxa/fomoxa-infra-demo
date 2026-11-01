# Game server (pod)

Top of this document: [documentation home](../README.md) · Previous: [nakama-module.md](nakama-module.md) · Next: [client-code.md](client-code.md)

The game server is a .NET console program in `MyGame/Server~/`. It replaces `MyGame.exe -pod`, so it runs in a container without Unity. In the cluster each pod is an Agones `GameServer` holding exactly one room. The image is built from `deploy/Dockerfile.server`.

## File map

| File | What it does |
|---|---|
| `Program.cs` | start-up, the main loop, signals, room claiming, metrics |
| `PodConfig.cs` | settings: command-line arguments, then environment variables, then defaults |
| `PodTransport.cs` | the encrypted UDP listener, built only after the directory has issued a ticket key |
| `PodAuthenticator.cs` | remembers which peer id is which player name |
| `PlayerEntity.cs` | the console copy of the `Player` prefab, with the same behaviours in the same order |
| `AgonesLifecycle.cs` | talks to the Agones sidecar: `Ready`, `Allocated`, `Health`, `Shutdown` |
| `BackendReporter.cs` | sends room counts and scores to the backend |

The simulation code is not here. `Player` and `Course` live in `MyGame/Assets/Network/` and are compiled into both the Unity client and this server, so both run the same movement and jump rules.

## Start-up sequence

1. Read the configuration ([environment variables and flags](../reference.md#5-environment-variables-and-flags)).
2. Connect to the Agones sidecar. Outside Kubernetes this is skipped and every Agones call does nothing (`[Agones] disabled (local dev no-op)`).
3. Register with the directory using the `pod` key, sending the pod name, address, port, version and region. The directory answers with a ticket key.
4. Start the encrypted UDP listener on the pod port (7778 in the cluster). If the ticket key later changes, the listener restarts with the new key.
5. Claim a room. In the cluster `FOMOXA_CLAIM_ONE=true`, so the pod tries each room in `FOMOXA_ROOM` (`arena:1` to `arena:16`) until the directory grants one. A room already held by another pod fails with `FAILED_PRECONDITION`, and the pod tries the next, then waits 2 seconds and tries again.
6. Start the loops: the simulation tick, the backend reporter, the metrics endpoint and the Agones health ping.

The log lines to look for are `registered`, `listening on udp`, `claimed room from pool`, and `holding room ... (state stays Ready until first player)`.

## Joining and the simulation

A client arrives with a connect token the backend obtained from the directory. The token carries the player name as application data. `PodAuthenticator` accepts any connection whose token the transport has already verified, and records the name against the peer id, which is how scores can be reported by name.

Each connection spawns a `PlayerEntity`. Its `Player` behaviour reads the client's `MoveInput` and advances the position with `Course.Simulate`. The server is authoritative: the client predicts, the server decides, and the transform is replicated back. The highest platform a player has stood on is their `Steps`.

## Agones states

| Event | State change |
|---|---|
| Pod holds a room, nobody has joined | stays `Ready` |
| First player connects | `Allocated` |
| Last player leaves | a 30 second grace timer starts, then the pod shuts down |
| A player returns during the grace time | the timer is cancelled |
| After `Shutdown` | the Fleet creates a fresh `Ready` GameServer to replace it |

This keeps a pod from being `Ready` while still holding the room of an old session. While running, the pod pings Agones health every 2 seconds.

## Draining

On the first `SIGTERM` or `SIGINT` the pod asks the directory to drain: the directory stops assigning new players or rooms to it. The pod then waits for the players to leave and exits when none remain. A second signal, or a pod that never registered, exits at once. If `drain_seconds` (300) passes first, the directory forces the stop and the pod calls `Shutdown` itself. Kubernetes allows 360 seconds before `SIGKILL`. The timeline is in [drain and accounts](../operations/02-drain-and-accounts.md).

## Reports and metrics

| What | Interval | Where it goes |
|---|---|---|
| Room player counts (`ReportRoom`) | about 2 seconds | backend, port 9602 |
| Scores (`ReportScores`) | about 2 seconds | backend, port 9602 |
| Score snapshot taken from the entities | every second | read by the reporter |
| Metrics | on scrape | HTTP on `FOMOXA_METRICS` (9611 in the cluster) |

The reporter is best effort. If the backend is down it logs the failure and the game continues.

Pod metrics use the prefix `fomoxa_pod_`.

| Metric | Meaning |
|---|---|
| `players` | peers connected to this pod |
| `rooms` | rooms the pod holds |
| `draining` | 1 while draining |
| `registered` | 1 once registered with the directory |
| `best_steps` | highest step among the players on the pod |
| `uptime_seconds` | seconds since start |

## Running it by hand

```bash
dotnet run --project MyGame/Server~ -- --directory=127.0.0.1:9500 --key=deploy/secrets/directory/pod --room=arena:1 --port=7778 --behind-gateway=false --trace
```

`--trace` prints each player's position every two seconds. The mock cluster starts several pods for you (`--pods=4`), so you rarely run this directly. The full flag list is in the [reference](../reference.md#5-environment-variables-and-flags), and [quick start](../getting-started/quickstart-mock-cluster.md) shows the mock cluster.
