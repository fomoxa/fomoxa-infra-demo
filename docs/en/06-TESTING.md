# 06 Testing

Vietnamese version: [../06-KIEM-THU.md](../06-KIEM-THU.md)

The repository's tests: what each one checks, how to run it (manual commands and the script), the expected result, and what it does not cover. Every result labeled (real) was produced while writing these documents, unless stated otherwise.

| # | Test | What it checks | Needs | Time | Notes |
|---|---|---|---|---|---|
| 1 | [Selftest of the mock cluster](#1-selftest-of-the-mock-cluster) | accounts, rooms, friends, invites, scores, records and TLS over real fRPC | .NET 8 | about 30 s | no Docker needed |
| 2 | [Bot test](#2-bot-test-a-bot-climbs-the-platforms-over-real-udp) | ticket, encrypted UDP, pod, jumping and landing on platforms, scoreboard, record | .NET 8 | about 40 s | the closest thing to a real player without Unity |
| 3 | [Nakama Lua module](#3-the-nakama-lua-module) | the Lua module: pairing, tickets, blocking internal RPCs | Nakama (compose or cluster) | about 10 s | |
| 4 | [Real backend with real Nakama](#4-real-backend-with-real-nakama) | the real backend, a real Nakama and real pods: every feature plus `/metrics` | Docker, .NET 8 | about 2 min | CI runs this one |
| 5 | [Static checks](#5-static-checks-generated-code-yaml-shell-lua-monitoring-config) | generated code matches models, YAML, shell, Lua equals the ConfigMap, monitoring config | varies | seconds | |
| 6 | [`test-p5.sh`](#6-test-p5-gameservers-on-kubernetes-and-agones-needs-the-unity-player) | Agones: `Ready` to `Allocated`, four clients paired | a K8s cluster and `MyGame.exe` | about 1 min | not run for real |
| 7 | [Manual Unity script](#7-manual-script-in-unity) | UI, camera, jumping, friends | Unity | 10 min | not run for real |

The `--selftest` and `--bot-test` modes each build their own mock cluster and then stop it. Stop any running mock cluster (Ctrl+C) before running them, or the ports collide.

## 1. Selftest of the mock cluster

Manual commands:

```bash
cd ~/fomoxa-infra-demo
dotnet publish "MyGame/Server~/MyGame.Server.csproj" -c Release -o .mock/server
dotnet publish tools/MockCluster/MockCluster.csproj   -c Release -o .mock/cluster
dotnet .mock/cluster/MockCluster.dll --server=.mock/server/MyGame.Server.dll --pods=2 --selftest
echo "exit code: $?"
```

Or run it in one go: `./scripts/run-mock-cluster.sh --selftest --pods=2`

It starts a mock directory and backend plus two real pods, and then a "client" using the game's real fRPC protocol runs these checks:

| Group | Checks |
|---|---|
| Accounts | registering a duplicate name, a weak password, a wrong password and an unknown account are all rejected; login ignores case; a recovery code is issued; recovery with a wrong code fails and with the right code succeeds; the old code stops working; the old password stops working; changing the password works (and a wrong old password is rejected) |
| Rooms | `ListRooms`; two players ask for "auto" together; `Presence` (the online list); a nonexistent room is rejected |
| Scores | a pod reports scores; the ranking is in the right order; the record keeps the highest step count (3, then 2, then 5 gives 5) |
| Social | a friend request reaches the recipient; accepting makes the friendship two-way; inviting a friend to a room; the friend shows as online and where; inviting a non-friend is refused |
| TLS | connecting over the TLS port with the right pin and calling an RPC works; a wrong pin gives `TlsRejected` |

Expected output (real): `[selftest] OK ...` lines, ending with:

```
[selftest] TAT CA DAT
[selftest] OK   Ket noi qua TLS (dung pin) va goi RPC (3 phong)
[selftest] TLS DAT
```

and `exit code: 0`. Any `FAIL` or `CO LOI` line gives exit code 3.

Not covered: UDP (see test 2), a real Nakama (test 4), Unity.

## 2. Bot test: a bot climbs the platforms over real UDP

Manual command:

```bash
dotnet .mock/cluster/MockCluster.dll --server=.mock/server/MyGame.Server.dll --pods=2 --bot-test
# put BOT_TRACE=1 in front to print the bot's position
```

Or run it in one go: `./scripts/run-mock-cluster.sh --bot-test --pods=2`

What happens: (1) the bot registers `botjumper` over fRPC and asks to join `arena:1` to get a ticket. (2) It builds a real Fomoxa network runtime (the same library Unity uses) and connects to the pod over encrypted UDP with that ticket. (3) The pod checks the ticket and learns that this is `botjumper`. (4) The bot reads the positions the server sends back and walks and jumps up the platforms. (5) After about 25 seconds it checks the scoreboard and the profile.

Expected output (real):

```
[bot] OK   Co ticket vao arena:1 -> 127.0.0.1:7779
[bot] ket noi: Starting None
[bot] ket noi: Authenticating None
[bot] ket noi: Started None
[bot] OK   Da vao pod (client Started)
[bot] OK   Bang diem co ten bot (botjumper:18/18)
[bot] OK   Bot leo duoc it nhat 3 bac (steps=18)
[bot] OK   Ky luc ca nhan duoc luu (best=18, matches=1)
[bot] TAT CA DAT
```

`steps=18` means the bot climbed all 18 platforms, which shows the server's jump and landing physics allow the whole route. The step count varies between runs because the bot is simple. The test threshold is 3 or more.

## 3. The Nakama Lua module

You need a Nakama running the module `deploy/nakama/modules/main.lua`. There are two ways.

```bash
# (a) Nakama and Postgres from docker compose (no Kubernetes)
docker compose -f deploy/compose.yaml up -d
until curl -fsS http://127.0.0.1:7350/healthcheck >/dev/null; do sleep 2; done

ALLOW_COMPOSE=1 ./scripts/test-nakama.sh
ALLOW_COMPOSE=1 ./scripts/test-nakama-pool.sh

docker compose -f deploy/compose.yaml down -v
```

```bash
# (b) Nakama in the k3d cluster (the scripts read its own keys from the fomoxa-nakama secret)
./scripts/test-nakama.sh
./scripts/test-nakama-pool.sh
```

`ALLOW_COMPOSE=1` exists because the script refuses to run when the compose Nakama holds port 7350. That protects you from testing the wrong Nakama when you think you are testing the cluster. Set it to say you mean it.

The scripts' steps are expanded into manual `curl` commands in [03, Step 12](03-DEPLOY-KUBERNETES.md#step-12-test-nakama-with-curl): log in two devices, `reset_match`, ana's `find_match` (`waiting`), bo's `find_match` (`matched` plus a ticket), ana's `poll_match`, then the backend's `verify_ticket` (the right person gets `ok:true`, a stranger gets `ok:false`). The pool test sends four players to "auto": pair 1 lands in one room and pair 2 in a different one.

Expected output (real):

```
OK: 2 client ghep dung room, ticket dung, user ngoai bi chan!
...
Chung minh thanh cong: Cap 1 (arena:1) va Cap 2 (arena:2) duoc cap 2 room khac nhau tu pool!
=== DoD P5.5 HOAN TAT THANH CONG! ===
```

PowerShell versions: `scripts\test-nakama.ps1` and `scripts\test-nakama-pool.ps1`.

## 4. Real backend with real Nakama

Goal: run the exact backend code the real cluster uses against a real Nakama (accounts, profiles and friends stored in Nakama storage through the Lua RPCs `kv_get` and `kv_put`, and pairing through Nakama), with real pods issuing tickets. Only the directory is faked.

Or run it in one go (this does all the steps below): `./scripts/test-real-backend.sh`

Manual commands:

```bash
cd ~/fomoxa-infra-demo
WORK=$PWD/.mock/real-test && mkdir -p "$WORK"

# 1) Nakama and Postgres (if not already running)
docker compose -f deploy/compose.yaml up -d
until curl -fsS http://127.0.0.1:7350/healthcheck >/dev/null; do sleep 2; done

# 2) Build the backend, game server and mock cluster
dotnet publish MyGame.Backend/MyGame.Backend/MyGame.Backend.csproj -c Release -p:FomoxaGenerate=false -o "$WORK/backend"
dotnet publish "MyGame/Server~/MyGame.Server.csproj" -c Release -o "$WORK/server"
dotnet publish tools/MockCluster/MockCluster.csproj -c Release -o "$WORK/mock"

# 3) Keys: a random directory.key; pod.key is 32 zero bytes (the selftest logs in to the pod port with it)
head -c 32 /dev/urandom > "$WORK/directory.key"
head -c 32 /dev/zero    > "$WORK/pod.key"

# 4) The REAL backend pointing at the real Nakama (in the background)
Fomoxa__Directory__Address=127.0.0.1:9500 \
Fomoxa__Directory__Key__File="$WORK/directory.key" \
Fomoxa__Pods__Key__File="$WORK/pod.key" \
Fomoxa__Players__WebSocket="" \
Fomoxa__Nakama__Url=http://127.0.0.1:7350 \
Fomoxa__Metrics__Listen=127.0.0.1:9610 \
    dotnet "$WORK/backend/MyGame.Backend.dll" > "$WORK/backend.log" 2>&1 &
BACKEND_PID=$!
sleep 6

# 5) The mock cluster acts ONLY as directory and pods (its own mock backend moves to another port) and runs the selftest against the real backend
dotnet "$WORK/mock/MockCluster.dll" \
    --players=127.0.0.1:19600 --pods-listen=127.0.0.1:19602 --no-tls --pods=4 \
    --server="$WORK/server/MyGame.Server.dll" \
    --selftest-target=9600,9602 --selftest

# 6) Look at the real backend's metrics, then clean up
curl -s http://127.0.0.1:9610/metrics | grep -E '^fomoxa_(logins_total|joins_total|online_players|score_reports_total)'
kill $BACKEND_PID
docker compose -f deploy/compose.yaml down -v
```

The parameters:

- `-p:FomoxaGenerate=false` skips running `fomoxac` during the build and uses the committed generated code. CI checks that separately with `--check`.
- `Fomoxa__X__Y=...` is how an environment variable sets the .NET setting `X:Y` (`__` replaces `:`). `Fomoxa__Nakama__Url` switches the backend to Nakama mode.
- `--selftest-target=9600,9602` runs the selftest against an external backend (players on 9600, pods on 9602) instead of the mock one.
- Player names in the selftest carry a random suffix (`st1a2b3c` and so on), because accounts persist in Nakama. A rerun does not hit "name already taken".

Expected output (real):

```
[selftest] OK   Dang ky tra ve ma khoi phuc (XNM7-NS5F-GBBC-QZV3)
[selftest] OK   Khoi phuc bang ma dung
[selftest] OK   ListRooms (auto[0/2] open, arena:1[0/2] open, arena:2[0/2] open, arena:3[0/2] open, arena:4[0/2] open)
[selftest] OK   JoinRoom auto (2 nguoi) -> 127.0.0.1:7778 / 127.0.0.1:7778 (qua Nakama that)
[selftest] OK   Cap ghep vao cung mot pod
[selftest] OK   JoinRoom phong khong ton tai bi tu choi
[selftest] OK   Bang diem xep hang (st8822e8:3/3, guestx:1/0)
[selftest] OK   Ky luc luu lai la bac cao nhat (best=5, matches=1)
[selftest] OK   Loi moi ket ban den nguoi nhan
[selftest] TAT CA DAT
=== /metrics cua backend ===
fomoxa_joins_total{result="ok"} 2
fomoxa_logins_total{mode="login",result="ok"} 5
fomoxa_logins_total{mode="register",result="ok"} 2
fomoxa_score_reports_total 3
fomoxa_online_players 2
```

If step 5 prints `Connection refused` right at the start of the selftest, the backend was not up yet. Increase `sleep 6`.

Not covered: Kubernetes, Agones, the gateway, `public_address`, UDP through k3d.

## 5. Static checks: generated code, YAML, shell, Lua, monitoring config

```bash
# Is the committed generated code still in step with the models? (non-zero exit if stale)
FOMOXAC=$PWD/MyGame.Backend/tools/fomoxac/linux-x64/fomoxac; chmod +x "$FOMOXAC"
( cd MyGame.Backend/MyGame.Backend && "$FOMOXAC" generate --check )
( cd MyGame && "$FOMOXAC" generate --check )

# Do the links inside the documents (files and #anchors) still resolve?
python3 scripts/check-docs-links.py

# Syntax of every bash script
for f in scripts/*.sh; do bash -n "$f" && echo "ok $f"; done

# Every Kubernetes manifest is valid YAML
python3 - <<'PY'
import glob, yaml
for f in sorted(glob.glob("deploy/k8s/*.yaml") + glob.glob("deploy/monitoring/*.yaml")):
    docs = [d for d in yaml.safe_load_all(open(f)) if d]
    print("ok", f, len(docs), "doc")
PY

# main.lua must be IDENTICAL to the copy embedded in nakama.yaml's ConfigMap
python3 - <<'PY'
import yaml
docs = list(yaml.safe_load_all(open("deploy/k8s/nakama.yaml")))
lua = [d for d in docs if d and d.get("kind") == "ConfigMap"][0]["data"]["main.lua"]
assert lua.strip() == open("deploy/nakama/modules/main.lua").read().strip(), "main.lua differs from the ConfigMap"
print("main.lua == ConfigMap: ok")
PY
```

`main.lua` exists in two places: the file used by docker compose, and the copy embedded in the ConfigMap for Kubernetes. Editing one and forgetting the other is a common mistake, and this check catches it. `promtool` and `amtool` check the monitoring config: see [05, built-in alerts](05-OPERATIONS.md#104-built-in-alerts).

Expected output (real): `Lien ket noi bo: OK`, `fomoxac: up to date` twice, `ok scripts/...` lines, and `main.lua == ConfigMap: ok`.

## 6. `test-p5`: GameServers on Kubernetes and Agones (needs the Unity player)

[03, Step 13](03-DEPLOY-KUBERNETES.md#step-13-test-with-the-unity-client-real-players) shows what the script does step by step.

```bash
./scripts/test-p5.sh                 # four automatic clients, checks exactly 2 Allocated pods
TEST_SCALE=1 ./scripts/test-p5.sh    # also scale the Fleet up by one, then back
```

Requirements: a running k3d cluster ([03](03-DEPLOY-KUBERNETES.md)), `MyGame/Builds/MyGame.exe` ([04](04-UNITY-CLIENT.md)), and WSL2 with `powershell.exe`. From plain PowerShell: `powershell -ExecutionPolicy Bypass -File scripts\test-p5.ps1`.

This was not run for real while writing.

## 7. Manual script in Unity

Run this once after any large client change, with the mock cluster as the backend for speed.

| # | Action | Expected |
|---|---|---|
| 1 | Open `Lobby.unity` and press Play | A login form with Login, Register and Forgot password tabs |
| 2 | Register with a password under 8 characters | A red message saying the password needs at least 8 characters |
| 3 | A valid registration | The recovery code appears (copyable); "I saved the code" takes you to the lobby |
| 4 | Log out, then log in with a wrong password | "sai tên hoặc mật khẩu" |
| 5 | Forgot password with the right code and a new password | You are in, and a new recovery code is shown |
| 6 | Account tab: change the password | "Đã đổi mật khẩu" |
| 7 | Click `arena:1` | The `Game` scene loads only after the ticket arrives; the character appears |
| 8 | Camera | Behind the character; right mouse or Q and E rotate; the wheel zooms; W moves toward where the camera looks |
| 9 | Space in a match | Jumps; does not leave the room; the HUD stays hidden |
| 10 | Jump onto platforms 1, 2, 3... | You can stand on top; walking sideways into a platform's body is blocked |
| 11 | Esc or Tab | The menu and scoreboard appear (with your step count); Esc again hides them |
| 12 | Two clients, become friends, invite to a room | The invite shows on the other player's Friends tab; Join takes them to the room |
| 13 | Leave room | Back to the lobby, still logged in; the record in the profile is updated |

## How this maps to CI (GitHub Actions)

`.github/workflows/ci.yml` runs on every push and pull request:

| Job | Steps | Tests above |
|---|---|---|
| `build-and-selftest` | `fomoxac generate --check` (both trees), build the backend, build the game server, `--selftest`, `--bot-test` | 1, 2, 5 |
| `real-backend` | `./scripts/test-real-backend.sh`, then `test-nakama.sh` and `test-nakama-pool.sh` against compose | 3, 4 |
| `lint-scripts-and-manifests` | document links, `bash -n`, valid YAML, `promtool` and `amtool`, `main.lua` equals the ConfigMap | 5 |

To rerun exactly what CI does, use the commands in sections 1 to 5: they are the contents of those steps.
