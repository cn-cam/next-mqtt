# 設定

NextMQTT は ASP.NET Core の構成システムを使います。設定は `appsettings.json`、
環境変数、コマンドライン引数、シークレットストアから取得できます。

## 優先順位

後のものほど優先されます：

1. `appsettings.json`
2. `appsettings.{Environment}.json`
3. ユーザーシークレット（`dotnet user-secrets`、Development のみ）
4. 環境変数
5. `NEXTMQTT_*` 環境変数
6. コマンドライン引数

コンテナイメージは `ASPNETCORE_ENVIRONMENT=Production` を設定します。既定の
`appsettings.json` はローカル評価向けに意図的に緩くしてあるため、本番前に認証と
ACL を必ず上書きしてください。

## 環境変数の形式

2 つの形式があります。

**プレフィックス付き構成キー**は `NEXTMQTT_` を使い、セクションは `__` で区切ります：

```bash
NEXTMQTT_BROKER__MAXPACKETSIZE=2097152
NEXTMQTT_SERVER__HTTP__PORT=9090
NEXTMQTT_CLUSTER__SEEDS__0=http://node-b:8080
```

**ブートストラップ変数**は明示的に読み込まれます：

| 変数 | 用途 |
| --- | --- |
| `NEXTMQTT_ADMIN_PASSWORD` | 平文の管理者パスワード。起動時にハッシュ化（推奨） |
| `NEXTMQTT_ADMIN_USERNAME` | 管理者ユーザー名の上書き |
| `NEXTMQTT_ADMIN_TOKEN_SECRET` | ダッシュボードトークンの固定 HMAC 鍵 |
| `NEXTMQTT_ALLOW_ANONYMOUS` | `true` / `false`、匿名 MQTT の可否 |

::: tip 管理者トークン
`NEXTMQTT_ADMIN_TOKEN_SECRET` が未設定だと一時鍵が生成されます。再起動で全トークンが
無効になり、複数ノードのダッシュボードでトークンを共有できません。本番では全ノードに
同じ値を設定してください。
:::

## リスナー

```json
{
  "Server": {
    "Tcp": { "Enabled": true, "Host": "0.0.0.0", "Port": 1883 },
    "Tls": {
      "Enabled": false,
      "Host": "0.0.0.0",
      "Port": 8883,
      "CertificatePath": "/certs/server.pem",
      "CertificateKeyPath": "/certs/server.key",
      "Domain": "mqtt.example.com",
      "CertificatePassword": null,
      "RequireClientCertificate": false
    },
    "WebSocket": { "Enabled": true, "Path": "/mqtt" },
    "Http": {
      "Port": 8080,
      "WebSocketPort": 8083,
      "TlsPort": 8084,
      "CertificatePath": null,
      "CertificateKeyPath": null,
      "Domain": null,
      "CertificatePassword": null,
      "AllowedOrigins": ["http://localhost:5173"]
    }
  }
}
```

| オプション | 既定 | 説明 |
| --- | --- | --- |
| `Tcp:Enabled` | `true` | 生の MQTT リスナー |
| `Tcp:Host` | `0.0.0.0` | バインドするアドレス |
| `Tcp:Port` | `1883` | MQTT ポート |
| `Tls:Enabled` | `false` | TLS MQTT リスナー |
| `Tls:CertificatePath` | — | PFX/PKCS#12 または PEM のパス |
| `Tls:CertificateKeyPath` | — | PEM の秘密鍵。省略時は同ディレクトリの同名 `.key` |
| `Tls:Domain` | — | 起動時に証明書がこの名前をカバーするか確認。不足は警告のみ |
| `Tls:CertificatePassword` | — | PFX のパスワード |
| `Tls:RequireClientCertificate` | `false` | クライアント証明書を要求（検証はプラットフォーム既定） |
| `WebSocket:Enabled` | `true` | WebSocket 経由の MQTT |
| `WebSocket:Path` | `/mqtt` | HTTP パス |
| `Http:Port` | `8080` | API とダッシュボードのポート |
| `Http:WebSocketPort` | `8083` | ブラウザー MQTT 用の追加 Kestrel エンドポイント |
| `Http:TlsPort` | `8084` | 証明書設定時の HTTPS エンドポイント |
| `Http:AllowedOrigins` | ローカル開発用 | ダッシュボード API の CORS 許可リスト |
| `DrainTimeoutSeconds` | `5` | 停止時に inflight QoS の確認を待つ時間 |

