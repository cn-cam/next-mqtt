# REST API

管理 API は `Server:Http:Port`（既定 8080）の `/api/v1` で提供されます。
ダッシュボードも同じエンドポイントを使います。

## 認証

公開エンドポイント：

- `GET /healthz`、`GET /readyz`
- `GET /api/v1/health`、`GET /api/v1/ready`、`GET /api/v1/version`
- `POST /api/v1/auth/login`
- `Admin:MetricsRequireAuth=false` のときの `GET /metrics`

それ以外は次のいずれかが必要です：

```http
Authorization: Bearer <token>
```

```http
X-API-Key: <api-key>
```

トークンはログインで発行され、`Admin:TokenLifetimeMinutes` に従って失効します。
API キーは SHA-256 ダイジェストで照合します。

### ログイン

```bash
TOKEN=$(curl -s http://localhost:8080/api/v1/auth/login \
  -H 'Content-Type: application/json' \
  -d '{"username":"admin","password":"change-me-now"}' | jq -r .token)
```

```json
{
  "token": "eyJhbGciOiJIUzI1NiIs...",
  "expiresAt": "2026-11-24T13:00:00Z",
  "username": "admin"
}
```

### エラー

エラーの形は固定です：

```json
{ "code": "unauthorized", "message": "A valid bearer token or API key is required." }
```

| ステータス | 意味 |
| --- | --- |
| 400 | リクエスト本文またはトピックが不正 |
| 401 | トークンまたは API キーがない/無効 |
| 404 | リソースが存在しない |
| 409 | クラスタメッセージが処理済み |
| 413 | リクエスト本文が大きすぎる |
| 500 | 未処理のサーバーエラー |

## エンドポイント

| メソッド | パス | 説明 |
| --- | --- | --- |
| GET | `/api/v1/health` | バージョンと稼働状態 |
| GET | `/api/v1/ready` | 準備状態とセッション数 |
| GET | `/api/v1/version` | 製品、ランタイム、環境 |
| POST | `/api/v1/auth/login` | ダッシュボードトークンの発行 |
| GET | `/api/v1/auth/me` | 現在のプリンシパル |
| GET | `/api/v1/stats` | カウンターと 60 秒の公開系列 |
| GET | `/api/v1/clients` | ページングされたクライアント/セッション |
| GET | `/api/v1/clients/{clientId}` | 単一クライアント |
| DELETE | `/api/v1/clients/{clientId}` | クライアントを切断 |
| GET | `/api/v1/subscriptions` | ページングされた購読 |
| GET | `/api/v1/retained` | ページングされた retained |
| DELETE | `/api/v1/retained?topic=...` | retained を削除 |
| POST | `/api/v1/publish` | Broker 経由で公開 |
| GET | `/api/v1/cluster` | メンバーシップのスナップショット |
| GET | `/api/v1/logs` | 最近のログ。レベルと文字列で絞り込み |
| GET | `/api/v1/plugins` | 読み込み済みプラグインと有効なフック |
| POST | `/api/v1/plugins/reload` | ディレクトリを再走査してフックを差し替え |
| GET | `/api/v1/config` | 有効な安全設定 |
| GET | `/metrics` | Prometheus 形式 |

### ログ

```http
GET /api/v1/logs?level=Warning&search=cluster&limit=200
```

```json
{
  "entries": [
    {
      "timestamp": "2026-11-25T20:00:52.123+00:00",
      "level": "Warning",
      "category": "NextMqtt.Broker.Clustering.ClusterRouter",
      "message": "Cluster node node-b timed out and was removed",
      "exception": null
    }
  ],
  "total": 42,
  "capacity": 2000,
  "level": "Warning"
}
```

`level` は `trace`、`debug`、`information`/`info`、`warning`/`warn`、`error`、
`critical`/`fatal` を受け付けます。エントリは有界のメモリリングに保持されるため、
`total` が `capacity` を超えることがあります。

### プラグイン

```http
GET /api/v1/plugins
```

```json
{
  "enabled": true,
  "directory": "plugins",
  "contractVersion": 1,
  "authenticator": "ProbeAuth.DenyAllAuthenticator",
  "authorizer": null,
  "authenticatorLoaded": true,
  "authorizerLoaded": false,
  "instances": 1,
  "reloads": 0,
  "plugins": [
    { "name": "probe-auth", "version": "1.0.0", "directory": "/app/plugins/probe-auth", "assembly": "ProbeAuth" }
  ]
}
```

