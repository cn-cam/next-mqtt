# はじめに

## 必要環境

| 方法 | 要件 |
| --- | --- |
| コンテナ | Docker 24+ |
| ソース | .NET SDK 10.0.100+、Node.js 22+ |
| Kubernetes | 1.28+、任意の Ingress |

## Docker

```bash
docker run -d --name nextmqtt \
  -p 1883:1883 -p 8080:8080 -p 8083:8083 \
  -e NEXTMQTT_ADMIN_PASSWORD='change-me-now' \
  ghcr.io/cn-cam/next-mqtt:latest
```

Docker Hub からも取得できます：

```bash
docker pull ooooooooocam/next-mqtt:latest
```

`http://localhost:8080` を開き、`admin` と設定したパスワードでログインします。

::: warning 開発向け既定値
既定では匿名接続と緩い ACL が有効です。公開前に
`NEXTMQTT_ALLOW_ANONYMOUS=false`、`NEXTMQTT_ACL__DEFAULTPOLICY=Deny` を設定してください。
:::

パスワード未設定時は起動ログに生成パスワードが出力されます。

## Compose

```bash
cp .env.example .env
docker compose up -d
```

3 ノードクラスタ：

```bash
docker compose -f deploy/compose/docker-compose.cluster.yml up -d
```

## ソースから

```bash
dotnet run --project src/NextMqtt.Server
```

ダッシュボード開発：

```bash
cd web
npm install
npm run dev
```

本番相当のローカル実行：

```bash
cd web && npm run build && cd ..
dotnet run --project src/NextMqtt.Server
```

## 最初のメッセージ

```bash
# ターミナル 1
dotnet run --project src/NextMqtt.Cli -- sub --topic demo/# --qos 1

# ターミナル 2
dotnet run --project src/NextMqtt.Cli -- pub --topic demo/hello --message hello --qos 1
```

任意の MQTT 3.1.1/5.0 クライアントで `mqtt://localhost:1883` または
`ws://localhost:8083/mqtt` に接続できます。

## ポート

| ポート | 用途 |
| --- | --- |
| 1883 | MQTT TCP |
| 8883 | MQTT TLS（任意） |
| 8083 | MQTT WebSocket `/mqtt` |
| 8084 | MQTT WSS（証明書が必要） |
| 8080 | API、ダッシュボード、`/metrics`、`/healthz` |

## 次のステップ

- [機能一覧](/ja/features)
- [設定](/ja/configuration)
- [認証と ACL](/ja/authentication)
- [デプロイ](/ja/deployment)
