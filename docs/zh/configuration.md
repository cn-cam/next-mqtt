# 配置

NextMQTT 使用 ASP.NET Core 配置系统，设置可以来自 `appsettings.json`、环境变量、
命令行参数或机密存储。

## 优先级

越靠后优先级越高：

1. `appsettings.json`
2. `appsettings.{Environment}.json`
3. 用户机密（`dotnet user-secrets`，仅 Development）
4. 环境变量
5. `NEXTMQTT_*` 环境变量
6. 命令行参数

容器镜像设置 `ASPNETCORE_ENVIRONMENT=Production`。默认的 `appsettings.json`
故意保持宽松，便于本地评估；上生产前务必覆盖认证与 ACL 设置。

## 环境变量格式

支持两种形式。

**带前缀的配置键**使用 `NEXTMQTT_` 前缀，段之间用 `__`：

```bash
NEXTMQTT_BROKER__MAXPACKETSIZE=2097152
NEXTMQTT_SERVER__HTTP__PORT=9090
NEXTMQTT_CLUSTER__SEEDS__0=http://node-b:8080
```

**引导变量**由程序显式读取：

| 变量 | 用途 |
| --- | --- |
| `NEXTMQTT_ADMIN_PASSWORD` | 明文管理密码，启动时哈希（推荐） |
| `NEXTMQTT_ADMIN_USERNAME` | 覆盖管理用户名 |
| `NEXTMQTT_ADMIN_TOKEN_SECRET` | 控制台令牌的稳定 HMAC 密钥 |
| `NEXTMQTT_ALLOW_ANONYMOUS` | `true` / `false`，是否允许匿名 MQTT |

::: tip 管理令牌
未设置 `NEXTMQTT_ADMIN_TOKEN_SECRET` 时会生成临时密钥：重启后所有令牌失效，
多节点控制台也无法共享令牌。生产环境应在每个节点设置相同的值。
:::

## 监听器

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

| 选项 | 默认 | 说明 |
| --- | --- | --- |
| `Tcp:Enabled` | `true` | 原始 MQTT 监听器 |
| `Tcp:Host` | `0.0.0.0` | 绑定地址 |
| `Tcp:Port` | `1883` | MQTT 端口 |
| `Tls:Enabled` | `false` | TLS MQTT 监听器 |
| `Tls:CertificatePath` | — | PFX/PKCS#12 或 PEM 路径 |
| `Tls:CertificateKeyPath` | — | PEM 私钥；省略时使用同目录同名 `.key` |
| `Tls:Domain` | — | 启动时校验证书是否覆盖该域名，不匹配只告警 |
| `Tls:CertificatePassword` | — | PFX 密码 |
| `Tls:RequireClientCertificate` | `false` | 是否要求客户端证书（校验用平台默认策略） |
| `WebSocket:Enabled` | `true` | 通过 WebSocket 提供 MQTT |
| `WebSocket:Path` | `/mqtt` | HTTP 路径 |
| `Http:Port` | `8080` | API 与控制台端口 |
| `Http:WebSocketPort` | `8083` | 供浏览器 MQTT 使用的额外 Kestrel 端点 |
| `Http:TlsPort` | `8084` | 配置证书后的 HTTPS 端点 |
| `Http:AllowedOrigins` | 本地开发地址 | 控制台 API 的 CORS 白名单 |
| `DrainTimeoutSeconds` | `5` | 停机时等待 inflight QoS 确认的时长 |

`Http:WebSocketPort` 与 `Http:Port` 相同即关闭额外端点。

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

| 选项 | 默认 | 说明 |
| --- | --- | --- |
| `MaxPacketSize` | 1 MiB | 单报文硬上限 |
| `MaxInflightMessages` | 32 | 出站 QoS 1/2 窗口，也是对端通告的 Receive Maximum |
| `MaxQueuedMessages` | 1000 | 每个持久会话的离线队列上限 |
| `MaxSubscriptionsPerSession` | 1000 | 每会话订阅配额 |
| `MaxTopicAlias` | 32 | 入站主题别名表大小 |
| `MaxSubscriptionIdentifiersPerClient` | 0 | 每客户端可携带订阅标识的订阅数，0 表示不限制 |
| `ConnectTimeout` | 10 s | 发送 CONNECT 的时限 |
| `RetransmitInterval` | 10 s | QoS 重传定时器 |
| `SweepInterval` | 30 s | 过期清理与维护周期 |
| `MaxClientIdLength` | 256 | 超过即拒绝 |
| `AllowZeroLengthClientId` | `true` | 客户端未提供 ID 时自动分配 |

