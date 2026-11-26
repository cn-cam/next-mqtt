# REST API

管理 API 由 `Server:Http:Port`（默认 8080）提供，前缀为 `/api/v1`。控制台使用
同一组端点。

## 认证

公开端点：

- `GET /healthz`、`GET /readyz`
- `GET /api/v1/health`、`GET /api/v1/ready`、`GET /api/v1/version`
- `POST /api/v1/auth/login`
- `Admin:MetricsRequireAuth=false` 时的 `GET /metrics`

其余端点需要以下之一：

```http
Authorization: Bearer <token>
```

```http
X-API-Key: <api-key>
```

令牌由登录颁发，按 `Admin:TokenLifetimeMinutes` 过期。API Key 按 SHA-256 摘要
匹配。

### 登录

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

### 错误

错误结构固定：

```json
{ "code": "unauthorized", "message": "A valid bearer token or API key is required." }
```

| 状态码 | 含义 |
| --- | --- |
| 400 | 请求体或主题非法 |
| 401 | 缺少或无效的令牌/API Key |
| 404 | 资源不存在 |
| 409 | 集群消息已处理过 |
| 413 | 请求体过大 |
| 500 | 未处理的服务端错误 |

## 端点

| 方法 | 路径 | 说明 |
| --- | --- | --- |
| GET | `/api/v1/health` | 版本与存活状态 |
| GET | `/api/v1/ready` | 就绪状态与会话数 |
| GET | `/api/v1/version` | 产品、运行时与环境 |
| POST | `/api/v1/auth/login` | 颁发控制台令牌 |
| GET | `/api/v1/auth/me` | 当前主体 |
| GET | `/api/v1/stats` | 计数器与 60 秒发布序列 |
| GET | `/api/v1/clients` | 分页的客户端/会话 |
| GET | `/api/v1/clients/{clientId}` | 单个客户端 |
| DELETE | `/api/v1/clients/{clientId}` | 断开客户端 |
| GET | `/api/v1/subscriptions` | 分页的订阅 |
| GET | `/api/v1/retained` | 分页的保留消息 |
| DELETE | `/api/v1/retained?topic=...` | 删除一条保留消息 |
| POST | `/api/v1/publish` | 通过 Broker 发布 |
| GET | `/api/v1/cluster` | 成员快照 |
| GET | `/api/v1/logs` | 最近的日志，可按级别与文本过滤 |
| GET | `/api/v1/plugins` | 已加载的插件与生效的钩子 |
| POST | `/api/v1/plugins/reload` | 重新扫描插件目录并替换钩子 |
| GET | `/api/v1/config` | 生效的安全配置 |
| GET | `/metrics` | Prometheus 指标 |

### 日志

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

`level` 接受 `trace`、`debug`、`information`/`info`、`warning`/`warn`、`error`、
`critical`/`fatal`。条目保存在有界的内存环形缓冲区中，因此 `total` 可能大于
`capacity`。

### 插件

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

`POST /api/v1/plugins/reload` 会重新读取配置中的 `Plugins` 段、重新扫描目录、加载
新的宿主、替换认证器与授权器，然后释放并卸载旧宿主。磁盘上已消失的插件会在下次
重载时回退到内置实现；正在处理的请求仍用旧实例完成。

实现 `IPluginLifecycle` 的插件在构造后收到 `Initialize(PluginContext)`，在加载
上下文卸载前收到 `Dispose()`，因此文件、线程与套接字可以被确定性地释放。
`Initialize` 抛异常的插件会被跳过并记录日志，其余插件继续加载。

### 客户端列表

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

搜索匹配客户端 ID、用户名与来源地址。页大小限制在 1–200。

### 断开客户端

```bash
curl -X DELETE http://localhost:8080/api/v1/clients/device-1 \
  -H "Authorization: Bearer $TOKEN"
```

找到客户端返回 `204`，否则 `404`。

### 订阅

```http
GET /api/v1/subscriptions?search=sensors&page=1&pageSize=100
```

每项包含 `clientId`、`filter`、`sharedGroup`、`qos`、`noLocal`、
`retainAsPublished`、`retainHandling` 与 `identifier`。

### 保留消息

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

按查询参数删除（主题包含 `/`，需要编码）：

```bash
curl -X DELETE "http://localhost:8080/api/v1/retained?topic=sensors%2Fdemo" \
  -H "Authorization: Bearer $TOKEN"
```

### 发布

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

- `encoding` 为 `plain`（UTF-8）或 `base64`。
- `qos` 必须是 0、1 或 2。
- `retain` 会把消息存为最后已知值。
- 这是管理发布，不受客户端 ACL 规则限制。

### 集群

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

### 统计

`stats` 包含单调计数器、仪表值与 `messagesPerSecond`（60 个一秒桶的数组）。
`collectedAt` 是服务端时间戳。

## 集群内部 API

以下是节点间专用端点，使用 `X-NextMQTT-Timestamp` 与 `X-NextMQTT-Signature`
签名（对 `timestamp.body` 做十六进制 HMAC-SHA256）：

- `POST /internal/cluster/heartbeat`
- `POST /internal/cluster/publish`
- `POST /internal/cluster/leave`
- `POST /internal/cluster/retained`
- `POST /internal/cluster/session/claim`

它们不受管理令牌鉴权保护，必须在边缘屏蔽。
