# デプロイ

::: warning 状態はメモリ内
セッション、キュー、retained は再起動で失われます。永続化はロードマップ参照。
:::

## コンテナ

```bash
docker pull ghcr.io/cn-cam/next-mqtt:latest
# または Docker Hub
docker pull oooooooooocam/next-mqtt:latest
```

```bash
docker run -d --name nextmqtt \
  -p 1883:1883 -p 8080:8080 -p 8083:8083 \
  -e NEXTMQTT_ADMIN_PASSWORD='change-me-now' \
  -e NEXTMQTT_ALLOW_ANONYMOUS=false \
  ghcr.io/cn-cam/next-mqtt:latest
```

| ポート | 用途 |
| --- | --- |
| 1883 | MQTT |
| 8883 | MQTT TLS |
| 8080 | API / UI |
| 8083 | WebSocket |
| 8084 | HTTPS / WSS |

## Compose

```bash
cp .env.example .env
docker compose up -d
```

3 ノード：

```bash
export NEXTMQTT_CLUSTER_SECRET='a-long-shared-secret'
docker compose -f deploy/compose/docker-compose.cluster.yml up -d
```

## Kubernetes (Kustomize)

```bash
kubectl create namespace nextmqtt

kubectl -n nextmqtt create secret generic nextmqtt-secrets \
  --from-literal=admin-password='change-me-now' \
  --from-literal=token-secret="$(openssl rand -base64 32)" \
  --from-literal=cluster-secret="$(openssl rand -base64 32)"

kubectl apply -k deploy/kubernetes
kubectl -n nextmqtt port-forward svc/nextmqtt-http 8080:8080
```

同梱の `secret.yaml` はテンプレートです。値は必ず変更してください。

## Helm

```bash
helm install nextmqtt deploy/helm/next-mqtt \
  --namespace nextmqtt --create-namespace \
  --set admin.password='change-me-now' \
  --set admin.tokenSecret="$(openssl rand -base64 32)" \
  --set cluster.secret="$(openssl rand -base64 32)" \
  --set ingress.enabled=true \
  --set ingress.host=mqtt.example.com
```

主な値：

| 値 | 既定 |
| --- | --- |
| `replicaCount` | 3 |
| `auth.allowAnonymous` | true |
| `acl.defaultPolicy` | Allow |
| `cluster.enabled` | true |
| `mqtt.type` | LoadBalancer |
| `ingress.enabled` | false |
| `autoscaling.enabled` | true |

## 本番チェックリスト

- 匿名無効、ACL 既定拒否
- 強力な管理パスワード、固定トークンキー
- クラスタキー 16 文字以上
- MQTT TLS、UI HTTPS
- Ingress/IP 制限
- `/internal/cluster/*` 遮断
- リソース制限、HPA、監視
- 設定と秘密情報のバックアップ

## 検証

```bash
curl -fsS http://localhost:8080/healthz

nextmqtt sub --topic smoke/# --count 1 &
nextmqtt pub --topic smoke/test --message ok

curl -fsS http://localhost:8080/api/v1/cluster \
  -H "Authorization: Bearer $TOKEN"
```