## 限流

默认关闭。两项都是令牌桶：允许突发，之后按持续速率放行。

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

| 选项 | 默认 | 说明 |
| --- | --- | --- |
| `ConnectPerSecondPerIp` | `0` | 每个来源地址每秒接受的 CONNECT，`0` 关闭 |
| `ConnectBurst` | `0` | 建连突发大小，`0` 取每秒速率 |
| `PublishPerSecondPerClient` | `0` | 每客户端每秒接受的 PUBLISH，`0` 关闭 |
| `PublishBurst` | `0` | 发布突发大小，`0` 取每秒速率 |
| `Topics[].Filter` | – | 共享配额对应的主题过滤器 |
| `Topics[].PublishPerSecond` | `0` | 所有客户端合计每秒发布数 |
| `Topics[].Burst` | `0` | 突发大小，`0` 取每秒速率 |

```bash
NEXTMQTT_RATELIMITS__CONNECTPERSECONDPERIP=20 \
NEXTMQTT_RATELIMITS__PUBLISHPERSECONDPERCLIENT=500 \
nextmqtt-server
```

主题配额以列表配置，因此可以只保护少数热点主题，而不限制全部流量：

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

超限行为：

- 超过按地址速率的 **CONNECT** 在 MQTT 5 下返回 CONNACK
  `0x9F Connection rate exceeded`，MQTT 3.1.1 下返回 `0x03 Server unavailable`，
  随后关闭连接。
- 超过按客户端速率的 **PUBLISH** 会被**丢弃但仍确认**（PUBACK；MQTT 5 的 QoS 2
  为 `0x96 Message rate too high` 的 PUBREC），避免客户端重传。丢弃计入
  `nextmqtt_publishes_rate_limited_total`、`nextmqtt_messages_dropped_total` 与
  `nextmqtt_publishes_received_total`。
- 建连拒绝计入 `nextmqtt_connections_rate_limited_total`。

限流描述的是**整个集群**：每个节点把配置的速率与突发量按存活节点数均分（最少一个
令牌），因此三节点、`PublishPerSecondPerClient = 500` 时每客户端合计约 500 条/秒，
而不是 1500。成员关系每轮巡检重新计算；由于没有全局计数器，刚加入或离开的节点可能
短暂超额。单机部署直接使用配置值。

## 存储

保留消息默认保存在内存中。将存储设为 `File` 可写入磁盘并在重启后恢复；会话与副本
在下方单独配置。

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

| 选项 | 默认 | 说明 |
| --- | --- | --- |
| `Mode` | `Memory` | `Memory` 或 `File` |
| `Path` | `data/retained.log` | 日志文件，父目录不存在会自动创建 |
| `CompactThresholdBytes` | 4 MiB | 超过该大小后重写日志 |

```bash
NEXTMQTT_STORAGE__RETAINED__MODE=file \
NEXTMQTT_STORAGE__RETAINED__PATH=/app/data/retained.log \
nextmqtt-server
```

日志为 JSON 行：每行一条 `set` 或 `remove` 操作，包含主题、Base64 负载、QoS、
时间戳与 MQTT 5 属性。启动时重放，跳过已过期条目，文件过大时压缩；最后一行损坏
会被忽略而不是导致启动失败。每个集群节点各写自己的日志，离线过的节点重启后仍能
恢复它保存过的内容。

持久会话单独快照：

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

| 选项 | 默认 | 说明 |
| --- | --- | --- |
| `Mode` | `Memory` | `Memory` 或 `File` |
| `Path` | `data/sessions.json` | 快照文件，整体原子替换 |
| `SnapshotSeconds` | 30 | 重写间隔；状态未变则跳过 |

