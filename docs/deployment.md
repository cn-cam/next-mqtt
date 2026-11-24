# Deployment

NextMQTT ships as a multi-architecture container image, Kustomize manifests and
a Helm chart. The same image runs a single node or a cluster member; clustering
is enabled entirely through configuration.

::: warning State is in memory
Sessions, queues and retained messages do not survive a broker restart in this
release. Plan rollouts and capacity accordingly. Persistence is on the
[roadmap](/roadmap).
:::

## Container image

```bash
docker pull ghcr.io/cn-cam/next-mqtt:latest
```

Available tags:

- `latest` — newest stable build from `main`
- `0.1.0`, `0.1` — release tags
- `sha-<commit>` — immutable commit builds

Platforms: `linux/amd64`, `linux/arm64`.

### Build locally

```bash
docker build -f deploy/docker/Dockerfile -t nextmqtt:local .
```

The build compiles the Vue dashboard, publishes the .NET server, copies the
bundle into `wwwroot` and runs as UID 10001.

### Run

```bash
docker run -d --name nextmqtt \
  -p 1883:1883 \
  -p 8080:8080 \
  -p 8083:8083 \
  -e NEXTMQTT_ADMIN_PASSWORD='change-me-now' \
  -e NEXTMQTT_ALLOW_ANONYMOUS=false \
  ghcr.io/cn-cam/next-mqtt:latest
```

The image includes a `HEALTHCHECK` against `/healthz`.

| Port | Purpose |
| --- | --- |
| 1883 | MQTT TCP |
| 8883 | MQTT TLS (enable with `Server:Tls`) |
| 8080 | API, dashboard, metrics |
| 8083 | MQTT WebSocket |
| 8084 | HTTPS/WSS (enable with `Server:Http:CertificatePath`) |

## Docker Compose

### Single node

```bash
cp .env.example .env
# edit NEXTMQTT_ADMIN_PASSWORD and NEXTMQTT_ADMIN_TOKEN_SECRET
docker compose up -d
docker compose logs -f nextmqtt
```

### Three-node cluster

```bash
export NEXTMQTT_CLUSTER_SECRET='a-long-shared-secret'
docker compose -f deploy/compose/docker-compose.cluster.yml up -d
```

Check membership:

```bash
curl -s -X POST http://localhost:8080/api/v1/auth/login \
  -H 'Content-Type: application/json' \
  -d '{"username":"admin","password":"change-me-now"}' > /tmp/login.json

TOKEN=$(jq -r .token /tmp/login.json)
curl -s http://localhost:8080/api/v1/cluster -H "Authorization: Bearer $TOKEN" | jq .
```

Publish on `localhost:1883` and subscribe on `localhost:1884` to verify routing.

## Kubernetes (Kustomize)

The manifests create 3 StatefulSet replicas, a headless service for peer
discovery, MQTT and dashboard services, an Ingress, a PodDisruptionBudget, an
HPA and a NetworkPolicy.

```bash
# 1. Create the namespace and secrets yourself (do not apply the placeholder Secret)
kubectl create namespace nextmqtt

kubectl -n nextmqtt create secret generic nextmqtt-secrets \
  --from-literal=admin-password='change-me-now' \
  --from-literal=token-secret="$(openssl rand -base64 32)" \
  --from-literal=cluster-secret="$(openssl rand -base64 32)"

# 2. Remove or ignore the bundled secret.yaml, then apply
kubectl apply -k deploy/kubernetes
```

If you apply the bundled `secret.yaml` anyway, change its values first. It is a
template, not a usable credential.

```bash
kubectl -n nextmqtt get pods,svc
kubectl -n nextmqtt port-forward svc/nextmqtt-http 8080:8080
# open http://localhost:8080
```

The default Ingress host is `nextmqtt.example.com`; edit
`deploy/kubernetes/ingress.yaml` or use the Helm value.

### What is created