`POST /api/v1/plugins/reload` は構成の `Plugins` セクションを読み直し、ディレクトリを
再走査し、新しいホストを読み込んで認証器と認可器を差し替え、その後で旧ホストを破棄
してアンロードします。ディスクから消えたプラグインは次のリロードで組み込み実装に
戻り、処理中のリクエストは旧インスタンスで完了します。

`IPluginLifecycle` を実装するプラグインは、構築後に `Initialize(PluginContext)`、
ロードコンテキストのアンロード前に `Dispose()` を受け取るため、ファイル、スレッド、
ソケットを確実に解放できます。`Initialize` が例外を投げたプラグインはログに残して
飛ばし、残りは読み込みを続けます。

### クライアント一覧

```http
GET /api/v1/clients?search=device&page=1&pageSize=50
```

```json
{
  "items": [
    {
      "clientId": "device-1",
      "username": "device-1",
      "protocolVersion": "5.0",
      "connected": true,
      "remoteAddress": "10.0.0.12:52133",
      "connectedAt": "2026-11-24T12:00:00Z",
      "lastSeen": "2026-11-24T12:00:30Z",
      "sessionExpiryInterval": 3600,
      "subscriptions": 4,
      "inflightMessages": 0,
      "queuedMessages": 0,
      "isSuperuser": false
    }
  ],
  "total": 1,
  "page": 1,
  "pageSize": 50
}
```

検索はクライアント ID、ユーザー名、リモートアドレスに一致します。ページサイズは
1〜200 に丸められます。

### クライアントの切断

```bash
curl -X DELETE http://localhost:8080/api/v1/clients/device-1 \
  -H "Authorization: Bearer $TOKEN"
```

見つかれば `204`、なければ `404` を返します。

### 購読

```http
GET /api/v1/subscriptions?search=sensors&page=1&pageSize=100
```

各項目は `clientId`、`filter`、`sharedGroup`、`qos`、`noLocal`、
`retainAsPublished`、`retainHandling`、`identifier` を含みます。

### retained メッセージ

```http
GET /api/v1/retained?search=sensors&page=1&pageSize=100
```

```json
{
  "items": [
    { "topic": "sensors/demo", "payloadBytes": 4, "qoS": "AtLeastOnce", "storedAt": "..." }
  ],
  "total": 1,
  "page": 1,
  "pageSize": 100
}
```

クエリパラメーターで削除します（トピックに `/` を含むためエンコードが必要）：

```bash
curl -X DELETE "http://localhost:8080/api/v1/retained?topic=sensors%2Fdemo" \
  -H "Authorization: Bearer $TOKEN"
```

### 公開

```bash
curl -X POST http://localhost:8080/api/v1/publish \
  -H "Authorization: Bearer $TOKEN" \
  -H 'Content-Type: application/json' \
  -d '{
    "topic": "devices/demo/command",
    "payload": "{\"action\":\"reboot\"}",
    "encoding": "plain",
    "qos": 1,
    "retain": false
  }'
```

```json
{ "topic": "devices/demo/command", "bytes": 18, "qos": 1, "retain": false }
```

- `encoding` は `plain`（UTF-8）または `base64`。
- `qos` は 0、1、2 のいずれか。
- `retain` は最後の値として保存します。
- 管理用の公開であり、クライアントの ACL ルールは適用されません。

### クラスタ

```json
{
  "nodeId": "node-a",
  "status": "clustered",
  "nodes": [
    {
      "nodeId": "node-a",
      "address": "http://node-a:8080",
      "status": "alive",
      "lastSeen": "2026-11-24T12:00:00Z",
      "isSelf": true
    }
  ]
}
```

### 統計

`stats` には単調カウンター、ゲージ、`messagesPerSecond`（1 秒ごとの 60 バケットの
配列）が含まれます。`collectedAt` はサーバーのタイムスタンプです。

## クラスタ内部 API

以下はノード間専用で、`X-NextMQTT-Timestamp` と `X-NextMQTT-Signature`
（`timestamp.body` に対する 16 進 HMAC-SHA256）で署名されます：

- `POST /internal/cluster/heartbeat`
- `POST /internal/cluster/publish`
- `POST /internal/cluster/leave`
- `POST /internal/cluster/retained`
- `POST /internal/cluster/session/claim`

管理者トークンの認証対象外です。エッジで遮断してください。