快照包含全部持久会话：身份、过期时间、订阅、带 MQTT 5 属性的离线队列、仍在等待
PUBREL 的入站 QoS 2 发布，以及等待确认的出站消息。确认状态本身不保存——重启后这些
消息作为队列消息重放，因此可能重复一次，窗口受 `MaxInflightMessages` 限制。优雅停机时
也会写一次快照。

### 会话副本

从对端收到的副本默认只在内存中。落盘后，即使所有节点同时重启，集群仍能恢复会话：

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

| 选项 | 默认 | 说明 |
| --- | --- | --- |
| `Mode` | `Memory` | `Memory` 或 `File` |
| `Path` | `data/session-replicas.json` | 基础路径，分片编号插在扩展名之前 |
| `ShardCount` | 16 | 副本分散到的文件数；`1` 表示单文件 |

每个分片是 `{ session, ownerNodeId, version, updatedAtUnix }` 的 JSON 数组。会话包含订阅、离线队列、入站 QoS 2 状态与等待确认的出站消息。副本按
客户端 ID 的哈希分配，改动只重写所属分片，未变化的分片完全跳过。有变化时最多每
5 秒写一次，停机时再写一次。损坏的分片按空处理；加载时丢弃已过会话过期时间的
副本。旧版本写出的单文件仍会在启动时读入，并在首次写入后由分片文件取代。

## 备份

`GET /api/v1/backup` 返回包含保留消息与持久会话的可移植 JSON 文档；
`POST /api/v1/backup` 导入。导入会替换保留消息集合，补入当前不在线的会话，并
保持在线会话不变。

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

## 集成

出站 webhook 把匹配的发布推送到 HTTP 端点，不阻塞 MQTT 投递。每条规则有独立的
有界队列与后台任务；队列满时事件被丢弃并计数。

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

| 选项 | 默认 | 说明 |
| --- | --- | --- |
| `Filter` | `#` | 选择消息的主题过滤器 |
| `Url` | – | 绝对 http/https 端点 |
| `TimeoutMs` | 5000 | 单次尝试超时 |
| `MaxQueue` | 1000 | 缓冲事件数，超出即丢弃 |
| `Headers` | – | 附加请求头 |
| `IncludePayload` | `true` | 是否发送 Base64 负载 |

每条规则对每个事件至少投递一次，最多尝试 3 次并退避；失败会记录日志并计数。
请求体为 JSON：`{ name, topic, qos, retain, receivedAt, payload }`。只有最初接收
该发布的节点会推送，因此集群不会重复触发 webhook。

### 路由规则

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

匹配的消息在保留存储、投递和集群转发之前被丢弃，并计入
`nextmqtt_messages_dropped_total`。`rewrite` 用 `Target` 替换过滤器中的字面部分
并保留其余部分，因此 `legacy/a/b` 变为 `modern/a/b`；不含通配符的过滤器替换整个
主题。丢弃规则优先于重写规则；重写产生非法主题时该消息被丢弃。

## 插件

插件在进程内、可回收的加载上下文中运行，由 `plugin.json` 清单发现。契约版本必须
与宿主一致。

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

| 选项 | 默认 | 说明 |
| --- | --- | --- |
| `Enabled` | `false` | 启动时加载插件 |
| `Directory` | `plugins` | 递归扫描 `plugin.json` 的目录 |
| `Authenticator` | – | 使用的 `IClientAuthenticator` 完整类型名 |
| `Authorizer` | – | 使用的 `ITopicAuthorizer` 完整类型名 |

只有清单指向存在的程序集且契约版本匹配时才会加载，否则记录日志并跳过。插件以
broker 权限运行——这是扩展机制，不是沙箱。

### NATS 桥接

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

匹配过滤器的发布通过 NATS 文本协议以 `PUB` 帧转发；`/` 变为 `.`，不安全字符
变为 `_`。有界队列吸收突发，后台任务每两秒重连，只有最初接收消息的节点会转发，
因此集群不会重复。

### Kafka 桥接

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

