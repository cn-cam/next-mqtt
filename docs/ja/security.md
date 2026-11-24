# セキュリティ

## 信頼境界

| 境界 | 保護 |
| --- | --- |
| MQTT クライアント → Broker | TLS、パスワード/JWT、ACL |
| ブラウザ → UI | Bearer / API Key、HTTPS |
| ノード → ノード | HMAC-SHA256、共有キー |
| 運用者 → コンテナ | UID 10001、非 root |

## チェックリスト

### 認証情報

- 強力な管理パスワード
- 安定したトークン署名キー
- `.env`、証明書、API キーをコミットしない
- 漏洩時は即ローテーション

### MQTT

- 匿名無効
- ACL 既定拒否 + 明示ルール
- 外部は TLS
- inflight/キュー/パケット制限
- エッジでレート制限

### UI / クラスタ

- HTTPS と送信元制限
- トークン有効期間を短縮
- `/metrics` を保護
- `/internal/cluster/*` を非公開
- クラスタキーは 16 文字以上、環境ごとに固有
- NTP 同期

### コンテナ

- UID 10001
- `allowPrivilegeEscalation: false`
- capabilities 全削除
- リソース制限
- 読み取り専用 root + 書き込み可能 `/tmp`

## 認証

- PBKDF2-SHA256、16 バイト塩、210,000 回
- JWT は HS256 のみ、exp/nbf/iss/aud/sub 検証
- 管理トークンは別キー
- API キーは SHA-256 ダイジェスト

MQTT JWT、クラスタ、管理のキーを再利用しないでください。

## DoS

パケットサイズ、inflight、キュー、送信チャネル、CONNECT タイムアウト、
keep-alive は制限済み。IP 単位の接続制限やメッセージレート制限は未実装です。
ロードバランサ、NetworkPolicy、リソース制限で補完してください。

## 報告

<https://github.com/cn-cam/next-mqtt/security/advisories/new>

公開 issue ではなく非公開レポートを使用してください。