`Http:WebSocketPort` を `Http:Port` と同じにすると追加エンドポイントは無効になります。

## Broker

```json
{
  "Broker": {
    "MaxPacketSize": 1048576,
    "MaxInflightMessages": 32,
    "MaxQueuedMessages": 1000,
    "MaxSubscriptionsPerSession": 1000,
    "MaxTopicAlias": 32,
    "ConnectTimeout": "00:00:10",
    "RetransmitInterval": "00:00:10",
    "SweepInterval": "00:00:30",
    "MaxClientIdLength": 256,
    "AllowZeroLengthClientId": true
  }
}
```

| オプション | 既定 | 説明 |
| --- | --- | --- |
| `MaxPacketSize` | 1 MiB | 1 パケットの上限 |
| `MaxInflightMessages` | 32 | 送信 QoS 1/2 のウィンドウ兼 Receive Maximum |
| `MaxQueuedMessages` | 1000 | 永続セッションごとのオフラインキュー上限 |
| `MaxSubscriptionsPerSession` | 1000 | セッションごとの購読数 |
| `MaxTopicAlias` | 32 | 受信トピック別名テーブルのサイズ |
| `MaxSubscriptionIdentifiersPerClient` | 0 | 識別子を付けられる購読数。0 は無制限 |
| `ConnectTimeout` | 10 秒 | CONNECT を送るまでの猶予 |
| `RetransmitInterval` | 10 秒 | QoS 再送タイマー |
| `SweepInterval` | 30 秒 | 期限切れ処理と保守の周期 |
| `MaxClientIdLength` | 256 | これを超える ID は拒否 |
| `AllowZeroLengthClientId` | `true` | ID 未指定時に自動割当 |

## レート制限

既定では無効です。どちらもトークンバケットで、バーストを許した後に持続レートを
適用します。

```json
{
  "RateLimits": {
    "ConnectPerSecondPerIp": 0,
    "ConnectBurst": 0,
    "PublishPerSecondPerClient": 0,
    "PublishBurst": 0
  }
}
```

| オプション | 既定 | 説明 |
| --- | --- | --- |
| `ConnectPerSecondPerIp` | `0` | 接続元アドレスごとの毎秒 CONNECT 数。`0` で無効 |
| `ConnectBurst` | `0` | 接続のバースト。`0` は毎秒レートを使用 |
| `PublishPerSecondPerClient` | `0` | クライアントごとの毎秒 PUBLISH 数。`0` で無効 |
| `PublishBurst` | `0` | 公開のバースト。`0` は毎秒レートを使用 |
| `Topics[].Filter` | – | 共有クォータのトピックフィルタ |
| `Topics[].PublishPerSecond` | `0` | 全クライアント合計の毎秒公開数 |
| `Topics[].Burst` | `0` | バースト。`0` は毎秒レートを使用 |

```bash
NEXTMQTT_RATELIMITS__CONNECTPERSECONDPERIP=20 \
NEXTMQTT_RATELIMITS__PUBLISHPERSECONDPERCLIENT=500 \
nextmqtt-server
```

トピッククォータはリストで設定するため、全体を絞らずに一部のホットトピックだけを
保護できます：

```json
{
  "RateLimits": {
    "Topics": [
      { "Filter": "telemetry/#", "PublishPerSecond": 2000, "Burst": 4000 },
      { "Filter": "commands/#", "PublishPerSecond": 50 }
    ]
  }
}
```

上限超過時の動作：

- アドレスごとのレートを超えた **CONNECT** は、MQTT 5 では CONNACK
  `0x9F Connection rate exceeded`、MQTT 3.1.1 では `0x03 Server unavailable` を
  返して切断します。
