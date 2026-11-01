# 03.1 Prepare, free ports, keys and certificate

Top of this document: [03-DEPLOY-KUBERNETES](README.md) · Next: [02-build-images.md](02-build-images.md)

## Step 1. Prepare and check free ports

Goal: stand in the right directory and make sure the ports the cluster will take are free.

```bash
cd ~/fomoxa-infra-demo

# Is Docker running?
docker info --format 'Docker {{.ServerVersion}} - OK'

# Does any container already hold a port we need? (compose Nakama, an old cluster, ...)
docker ps --format '{{.Names}}\t{{.Ports}}' | grep -E ':(7777|9600|9643|7350)->' || echo "The ports are free"

# Does any process outside Docker listen on them? (the mock cluster from document 02, ...)
ss -ltnu | grep -E ':(7777|9600|9643|7350)\b' || echo "Nothing is listening"
```

k3d forwards four ports of your machine into the cluster. If one is taken, `k3d cluster create` fails partway through.

Expected output:

```
Docker 27.3.1 - OK
The ports are free
Nothing is listening
```

Common errors:

| Symptom | Fix |
|---|---|
| `fomoxa-test-nakama-1` holds `7350` | That is the local-development `docker compose`. Run `docker compose -f deploy/compose.yaml down`. If you leave it up, the test scripts hit the compose Nakama and can report a false success |
| The mock cluster from document 02 is running | Stop it with Ctrl+C. It also uses 9600 and 9643 |
| Docker is not running | [01, Step 3](../getting-started/prepare-environment.md#step-3-install-docker) |

## Step 2. Generate keys and the certificate

Goal: create the random keys the services use to authenticate each other, separate secrets for Nakama, and a TLS certificate. Everything goes into `deploy/secrets/`, which is in `.gitignore`. Never commit it.

Why: the directory only accepts a backend, pod or gateway that presents the right key. The backend only accepts pods that have its own key. Nakama uses its own keys instead of `defaultkey`.

```bash
mkdir -p deploy/secrets/directory deploy/secrets/nakama deploy/secrets/tls

# gen <file> <bytes>: write <bytes> random bytes as hex (2 characters per byte), no trailing newline
gen() { openssl rand -hex "$2" | tr -d '\n' > "$1"; echo "  + $1 ($(wc -c < "$1") characters)"; }

# --- keys for the directory and the programs that connect to it ---
gen deploy/secrets/directory/root     32   # the directory's root key (MUST be exactly 64 hex characters)
gen deploy/secrets/directory/admin    24   # admin rights (drain, read the pod list)
gen deploy/secrets/directory/backend  24   # backend asks for tickets
gen deploy/secrets/directory/gateway  24   # gateway reads the pod list
gen deploy/secrets/directory/pod      24   # game servers register rooms
gen deploy/secrets/backend-pod        24   # game servers report player counts and scores to the backend (port 9602)

# --- Nakama: its own keys and passwords (instead of defaultkey, defaulthttpkey, admin:admin, localdb) ---
gen deploy/secrets/nakama/server-key       16   # clients and the backend log in to Nakama
gen deploy/secrets/nakama/http-key         24   # only the backend calls internal RPCs (kv_get, verify_ticket, ...)
gen deploy/secrets/nakama/console-password 16   # Nakama admin console password
gen deploy/secrets/nakama/session-key      24   # encrypts session tokens
gen deploy/secrets/nakama/refresh-key      24   # encrypts refresh tokens
gen deploy/secrets/nakama/db-password      16   # Postgres password

# --- self-signed TLS certificate for the TLS proxy (port 9643) ---
openssl req -x509 -newkey rsa:2048 -nodes -days 730 \
    -keyout deploy/secrets/tls/tls.key -out deploy/secrets/tls/tls.crt \
    -subj "/CN=fomoxa.local" \
    -addext "subjectAltName=DNS:fomoxa.local,DNS:localhost,IP:127.0.0.1" \
    -addext "extendedKeyUsage=serverAuth" 2>/dev/null
chmod 600 deploy/secrets/tls/tls.key

# SHA-256 fingerprint of the certificate (the "pin"). The client uses it to confirm it talks to the right proxy
openssl x509 -in deploy/secrets/tls/tls.crt -outform DER | openssl dgst -sha256 -hex | awk '{print toupper($NF)}'
```

`openssl rand -hex N` prints N random bytes as 2N hex characters, and `tr -d '\n'` removes the trailing newline (a key must not have an extra line). The self-signed certificate is for development and internal use. For a public deployment use a certificate from a CA (see [05, TLS](../operations/03-tls-and-keys.md#tls-between-client-and-backend)).

Or run it in one go: `./scripts/gen-keys.sh` writes the 12 keys above, and `./scripts/gen-tls-cert.sh` writes the certificate and prints the pin. Neither overwrites an existing file unless you pass `-f`.

Expected output (from `gen-keys.sh` and `gen-tls-cert.sh`):

```
=== Sinh keys cho Fomoxa ===
  + directory/root
  + directory/admin
  + directory/backend
  + directory/gateway
  + directory/pod
  + secrets/backend-pod
  + nakama/server-key
  + nakama/http-key
  + nakama/console-password
  + nakama/session-key
  + nakama/refresh-key
  + nakama/db-password
Hoan tat sinh keys tai /home/you/fomoxa-infra-demo/deploy/secrets
Da tao /home/you/fomoxa-infra-demo/deploy/secrets/tls/tls.crt va tls.key (CN=fomoxa.local)
Van tay SHA-256 (pin): FB3FF3780A89AD4A3CB83402C454F0247AA0238C1B304EA8DB475497B73C94DE
```

Check:

```bash
find deploy/secrets -type f | sort
wc -c deploy/secrets/directory/root deploy/secrets/directory/backend deploy/secrets/nakama/http-key
```

You should see 14 files (12 keys plus `tls.crt` and `tls.key`). `root` is exactly 64 bytes, `backend` is 48 and `http-key` is 48.

Common errors:

| Symptom | Fix |
|---|---|
| `openssl: command not found` | `sudo apt install -y openssl` |
| The `root` key is not 64 characters | Recreate it with `gen deploy/secrets/directory/root 32`. The directory rejects a root key of the wrong length |
| An old cluster is running with old keys and you regenerated them | The secrets in the cluster are still the old ones, so they no longer match. Redo [Step 8](04-secrets.md#step-8-namespace-and-secrets) and restart the deployments |
