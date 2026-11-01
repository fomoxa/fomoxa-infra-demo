# 02 Quick start with the mock cluster (no Kubernetes)

The mock cluster (`tools/MockCluster`) runs on one machine with no Docker, k3d, Agones or Nakama. It provides what the Unity client needs: a backend, a directory, and real game servers. Use it to try the client, develop features, run quick tests, or see the system work before building the real cluster.

```
 Unity client --TCP 9600--> mock backend --+   (login, rooms, friends, scores: the real fRPC protocol)
              --TCP 9643--> TLS endpoint --+   (optional, like the real cluster's TLS proxy)
              --UDP 7778..7781--> 4 real pods  (MyGame.Server with the real jump and landing physics)
                                   ^
                       mock directory (9500) issues tickets; pods report scores to port 9602
```

It differs from the real cluster in a few ways. Matchmaking is instant instead of waiting for two players as Nakama does. Accounts live in memory. There is no Agones or gateway, and keys are not checked. Use it only on a local machine.

Everything in this document was run while writing it (output labeled "real"), except the Unity part. You need the .NET 8 SDK ([01, Step 7](prepare-environment.md#step-7-install-the-net-8-sdk)).

## Step 1. Build the game server and the mock cluster

Goal: two compiled programs, `MyGame.Server` (the pod) and `MockCluster`.

```bash
cd ~/fomoxa-infra-demo

dotnet publish "MyGame/Server~/MyGame.Server.csproj" -c Release -o .mock/server
dotnet publish tools/MockCluster/MockCluster.csproj   -c Release -o .mock/cluster
```

`dotnet publish ... -c Release -o <dir>` compiles in Release mode and collects everything needed to run into `<dir>`. The first run takes a few minutes while `dotnet` downloads NuGet packages (AgonesSDK, BouncyCastle). The `.mock/` directory is in `.gitignore`.

Or run it in one go: `./scripts/run-mock-cluster.sh`. It runs those two `publish` commands and then starts the cluster from Step 2.

Expected output (sample):

```
  MyGame.Server -> /home/you/fomoxa-infra-demo/MyGame/Server~/bin/Release/net8.0/MyGame.Server.dll
  MyGame.Server -> /home/you/fomoxa-infra-demo/.mock/server/
  MockCluster -> /home/you/fomoxa-infra-demo/tools/MockCluster/bin/Release/net8.0/MockCluster.dll
  MockCluster -> /home/you/fomoxa-infra-demo/.mock/cluster/
```

Check:

```bash
ls .mock/server/MyGame.Server.dll .mock/cluster/MockCluster.dll
```

Common errors:

| Symptom | Fix |
|---|---|
| `dotnet: command not found` | [01, Step 7](prepare-environment.md#step-7-install-the-net-8-sdk) |
| `error NU1301: Unable to load the service index for source https://api.nuget.org` | the machine cannot reach NuGet. Check the network or proxy and rerun |
| `The type or namespace name ... could not be found` right after `git pull` | the generated code is out of step with the models. See [04, regenerating code](../unity-client.md#regenerating-code-after-changing-a-model-or-rpc) |

## Step 2. Start the mock cluster

Goal: start the backend (9600), directory (9500), TLS endpoint (9643) and four real pods (UDP 7778 to 7781).

```bash
dotnet .mock/cluster/MockCluster.dll --server=.mock/server/MyGame.Server.dll
```

`MockCluster.dll` opens the mock services and then launches four `MyGame.Server` processes itself, one UDP port each, each holding one room `arena:N`. `--server=` tells it where the pod is. Keep this window open and stop it with Ctrl+C.

Expected output (real):

```
[Mock] directory giả  : 0.0.0.0:9500
[Mock] backend giả    : 0.0.0.0:9600  (client Unity: 127.0.0.1:9600)
[Mock] cổng pod giả   : 0.0.0.0:9602
[Mock] backend TLS    : cổng 9643  pin SHA-256 = C5B502A6036815A39E6E60AC21398651BFFB9AD4FF19BDD5C6397D9FF74C5167
[Mock]   Unity: -backend:127.0.0.1:9643 -tlspin:C5B502A6036815A39E6E60AC21398651BFFB9AD4FF19BDD5C6397D9FF74C5167
[Mock] tài khoản: trong bộ nhớ (mất khi tắt; dùng --data=file.json để giữ lại)
[pod-4] [Backend] bao so nguoi choi ve 127.0.0.1:9602
[pod-1] [Pod] registering mock-pod-1 (127.0.0.1:7778, version 1.0) at 127.0.0.1:9500
[Directory] pod #1 'mock-pod-4' 127.0.0.1:7781 v1.0 dang ky
[pod-4] [Pod] registered id=1 changed=False
[pod-4] [Pod] listening on udp 7781
[Directory] pod #1 giu room arena:1
[pod-4] [Pod] claimed room from pool: arena:1
...
[Mock] 4 pod da dang ky. Cum gia san sang. Ctrl+C de tat.
```

Your `pin SHA-256` will differ. It is generated once and saved in `.mock/mock-tls.pfx`.

Check, from a second terminal:

```bash
timeout 3 bash -c '</dev/tcp/127.0.0.1/9600' && echo "9600 open"
timeout 3 bash -c '</dev/tcp/127.0.0.1/9643' && echo "9643 open"
timeout 3 bash -c '</dev/tcp/127.0.0.1/9500' && echo "9500 open (mock directory)"
```

`/dev/tcp/host/port` is bash's way of opening a TCP connection. If the port is open, the command prints the line after `&&`.

Common errors:

| Symptom | Fix |
|---|---|
| `Address already in use` (9600, 9500, 9643, 9602) | another process holds the port: `ss -ltnp \| grep -E '9600\|9500\|9643\|9602'`. Change ports with `--players=0.0.0.0:19600` and similar flags, or stop the other process. The k3d cluster also maps 9600 and 9643, so do not run both at once |
| `khong thay MyGame.Server.dll` (cannot find the server DLL) | wrong `--server=` path. Check `ls .mock/server/MyGame.Server.dll` |
| `pod chua dang ky du sau 30 giay` (pods not registered after 30 s) | read the `[pod-N]` lines above it. The usual cause is a pod that cannot reach port 9500 |

## Step 3. Connect the Unity client

Goal: register an account, see the rooms, join one, and jump onto the platforms.

The Unity client defaults to `127.0.0.1:9600`, which is where the mock cluster listens, so it needs no flags. Run the Windows build:

```powershell
.\Builds\MyGame.exe
```

or press Play in the Unity Editor with `Assets/Scenes/Lobby.unity` open. Building the client is covered in [04](../unity-client.md).

In the game:

1. Open the Register tab, enter a name (3 to 24 characters: letters, digits, `_` or `-`) and a password of at least 8 characters, then choose Create account.
2. Save the recovery code that appears. It is shown once. Choose "I saved the code".
3. On the Rooms tab, click `arena:1` (or "Auto matchmaking").
4. In the match, WASD moves (relative to the camera), Space jumps, the right mouse button or Q and E rotate the camera, and Esc or Tab opens the menu and scoreboard.

To try TLS, start the client with `-backend:127.0.0.1:9643 -tlspin:<the PIN printed in Step 2>`.

To keep accounts between runs, add `--data=.mock/accounts.json` to the Step 2 command.

## Step 4. Check it without Unity

The mock cluster has two automatic tests. Each builds its own cluster, so stop the running one first.

### 4.1 `--selftest`: accounts, rooms, friends, scores, TLS

```bash
dotnet .mock/cluster/MockCluster.dll --server=.mock/server/MyGame.Server.dll --pods=2 --selftest
```

A client connects to the mock backend over the real fRPC protocol. It registers, recovers and changes passwords, puts two players in a room, makes friends, sends an invite, reports scores from a stand-in pod, and connects over TLS (the right pin is accepted and a wrong pin is rejected). The exit code is 0 when everything passes.

Or run it in one go: `./scripts/run-mock-cluster.sh --selftest --pods=2`

Expected output (real, shortened: some lines are removed, and your codes and ports will differ):

```
[selftest] OK   Dang ky trung ten bi tu choi
[selftest] OK   Dang ky mat khau yeu bi tu choi
[selftest] OK   Sai mat khau bi tu choi
[selftest] OK   Dang ky tra ve ma khoi phuc (CXNZ-UN6J-UVGD-4JGS)
[selftest] OK   Khoi phuc bang ma dung
[selftest] OK   Ma khoi phuc cu het hieu luc
[selftest] OK   ListRooms (auto[0/2] open, arena:1[0/16] open, arena:2[0/16] open)
[selftest] OK   JoinRoom auto (2 nguoi) -> 127.0.0.1:7778 / 127.0.0.1:7778
[selftest] OK   Presence (st8822e8@arena:1)
[selftest] OK   Doi mat khau
[selftest] OK   Bang diem xep hang (st8822e8:3/3, guestx:1/0)
[selftest] OK   Ky luc luu lai la bac cao nhat (best=5, matches=1)
[selftest] OK   Thanh ban be hai chieu
[selftest] OK   Moi ban vao phong
[selftest] TAT CA DAT
[selftest] OK   Ket noi qua TLS (dung pin) va goi RPC (3 phong)
[selftest] TLS DAT
```

Check `echo $?` right after the command: it should print `0`. Any `FAIL` or `CO LOI` line means a part failed.

### 4.2 `--bot-test`: a bot climbs the platforms over real UDP

```bash
dotnet .mock/cluster/MockCluster.dll --server=.mock/server/MyGame.Server.dll --pods=2 --bot-test
```

A bot registers, asks for a ticket, connects to a pod over real encrypted UDP, and walks and jumps up the platforms using the positions the server sends back, for about 25 seconds. It then checks the scoreboard and the saved record. This is the closest thing to a real player that does not need Unity.

Or run it in one go: `./scripts/run-mock-cluster.sh --bot-test --pods=2`

Expected output (real, shortened):

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

Put `BOT_TRACE=1` before the command to print the bot's position over time.

## MockCluster options

| Option | Default | Meaning |
|---|---|---|
| `--server=<path>` | required unless `--no-spawn` | the `MyGame.Server.dll` used to launch pods |
| `--pods=N` | 4 | number of pods (rooms `arena:1..N`) |
| `--base-port=P` | 7778 | UDP port of the first pod; later pods count up |
| `--players=host:port` | `0.0.0.0:9600` | mock backend port |
| `--directory=host:port` | `0.0.0.0:9500` | mock directory port |
| `--pods-listen=host:port` | `0.0.0.0:9602` | port where pods report scores |
| `--tls-listen=host:port`, `--no-tls` | `0.0.0.0:9643` | TLS endpoint, or turn it off |
| `--data=<file.json>` | memory | save accounts to a file |
| `--match-delay=<seconds>` | 0 | simulate matchmaking wait time |
| `--strict-version` | off | require the client version to equal the pod version |
| `--no-spawn` | off | do not launch pods; you run them yourself |
| `--selftest`, `--bot-test`, `--probe=host:port`, `--selftest-target=9600,9602` | | test modes, see [06](../testing.md) |

## Checklist

- [ ] Both `dotnet publish` commands finished and `.mock/server/MyGame.Server.dll` and `.mock/cluster/MockCluster.dll` exist
- [ ] The cluster prints `4 pod da dang ky. Cum gia san sang`
- [ ] `--selftest` ends with `TAT CA DAT` and `TLS DAT`
- [ ] `--bot-test` ends with `TAT CA DAT`
- [ ] With Unity: register, join `arena:1`, and jump

Next: [03 Deploy to Kubernetes](../deploy/README.md) for the real cluster, or [04 Unity client](../unity-client.md).
