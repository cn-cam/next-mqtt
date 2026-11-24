# 认证与 ACL

## 客户端认证顺序

1. 本地用户名 / PBKDF2 密码
2. MQTT 密码字段中的 HS256 JWT
3. 匿名（仅在显式启用时）

失败返回 CONNACK `0x86`；MQTT 3.1.1 对应返回码 4。

## 密码用户

```json
{
  "Authentication": {
    "AllowAnonymous": false,
    "Users": [
      {
        "Username": "device-1",
        "PasswordHash": "pbkdf2-sha256$210000$...$...",
        "Roles": ["devices"]
      }
    ]
  }
}
```

```bash
nextmqtt hash-password --password 's3cret'
```

哈希包含迭代次数与随机盐，校验使用常量时间比较。

## JWT

```json
{
  "Authentication": {
    "Jwt": {
      "Secret": "至少 32 字符的随机密钥",
      "Issuer": "https://issuer.example",
      "Audience": "nextmqtt",
      "RequireExpiration": true
    }
  }
}
```

客户端把 JWT 放在 MQTT 密码字段：

- 仅支持 `HS256`
- 校验 `exp`、`nbf`、`iss`、`aud`
- `sub` 作为用户名
- `roles` 包含 `admin` 时成为超级用户

## 匿名

```bash
NEXTMQTT_ALLOW_ANONYMOUS=true
```

匿名主体没有 ACL 特权，除非规则明确允许。公网部署应关闭。

## 主题 ACL

```json
{
  "Acl": {
    "DefaultPolicy": "Deny",
    "Rules": [
      { "Effect": "Allow", "Action": "Subscribe", "Topic": "$SYS/#" },
      { "Effect": "Allow", "Action": "Both", "Topic": "devices/${clientid}/#" },
      { "Effect": "Deny", "Action": "Publish", "Topic": "devices/+/command" }
    ]
  }
}
```

- 规则按顺序匹配，首条命中生效。
- `Action`：`Publish`、`Subscribe`、`Both`。
- `Username` / `ClientId` 支持 `*`、`?` 通配。
- `Topic` 支持 `${username}`、`${clientid}`。

订阅规则必须**覆盖**请求的过滤器：

| 规则 | 请求 | 允许 |
| --- | --- | --- |
| `sensors/#` | `sensors/+/temperature` | ✅ |
| `sensors/+` | `sensors/#` | ❌ |
| `#` | `$SYS/#` | ❌（`$` 规则） |

保留消息在投递时再次检查 ACL。

## 超级用户

- `Authentication:Superusers` 中的用户名
- 本地用户或 JWT 的 `admin` 角色

超级用户绕过全部 ACL。

## 控制台认证

- `NEXTMQTT_ADMIN_PASSWORD` 启动时哈希
- `POST /api/v1/auth/login` 返回 HS256 Bearer Token
- Token 有效期由 `Admin:TokenLifetimeMinutes` 控制
- `X-API-Key` 使用 `Admin:ApiKeySha256` 白名单

控制台令牌不是 MQTT 凭据。未设置 `NEXTMQTT_ADMIN_TOKEN_SECRET` 时会生成临时密钥，
重启后令牌失效。

## 加固建议

- 公网关闭匿名，ACL 默认拒绝。
- 为 MQTT、控制台、集群使用不同密钥。
- 控制台走 HTTPS，限制来源 IP。
- 定期轮换令牌与密钥。
