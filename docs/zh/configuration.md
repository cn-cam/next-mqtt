# 配置

NextMQTT 使用 ASP.NET Core 配置系统。优先级从低到高：

1. `appsettings.json`
2. `appsettings.{Environment}.json`
3. 用户机密（仅开发）
4. 环境变量
5. `NEXTMQTT_*` 环境变量
6. 命令行参数

## 环境变量

配置键使用 `NEXTMQTT_` 前缀，段之间用 `__`：

```bash
NEXTMQTT_BROKER__MAXPACKETSIZE=2097152
NEXTMQTT_SERVER__HTTP__PORT=9090
NEXTMQTT_CLUSTER__SEEDS__0=http://node-b:8080
```

专用引导变量：

| 变量 | 用途 |
| --- | --- |
| `NEXTMQTT_ADMIN_PASSWORD` | 管理密码，启动时哈希（推荐） |
| `NEXTMQTT_ADMIN_USERNAME` | 管理用户名 |
| `NEXTMQTT_ADMIN_TOKEN_SECRET` | 控制台令牌签名密钥 |
| `NEXTMQTT_ALLOW_ANONYMOUS` | `true` / `false` 匿名 MQTT |

## 监听器

```json
{
  "Server": {
    "Tcp": { "Enabled": true, "Host": "0.0.0.0", "Port": 1883 },
    "Tls": {
      "Enabled": false,
      "Port": 8883,
      "CertificatePath": "/certs/server.pfx",
      "CertificatePassword": "secret"
    },
    "WebSocket": { "Enabled": true, "Path": "/mqtt" },
    "Http": {
      "Port": 8080,
      "WebSocketPort": 8083,
      "TlsPort": 8084,
      "CertificatePath": null,
      "AllowedOrigins": ["http://localhost:5173"]
    }
  }
}
```

| 选项 | 默认 | 说明 |
| --- | --- | --- |
| `Tcp:Port` | 1883 | MQTT TCP |
| `Tls:Port` | 8883 | MQTT TLS |
| `Http:Port` | 8080 | API 与控制台 |
| `Http:WebSocketPort` | 8083 | 浏览器 MQTT |
| `Http:TlsPort` | 8084 | HTTPS/WSS（需证书） |

## Broker

| 选项 | 默认 | 说明 |
| --- | --- | --- |
| `MaxPacketSize` | 1048576 | 单报文上限 |
| `MaxInflightMessages` | 32 | 每会话 inflight 窗口 |
| `MaxQueuedMessages` | 1000 | 离线队列上限 |
| `MaxSubscriptionsPerSession` | 1000 | 每会话订阅上限 |
| `MaxTopicAlias` | 32 | 入站主题别名表 |
| `ConnectTimeout` | `00:00:10` | CONNECT 超时 |
| `RetransmitInterval` | `00:00:10` | QoS 重传周期 |
| `SweepInterval` | `00:00:30` | 过期清理周期 |
| `MaxClientIdLength` | 256 | 客户端 ID 长度 |
| `AllowZeroLengthClientId` | `true` | 自动分配 ID |

## 限流

默认关闭。两项都是令牌桶：允许突发，然后按持续速率放行。

| 选项 | 默认 | 说明 |
| --- | --- | --- |
| `ConnectPerSecondPerIp` | `0` | 每个来源地址每秒接受的 CONNECT 数，`0` 关闭 |
| `ConnectBurst` | `0` | 建连突发大小，`0` 取每秒速率 |
| `PublishPerSecondPerClient` | `0` | 每客户端每秒接受的 PUBLISH 数，`0` 关闭 |
| `PublishBurst` | `0` | 发布突发大小，`0` 取每秒速率 |

```bash
NEXTMQTT_RATELIMITS__CONNECTPERSECONDPERIP=20 \
NEXTMQTT_RATELIMITS__PUBLISHPERSECONDPERCLIENT=500 \
dotnet NextMqtt.Server.dll
```

超限行为：

- 超过建连速率的 CONNECT 在 MQTT 5 下返回 CONNACK `0x9F`，MQTT 3.1.1 下返回
  `0x03`，随后断开。
- 超过发布速率的 PUBLISH 会被**丢弃但仍确认**（PUBACK；MQTT 5 QoS 2 为 PUBREC
  `0x96`），避免客户端无限重传。计数见
  `nextmqtt_publishes_rate_limited_total`、`nextmqtt_messages_dropped_total`。
- 建连拒绝计入 `nextmqtt_connections_rate_limited_total`。

限流按节点生效：三节点集群每节点各自放行配置的速率。

## 认证与 ACL

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
      "Secret": "至少 32 字符",
      "RequireExpiration": true
    }
  },
  "Acl": {
    "DefaultPolicy": "Deny",
    "Rules": [
      { "Effect": "Allow", "Action": "Both", "Topic": "devices/${clientid}/#" },
      { "Effect": "Allow", "Action": "Subscribe", "Topic": "public/#" }
    ]
  }
}
```

生成密码哈希：

```bash
nextmqtt hash-password --password 'your-password'
```

`Superusers` 或角色 `admin` 会绕过 ACL。规则按顺序匹配，`Topic` 支持
`${username}` / `${clientid}`。

## 管理端

| 选项 | 说明 |
| --- | --- |
| `Username` | 控制台用户名 |
| `PasswordHash` | PBKDF2 哈希；推荐用环境变量 |
| `TokenSecret` | 令牌 HMAC 密钥 |
| `TokenLifetimeMinutes` | 令牌有效期（5–10080 分钟） |
| `MetricsRequireAuth` | 是否保护 `/metrics` |
| `ApiKeySha256` | 允许的 API Key SHA-256 摘要 |

## 集群

```json
{
  "Cluster": {
    "Enabled": true,
    "NodeId": "node-a",
    "AdvertiseUrl": "http://node-a:8080",
    "Seeds": ["http://node-b:8080"],
    "Secret": "至少 16 字符的共享密钥",
    "HeartbeatSeconds": 5,
    "NodeTimeoutSeconds": 20
  }
}
```

启用集群时 `AdvertiseUrl` 必须可被其他节点访问，`Secret` 至少 16 字符。详见
[集群](/zh/clustering)。

## 日志

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

日志输出到 stdout。排错时可临时设为 `Debug`，但不要在生产环境长期开启。