| Resource | Purpose |
| --- | --- |
| `StatefulSet/nextmqtt` | 3 broker pods with stable DNS names |
| `Service/nextmqtt-headless` | Peer discovery and cluster addressing |
| `Service/nextmqtt-mqtt` | MQTT TCP/TLS/WS, type LoadBalancer |
| `Service/nextmqtt-http` | Dashboard/API behind Ingress |
| `ConfigMap/nextmqtt-config` | Non-secret settings |
| `Secret/nextmqtt-secrets` | Admin, token and cluster secrets |
| `Ingress` | Dashboard and WebSocket routing |
| `PodDisruptionBudget` | Keeps at least 2 nodes during disruptions |
| `HorizontalPodAutoscaler` | 3–9 pods at 70% CPU |
| `NetworkPolicy` | Allows MQTT/HTTP ports |
| `ServiceAccount` | No API token mounted |

Cluster member addresses are derived from pod DNS:

```text
http://nextmqtt-0.nextmqtt-headless.nextmqtt.svc.cluster.local:8080
http://nextmqtt-1.nextmqtt-headless.nextmqtt.svc.cluster.local:8080
http://nextmqtt-2.nextmqtt-headless.nextmqtt.svc.cluster.local:8080
```

## Kubernetes (Helm)

```bash
helm install nextmqtt deploy/helm/next-mqtt \
  --namespace nextmqtt --create-namespace \
  --set admin.password='change-me-now' \
  --set admin.tokenSecret="$(openssl rand -base64 32)" \
  --set cluster.secret="$(openssl rand -base64 32)" \
  --set ingress.enabled=true \
  --set ingress.host=mqtt.example.com
```

Common overrides:

| Value | Default | Description |
| --- | --- | --- |
| `replicaCount` | `3` | Initial cluster size |
| `image.tag` | Chart appVersion | Image tag |
| `auth.allowAnonymous` | `true` | Disable for production |
| `acl.defaultPolicy` | `Allow` | Set to `Deny` for production |
| `cluster.enabled` | `true` | Toggle clustering |
| `mqtt.type` | `LoadBalancer` | Service type for MQTT |
| `ingress.enabled` | `false` | Expose the dashboard |
| `autoscaling.enabled` | `true` | Enable HPA |
| `podDisruptionBudget.enabled` | `true` | Keep 2 nodes available |
| `existingSecret` | `""` | Use your own secret with three keys |

Helm creates a secret with keys `admin-password`, `token-secret` and
`cluster-secret` unless `existingSecret` is set.

### Upgrade

```bash
helm upgrade nextmqtt deploy/helm/next-mqtt -n nextmqtt --reuse-values \
  --set image.tag=0.2.0
```

Rolling restarts drop in-memory sessions. Ensure clients reconnect and drain
nodes one at a time.

## Production checklist

- [ ] `NEXTMQTT_ALLOW_ANONYMOUS=false`
- [ ] `Acl:DefaultPolicy=Deny` with explicit rules
- [ ] Strong `NEXTMQTT_ADMIN_PASSWORD`
- [ ] Stable `NEXTMQTT_ADMIN_TOKEN_SECRET` shared by all nodes
- [ ] Unique 16+ character `Cluster:Secret`
- [ ] TLS for MQTT and HTTPS for the dashboard
- [ ] Ingress restricted to trusted networks or behind SSO
- [ ] NetworkPolicy blocks `/internal/cluster/*` from clients
- [ ] Resources and HPA aligned with traffic
- [ ] Monitoring scrapes `/metrics` and alerts on drops/auth failures
- [ ] Backups of configuration and secrets

## Verification

```bash
# API
curl -fsS http://localhost:8080/healthz

# MQTT round trip
dotnet run --project src/NextMqtt.Cli -- sub --topic smoke/# --count 1 &
dotnet run --project src/NextMqtt.Cli -- pub --topic smoke/test --message ok

# Cluster
curl -fsS http://localhost:8080/api/v1/cluster -H "Authorization: Bearer $TOKEN"
```

## Uninstall

```bash
helm uninstall nextmqtt -n nextmqtt
# or
kubectl delete -k deploy/kubernetes
kubectl delete namespace nextmqtt
```