- クライアントごとのレートを超えた **PUBLISH** は**破棄されますが確認は返します**
  （PUBACK、MQTT 5 の QoS 2 は `0x96 Message rate too high` の PUBREC）。再送を
  防ぐためです。破棄は `nextmqtt_publishes_rate_limited_total`、
  `nextmqtt_messages_dropped_total`、`nextmqtt_publishes_received_total` に計上
  されます。
- 接続拒否は `nextmqtt_connections_rate_limited_total` に計上されます。

制限は**クラスタ全体**に対する値です。各ノードは設定レートとバーストを生存ノード数で
割ります（最低 1 トークン）。3 ノードで `PublishPerSecondPerClient = 500` の場合、
クライアントあたり合計約 500 メッセージ/秒であり、1500 にはなりません。メンバーシップ
は巡回ごとに再評価されますが、グローバルカウンターがないため参加・離脱の直後は一時的に
超過し得ます。単体構成では設定値がそのまま使われます。

## ストレージ

retained メッセージは既定でメモリ上にあります。`File` にするとディスクへ記録され、
再起動後も残ります。セッションとレプリカは以下で個別に設定します。

```json
{
  "Storage": {
    "Retained": {
      "Mode": "Memory",
      "Path": "data/retained.log",
      "CompactThresholdBytes": 4194304
    }
  }
}
```

| オプション | 既定 | 説明 |
| --- | --- | --- |
| `Mode` | `Memory` | `Memory` または `File` |
| `Path` | `data/retained.log` | ジャーナルファイル。親ディレクトリは自動作成 |
| `CompactThresholdBytes` | 4 MiB | これを超えたらジャーナルを書き直す |

```bash
NEXTMQTT_STORAGE__RETAINED__MODE=file \
NEXTMQTT_STORAGE__RETAINED__PATH=/app/data/retained.log \
nextmqtt-server
```

ジャーナルは JSON 行で、1 行が `set` または `remove` 操作です。トピック、Base64
ペイロード、QoS、タイムスタンプ、MQTT 5 プロパティを含みます。起動時に再生し、
期限切れは飛ばし、大きくなれば圧縮します。末尾の壊れた行は無視され、起動失敗には
なりません。各ノードが自分のジャーナルを持つため、オフラインだったノードも再起動後に
保存済みの内容を復元できます。

永続セッションは別のスナップショットです：

```json
{
  "Storage": {
    "Session": {
      "Mode": "Memory",
      "Path": "data/sessions.json",
      "SnapshotSeconds": 30
    }
  }
}
```

| オプション | 既定 | 説明 |
| --- | --- | --- |
| `Mode` | `Memory` | `Memory` または `File` |
| `Path` | `data/sessions.json` | スナップショット。原子的に置き換え |
| `SnapshotSeconds` | 30 | 書き直しの間隔。変化がなければ省略 |

スナップショットには全永続セッションの ID、期限、購読、MQTT 5 プロパティ付きの
オフラインキュー、PUBREL を待つ受信 QoS 2 の公開、確認待ちの送信メッセージが入ります。
確認状態そのものは保存しないため、再起動後はキューの形で再送され、二重に届くことが
あります。ウィンドウは `MaxInflightMessages` で制限されます。正常停止時にも 1 回
書き込みます。

### セッションレプリカ

ピアから受け取ったレプリカは既定でメモリ上にあります。永続化すると、全ノードが同時に
再起動してもクラスタはセッションを復元できます：

```json
{
  "Storage": {
    "Replicas": {
      "Mode": "File",
      "Path": "data/session-replicas.json",
      "ShardCount": 16
    }
  }
}
```

| オプション | 既定 | 説明 |
| --- | --- | --- |
| `Mode` | `Memory` | `Memory` または `File` |
| `Path` | `data/session-replicas.json` | 基本パス。拡張子の前にシャード番号が入る |
| `ShardCount` | 16 | レプリカを分散するファイル数。`1` は単一ファイル |

