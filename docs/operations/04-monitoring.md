# 05.4 Monitoring

Top of this document: [05-OPERATIONS](README.md) · Previous: [03-tls-and-keys.md](03-tls-and-keys.md) · Next: [05-scaling-security-backup.md](05-scaling-security-backup.md)

## Monitoring

Goal: metrics (Prometheus), charts (Grafana), alerts (Alertmanager) and central logs (Loki).

### 10.1 Install

```bash
kubectl apply -f deploy/monitoring/00-namespace.yaml
kubectl create secret generic grafana-admin -n monitoring --from-literal=password="$(openssl rand -hex 12)"   # first time only
kubectl apply -f deploy/monitoring/
kubectl rollout status deployment/prometheus   -n monitoring
kubectl rollout status deployment/grafana      -n monitoring
```

Or run it in one go: `INSTALL_MONITORING=1 ./scripts/k8s-up.sh` runs these commands.

This creates a separate `monitoring` namespace, a secret with a random Grafana admin password, and then Prometheus, Alertmanager, Grafana, Loki and Promtail.

Check: `kubectl get pods -n monitoring` shows prometheus, alertmanager, grafana and loki `Running`, and one `promtail` per node.

### 10.2 Open the interfaces

```bash
kubectl port-forward -n monitoring svc/grafana 3000:3000        # http://127.0.0.1:3000  (user: admin)
kubectl get secret grafana-admin -n monitoring -o jsonpath='{.data.password}' | base64 -d; echo
kubectl port-forward -n monitoring svc/prometheus 9090:9090     # http://127.0.0.1:9090/targets
kubectl port-forward -n monitoring svc/alertmanager 9093:9093   # http://127.0.0.1:9093
```

In Grafana, open the Fomoxa folder and the "Fomoxa - tổng quan" dashboard (online players, Ready GameServers, logins per second, players per pod, highest step per pod). To read logs, use Explore, pick the Loki source, and query `{namespace="fomoxa", app="backend"}` or `{pod=~"fomoxa-server-.*"}`.

In Prometheus, Status, then Targets should list these as `UP`: the backend (`:9610`), the GameServers (`:9611`), nakama (`:9100`) and the Agones controller.

### 10.3 Fomoxa's metrics

| Metric | Type | Source | Meaning |
|---|---|---|---|
| `fomoxa_online_players` | gauge | backend | players who reported "still online" in the last 20 seconds |
| `fomoxa_logins_total{mode,result}` | counter | backend | logins, registrations and recoveries, ok or fail |
| `fomoxa_joins_total{result}` | counter | backend | successful room joins |
| `fomoxa_rooms_reported`, `fomoxa_room_players` | gauge | backend | rooms and total players as reported by pods |
| `fomoxa_score_reports_total`, `fomoxa_password_changes_total`, `fomoxa_friend_requests_total`, `fomoxa_room_invites_total` | counter | backend | social and score activity |
| `fomoxa_pod_players`, `fomoxa_pod_rooms`, `fomoxa_pod_registered`, `fomoxa_pod_draining`, `fomoxa_pod_best_steps`, `fomoxa_pod_uptime_seconds` | gauge | each game server | the state of one pod |
| `agones_fleets_replicas_count{type}` | gauge | Agones | the Fleet: `total`, `allocated`, `ready`, `desired` |

### 10.4 Built-in alerts

| Alert | Condition | Level |
|---|---|---|
| `BackendDown` | the backend's metrics cannot be scraped for 1 minute | critical |
| `NakamaDown` | Nakama's metrics cannot be scraped for 1 minute | critical |
| `NoReadyGameServer` | no `Ready` GameServer for 2 minutes | critical |
| `GameServerNotRegistered` | a pod runs but has not registered with the directory for 2 minutes | warning |
| `HighLoginFailures` | more than 1 failed login per second for 5 minutes (password guessing) | warning |
| `FleetAtMaxReplicas` | the Fleet has been at the 16-pod ceiling for 10 minutes | warning |
| `PodNearlyFull` | 14 or more players on one pod for 5 minutes | info |

By default Alertmanager only groups alerts and sends them nowhere. To receive notifications, edit `receivers` in `deploy/monitoring/20-alertmanager.yaml` (Slack and webhook examples are there as comments), then:

```bash
kubectl apply -f deploy/monitoring/20-alertmanager.yaml
kubectl rollout restart deployment/alertmanager -n monitoring
```

To validate the configuration before applying it (needs Docker):

```bash
# extract the config to files, then check them with the official tool
python3 - <<'PY'
import yaml
d = list(yaml.safe_load_all(open("deploy/monitoring/10-prometheus.yaml")))
pick = lambda n: [x for x in d if x.get("metadata", {}).get("name") == n][0]["data"]
import os; os.makedirs("/tmp/prom/rules", exist_ok=True)
open("/tmp/prom/prometheus.yml", "w").write(pick("prometheus-config")["prometheus.yml"].replace("/etc/prometheus/rules/*.yml", "/tmp/prom/rules/*.yml"))
open("/tmp/prom/rules/fomoxa.yml", "w").write(pick("prometheus-rules")["fomoxa.yml"])
PY
docker run --rm --entrypoint promtool -v /tmp/prom:/tmp/prom prom/prometheus:v2.55.1 check config /tmp/prom/prometheus.yml
```

Expected output:

```
Checking /tmp/prom/prometheus.yml
  SUCCESS: 1 rule files found
 SUCCESS: /tmp/prom/prometheus.yml is valid prometheus config file syntax
```

Prometheus, Loki and Grafana store data in `emptyDir`, so it is lost when their pods restart. That is fine for development and demos. Production needs persistent volumes.
