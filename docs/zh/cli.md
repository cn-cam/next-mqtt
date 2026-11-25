# CLI 参考

`nextmqtt` 是基于共享编解码实现的小型 MQTT 3.1.1/5.0 客户端，适合冒烟测试、
合成负载和生成密码哈希。

```bash
dotnet run --project src/NextMqtt.Cli -- <命令> [选项]
```

## 命令

| 命令 | 用途 |
| --- | --- |
| `pub` | 发布一条消息 |
| `sub` | 订阅并打印消息 |
| `bench` | 多客户端发布压测 |
| `hash-password` | 生成配置用的 PBKDF2 哈希 |
| `version` | 输出版本 |

## 连接选项

| 选项 | 默认 | 说明 |
| --- | --- | --- |
| `--host` | `localhost` | Broker 地址 |
| `--port` | 1883（`--tls` 时为 8883） | 端口 |
| `--tls` | 关闭 | 使用 TLS |
| `--insecure` | 关闭 | 跳过证书校验（仅测试） |
| `--username` | — | MQTT 用户名 |
| `--password` | — | MQTT 密码或 JWT |
| `--client-id` | 自动生成 | 客户端 ID |
| `--protocol` | `5` | `5` 或 `3.1.1` |
| `--session-expiry` | 0 | MQTT 5 会话过期秒数 |
| `--resume` | 关闭 | 关闭 clean start，恢复会话 |

## 发布

```bash
nextmqtt pub --topic demo/hello --message "hello" --qos 1
nextmqtt pub --topic firmware/device-1 --file firmware.bin --qos 1
printf 'from stdin' | nextmqtt pub --topic demo/stdin --stdin
nextmqtt pub --topic demo/retained --message value --retain
```

| 选项 | 说明 |
| --- | --- |
| `--topic` | 必填；Broker 拒绝通配符 |
| `--message` | UTF-8 负载 |
| `--file` | 原始文件负载 |
| `--stdin` | 从标准输入读取 |
| `--qos` | 0、1 或 2 |
| `--retain` | 保存为保留消息 |

QoS 1 等待 PUBACK；QoS 2 完成 PUBREC/PUBREL/PUBCOMP。失败原因码返回退出码 1。

## 订阅

```bash
nextmqtt sub --topic 'sensors/#' --qos 1
nextmqtt sub --topic 'alerts/#,audit/#' --count 10
```

| 选项 | 说明 |
| --- | --- |
| `--topic` | 必填；支持逗号分隔多个过滤器 |
| `--qos` | 请求 QoS |
| `--count` | 收到 N 条后退出；0 表示持续运行 |

订阅端会自动确认 QoS 1/2 消息，打印主题、QoS、retain、字节数和 UTF-8/Base64
负载。

## 请求与应答

```bash
nextmqtt request --topic svc/echo --payload '{"ping":1}' --timeout 5
```

先订阅回复主题，再用 MQTT 5 的 `Response Topic` 与 `Correlation Data` 发布请求，
收到关联数据匹配的第一条回复后打印内容。需要 MQTT 5（默认）。

| 选项 | 默认 | 说明 |
| --- | --- | --- |
| `--topic` | – | 请求主题（必填） |
| `--payload` | 空 | 请求负载文本 |
| `--response-topic` | `/reply/<client>/<guid>` | 回复必须使用的主题 |
| `--correlation` | 随机 GUID | 用于匹配的关联数据 |
| `--timeout` | 5 | 等待回复的秒数 |
| `--qos` | 0 | 请求与订阅的 QoS |

超时未收到回复时退出码为 1。
## 压测

```bash
nextmqtt bench --clients 20 --messages 1000 --size 128 --qos 1
```

| 选项 | 默认 | 说明 |
| --- | --- | --- |
| `--clients` | 10 | 并发连接数 |
| `--messages` | 1000 | 每客户端消息数 |
| `--size` | 64 | 负载字节数 |
| `--qos` | 0 | 发布 QoS |
| `--topic` | `bench` | 主题前缀，每客户端使用 `<topic>/<index>` |

输出耗时、消息/秒、MiB/秒和失败数。该工具是合成发布压测，不等同于完整合规或
容量测试；请在隔离环境运行并观察 Broker CPU 与丢弃指标。

### 连接压测

```bash
nextmqtt bench-connections --connections 5000 --hold 5
```

| 选项 | 默认 | 说明 |
| --- | --- | --- |
| `--connections` | 1000 | 并行建立的连接数 |
| `--hold` | 5 | 保持连接的秒数（0 表示立即关闭） |

输出成功连接数、每秒建连速率、失败数，并在保持窗口结束后全部关闭。只要有连接
失败，退出码即非零。单机超过约 1 万连接时请放宽客户端临时端口范围或使用多个
压测机，否则瓶颈会落在客户端而不是 Broker。

## 密码哈希

```bash
nextmqtt hash-password --password 'correct horse battery staple'
```

输出：

```text
pbkdf2-sha256$210000$<salt-base64>$<hash-base64>
```

用于 `Authentication:Users[].PasswordHash` 或 `Admin:PasswordHash`。引导管理员
更推荐使用 `NEXTMQTT_ADMIN_PASSWORD`。

## 退出码

| 码 | 含义 |
| --- | --- |
| 0 | 成功 |
| 1 | Broker 拒绝、连接失败或压测失败 |
| 2 | 命令行参数错误 |
| 130 | Ctrl+C 中断 |

## TLS 示例

```bash
nextmqtt sub --host mqtt.example.com --port 8883 --tls --topic demo/#
nextmqtt pub --host mqtt.example.com --port 8883 --tls --insecure \
  --topic demo/test --message hello --qos 1
```

`--insecure` 仅用于本地自签名证书。
