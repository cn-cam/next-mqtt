# 認証と ACL

NextMQTT は 3 つのクライアント認証方式と、順序付きのトピック ACL を提供します。
ダッシュボードの認証は別系統で、このページの末尾で説明します。

## クライアント認証

`ClientAuthenticator` は次の順に試します：

1. ローカルのユーザー名/パスワード（PBKDF2-SHA256）
2. MQTT のパスワードフィールドに入れた HS256 JWT
3. 有効なら匿名アクセス

失敗すると CONNACK `0x86`（ユーザー名またはパスワードが不正）を返します。
MQTT 3.1.1 のクライアントには戻り値 `4` を返します。

### パスワードユーザー

ユーザーは `Authentication:Users` に設定します：

```json
{
  "Authentication": {
    "AllowAnonymous": false,
    "Users": [
      {
        "Username": "device-1",
        "PasswordHash": "pbkdf2-sha256$210000$c2FsdA==$aGFzaA==",
        "Roles": ["devices"]
      }
    ]
  }
}
```

ハッシュを生成します：

```bash
dotnet run --project src/NextMqtt.Cli -- hash-password --password 's3cret'
```

パスワード比較には `CryptographicOperations.FixedTimeEquals` を使います。エンコード
形式が反復回数を保持するため、ハッシュを段階的に更新できます。

### JWT

共有鍵を設定します：

```json
{
  "Authentication": {
    "Jwt": {
      "Secret": "32 文字以上のランダム文字列に置き換える",
      "Issuer": "https://issuer.example",
      "Audience": "nextmqtt",
      "RequireExpiration": true
    }
  }
}
```

クライアントは任意のユーザー名と、パスワードとして JWT を送ります：

```text
Username: anything
Password: eyJhbGciOiJIUzI1NiIs...
```

要件：

- `alg` は `HS256` でなければなりません。
- `exp` は未来である必要があります（`RequireExpiration` が false の場合は不要）。
- `nbf` は 30 秒の余裕をもって検証します。
- `iss`/`aud` は設定されていれば検証します。
- `sub` クレームがクライアントのユーザー名になります。
- `roles` クレームに `admin` が含まれるとスーパーユーザーになります。

対応するのは HMAC トークンだけです。非対称鍵を使う場合は、自前のゲートウェイで認証を
終端して短命な HS256 トークンを転送するか、`IClientAuthenticator` を拡張してください。

### 匿名アクセス

```bash
NEXTMQTT_ALLOW_ANONYMOUS=true
```

匿名クライアントはクライアント ID を名前とする主体を受け取りますが、ACL が許可しない
限り権限はありません。ローカル開発には便利ですが、本番では無効にしてください。

## トピック ACL

ACL ルールは `Acl` の下に置きます：

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

### セマンティクス

- ルールは順序付きで、アクションと ID の glob が一致した最初のルールが決定します。
- `Action` は `Publish`、`Subscribe`、`Both` のいずれかです。
- `Username` と `ClientId` は `*` と `?` の glob を受け付けます。省略すると任意。
- `Topic` は `${username}` と `${clientid}` を展開できます。
- どのルールにも一致しない場合は `DefaultPolicy` が適用されます。

公開の認可は、ルールのフィルタを具体的なトピックに対して評価します。購読の認可は、
ルールのフィルタが要求フィルタを**包含**している必要があります：

| ルール | 要求フィルタ | 許可 |
| --- | --- | --- |
| `sensors/#` | `sensors/+/temperature` | ✅ |
| `sensors/#` | `sensors/#` | ✅ |
| `sensors/+` | `sensors/#` | ❌ |
| `#` | `$SYS/#` | ❌（先頭 `$` の規則） |
| `devices/${clientid}/#` | `devices/device-1/#` | クライアント `device-1` なら ✅ |

retained メッセージは配信時にも再確認するため、購読ルールが包含しない retained
トピックは読めません。

### スーパーユーザー

次のいずれかでスーパーユーザーになります：

- ユーザー名が `Authentication:Superusers` に含まれる、または
- ローカルユーザーのロールか JWT の `roles` に `admin` が含まれる。

スーパーユーザーは ACL を完全に迂回します。このリストは管理面の一部として扱って
ください。

## ダッシュボード認証

ダッシュボードは別の資格情報ストアを使います：

- `NEXTMQTT_ADMIN_PASSWORD` は起動時にハッシュ化されます。
- `POST /api/v1/auth/login` が `Admin:TokenLifetimeMinutes` 有効な HS256 トークンを
  返します。
- トークンはブラウザーの `localStorage` に保存され、
  `Authorization: Bearer` として送られます。
- `X-API-Key` は、SHA-256 ダイジェストが `Admin:ApiKeySha256` にある場合に自動化向け
  に受け付けます。

ダッシュボードのトークンは **MQTT の資格情報ではありません**。REST API だけを認可
します。

### ブートストラップパスワード

パスワードが未設定の場合：

- ブローカーがランダムなパスワードを生成し、
- メモリ上でハッシュ化し、
- 平文を一度だけ警告ログへ書きます。

パスワードはディスクに保存されません。パスワードを設定せずに再起動すると新しい
パスワードが生成されます。安定運用には `NEXTMQTT_ADMIN_PASSWORD` か
`Admin:PasswordHash` を設定してください。

### トークンの安全性

- `NEXTMQTT_ADMIN_TOKEN_SECRET` を全ノードで同じランダム値に設定します。
- トークンは Bearer 資格情報です。ダッシュボードには HTTPS を使ってください。
- 高い安全性が必要な環境では `TokenLifetimeMinutes` を短くします。
- サインアウトはブラウザーのコピーを消すだけです。全トークンを無効化するには
  トークン秘密鍵をローテーションします。

## 脅威モデル上の注意

- クラスタ秘密鍵はノードを認証するもので、クライアント用ではありません。イメージや
  ログに含めないでください。
- MQTT ペイロードは不透明なバイト列です。ペイロード形式インジケーターは構造上の
  正しさだけを検証します。
- 接続と公開のレート制限は、送信元アドレス単位とクライアント単位のトークンバケット
  （`RateLimits`）です。敵対的なネットワークではエッジの制限と併用してください。
- 任意のクライアント証明書の検証はプラットフォーム既定に従います。相互 TLS の
  ポリシーは導入ごとに決めてください。
