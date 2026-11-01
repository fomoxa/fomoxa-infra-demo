# Nakama module

Top of this document: [documentation home](../README.md) · Previous: [backend.md](backend.md) · Next: [game-server.md](game-server.md)

The whole Nakama side is one Lua file, `deploy/nakama/modules/main.lua`. Nakama loads it as a script and the file registers eight RPCs. It does two jobs: pairing players into a room and handing out tickets, and acting as a key-value store the backend uses for accounts, profiles and friends.

The file exists in two places that must stay identical: the file itself, which docker compose mounts, and a copy inside the `nakama-modules` ConfigMap in `deploy/k8s/nakama.yaml`. CI compares them (see [testing](../testing.md#5-static-checks-generated-code-yaml-shell-lua-monitoring-config)).

## RPCs

| RPC | Called by | Auth | Purpose |
|---|---|---|---|
| `find_match` | backend, on behalf of the player | the player's Bearer token | join the queue for a room, or pair with the player already waiting |
| `poll_match` | backend | player token | read the result for the same queue |
| `verify_ticket` | backend | `http_key` | check that a ticket is valid, unexpired, and belongs to this player and room |
| `reset_match` | backend, test scripts | `http_key` | clear a waiting queue |
| `list_rooms` | backend | `http_key` | the room pool with a state for each room |
| `kv_get`, `kv_put` | backend | `http_key` | the key-value store for accounts |
| `health` | anyone | none | returns `{ ok, now }` |

A backend-only RPC checks `ctx.user_id`. A call made with the `http_key` has no user id and passes. A call made by a logged-in player has one and is rejected with "chi backend duoc goi RPC nay" (code 7, permission denied). This is why a player cannot call `kv_get` to read someone else's account. [Testing Nakama with curl](../deploy/07-test-nakama.md) checks it.

## Storage collections

All records are written under the system user `00000000-0000-0000-0000-000000000000`.

| Collection | Key | Holds | Lifetime |
|---|---|---|---|
| `match_queue` | `pool:auto` or a room key | the player waiting and when they started | 30 seconds |
| `match_result` | `<queue>:<user>`, `<room>:<user>`, `user:<user>` | the result `matched` with room, ticket and expiry | until the ticket expires |
| `match_ticket` | the ticket id | room, the two users, expiry | 120 seconds |
| `room_stat` | the room key | the two users in the room and an expiry | 120 seconds |
| `account`, `profile`, `social` | lowercase player name | a JSON string the backend writes (`{ v = ... }`) | permanent |

## How pairing works

`find_match` takes `{ room, pool }`. A room of `auto`, `pool` or empty means "any room". The queue key is then `pool:auto`, and otherwise the room key.

1. Remove any old finished result for this user in this queue, so `poll_match` cannot return an earlier match.
2. Read the queue record. If a different player waits and started less than 30 seconds ago, pair with them. If the same player asks again, answer `waiting`.
3. To pair, empty the queue record with the record's version, so only one request wins. Then choose the room: the requested one, or for `auto` the next room from `pool` (a round-robin counter in `pool:counter`). Write the ticket, the room state and the results for both players.
4. If nobody waits, write this player into the queue and answer `waiting`.
5. Both writes use the version of the record that was read. If another request changed it in between, the write fails and the loop tries again, up to five times, then the RPC answers "match_queue ban, thu lai" (code 14, unavailable).

When the queue has no record yet, the version is `"*"`, which writes only if the key does not exist. An empty version would skip the check, and two players asking in the same moment would overwrite each other and never pair.

`poll_match` reads only the result of the same queue, so a player who just matched in one room and asks for another does not receive the old result. An expired result is deleted and the answer goes back to `waiting`.

## The pool

`ROOM_POOL` holds `arena:1` to `arena:4` and is the fallback. The backend normally sends `pool`: the rooms that game servers have reported as existing and not full. Nakama then chooses only among rooms a pod holds, so a pairing never points at a room with no server.

Every room has a capacity of 2 in the module, because a ticket carries exactly two users. A named room such as `arena:1` pairs the next two players who ask for that room.

## `list_rooms`

Returns `auto` plus each pool room with `players` (0, 1 or 2) and a `state` of `open`, `waiting` or `full`. The numbers count waiting or paired players, not live connections. The backend replaces them with the counts pods report.

## Key-value RPCs

`kv_get { collection, key }` answers `{ found, value, version }`. `kv_put { collection, key, value, version }` answers `{ ok }`. The version follows the same rule as `KeyValue.cs` in the backend: an empty string overwrites, `"*"` writes only if absent, anything else writes only if it matches.

Nakama stores `value` as a string inside `{ v = value }`, so the backend owns the JSON format of accounts, profiles and friends.

## Changing the module

1. Edit `deploy/nakama/modules/main.lua`.
2. Copy the new text into `data.main.lua` of the `nakama-modules` ConfigMap (indent by four spaces).
3. Run the checks: `./scripts/test-nakama.sh` and `./scripts/test-nakama-pool.sh` with `ALLOW_COMPOSE=1` against docker compose.
4. In a cluster: `kubectl apply -f deploy/k8s/nakama.yaml`, then `kubectl rollout restart deployment/nakama -n fomoxa`.
