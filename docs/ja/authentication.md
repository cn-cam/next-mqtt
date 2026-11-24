# 認証と ACL

## 認証順序

1. ローカルユーザー（PBKDF2）
2. MQTT パスワード欄の HS256 JWT
3. 匿名（明示的に有効化した場合）

失敗時は CONNACK `0x86`（3.1.1 は 4）。

## パスワード

```json
{
  "Authentication": {
    "AllowAnonymous": false,
    "Users": [
      { "Username": "device-1", "PasswordHash": "pbkdf2-sha256$...", "Roles": ["devices"] }
    ]
  }
}
```

```bash
nextmqtt hash-password --password 's3cret'
```

## JWT

- `alg=HS256` のみ
- `exp`、`nbf`、`iss`、`aud` を検証
- `sub` をユーザー名に使用
- `roles` に `admin` があるとスーパーユーザー

```json
{
  "Authentication": {
    "Jwt": { "Secret": "32 文字以上のランダムキー", "RequireExpiration": true }
  }
}
```

## 匿名

```bash
NEXTMQTT_ALLOW_ANONYMOUS=true
```

ACL で明示的に許可しない限り権限はありません。公開環境では無効化してください。

## ACL

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

- 上から順に評価し、最初の一致を採用
- `Action`: Publish / Subscribe / Both
- `Username` / `ClientId` は `*`、`?`
- `Topic` は `${username}`、`${clientid}`

購読ルールは要求フィルタを**包含**する必要があります：

| ルール | 要求 | 許可 |
| --- | --- | --- |
| `sensors/#` | `sensors/+/temperature` | ✅ |
| `sensors/+` | `sensors/#` | ❌ |
| `#` | `$SYS/#` | ❌ |

Retained は配信時にも ACL を再確認します。

## 管理 UI

- `NEXTMQTT_ADMIN_PASSWORD` を起動時にハッシュ
- `POST /api/v1/auth/login` が Bearer トークンを返す
- `X-API-Key` は `Admin:ApiKeySha256` と照合

`NEXTMQTT_ADMIN_TOKEN_SECRET` 未設定時は一時キーが生成され、再起動で失効します。

## 強化

- 匿名無効、ACL 既定拒否
- MQTT / 管理 / クラスタで別キー
- 管理 UI は HTTPS と IP 制限
- 定期的にキーをローテーション
