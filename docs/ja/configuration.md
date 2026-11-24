# 設定

優先順位（下が優先）：

1. `appsettings.json`
2. `appsettings.{Environment}.json`
3. ユーザーシークレット（開発時）
4. 環境変数
5. `NEXTMQTT_*` 環境変数
6. コマンドライン

## 環境変数

`NEXTMQTT_` 接頭辞、`__` でセクション区切り：

```bash
NEXTMQTT_BROKER__MAXPACKETSIZE=2097152
NEXTMQTT_SERVER__HTTP__PORT=9090
NEXTMQTT_CLUSTER__SEEDS__0=http://node-b:8080
```

| 変数 | 用途 |
| --- | --- |
| `NEXTMQTT_ADMIN_PASSWORD` | 管理パスワード（推奨） |
| `NEXTMQTT_ADMIN_USERNAME` | 管理ユーザー名 |
| `NEXTMQTT_ADMIN_TOKEN_SECRET` | トークン署名キー |
| `NEXTMQTT_ALLOW_ANONYMOUS` | 匿名 MQTT の可否 |

## サーバー

| 設定 | 既定 | 説明 |
| --- | --- | --- |
| `Server:Tcp:Port` | 1883 | MQTT |
| `Server:Tls:Port` | 8883 | MQTT TLS |
| `Server:Http:Port` | 8080 | API / UI |
| `Server:Http:WebSocketPort` | 8083 | ブラウザ MQTT |
| `Server:Http:TlsPort` | 8084 | HTTPS/WSS |
| `Server:Tls:CertificatePath` | — | PFX/PEM |

## Broker

| 設定 | 既定 |
| --- | --- |
| `MaxPacketSize` | 1 MiB |
| `MaxInflightMessages` | 32 |
| `MaxQueuedMessages` | 1000 |
| `MaxSubscriptionsPerSession` | 1000 |
| `MaxTopicAlias` | 32 |
| `ConnectTimeout` | 10 s |
| `RetransmitInterval` | 10 s |
| `SweepInterval` | 30 s |

## 認証

```json
{
  "Authentication": {
    "AllowAnonymous": false,
    "Users": [
      { "Username": "device-1", "PasswordHash": "pbkdf2-sha256$...", "Roles": ["devices"] }
    ],
    "Jwt": { "Secret": "32 文字以上", "RequireExpiration": true }
  }
}
```

```bash
nextmqtt hash-password --password 'your-password'
```

## ACL

```json
{
  "Acl": {
    "DefaultPolicy": "Deny",
    "Rules": [
      { "Effect": "Allow", "Action": "Both", "Topic": "devices/${clientid}/#" }
    ]
  }
}
```

順序評価、`${username}` / `${clientid}` 展開、`*`/`?` glob 対応。

## 管理 / クラスタ

| 設定 | 説明 |
| --- | --- |
| `Admin:TokenLifetimeMinutes` | トークン有効期間 |
| `Admin:MetricsRequireAuth` | `/metrics` 保護 |
| `Admin:ApiKeySha256` | API キー SHA-256 |
| `Cluster:AdvertiseUrl` | 他ノードから到達可能な URL |
| `Cluster:Secret` | 16 文字以上の共有キー |
| `Cluster:Seeds` | 初期接続先 |

## ログ

`Logging:LogLevel:NextMqtt` を `Debug` にするとプロトコルエラーを確認できます。
本番では常用しないでください。
