# 快速开始

NextMQTT 是单进程 Broker，内置 HTTP API 与控制台。本页从零启动一个可用的
Broker，并完成第一次消息收发。

## 环境要求

| 方式 | 要求 |
| --- | --- |
| 容器 | Docker 24+（可选 Compose v2） |
| 源码运行 | .NET SDK 10.0.100+，Node.js 22+（推荐 24） |
| Kubernetes | Kubernetes 1.28+，可选 Ingress |

## Docker 运行

```bash
docker run -d --name nextmqtt \
  -p 1883:1883 \
  -p 8080:8080 \
  -p 8083:8083 \
  -e NEXTMQTT_ADMIN_PASSWORD='change-me-now' \
  ghcr.io/cn-cam/next-mqtt:latest
```

Docker Hub 镜像：

```bash
docker pull ooooooooocam/next-mqtt:latest
```

浏览器打开 <http://localhost:8080>，使用 `admin` 与上面的密码登录。

::: warning 开发默认值
默认配置允许匿名 MQTT 客户端与宽松的 ACL，便于快速验证。对公网暴露
1883/8080 之前请务必加固：

```bash
NEXTMQTT_ALLOW_ANONYMOUS=false
NEXTMQTT_ACL__DEFAULTPOLICY=Deny
```
:::

未设置管理密码时，Broker 会随机生成并写入日志：

```bash
docker logs nextmqtt | grep "bootstrap password"
```

## Compose 运行

单节点：

```bash
cp .env.example .env
# 修改 .env 后
docker compose up -d
```

三节点集群：

```bash
docker compose -f deploy/compose/docker-compose.cluster.yml up -d
```

| 节点 | MQTT | 控制台 |
| --- | --- | --- |
| node-a | `localhost:1883` | <http://localhost:8080> |
| node-b | `localhost:1884` | <http://localhost:8081> |
| node-c | `localhost:1885` | <http://localhost:8082> |

在 node-a 发布、在 node-b 订阅，即可验证跨节点路由。

## 源码运行

```bash
dotnet run --project src/NextMqtt.Server
```

`dotnet run` 不会构建前端。开发控制台时：

```bash
cd web
npm install
npm run dev        # http://localhost:5173，/api 代理到 :8080
```

生产式本地运行：

```bash
cd web && npm run build && cd ..
dotnet run --project src/NextMqtt.Server
```

服务端会自动发现 `web/dist`。

## 第一条消息

使用内置 CLI（支持 MQTT 5 与 3.1.1）：

```bash
# 终端 1
dotnet run --project src/NextMqtt.Cli -- sub --topic demo/# --qos 1

# 终端 2
dotnet run --project src/NextMqtt.Cli -- pub --topic demo/hello --message "hello" --qos 1
```

添加 `--retain` 后先断开订阅者再重新订阅，即可立即收到保留消息。

也可以使用任意 MQTT 3.1.1/5.0 客户端连接 `mqtt://localhost:1883` 或
`ws://localhost:8083/mqtt`。

## 端口

| 端口 | 监听器 | 配置 |
| --- | --- | --- |
| 1883 | MQTT TCP | `Server:Tcp` |
| 8883 | MQTT TLS | `Server:Tls`（默认关闭） |
| 8083 | MQTT WebSocket `/mqtt` | `Server:Http:WebSocketPort` |
| 8084 | MQTT WSS | `Server:Http:TlsPort` |
| 8080 | REST API、控制台、`/metrics`、`/healthz` | `Server:Http:Port` |

## 控制台导航

| 页面 | 内容 |
| --- | --- |
| 概览 | 客户端/会话/订阅、发布吞吐、Broker 计数、节点列表 |
| 客户端 | 在线与持久离线会话，可强制断开 |
| 订阅 | 主题过滤器、共享组、QoS 与 MQTT 5 订阅选项 |
| 保留消息 | 存储的消息、大小与过期时间，可删除 |
| 发布 | 以纯文本或 Base64 通过 Broker 发布 |
| 集群 | 成员、节点健康与拓扑 |
| 设置 | 生效的监听器与限制、主题、语言与令牌操作 |

## 下一步

- [功能对齐清单](/zh/features) — 已实现、部分实现与规划能力
- [命令行参考](/zh/cli) — 发布、订阅、压测与密码哈希
- [配置](/zh/configuration) — 全部选项与环境变量
- [认证与 ACL](/zh/authentication) — 用户、JWT 与主题规则
- [部署](/zh/deployment) — Docker、Kubernetes 与 Helm
- [集群](/zh/clustering) — 三节点与路由模型
- [运维](/zh/operations) — 指标、健康检查与排错
