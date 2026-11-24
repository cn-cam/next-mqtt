# REST API

`http://host:8080/api/v1`

## 認証

公開：`/healthz`、`/readyz`、`/api/v1/health`、`/api/v1/ready`、
`/api/v1/version`、`POST /api/v1/auth/login`。

```http
Authorization: Bearer <token>
```

```http
X-API-Key: <api-key>
```

```bash
TOKEN=$(curl -s http://localhost:8080/api/v1/auth/login \
  -H 'Content-Type: application/json' \
  -d '{"username":"admin","password":"change-me-now"}' | jq -r .token)
```

## エンドポイント

| メソッド | パス | 説明 |
| --- | --- | --- |
| GET | `/api/v1/stats` | 統計と 60 秒系列 |
| GET | `/api/v1/clients` | クライアント一覧 |
| DELETE | `/api/v1/clients/{id}` | 切断 |
| GET | `/api/v1/subscriptions` | 購読一覧 |
| GET | `/api/v1/retained` | retained 一覧 |
| DELETE | `/api/v1/retained?topic=...` | retained 削除 |
| POST | `/api/v1/publish` | publish |
| GET | `/api/v1/cluster` | クラスタ |
| GET | `/api/v1/config` | 有効設定 |
| GET | `/metrics` | Prometheus |

## 一覧

```http
GET /api/v1/clients?search=device&page=1&pageSize=50
```

```json
{
  "items": [
    {
      "clientId": "device-1",
      "connected": true,
      "protocolVersion": "5.0",
      "subscriptions": 4,
      "inflightMessages": 0,
      "queuedMessages": 0
    }
  ],
  "total": 1,
  "page": 1,
  "pageSize": 50
}
```

## 切断

```bash
curl -X DELETE http://localhost:8080/api/v1/clients/device-1 \
  -H "Authorization: Bearer $TOKEN"
```

## Publish

```bash
curl -X POST http://localhost:8080/api/v1/publish \
  -H "Authorization: Bearer $TOKEN" \
  -H 'Content-Type: application/json' \
  -d '{
    "topic": "devices/demo/command",
    "payload": "hello",
    "encoding": "plain",
    "qos": 1,
    "retain": false
  }'
```

`encoding` は `plain` または `base64`。管理 publish はクライアント ACL の対象外です。

## Retained 削除

```bash
curl -X DELETE "http://localhost:8080/api/v1/retained?topic=sensors%2Fdemo" \
  -H "Authorization: Bearer $TOKEN"
```

## 内部 API

`/internal/cluster/heartbeat`、`/publish`、`/leave` はノード間専用で
HMAC 署名を使用します。外部公開しないでください。