各シャードは `{ session, ownerNodeId, version, updatedAtUnix }` の JSON 配列です。セッションは購読、オフラインキュー、受信 QoS 2 状態、確認待ちの送信メッセージを含みます。
レプリカはクライアント ID のハッシュで割り当てられるため、変更があったシャードだけを
書き直し、変化のないシャードは完全に飛ばします。書き込みは変化がある間は最大 5 秒に
1 回、停止時に 1 回です。壊れたシャードは空として扱い、期限切れのレプリカは読み込み時に
捨てます。旧バージョンの単一ファイルも起動時に読み込み、最初の保存でシャードに
置き換わります。

## バックアップ

`GET /api/v1/backup` は retained メッセージと永続セッションを含む可搬な JSON を
返し、`POST /api/v1/backup` で取り込みます。取り込みは retained 集合を置き換え、
現在オフラインのセッションだけを追加し、オンラインのセッションには触れません。

```bash
TOKEN=$(curl -s -X POST http://localhost:8080/api/v1/auth/login \
  -H 'content-type: application/json' \
  -d '{"username":"admin","password":"..."}' | jq -r .token)

curl -s http://localhost:8080/api/v1/backup \
  -H "Authorization: Bearer $TOKEN" > nextmqtt-backup.json

curl -s -X POST http://localhost:8080/api/v1/backup \
  -H "Authorization: Bearer $TOKEN" \
  -H 'content-type: application/json' \
  --data @nextmqtt-backup.json
```

## 連携

送信 webhook は一致した公開を HTTP エンドポイントへ送ります。MQTT 配信はブロック
しません。ルールごとに有界キューとバックグラウンドワーカーを持ち、キューが満杯なら
イベントを破棄して計上します。

```json
{
  "Integrations": {
    "Webhooks": [
      {
        "Name": "audit",
        "Filter": "audit/#",
        "Url": "https://ops.example.com/hooks/mqtt",
        "TimeoutMs": 3000,
        "MaxQueue": 1000,
        "Headers": { "Authorization": "Bearer <token>" },
        "IncludePayload": true
      }
    ]
  }
}
```

| オプション | 既定 | 説明 |
| --- | --- | --- |
| `Filter` | `#` | 対象を選ぶトピックフィルタ |
| `Url` | – | 絶対 http/https エンドポイント |
| `TimeoutMs` | 5000 | 1 回の試行のタイムアウト |
| `MaxQueue` | 1000 | 破棄までのバッファ数 |
| `Headers` | – | 追加のリクエストヘッダー |
| `IncludePayload` | `true` | Base64 ペイロードを送る |

ルールごと・イベントごとに少なくとも 1 回配信し、最大 3 回までバックオフ付きで
再試行します。失敗はログと計数に残ります。本文は JSON で
`{ name, topic, qos, retain, receivedAt, payload }` です。最初に公開を受け付けた
ノードだけが送るため、クラスタで重複しません。

### ルーティングルール

```json
{
  "Integrations": {
    "Rules": [
      { "Filter": "debug/#", "Action": "drop" },
      { "Filter": "health/ping", "Action": "drop" },
      { "Filter": "legacy/#", "Action": "rewrite", "Target": "modern/" }
    ]
  }
}
```

一致したメッセージは retained 保存、配信、クラスタ転送の前に破棄され、
`nextmqtt_messages_dropped_total` に計上されます。`rewrite` はフィルタのリテラル部分を
`Target` に置き換えて残りを保つため、`legacy/a/b` は `modern/a/b` になります。
ワイルドカードのないフィルタはトピック全体を置き換えます。drop は rewrite より優先され、
書き換え後のトピックが不正ならメッセージは破棄されます。

## プラグイン

プラグインはプロセス内の回収可能なロードコンテキストで動作し、`plugin.json`
マニフェストから検出されます。契約バージョンはホストと一致する必要があります。

```json
{
  "Plugins": {
    "Enabled": true,
    "Directory": "plugins",
    "Authenticator": "MyCompany.Auth.MyAuthenticator"
  }
}
```

```text
plugins/audit/
  plugin.json          { "name": "audit", "version": "1.0.0", "contractVersion": 1, "entryAssembly": "MyCompany.Audit.dll" }
  MyCompany.Audit.dll
```

