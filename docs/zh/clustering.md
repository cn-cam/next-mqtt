# 集群

NextMQTT 集群是无中心网状结构。每个节点拥有自己的会话，并按订阅过滤器转发消息。

## 配置

```json
{
  "Cluster": {
    "Enabled": true,
    "NodeId": "node-a",
    "AdvertiseUrl": "http://node-a:8080",
    "Seeds": ["http://node-b:8080", "http://node-c:8080"],
    "Secret": "至少 16 字符的共享密钥",
    "HeartbeatSeconds": 5,
    "NodeTimeoutSeconds": 20
  }
}
```

环境变量：

```bash
NEXTMQTT_CLUSTER__ENABLED=true
NEXTMQTT_CLUSTER__NODEID=node-a
NEXTMQTT_CLUSTER__ADVERTISEURL=http://node-a:8080
NEXTMQTT_CLUSTER__SEEDS__0=http://node-b:8080
NEXTMQTT_CLUSTER__SECRET=shared-secret-at-least-16-chars
```

要求：节点间 8080 可达、时钟偏差小于 5 分钟、NodeId 唯一、密钥一致。

## 成员发现

- 每个节点周期性发送心跳（节点 ID、地址、普通订阅过滤器）
- 接收方合并成员并返回自己的成员表
- 新节点只需一个可达种子，即可通过 gossip 发现其余节点
- 超过超时变为 suspect，3 倍超时后移除

控制台“集群”页与 `GET /api/v1/cluster` 可查看当前视图。

## 路由

1. 发布先在本地投递
2. 集群开启时：
   - retain 发布复制到所有存活节点
   - 普通发布只转发给广告了匹配过滤器的节点
3. 远端校验 HMAC、按消息 ID 去重，再走本地路由，不再二次转发

```text
publisher ──► node-a ──► 本地订阅者
                  ├── 签名 POST ──► node-b
                  └── 签名 POST ──► node-c
```

## 投递语义

- **单链路尽力而为**：跨节点是 HTTP 转发，节点故障可能丢失该路径消息
- **去重**：有界“已见消息”缓存
- **会话归属**：会话固定在接入节点，换节点重连会新建会话
- **共享订阅按节点计算**：一条消息可能每个节点各投递一次
- **过滤器最终一致**：新订阅最多等待一个心跳周期

## 安全

内部 API 使用 HMAC-SHA256：
`X-NextMQTT-Timestamp` + `X-NextMQTT-Signature`，签名内容为
`timestamp.body`，允许 5 分钟时钟偏差。

- 密钥不随请求传输
- 跨不可信网络时使用 HTTPS AdvertiseUrl
- 在入口或 NetworkPolicy 中阻止 `/internal/cluster/*`
- 轮换密钥需要重启节点，暂不支持滚动轮换

控制台登录与集群密钥相互独立。未设置 `NEXTMQTT_ADMIN_TOKEN_SECRET` 时，每个节点
用自己的临时密钥签发 Bearer Token，因此一个节点签发的 Token 无法在另一个节点
使用。若希望只登录一次即可访问任意节点的控制台或 API，请在所有节点设置相同的
值。

## Kubernetes

StatefulSet 使用 headless service 与 Pod DNS：

```text
NEXTMQTT_CLUSTER__NODEID=$(POD_NAME)
NEXTMQTT_CLUSTER__ADVERTISEURL=http://$(POD_NAME).nextmqtt-headless.nextmqtt.svc.cluster.local:8080
```

种子为前三个 Pod。扩容后的新 Pod 通过种子加入并由 gossip 传播。

## 验证

```bash
curl -s http://localhost:8080/api/v1/cluster -H "Authorization: Bearer $TOKEN" | jq '.nodes'

nextmqtt sub --port 1884 --topic cluster/# --count 1 &
nextmqtt pub --port 1883 --topic cluster/test --message hello --qos 1
```

订阅后至少等待一个心跳再发布。

## 排错

| 现象 | 检查 |
| --- | --- |
| 只有本节点 | AdvertiseUrl、8080 防火墙、种子拼写 |
| 401 | 密钥不一致或时钟偏差 |
| 节点反复 alive/dead | 心跳与超时配置、CPU |
| 远端缺少 retain | 发布时对端离线，需重新发布 |
| 远端订阅漏消息 | 过滤器尚未传播，等待心跳 |
| 重复投递 | 去重缓存被清理或时钟异常 |

## 限制

- 无跨节点会话迁移/持久化
- 无全局共享订阅协调
- 保留消息为尽力复制
- 每次心跳全量过滤器，默认上限 10,000
