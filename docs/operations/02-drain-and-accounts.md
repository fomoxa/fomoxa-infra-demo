# 05.2 Safe shutdown (drain) and player accounts

Top of this document: [05-OPERATIONS](README.md) · Previous: [01-status-and-updates.md](01-status-and-updates.md) · Next: [03-tls-and-keys.md](03-tls-and-keys.md)

## 6. Shutting down a game server safely (drain)

Goal: stop or replace a game server without cutting off players.

How it works (implemented in `MyGame/Server~/Program.cs`): when the pod gets `SIGTERM` (Kubernetes sends it when a pod is deleted), the game server calls `RequestDrain` on the directory. The directory stops assigning new players and rooms to that pod, and the pod waits until no players remain before exiting. It waits at most `drain_seconds = 300`, after which the directory forces it to stop. Kubernetes gives the pod `terminationGracePeriodSeconds: 360` before `SIGKILL`, and 360 must stay larger than 300. A second `SIGTERM` makes it exit at once.

To try it:

```bash
# Find a GameServer that has players (STATE=Allocated)
kubectl get gs -n fomoxa
GS=<gameserver-name>

# Delete it and watch the log
kubectl delete gs "$GS" -n fomoxa --wait=false
kubectl logs -n fomoxa "$GS" -c fomoxa-server -f --tail=5
```

Expected output (the line formats are real):

```
[Pod] SIGTERM: xin drain, cho het nguoi choi (gui tin hieu lan nua de thoat ngay)
...(players keep playing normally)...
[Pod] drain: het nguoi choi, thoat
```

For a pod with no players (`Ready`) the last line appears almost immediately, and the Fleet creates a replacement GameServer.

This was checked at the process level (the pod exits when players are gone). It has not been run fully against the real directory with `kubectl delete` on a cluster.

## 7. Accounts and player data

Accounts, passwords (hashed with PBKDF2-SHA256, 120,000 iterations, a separate salt each), recovery codes (only the hash is stored), profiles and friends live in Nakama storage (the collections `account`, `profile` and `social`). They are reached through two internal RPCs, `kv_get` and `kv_put`, which only the backend can call because it holds the `http_key`.

Players handle their own accounts in the game: register, change the password (Account tab), recover a forgotten password (Forgot password tab with the recovery code), add friends, and invite friends to a room.

An administrator can check whether an account exists. This returns the password hash, so treat the output as sensitive:

```bash
BASE=http://127.0.0.1:7350
HTTP_KEY=$(kubectl get secret fomoxa-nakama -n fomoxa -o jsonpath='{.data.http-key}' | base64 -d)
HTTP_CREDS=$(printf '%s:' "$HTTP_KEY" | base64)

curl -s -X POST "$BASE/v2/rpc/kv_get?unwrap" -H "Authorization: Basic $HTTP_CREDS" \
     -H 'Content-Type: application/json' -d '{"collection":"profile","key":"ana"}'; echo
```

The reply is `{"found":false}` if there is no record, or `{"found":true,"value":"{\"BestSteps\":5,...}","version":"..."}`. The `key` is the lowercase name. The collections are `account` (password hash, sensitive), `profile` (record, match count) and `social` (friends).

Brute-force protection: five wrong passwords in a row lock that name for 60 seconds. Watch the metric `fomoxa_logins_total{result="fail"}` and the `HighLoginFailures` alert.

A player who loses both the password and the recovery code cannot be reset by any tool. As a workaround, delete their `account` record in the Nakama console (the Storage page, collection `account`) so they can register the name again. The old account is lost, and the `profile` record remains unless you delete it too.

## Under the hood: the clock of one drain

The numbers come from `deploy/k8s/directory.yaml` and `fleet.yaml`:

```
t = 0      kubectl delete gs ... (or a rollout): Kubernetes sends SIGTERM
t = 0      the pod calls RequestDrain: the directory stops assigning new players and rooms to it
t = 0..300 the pod waits for current players to leave (drain_seconds = 300)
t = 300    the directory forces a stop if players remain
t = 360    Kubernetes sends SIGKILL (terminationGracePeriodSeconds = 360)
```

Why 360 is larger than 300: if the two were equal, Kubernetes could kill the pod just as the directory is still closing the room. The 60 second gap gives the pod time to report `Shutdown` to Agones and exit cleanly.

Watch the states during a drain in another window:

```bash
kubectl get gs -n fomoxa -w
```

The old GameServer moves from `Allocated` to `Shutdown`, and the Fleet creates a new one that goes `Starting` then `Ready`. New players arriving meanwhile are placed on another pod, so the `Ready` count dips by one for a while.

### When a drain does not finish

| Sign | Common cause | What to do |
|---|---|---|
| Pod stays `Terminating` for exactly six minutes, then vanishes | players did not leave; SIGKILL after the deadline | expected; next time lower `drain_seconds` or warn players |
| No `xin drain` line in the log | the process never received SIGTERM (wrapped in a shell) | check the image `ENTRYPOINT` runs the binary directly |
| Pod disappears at once and players are dropped | `--grace-period=0` or `--force` was used | do not use those flags on pods with players |

## Where account data lives

Three collections in Nakama storage. The key is always the lowercase player name:

| Collection | Content | Sensitivity |
|---|---|---|
| `account` | password hash, salt, recovery code hash (the failure count lives only in backend memory and resets on restart) | high |
| `profile` | `BestSteps`, match count, stats | low |
| `social` | friends list, invitations | low |

To see all of it in a UI, open the Nakama console:

```bash
kubectl port-forward svc/nakama-console 7351:7351 -n fomoxa
```

Then browse to `http://127.0.0.1:7351`, user `admin`, and read the password from the secret:

```bash
kubectl get secret fomoxa-nakama -n fomoxa -o jsonpath='{.data.console-password}' | base64 -d; echo
```

In the console, the *Storage* section lists the three collections. Treat it as read only and do not edit `account` records by hand.