| オプション | 既定 | 説明 |
| --- | --- | --- |
| `Enabled` | `false` | 起動時にプラグインを読み込む |
| `Directory` | `plugins` | `plugin.json` を再帰的に探すディレクトリ |
| `Authenticator` | – | 使用する `IClientAuthenticator` の完全型名 |
| `Authorizer` | – | 使用する `ITopicAuthorizer` の完全型名 |

マニフェストが実在するアセンブリを指し、契約バージョンが一致する場合だけ読み込みます。
それ以外はログに残して飛ばします。プラグインは broker 権限で動作します。拡張機構で
あり、サンドボックスではありません。

### NATS ブリッジ

```json
{
  "Integrations": {
    "Nats": {
      "Enabled": true,
      "Url": "nats://127.0.0.1:4222",
      "SubjectPrefix": "mqtt.",
      "Filter": "telemetry/#",
      "MaxQueue": 5000
    }
  }
}
```

フィルタに一致する公開は NATS テキストプロトコルの `PUB` フレームとして転送されます。
`/` は `.` に、安全でない文字は `_` になります。有界キューがバーストを吸収し、
ワーカーは 2 秒ごとに再接続します。最初にメッセージを受け付けたノードだけが転送する
ため、クラスタで重複しません。

### Kafka ブリッジ

```json
{
  "Integrations": {
    "Kafka": {
      "Enabled": true,
      "BootstrapServers": "127.0.0.1:9092",
      "TopicPrefix": "mqtt.",
      "Filter": "telemetry/#",
      "Compression": "gzip"
    }
  }
}
```

`Confluent.Kafka`（Apache-2.0）は唯一の実行時 NuGet 依存で、ADR 0007 で承認済みです。
トピックの対応は NATS と同じ（`/` → `.`、安全でない文字 → `_`）で、有界キューが
バーストを吸収します。ブローカーに接続できなくても起動時は警告を出すだけで、リスナー
の起動は失敗しません。

### RabbitMQ ブリッジ

```json
{
  "Integrations": {
    "Rabbit": {
      "Enabled": true,
      "HostName": "127.0.0.1",
      "Port": 5672,
      "UserName": "…",
      "Password": "…",
      "Exchange": "nextmqtt",
      "RoutingKeyPrefix": "mqtt.",
      "Filter": "telemetry/#"
    }
  }
}
```

メッセージは durable な `topic` 交換機へ publisher confirm 付きで公開されるため、
`Forwarded` は RabbitMQ が受け付けた分だけを数えます。ルーティングキーの対応は NATS と
同じ（`/` → `.`、安全でない文字 → `_`）です。Kafka と RabbitMQ は同時に動かせます。

## 認証

```json
{
  "Authentication": {
    "AllowAnonymous": false,
    "Superusers": ["ops"],
    "Users": [
      {
        "Username": "device-1",
        "PasswordHash": "pbkdf2-sha256$210000$...$...",
        "Roles": ["devices"]
      }
    ],
    "Jwt": {
      "Secret": "32 文字以上のランダム文字列",
      "Issuer": null,
      "Audience": null,
      "UsernameClaim": "sub",
      "RoleClaim": "roles",
      "RequireExpiration": true
    }
  }
}
```

CLI でパスワードハッシュを生成します：

```bash
dotnet run --project src/NextMqtt.Cli -- hash-password --password 'correct horse battery staple'
```

ハッシュ形式は `pbkdf2-sha256$反復回数$salt-base64$hash-base64` で、既定は 210,000
回です。

JWT 認証は MQTT のパスワードフィールドを使います。有効な HS256 署名、`exp`
（既定で必須）、設定した issuer と audience を満たすトークンを受け付けます。`sub`
クレームがユーザー名になり、`roles` クレームがスーパーユーザー（`admin`）を決め、
今後のポリシーモジュールでも利用できます。

## ACL

```json
{
  "Acl": {
    "DefaultPolicy": "Deny",
    "Rules": [
      { "Effect": "Allow", "Action": "Subscribe", "Topic": "$SYS/#" },
      { "Effect": "Allow", "Action": "Both", "Topic": "devices/${clientid}/#" },
      { "Effect": "Allow", "Action": "Subscribe", "Topic": "public/#" },
      { "Effect": "Deny", "Action": "Publish", "Topic": "public/audit/#", "Username": "*" }
    ]
  }
}
```

