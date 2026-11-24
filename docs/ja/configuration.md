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

## レート制限

既定は無効。どちらもトークンバケットで、バースト後に持続レートで制限します。

| 設定 | 既定 | 説明 |
| --- | --- | --- |
| `ConnectPerSecondPerIp` | `0` | 送信元アドレスあたり毎秒の CONNECT 受付数。`0` で無効 |
| `ConnectBurst` | `0` | 接続のバースト。`0` は毎秒レート |
| `PublishPerSecondPerClient` | `0` | クライアントあたり毎秒の PUBLISH 受付数。`0` で無効 |
| `PublishBurst` | `0` | 公開のバースト。`0` は毎秒レート |

```bash
NEXTMQTT_RATELIMITS__CONNECTPERSECONDPERIP=20 \
NEXTMQTT_RATELIMITS__PUBLISHPERSECONDPERCLIENT=500 \
dotnet NextMqtt.Server.dll
```

上限を超えた場合:

- 接続レート超過の CONNECT は MQTT 5 では CONNACK `0x9F`、MQTT 3.1.1 では
  `0x03` を返して切断します。
- 公開レート超過の PUBLISH は**破棄しますが確認応答は返します**（PUBACK、
  MQTT 5 の QoS 2 は PUBREC `0x96`）。再送ループを避けるためです。件数は
  `nextmqtt_publishes_rate_limited_total` と
  `nextmqtt_messages_dropped_total` で確認できます。
- 接続拒否は `nextmqtt_connections_rate_limited_total` に計上されます。

制限は**クラスタ全体**に対する値です。各ノードは設定レートとバーストを稼働ノード数で割って（最低 1 トークン）適用するため、3 ノードで `PublishPerSecondPerClient = 500` の場合、合計で毎秒約 500 件になります。メンバーは監視間隔ごとに再計算され、参加・離脱直後は一時的に超過し得ます（グローバルカウンタなし）。単体運用では設定値がそのまま使われます。

## ストレージ

retained は既定でメモリ内にあります。`File` にするとディスクへ追記し、再起動後も
保持します。セッション、キュー、購読はこのリリースではメモリ内のままです。

| 設定 | 既定 | 説明 |
| --- | --- | --- |
| `Mode` | `Memory` | `Memory` または `File` |
| `Path` | `data/retained.log` | ジャーナルファイル（親ディレクトリは自動作成） |
| `CompactThresholdBytes` | 4 MiB | このサイズを超えたら書き直す |

```bash
NEXTMQTT_STORAGE__RETAINED__MODE=file \
NEXTMQTT_STORAGE__RETAINED__PATH=/app/data/retained.log \
dotnet NextMqtt.Server.dll
```

ジャーナルは 1 行 1 操作（`set` / `remove`）の JSON 行で、トピック、Base64 の
ペイロード、QoS、時刻、MQTT 5 プロパティを含みます。起動時に順に再生し、期限切れ
を除外し、肥大時に compaction します。最後の行が壊れていても起動は失敗しません。
各ノードが自分のジャーナルを持ちます。

永続セッションは別スナップショットです。Session:Mode=File で Session:Path（既定 data/sessions.json）に書き、Session:SnapshotSeconds（既定 30）ごとに更新します。スナップショットには identity、期限、購読、オフラインキューが入り、inflight QoS は含みません。正常停止時にも書き出します。

論理バックアップは GET /api/v1/backup で取得し、POST /api/v1/backup で復元します（retained は置換、オンラインでないセッションのみ追加）。

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