`Confluent.Kafka`（Apache-2.0）是唯一的运行时 NuGet 依赖，见 ADR 0007。主题映射与
NATS 相同（`/` → `.`，不安全字符 → `_`），有界队列吸收突发；地址不可用时只在
启动时记录警告，不会让监听器启动失败。

### RabbitMQ 桥接

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

消息发布到持久化的 `topic` 交换机，并启用 publisher confirm，因此 `Forwarded`
只统计 RabbitMQ 确认接受的消息。路由键映射与 NATS 相同（`/` → `.`，不安全字符
→ `_`）。Kafka 与 RabbitMQ 可以同时启用。

## 认证

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
      "Secret": "至少 32 个随机字符",
      "Issuer": null,
      "Audience": null,
      "UsernameClaim": "sub",
      "RoleClaim": "roles",
      "RequireExpiration": true
    }
  }
}
```

用 CLI 生成密码哈希：

```bash
dotnet run --project src/NextMqtt.Cli -- hash-password --password 'correct horse battery staple'
```

哈希格式为 `pbkdf2-sha256$迭代次数$salt-base64$hash-base64`，默认 210,000 次迭代。

JWT 认证使用 MQTT 的密码字段。令牌需具备有效的 HS256 签名、`exp`（默认要求）
以及配置的 issuer 与 audience。`sub` 声明作为用户名；`roles` 声明决定超级用户
身份（`admin`），也可供后续策略模块使用。

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

规则按顺序求值，第一条匹配的规则生效。`Action`、`Username`、`ClientId` 的 glob
都匹配时规则才命中。`Topic` 支持 `${username}` 与 `${clientid}` 占位符。
`DefaultPolicy` 为 `Deny` 或 `Allow`。

订阅规则必须**覆盖**请求的过滤器，而不只是有交集。例如 `sensors/#` 覆盖
`sensors/+/temperature`，而 `sensors/+` 不覆盖 `sensors/#`。

超级用户绕过全部 ACL 检查。列在 `Authentication:Superusers` 中或拥有 `admin`
角色的用户即为超级用户。

## 管理端

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

| 选项 | 说明 |
| --- | --- |
| `Username` | 控制台用户名 |
| `PasswordHash` | PBKDF2 哈希；用 `NEXTMQTT_ADMIN_PASSWORD` 更省事 |
| `TokenSecret` | 控制台令牌的 HMAC 密钥 |
| `TokenLifetimeMinutes` | 令牌有效期，限制在 5–10,080 分钟 |
| `MetricsRequireAuth` | 是否用管理令牌保护 `/metrics` |
| `ApiKeySha256` | 允许通过 `X-API-Key` 使用的 SHA-256 十六进制摘要 |

生成 API Key 摘要：

```bash
printf '%s' 'my-api-key' | sha256sum
```

## 集群

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

`SessionReplicaSeconds` 控制会话副本推送给对端的频率，使节点崩溃时不会丢失它持有
的会话；`0` 关闭复制。每轮推送 `SessionReplicaBatchSize` 个会话，并从上一轮结束的
位置继续。持久会话数超过批大小时，单个会话的副本最多滞后
`ceil(会话数 / 批大小) × SessionReplicaSeconds`；大节点上可调大批大小，或接受更长的
恢复点。`SessionReplicaCapacity` 限制本节点内存中保留的副本数（默认 20,000），超出时
先删过期副本，再按更新时间淘汰最旧的。

启用集群时 `AdvertiseUrl` 必须能被其他节点访问，`Secret` 至少 16 个字符。详见
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

日志以适合容器的纯文本格式输出到 stdout。需要协议级细节时设置
`Logging:LogLevel:NextMqtt=Debug`。

## 反向代理

除非设置了 `Http:CertificatePath`，Kestrel 不为控制台终止 TLS。若在 ingress 或
负载均衡器处终止：

- 转发 `X-Forwarded-For` 与 `X-Forwarded-Proto`；
- 为 `/mqtt` 启用 WebSocket 升级；
- 读超时保持大于 MQTT keep-alive 间隔；
- 将 `/internal/cluster/*` 限制为节点之间的流量。
