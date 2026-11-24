# 인증과 ACL

## 인증 순서

1. 로컬 사용자(PBKDF2)
2. MQTT 비밀번호 필드의 HS256 JWT
3. 익명(명시적으로 활성화한 경우)

실패 시 CONNACK `0x86`(3.1.1은 4).

## 비밀번호

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

- `alg=HS256`만 지원
- `exp`, `nbf`, `iss`, `aud` 검증
- `sub`를 사용자명으로 사용
- `roles`에 `admin`이 있으면 슈퍼유저

```json
{
  "Authentication": {
    "Jwt": { "Secret": "32자 이상 랜덤 키", "RequireExpiration": true }
  }
}
```

## 익명

```bash
NEXTMQTT_ALLOW_ANONYMOUS=true
```

ACL이 명시적으로 허용하지 않으면 권한이 없습니다. 공개 환경에서는 비활성화하세요.

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

- 위에서부터 첫 일치 규칙 적용
- `Action`: Publish / Subscribe / Both
- `Username` / `ClientId`: `*`, `?`
- `Topic`: `${username}`, `${clientid}`

구독 규칙은 요청 필터를 **포함**해야 합니다:

| 규칙 | 요청 | 허용 |
| --- | --- | --- |
| `sensors/#` | `sensors/+/temperature` | ✅ |
| `sensors/+` | `sensors/#` | ❌ |
| `#` | `$SYS/#` | ❌ |

Retained는 전달 시 ACL을 다시 확인합니다.

## 관리 UI

- `NEXTMQTT_ADMIN_PASSWORD`를 시작 시 해시
- `POST /api/v1/auth/login`이 Bearer 토큰 발급
- `X-API-Key`는 `Admin:ApiKeySha256`과 비교

`NEXTMQTT_ADMIN_TOKEN_SECRET`이 없으면 임시 키가 생성되어 재시작 시 토큰이
무효화됩니다.

## 강화

- 익명 비활성화, ACL 기본 거부
- MQTT/관리/클러스터 키 분리
- 관리 UI HTTPS와 IP 제한
- 주기적 키 교체
