# 安全模型

## 信任边界

| 边界 | 保护 |
| --- | --- |
| MQTT 客户端 → Broker | 可选 TLS、密码/JWT、主题 ACL |
| 浏览器 → 控制台 | Bearer Token / API Key、HTTPS |
| 节点 → 节点 | HMAC-SHA256、共享密钥、可选 HTTPS |
| 运维 → 容器 | UID 10001、非 root、丢弃 capabilities |
| 构建 → 发布 | 合规扫描、CodeQL、固定依赖 |

## 加固清单

### 凭据

- [ ] 设置强 `NEXTMQTT_ADMIN_PASSWORD` 或预先生成哈希
- [ ] 设置稳定且随机的 `NEXTMQTT_ADMIN_TOKEN_SECRET`
- [ ] 不提交 `.env`、私钥、证书、API Key
- [ ] 怀疑泄露后立即轮换
- [ ] 可用时使用硬件保护的密钥存储

```bash
openssl rand -base64 32
```

### MQTT

- [ ] 关闭匿名
- [ ] ACL 默认拒绝并写明确规则
- [ ] 外部网络强制 TLS
- [ ] 配置合理的 inflight、队列、报文限制
- [ ] 在边缘做连接/消息速率限制

### 控制台

- [ ] HTTPS + 来源限制
- [ ] 缩短 `Admin:TokenLifetimeMinutes`
- [ ] 暴露 `/metrics` 时开启 `MetricsRequireAuth`
- [ ] 不公开 `/internal/cluster/*`
- [ ] CORS 仅允许实际控制台来源

### 集群

- [ ] 每个环境独立密钥，至少 16 字符
- [ ] 节点间 NTP 同步
- [ ] 不可信网络使用 HTTPS
- [ ] 入口屏蔽内部路径
- [ ] 在维护窗口内轮换密钥

### 容器/Kubernetes

- [ ] 使用 UID 10001
- [ ] `allowPrivilegeEscalation: false`，丢弃全部 capabilities
- [ ] 设置资源限制
- [ ] 只读根文件系统 + 可写 `/tmp`、`/app/data`
- [ ] 应用 NetworkPolicy 或更严格的 egress 规则
- [ ] 使用 Secret/外部密钥管理（`existingSecret`）

## 认证细节

### 密码哈希

PBKDF2-SHA256，随机 16 字节盐，默认 210,000 次迭代。

### JWT

JWT 仅 HS256，校验 `exp`、`nbf`、`iss`、`aud`、`sub`。

### 管理令牌

控制台令牌使用 `Admin:TokenSecret` 签名的独立 HS256 JWT，仅 REST API 中间件接受。
API Key 以 SHA-256 摘要白名单匹配，与管理员令牌同为 Bearer 凭据。

不要复用 MQTT JWT、集群、控制台三类密钥。

## 数据处理

- 负载按不透明字节处理，默认不记录
- 主题名与用户属性会出现在控制台
- 会话、队列、保留消息在内存中，启用文件存储后也会落盘；崩溃转储或数据卷可能包含
  这些内容，需限制访问
- 集群去重缓存保存消息 ID 最多 10 分钟

## DoS 注意事项

Broker 限制了报文大小、inflight、离线队列、出站 Channel、CONNECT 超时和保活。
按来源地址的连接限流、按客户端的发布限流与主题配额可在 `RateLimits` 中开启，默认关闭。
请与下列措施配合：

- NetworkPolicy 与负载均衡限制
- 边缘的连接限流
- 在接受大负载前完成认证
- 容器资源限制

## 漏洞报告

使用 GitHub 私有漏洞报告：

<https://github.com/cn-cam/next-mqtt/security/advisories/new>

请提供版本、部署方式、复现步骤和影响。不要在公开 issue 中披露未修复漏洞。

## 独立性与商标

本项目不包含任何其他 MQTT Broker 产品的源代码、文档文字、配置格式、Logo 或商标。
品牌规则由 CI 强制，见 [AGENTS.md](https://github.com/cn-cam/next-mqtt/blob/main/AGENTS.md)
与 [TRADEMARKS.md](https://github.com/cn-cam/next-mqtt/blob/main/TRADEMARKS.md)。
