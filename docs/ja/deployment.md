# デプロイ

NextMQTT はマルチアーキテクチャのコンテナイメージ、Kustomize マニフェスト、Helm
チャートとして提供されます。同じイメージが単体ノードにもクラスタメンバーにもなり、
クラスタリングは設定だけで有効になります。

::: warning 既定はメモリ内
retained、セッション、キューはストレージを設定しない限りメモリ内にあります。
状態を残す必要があるロールアウトの前に、retained ジャーナル、セッションスナップショット、
レプリカのファイルストアを有効にしてください。inflight の QoS ハンドシェイクは常に
クライアントが再送します。
:::

## コンテナイメージ

```bash
docker pull ghcr.io/cn-cam/next-mqtt:latest
```

利用できるタグ：

- `latest` — `main` の最新安定ビルド
- `0.23.1`、`0.23` — リリースタグ
- `sha-<commit>` — 不変のコミットビルド

プラットフォーム：`linux/amd64`、`linux/arm64`。

### ローカルビルド

```bash
docker build -f deploy/docker/Dockerfile -t nextmqtt:local .
```

ビルドは Vue ダッシュボードをコンパイルし、.NET サーバーを発行し、バンドルを
`wwwroot` にコピーして、UID 10001 で実行します。

### 実行

```bash
docker run -d --name nextmqtt \
  -p 1883:1883 \
  -p 8080:8080 \
  -p 8083:8083 \
  -e NEXTMQTT_ADMIN_PASSWORD='change-me-now' \
  -e NEXTMQTT_ALLOW_ANONYMOUS=false \
  ghcr.io/cn-cam/next-mqtt:latest
```

イメージには `/healthz` を叩く `HEALTHCHECK` が含まれます。

| ポート | 用途 |
| --- | --- |
| 1883 | MQTT TCP |
| 8883 | MQTT TLS（`Server:Tls` で有効化） |
| 8080 | API、ダッシュボード、メトリクス |
| 8083 | MQTT WebSocket |
| 8084 | HTTPS/WSS（`Server:Http:CertificatePath` で有効化） |

## Docker Compose

### 単体ノード

```bash
cp .env.example .env
# NEXTMQTT_ADMIN_PASSWORD と NEXTMQTT_ADMIN_TOKEN_SECRET を編集
docker compose up -d
docker compose logs -f nextmqtt
```

### 3 ノードクラスタ

```bash
export NEXTMQTT_CLUSTER_SECRET='a-long-shared-secret'
docker compose -f deploy/compose/docker-compose.cluster.yml up -d
```

クラスタ用 Compose はダッシュボードを **8080–8082**、HTTPS を **8086–8088** に
公開します（コンテナ内は 8084 のまま）。node-a は
`https://<証明書の名前>:8086/` で開きます。8084 はコンテナネットワーク内でのみ
到達できます。ホストポートの一覧は `deploy/compose/README.md` を参照してください。

ダッシュボードと REST API は**ノードローカル**です。各ノードは自分のセッションだけを
表示し、`NEXTMQTT_ADMIN_TOKEN_SECRET` が全ノードで同じ場合にだけトークンがクラスタ
全体で受け付けられます。単一の公開入口が必要な場合は dashboard override を追加します：

```bash
docker compose -f deploy/compose/docker-compose.cluster.yml \
               -f deploy/compose/docker-compose.dashboard.yml up -d
```

**8443** で TLS を終端し、`ip_hash` で node-a/b/c に転送するため、ブラウザーは 1 つの
ノードに固定されます。本番ではスティッキーセッション付きの ingress かロードバランサーを
使い、8086–8088 は非公開にしてください。

メンバーシップを確認します：

```bash
curl -s -X POST http://localhost:8080/api/v1/auth/login \
  -H 'Content-Type: application/json' \
  -d '{"username":"admin","password":"change-me-now"}' > /tmp/login.json

TOKEN=$(jq -r .token /tmp/login.json)
curl -s http://localhost:8080/api/v1/cluster -H "Authorization: Bearer $TOKEN" | jq .
```

`localhost:1883` で公開し、`localhost:1884` で購読するとルーティングを確認できます。

## Kubernetes（Kustomize）

マニフェストは 3 つの StatefulSet レプリカ、ピア発見用の headless サービス、MQTT と
ダッシュボードのサービス、Ingress、PodDisruptionBudget、HPA、NetworkPolicy を作成
します。

```bash
# 1. 名前空間とシークレットは自分で作る（同梱の Secret は適用しない）
kubectl create namespace nextmqtt

kubectl -n nextmqtt create secret generic nextmqtt-secrets \
  --from-literal=admin-password='change-me-now' \
  --from-literal=token-secret="$(openssl rand -base64 32)" \
  --from-literal=cluster-secret="$(openssl rand -base64 32)"

# 2. 同梱の secret.yaml を削除するか無視してから適用
kubectl apply -k deploy/kubernetes
```

同梱の `secret.yaml` をそのまま適用する場合は、先に値を変更してください。あれは
テンプレートであり、使える資格情報ではありません。

