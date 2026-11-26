# 开发

## 环境

| 工具 | 版本 |
| --- | --- |
| .NET SDK | 10.0.100+ |
| Node.js | 22+（推荐 24） |
| Docker | 可选 |

## 构建与测试

```bash
dotnet restore NextMqtt.slnx
dotnet build NextMqtt.slnx -c Release
dotnet test NextMqtt.slnx -c Release
```

| 项目 | 范围 |
| --- | --- |
| `NextMqtt.Core.Tests` | 编解码、畸形报文、主题、Trie |
| `NextMqtt.Broker.Tests` | 会话、队列、保留消息、认证、ACL、集群 |
| `NextMqtt.Server.Tests` | 真实 TCP 集成测试 |

当前 211 个测试通过（Core 63、Broker 105、Server 43）。

## 控制台

```bash
cd web
npm install
npm run dev        # http://localhost:5173
npm run typecheck
npm run build
```

Vite 代理 `/api`、`/healthz`、`/readyz`、`/metrics` 到 `:8080`。

## 文档

```bash
cd docs
npm install
npm run dev
npm run build
```

GitHub Pages 构建时设置 `DOCS_BASE=/next-mqtt/`。

## 目录

```text
src/NextMqtt.Core        编解码与主题
src/NextMqtt.Broker      会话、路由、认证、集群
src/NextMqtt.Server      监听器、API、静态控制台
src/NextMqtt.Cli         pub/sub/bench/hash
tests/                   单元与集成测试
web/                     Vue 3 控制台
docs/                    VitePress 四语文档
deploy/                  Docker/Compose/Kubernetes/Helm
```

## 协议改动

1. 修改 `src/NextMqtt.Core/Protocol`
2. 更新编解码与属性/标志校验
3. 添加往返与畸形输入测试
4. 接入 `ClientConnection` / `BrokerSession`
5. 增加真实 TCP 集成测试
6. 更新 `docs/protocol.md` 与 CHANGELOG

非显而易见的行为请在注释中引用 OASIS MQTT 规范章节。

## 约定

- `net10.0`，nullable，implicit usings
- file-scoped namespace，4 空格
- 异步方法以 `Async` 结尾并传递 `CancellationToken`
- 请求路径禁止 `.Result`、`.Wait()`、`Thread.Sleep`
- 有界队列，丢弃必须有指标
- 控制台使用 `<script setup lang="ts">` 与类型化 props

## 合规

提交前运行：

```bash
./scripts/compliance-check.sh
./scripts/compliance-check.ps1
```

扫描器运行时构造受禁 token，检查文件名、内容、提交历史、分支、Tag。CI 强制执行，
不允许例外。

## 运行时依赖

运行时无第三方 NuGet 依赖。新增依赖需要 ADR 与许可证清单更新。