ルールは順に評価され、最初に一致したものが採用されます。`Action`、`Username`、
`ClientId` の glob がすべて一致するとヒットします。`Topic` では `${username}` と
`${clientid}` を展開できます。`DefaultPolicy` は `Deny` または `Allow` です。

購読ルールは要求されたフィルタを**包含**する必要があり、単に重なるだけでは足りません。
たとえば `sensors/#` は `sensors/+/temperature` を包含しますが、`sensors/+` は
`sensors/#` を包含しません。

スーパーユーザーはすべての ACL 検査を迂回します。`Authentication:Superusers` に
載っているか、`admin` ロールを持つユーザーが該当します。

## 管理

```json
{
  "Admin": {
    "Username": "admin",
    "TokenSecret": null,
    "TokenLifetimeMinutes": 480,
    "MetricsRequireAuth": false,
    "ApiKeySha256": []
  }
}
```

| オプション | 説明 |
| --- | --- |
| `Username` | ダッシュボードのユーザー名 |
| `PasswordHash` | PBKDF2 ハッシュ。`NEXTMQTT_ADMIN_PASSWORD` の方が簡単 |
| `TokenSecret` | ダッシュボードトークンの HMAC 鍵 |
| `TokenLifetimeMinutes` | トークン寿命。5〜10,080 分に丸め |
| `MetricsRequireAuth` | `/metrics` を管理者トークンで保護 |
| `ApiKeySha256` | `X-API-Key` で受け付ける SHA-256 の 16 進ダイジェスト |

API キーのダイジェストを生成します：

```bash
printf '%s' 'my-api-key' | sha256sum
```

## クラスタ

```json
{
  "Cluster": {
    "Enabled": true,
    "NodeId": "node-a",
    "AdvertiseUrl": "http://node-a:8080",
    "Seeds": ["http://node-b:8080", "http://node-c:8080"],
    "Secret": "shared-secret-at-least-16-chars",
    "HeartbeatSeconds": 5,
    "NodeTimeoutSeconds": 20,
    "RequestTimeoutSeconds": 5,
    "MaxFiltersPerHeartbeat": 10000,
    "SessionReplicaSeconds": 5,
    "SessionReplicaBatchSize": 200,
    "SessionReplicaCapacity": 20000
  }
}
```

`SessionReplicaSeconds` はセッションレプリカをピアへ送る間隔で、ノード停止時に
所有セッションを失わないようにします。`0` で複製を無効にします。1 ラウンドで
`SessionReplicaBatchSize` 個を送り、前回の続きから進みます。永続セッションが
バッチサイズより多い場合、1 セッションのレプリカは最大
`ceil(セッション数 / バッチサイズ) × SessionReplicaSeconds` 古くなります。大きな
ノードではバッチサイズを上げるか、長めの復旧点を受け入れてください。
`SessionReplicaCapacity` はノードがメモリに保持するレプリカ数の上限（既定 20,000）で、
超過時は期限切れ、次に更新時刻の古い順に削除します。

クラスタを有効にすると、`AdvertiseUrl` はピアから到達可能で、`Secret` は 16 文字
以上である必要があります。詳しくは[クラスタ](/ja/clustering)を参照してください。

## ログ

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning",
      "NextMqtt": "Information"
    }
  }
}
```

ログはコンテナ向けの平文形式で stdout に出ます。プロトコルレベルの詳細が必要なときは
`Logging:LogLevel:NextMqtt=Debug` を設定します。

## リバースプロキシ

`Http:CertificatePath` を設定しない限り、Kestrel はダッシュボードの TLS を終端しません。
ingress やロードバランサーで終端する場合：

- `X-Forwarded-For` と `X-Forwarded-Proto` を転送する；
- `/mqtt` の WebSocket アップグレードを有効にする；
- 読み取りタイムアウトを MQTT の keep-alive より長くする；
- `/internal/cluster/*` をノード間トラフィックだけに制限する。