```bash
kubectl -n nextmqtt get pods,svc
kubectl -n nextmqtt port-forward svc/nextmqtt-http 8080:8080
# http://localhost:8080 を開く
```

既定の Ingress ホストは `nextmqtt.example.com` です。
`deploy/kubernetes/ingress.yaml` を編集するか Helm の値を使ってください。

### 作成されるもの

| リソース | 用途 |
| --- | --- |
| `StatefulSet/nextmqtt` | 安定した DNS 名を持つ 3 つのブローカー Pod |
| `Service/nextmqtt-headless` | ピア発見とクラスタアドレス |
| `Service/nextmqtt-mqtt` | MQTT TCP/TLS/WS、LoadBalancer |
| `Service/nextmqtt-http` | Ingress 背後のダッシュボード/API |
| `ConfigMap/nextmqtt-config` | 秘密でない設定 |
| `Secret/nextmqtt-secrets` | 管理者、トークン、クラスタの秘密 |
| `Ingress` | ダッシュボードと WebSocket のルーティング |
| `PodDisruptionBudget` | 障害時も最低 2 ノードを維持 |
| `HorizontalPodAutoscaler` | CPU 70% で 3〜9 Pod |
| `NetworkPolicy` | MQTT/HTTP ポートを許可 |
| `ServiceAccount` | API トークンをマウントしない |

クラスタメンバーのアドレスは Pod DNS から導出されます：

```text
http://nextmqtt-0.nextmqtt-headless.nextmqtt.svc.cluster.local:8080
http://nextmqtt-1.nextmqtt-headless.nextmqtt.svc.cluster.local:8080
http://nextmqtt-2.nextmqtt-headless.nextmqtt.svc.cluster.local:8080
```

## Kubernetes（Helm）

```bash
helm install nextmqtt deploy/helm/next-mqtt \
  --namespace nextmqtt --create-namespace \
  --set admin.password='change-me-now' \
  --set admin.tokenSecret="$(openssl rand -base64 32)" \
  --set cluster.secret="$(openssl rand -base64 32)" \
  --set ingress.enabled=true \
  --set ingress.host=mqtt.example.com
```

よく使う上書き：

| 値 | 既定 | 説明 |
| --- | --- | --- |
| `replicaCount` | `3` | 初期クラスタサイズ |
| `image.tag` | Chart の appVersion | イメージタグ |
| `auth.allowAnonymous` | `true` | 本番では無効化 |
| `acl.defaultPolicy` | `Allow` | 本番では `Deny` |
| `cluster.enabled` | `true` | クラスタリングの切り替え |
| `mqtt.type` | `LoadBalancer` | MQTT のサービス種別 |
| `ingress.enabled` | `false` | ダッシュボードを公開 |
| `autoscaling.enabled` | `true` | HPA を有効化 |
| `podDisruptionBudget.enabled` | `true` | 2 ノードを維持 |
| `existingSecret` | `""` | 3 つのキーを持つ自前のシークレット |

`existingSecret` を設定しない限り、Helm は `admin-password`、`token-secret`、
`cluster-secret` を持つシークレットを作成します。

### アップグレード

```bash
helm upgrade nextmqtt deploy/helm/next-mqtt -n nextmqtt --reuse-values \
  --set image.tag=0.23.1
```

ファイルストアをマウントしレプリカが新しい状態であれば、ローリング再起動でも永続
セッションは残ります。ない場合はメモリ内のセッションが失われます。ノードを 1 台ずつ
ドレインし、クライアントが再接続することを確認してください。

## 本番チェックリスト

- [ ] `NEXTMQTT_ALLOW_ANONYMOUS=false`
- [ ] `Acl:DefaultPolicy=Deny` と明示的なルール
- [ ] 強力な `NEXTMQTT_ADMIN_PASSWORD`
- [ ] 全ノードで共有する固定の `NEXTMQTT_ADMIN_TOKEN_SECRET`
- [ ] 16 文字以上の一意な `Cluster:Secret`
- [ ] MQTT の TLS とダッシュボードの HTTPS
- [ ] Ingress を信頼できるネットワークか SSO の背後に制限
- [ ] NetworkPolicy で `/internal/cluster/*` をクライアントから遮断
- [ ] `Storage:Retained:Mode=File`、`Storage:Session:Mode=File`、
      `Storage:Replicas:Mode=File` を永続ボリューム上に置く
- [ ] トラフィックに合わせたリソースと HPA
- [ ] 監視が `/metrics` を収集し、破棄と認証失敗をアラート
- [ ] 設定と秘密情報のバックアップ

## 検証

```bash
# API
curl -fsS http://localhost:8080/healthz

# MQTT 往復
dotnet run --project src/NextMqtt.Cli -- sub --topic smoke/# --count 1 &
dotnet run --project src/NextMqtt.Cli -- pub --topic smoke/test --message ok

# クラスタ
curl -fsS http://localhost:8080/api/v1/cluster -H "Authorization: Bearer $TOKEN"
```

## アンインストール

```bash
helm uninstall nextmqtt -n nextmqtt
# または
kubectl delete -k deploy/kubernetes
kubectl delete namespace nextmqtt
```
