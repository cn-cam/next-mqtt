# はじめに

NextMQTT は HTTP API とダッシュボードを内蔵した単一のブローカープロセスです。この
ページでは数分でブローカーを起動し、最初の MQTT 交換まで進みます。

## 要件

| 形態 | 要件 |
| --- | --- |
| コンテナ | Docker 24+（Compose v2 は任意） |
| ソースから | .NET SDK 10.0.100+、Node.js 22+（24 推奨） |
| Kubernetes | Kubernetes 1.28+、Ingress コントローラー（任意） |
| イメージビルド | Docker BuildKit |

## Docker で実行

```bash
docker run -d --name nextmqtt \
  -p 1883:1883 \
  -p 8080:8080 \
  -p 8083:8083 \
  -e NEXTMQTT_ADMIN_PASSWORD='change-me-now' \
  ghcr.io/cn-cam/next-mqtt:latest
```

Docker Hub ミラーも公開されています：

```bash
docker pull oooooooooocam/next-mqtt:latest
```

<http://localhost:8080> を開き、渡したパスワードで `admin` としてサインインします。
ダッシュボードはブローカー自身が配信するため、別の Web サーバーは不要です。

::: warning 開発用の既定値
同梱の設定はクイックスタートが動くように匿名 MQTT と緩い ACL を許可しています。
ポート 1883 や 8080 を信頼できないネットワークへ公開する前に、
[セキュリティガイド](/ja/security)に従って次を設定してください：

```bash
NEXTMQTT_ALLOW_ANONYMOUS=false
NEXTMQTT_ACL__DEFAULTPOLICY=Deny
```
:::

管理者パスワードを設定しない場合、ブローカーは起動時に生成してログへ書き出します：

```bash
docker logs nextmqtt | grep "bootstrap password"
```

## Compose で実行

単体ノード：

```bash
cp .env.example .env
# .env を編集してから：
docker compose up -d
```

3 ノードクラスタ：

```bash
docker compose -f deploy/compose/docker-compose.cluster.yml up -d
```

| ノード | MQTT | ダッシュボード |
| --- | --- | --- |
| node-a | `localhost:1883` | <http://localhost:8080> |
| node-b | `localhost:1884` | <http://localhost:8081> |
| node-c | `localhost:1885` | <http://localhost:8082> |

あるノードで公開し、別のノードで購読すると、クラスタルーティングを確認できます。

## ソースから実行

```bash
dotnet run --project src/NextMqtt.Server
```

`dotnet run` はダッシュボードのバンドルをビルドしません。UI 開発では：

```bash
cd web
npm install
npm run dev        # http://localhost:5173、/api を :8080 へプロキシ
```

本番に近いローカル実行では、先にバンドルをビルドします：

```bash
cd web && npm run build && cd ..
dotnet run --project src/NextMqtt.Server
```

サーバーは開発時に `web/dist` を自動で読み込み、`dotnet publish` 時には `wwwroot` へ
コピーします。

## 最初のメッセージ

MQTT 5 と MQTT 3.1.1 を話す同梱 CLI を使います：

```bash
# ターミナル 1
dotnet run --project src/NextMqtt.Cli -- sub --topic demo/# --qos 1

# ターミナル 2
dotnet run --project src/NextMqtt.Cli -- pub --topic demo/hello --message "hello" --qos 1
```

購読側はトピック、QoS、retain フラグ、ペイロードを表示します。公開コマンドに
`--retain` を付けて購読側を切断し、もう一度購読すると、retained ストアから即座に
配信されます。

任意の MQTT 3.1.1/5.0 クライアントライブラリを `mqtt://localhost:1883` や
`ws://localhost:8083/mqtt` に対して使うこともできます。

## ポート

| ポート | リスナー | 設定 |
| --- | --- | --- |
| 1883 | MQTT over TCP | `Server:Tcp` |
| 8883 | MQTT over TLS | `Server:Tls`（既定で無効） |
| 8083 | `/mqtt` の MQTT over WebSocket | `Server:Http:WebSocketPort` |
| 8084 | MQTT over セキュア WebSocket | `Server:Http:TlsPort` |
| 8080 | REST API、ダッシュボード、`/metrics`、`/healthz` | `Server:Http:Port` |

すべてのリスナーは既定で `0.0.0.0` にバインドします。コンテナでは実際に必要な
ポートだけを公開してください。

## ダッシュボードツアー

| ページ | 表示内容 |
| --- | --- |
| Overview | クライアント/セッション/購読のゲージ、公開スループット、ブローカーのカウンター、ノード一覧 |
| Clients | オンラインと永続オフラインのセッション、クライアントの切断 |
| Subscriptions | フィルタ、共有グループ、QoS、MQTT 5 購読オプション |
| Retained | 保存メッセージ、ペイロードサイズ、期限、削除 |
| Publish | 平文または Base64 ペイロードをブローカー経由で公開 |
| Cluster | メンバーシップ、ノードの状態、トポロジー |
| Settings | 有効なリスナーと制限、テーマ、言語、トークン操作 |

ダッシュボードは数秒ごとに API をポーリングします。ヘッダーに最後に更新された時刻が
表示されます。

## 次のステップ

- [設定](/ja/configuration) — すべてのオプションと環境変数
- [認証と ACL](/ja/authentication) — ユーザー、JWT、トピックルール
- [デプロイ](/ja/deployment) — Docker、Compose、Kubernetes、Helm
- [クラスタ](/ja/clustering) — 3 ノード構成とルーティングモデル
- [運用](/ja/operations) — メトリクス、ヘルスチェック、トラブルシューティング
