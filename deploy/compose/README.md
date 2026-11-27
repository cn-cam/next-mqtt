# Compose deployments

Both shapes use the same image (`ghcr.io/cn-cam/next-mqtt`).

## Single node

```bash
cp .env.example .env
# edit NEXTMQTT_ADMIN_PASSWORD and NEXTMQTT_ADMIN_TOKEN_SECRET
docker compose up -d
```

| Host port | Purpose |
| --- | --- |
| 1883 | MQTT TCP |
| 8080 | API and dashboard |
| 8083 | MQTT over WebSocket |

## Three-node cluster

```bash
export NEXTMQTT_CLUSTER_SECRET='a-long-shared-secret'
docker compose -f docker-compose.cluster.yml up -d
```

| Node | MQTT | Dashboard | HTTPS |
| --- | --- | --- | --- |
| node-a | 1883 | 8080 | 8086 |
| node-b | 1884 | 8081 | 8087 |
| node-c | 1885 | 8082 | 8088 |

The HTTPS ports publish the container's `Http:TlsPort` (8084). Open the dashboard
through the certificate name and the **host** port, for example
`https://next-mqtt.deploy-ssl.co:8086/`. Port 8084 is only valid inside the
container network.

These per-node ports are useful for debugging a specific node. For a single
public dashboard entry point, add the dashboard override:

```bash
docker compose -f docker-compose.cluster.yml \
               -f docker-compose.dashboard.yml up -d
```

It starts an nginx service that terminates TLS once and forwards to node-a/b/c
on `https://<certificate-name>:8443/`. `ip_hash` keeps a browser on one node so
its token and WebSocket stay valid. Set the same
`NEXTMQTT_ADMIN_TOKEN_SECRET` on every node; otherwise a login only works on the
node that issued the token. In production, replace this local proxy with your
ingress or load balancer and keep the three HTTP ports private.

TLS and HTTPS are enabled from `.env`:

```bash
NEXTMQTT_TLS_ENABLED=true
NEXTMQTT_TLS_CERT=/certs/server.pem
NEXTMQTT_TLS_KEY=/certs/server.key
NEXTMQTT_TLS_DOMAIN=mqtt.example.com
NEXTMQTT_HTTPS_CERT=/certs/server.pem
NEXTMQTT_HTTPS_KEY=/certs/server.key
NEXTMQTT_HTTPS_DOMAIN=mqtt.example.com
```

Mount the certificate directory read-only in the compose file; never commit the
certificate or key.
