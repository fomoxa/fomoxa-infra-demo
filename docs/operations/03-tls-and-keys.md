# 05.3 TLS and Nakama keys

Top of this document: [05-OPERATIONS](README.md) · Previous: [02-drain-and-accounts.md](02-drain-and-accounts.md) · Next: [04-monitoring.md](04-monitoring.md)

## TLS between client and backend

The backend's fRPC does not encrypt by itself, and the login password travels over it. In production, put the backend behind a TLS proxy (`tls-proxy`, nginx, port 9643). The UDP game traffic is already encrypted by Fomoxa's ticket mechanism and needs nothing more.

### 8.1 See which certificate the proxy presents

```bash
# The fingerprint of the certificate the server PRESENTS (compare with the client's pin)
openssl s_client -connect 127.0.0.1:9643 -servername fomoxa.local </dev/null 2>/dev/null \
    | openssl x509 -noout -fingerprint -sha256 -subject
```

Expected output (against the mock cluster's TLS endpoint):

```
sha256 Fingerprint=C5:B5:02:A6:03:68:15:A3:9E:6E:60:AC:21:39:86:51:BF:FB:9A:D4:FF:19:BD:D5:C6:39:7D:9F:F7:4C:51:67
subject=CN = fomoxa-mock
```

Remove the colons and you have the exact string for the client's `-tlspin:` (`C5B502A6...5167`).

### 8.2 Replace the certificate

```bash
./scripts/gen-tls-cert.sh -f                     # recreate tls.crt and tls.key, print the NEW pin
kubectl create secret tls fomoxa-tls -n fomoxa \
    --cert=deploy/secrets/tls/tls.crt --key=deploy/secrets/tls/tls.key \
    --dry-run=client -o yaml | kubectl apply -f -
kubectl rollout restart deployment/tls-proxy -n fomoxa
```

Then update the pin in every client (`-tlspin:` or the Tls Pin Sha256 field of `RoomJoiner`). Otherwise clients refuse the connection with a fingerprint mismatch.

### 8.3 Allow only the encrypted path

```bash
kubectl patch svc backend -n fomoxa -p '{"spec":{"type":"ClusterIP"}}'     # close 9600 to the outside
# (or on first run: TLS_ONLY=1 ./scripts/k8s-up.sh)
```

After that, clients must use `-backend:127.0.0.1:9643 -tlspin:...`. A k3d cluster still maps port 9600 to your machine, but with the service no longer a `LoadBalancer` nothing answers behind it. To undo it, run `kubectl apply -f deploy/k8s/backend.yaml`.

### 8.4 Production with a domain name

Use a certificate from a public CA (for example cert-manager with Let's Encrypt) for the `fomoxa-tls` secret. Then no pin is needed: the client checks against the Mozilla root store built into the library, and you pass only `-tls`. Point the domain at the `tls-proxy` service's IP. The certificate must list that domain in its SAN.

## 9. Nakama keys and passwords

The Kubernetes cluster no longer uses `defaultkey`, `defaulthttpkey`, `admin:admin`, or the database password `localdb`. They are in the secret `fomoxa-nakama`, which `nakama`, `backend` and `postgres` read. (`deploy/compose.yaml`, used only for local development, keeps the defaults.)

To see the current values:

```bash
for k in server-key http-key console-password; do
  printf '%-18s ' "$k"; kubectl get secret fomoxa-nakama -n fomoxa -o jsonpath="{.data.$k}" | base64 -d; echo
done
```

To rotate a key, not including the database password:

```bash
openssl rand -hex 24 | tr -d '\n' > deploy/secrets/nakama/http-key       # for example, rotate the http-key
kubectl create secret generic fomoxa-nakama -n fomoxa \
    --from-file=server-key=deploy/secrets/nakama/server-key \
    --from-file=http-key=deploy/secrets/nakama/http-key \
    --from-file=console-password=deploy/secrets/nakama/console-password \
    --from-file=session-key=deploy/secrets/nakama/session-key \
    --from-file=refresh-key=deploy/secrets/nakama/refresh-key \
    --from-file=db-password=deploy/secrets/nakama/db-password \
    --dry-run=client -o yaml | kubectl apply -f -
kubectl rollout restart deployment/nakama deployment/backend -n fomoxa
```

Things to know:

- Changing `session-key` or `refresh-key` invalidates every existing session, so players must log in again.
- Changing `db-password` stops Nakama from starting on a Postgres volume that already has data, because the password is stored inside the Postgres data. To change it, first run `ALTER USER postgres PASSWORD '...'` inside Postgres and then change the secret, or accept losing the data (`kubectl delete pvc postgres-data -n fomoxa`).
- The `test-nakama*.sh` scripts read the keys from the secret by themselves. To force the default keys when testing the `docker compose` Nakama: `NAKAMA_USE_DEFAULT_KEYS=1 ./scripts/test-nakama.sh`.
