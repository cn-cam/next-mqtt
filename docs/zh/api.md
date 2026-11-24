# REST API

API 默认监听 `8080`，前缀 `/api/v1`。

## 认证

公开：`/healthz`、`/readyz`、`/api/v1/health`、`/api/v1/ready`、
`/api/v1/version`、`POST /api/v1/auth/login`、默认 `/metrics`。

其余需要：

```http
Authorization: Bearer <token>
```

或：

```http
X-API-Key: <api-key>
```

登录：

```bash
TOKEN=$(curl -s http://localhost:8080/api/v1/auth/login \
  -H 'Content-Type: application/json' \
  -d '{"username":"admin","password":"change-me-now"}' | jq -r .token)
```

错误格式：

```json
{ "code": "unauthorized", "message": "..." }
```

| 状态码 | 含义 |
| --- | --- |
| 400 | 参数/主题非法 |
| 401 | 未认证 |
| 404 | 不存在 |
| 409 | 集群消息重复 |
| 413 | 请求体过大 |

## 端点

| 方法 | 路径 | 说明 |
| --- | --- | --- |
| GET | `/api/v1/health` | 健康与版本 |
| GET | `/api/v1/ready` | 就绪与会话数 |
| GET | `/api/v1/version` | 产品/运行时/环境 |
| POST | `/api/v1/auth/login` | 获取控制台令牌 |
| GET | `/api/v1/auth/me` | 当前主体 |
| GET | `/api/v1/stats` | 计数器与 60 秒发布序列 |
| GET | `/api/v1/clients` | 客户端分页 |
| GET | `/api/v1/clients/{clientId}` | 单个客户端 |
| DELETE | `/api/v1/clients/{clientId}` | 断开客户端 |
| GET | `/api/v1/subscriptions` | 订阅分页 |
| GET | `/api/v1/retained` | 保留消息分页 |
| DELETE | `/api/v1/retained?topic=...` | 删除保留消息 |
| POST | `/api/v1/publish` | 通过 Broker 发布 |
| GET | `/api/v1/cluster` | 集群成员 |
| GET | `/api/v1/config` | 生效的安全配置 |
| GET | `/metrics` | Prometheus |

## 客户端

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

断开：

```bash
curl -X DELETE http://localhost:8080/api/v1/clients/device-1 \
  -H "Authorization: Bearer $TOKEN"
```

## 保留消息

```bash
curl -X DELETE "http://localhost:8080/api/v1/retained?topic=sensors%2Fdemo" \
  -H "Authorization: Bearer $TOKEN"
```

## 发布

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

- `encoding`: `plain` 或 `base64`
- `qos`: 0/1/2
- 管理发布不受客户端 ACL 限制

## 集群内部 API

仅节点间使用，HMAC 签名：

- `POST /internal/cluster/heartbeat`
- `POST /internal/cluster/publish`
- `POST /internal/cluster/leave`

不在管理 Token 鉴权范围内，应在边缘屏蔽。
