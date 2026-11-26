# 开发

## 工具链

| 工具 | 版本 |
| --- | --- |
| .NET SDK | 10.0.100+ |
| Node.js | 22+（推荐 24） |
| npm | 10+ |
| Docker | 可选，用于镜像与集群测试 |
| Helm | 可选，用于渲染 Chart |

`global.json` 固定 SDK 功能带，并允许本地开发使用更新的主版本 SDK。

## 构建与测试

```bash
dotnet restore NextMqtt.slnx
dotnet build NextMqtt.slnx -c Release
dotnet test NextMqtt.slnx -c Release
```

测试套件：

| 项目 | 范围 |
| --- | --- |
| `NextMqtt.Core.Tests` | 编解码往返、畸形报文、主题匹配、Trie |
| `NextMqtt.Broker.Tests` | 会话、队列、保留消息、认证、ACL、集群路由 |
| `NextMqtt.Server.Tests` | 使用共享编解码的真实 loopback TCP 集成测试 |

集成测试在临时端口启动 Broker，覆盖 CONNECT、QoS 0/1/2、保留消息、遗嘱、持久会话、
会话接管、ACL 与认证。

## 控制台

```bash
cd web
npm install
npm run dev        # http://localhost:5173
npm run typecheck
npm run build
```

Vite 把 `/api`、`/healthz`、`/readyz`、`/metrics` 代理到 `http://localhost:8080`。

UI 使用 Vue 3 + TypeScript：

- 手写 CSS（不引入组件框架）
- 小型 SVG 图标集
- 轻量响应式 i18n 层（英文与中文）
- 轮询 composable，而不是 WebSocket 控制台通道

## 文档

```bash
cd docs
npm install
npm run dev        # http://localhost:5173（VitePress 默认）
npm run build
```

为 GitHub Pages 构建时设置 `DOCS_BASE=/next-mqtt/`。

## 仓库结构

```text
src/NextMqtt.Core        编解码与主题基础（无依赖）
src/NextMqtt.Broker      会话、路由、认证、ACL、保留消息、集群
src/NextMqtt.Server      宿主、监听器、REST API、静态控制台
src/NextMqtt.Cli         pub/sub/bench/hash 命令
tests/                   单元与集成测试
web/                     Vue 3 控制台
docs/                    VitePress 文档
deploy/                  Docker、Compose、Kubernetes、Helm
scripts/                 合规与维护脚本
```

## 添加协议功能

1. 在 `src/NextMqtt.Core/Protocol` 添加或扩展报文模型。
2. 更新解码器与编码器，包括标志位与属性校验。
3. 添加往返测试与畸形输入测试。
4. 把行为接入 `ClientConnection` 或 `BrokerSession`。
5. 在 `NextMqtt.Server.Tests` 添加 loopback 集成测试。
6. 更新 `docs/protocol.md` 与 `CHANGELOG.md`。

不直观的行为应在注释中引用相关 OASIS MQTT 规范章节。

## 约定

- `net10.0`，启用 nullable，implicit usings。
- 文件作用域命名空间，4 空格缩进，`_camelCase` 字段。
- 异步方法以 `Async` 结尾并接受 `CancellationToken`。
- 请求路径禁止 `.Result`、`.Wait()`、`Thread.Sleep`。
- 使用有界队列与 Channel，把丢弃暴露在指标中。
- 优先 `sealed` 类与不可变选项。
- 控制台组件使用 `<script setup lang="ts">` 与带类型的 props。

## 合规门禁

每个提交都必须通过：

```bash
./scripts/compliance-check.sh     # Linux/macOS/Git Bash
./scripts/compliance-check.ps1    # Windows
```

扫描器在运行时拼装禁用品牌词，检查文件名、已跟踪与未跟踪内容、提交信息、分支与标签。
它也是 CI 的必过任务。

注释、测试、文档都没有例外。若术语冲突，请改名。

## 运行时依赖

core、broker、server **没有第三方 NuGet 依赖**。Kafka 与 RabbitMQ 桥接使用 ADR 批准的
固定版本 `Confluent.Kafka` 与 `RabbitMQ.Client`。添加其他依赖需要 `docs/adr/` 中的
架构决策记录，并更新 `docs/legal/third-party-licenses.md`。

## 提交风格

Conventional Commits、祈使句、主题不超过 72 字符：

```text
feat(broker): support message expiry on retained delivery
fix(web): keep the session table stable while polling
docs: document the cluster secret rotation caveat
```

每个提交都应能构建并通过测试。行为变更的 PR 需要包含测试与文档更新。

## 手动验证步骤

```bash
# 启动 Broker
dotnet run --project src/NextMqtt.Server

# 一个终端订阅
dotnet run --project src/NextMqtt.Cli -- sub --topic test/# --count 1

# 另一个终端发布
dotnet run --project src/NextMqtt.Cli -- pub --topic test/hello --message hi --qos 1
```

集群：

```bash
docker compose -f deploy/compose/docker-compose.cluster.yml up -d --build
# 等待 healthy，然后对 1883 与 1884 运行两条 CLI 命令
```

## 代码评审清单

- [ ] 没有禁用品牌词
- [ ] 协议变更由编解码器与集成测试覆盖
- [ ] 有界队列与超时保持不变
- [ ] 错误映射到正确的 MQTT 原因码
- [ ] 控制台类型与 API 契约一致
- [ ] 文档、变更记录与示例已更新
- [ ] 没有提交秘密或个人令牌
