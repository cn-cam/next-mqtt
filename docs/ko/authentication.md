# 인증과 ACL

NextMQTT는 세 가지 클라이언트 인증 방식과 순서 있는 토픽 ACL을 제공합니다.
대시보드 인증은 별도이며 이 페이지 끝에서 설명합니다.

## 클라이언트 인증

`ClientAuthenticator`는 다음 순서로 시도합니다:

1. 로컬 사용자 이름/비밀번호(PBKDF2-SHA256)
2. MQTT 비밀번호 필드의 HS256 JWT
3. 활성화된 경우 익명 접근

실패하면 CONNACK `0x86`(사용자 이름 또는 비밀번호 오류)을 반환합니다. MQTT 3.1.1
클라이언트는 반환 코드 `4`를 받습니다.

### 비밀번호 사용자

사용자는 `Authentication:Users`에 설정합니다:

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

해시를 만듭니다:

```bash
dotnet run --project src/NextMqtt.Cli -- hash-password --password 's3cret'
```

비밀번호 비교는 `CryptographicOperations.FixedTimeEquals`를 사용합니다. 인코딩 형식이
반복 횟수를 함께 저장하므로 해시를 점진적으로 올릴 수 있습니다.

### JWT

공유 시크릿을 설정합니다:

```json
{
  "Authentication": {
    "Jwt": {
      "Secret": "32자 이상의 무작위 문자열로 교체",
      "Issuer": "https://issuer.example",
      "Audience": "nextmqtt",
      "RequireExpiration": true
    }
  }
}
```

클라이언트는 아무 사용자 이름과 JWT를 비밀번호로 보냅니다:

```text
Username: anything
Password: eyJhbGciOiJIUzI1NiIs...
```

요건:

- `alg`는 `HS256`이어야 합니다.
- `exp`는 미래여야 합니다(`RequireExpiration`이 false면 생략 가능).
- `nbf`는 30초 여유를 두고 검증합니다.
- `iss`/`aud`는 설정된 경우 검증합니다.
- `sub` 클레임이 클라이언트 사용자 이름이 됩니다.
- `roles` 클레임에 `admin`이 있으면 슈퍼유저가 됩니다.

HMAC 토큰만 지원합니다. 비대칭 키를 쓰려면 자체 게이트웨이에서 인증을 종단하고
단기 HS256 토큰을 전달하거나 `IClientAuthenticator`를 확장하십시오.

### 익명 접근

```bash
NEXTMQTT_ALLOW_ANONYMOUS=true
```

익명 클라이언트는 클라이언트 ID를 이름으로 하는 주체를 받지만, ACL이 허용하지 않으면
권한이 없습니다. 로컬 개발에는 편리하지만 운영에서는 꺼야 합니다.

## 토픽 ACL

ACL 규칙은 `Acl` 아래에 둡니다:

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

### 의미

- 규칙은 순서대로 평가되며, 액션과 ID glob이 맞는 첫 규칙이 결정합니다.
- `Action`은 `Publish`, `Subscribe`, `Both` 중 하나입니다.
- `Username`과 `ClientId`는 `*`, `?` glob을 받습니다. 생략하면 모든 값입니다.
- `Topic`은 `${username}`과 `${clientid}`를 치환합니다.
- 어떤 규칙도 맞지 않으면 `DefaultPolicy`가 적용됩니다.

발행 인가는 규칙 필터를 실제 토픽에 대해 평가합니다. 구독 인가는 규칙 필터가 요청
필터를 **포함**해야 합니다:

| 규칙 | 요청 필터 | 허용 |
| --- | --- | --- |
| `sensors/#` | `sensors/+/temperature` | ✅ |
| `sensors/#` | `sensors/#` | ✅ |
| `sensors/+` | `sensors/#` | ❌ |
| `#` | `$SYS/#` | ❌(선두 `$` 규칙) |
| `devices/${clientid}/#` | `devices/device-1/#` | 클라이언트 `device-1`이면 ✅ |

retained 메시지는 전달 시 다시 확인하므로, 구독 규칙이 포함하지 않는 retained 토픽은
읽을 수 없습니다.

### 슈퍼유저

다음 중 하나면 슈퍼유저입니다:

- 사용자 이름이 `Authentication:Superusers`에 있음, 또는
- 로컬 사용자 역할이나 JWT `roles`에 `admin`이 있음.

슈퍼유저는 ACL을 완전히 우회합니다. 이 목록을 관리 표면의 일부로 취급하십시오.

## 대시보드 인증

대시보드는 별도 자격 증명 저장소를 사용합니다:

- `NEXTMQTT_ADMIN_PASSWORD`는 시작 시 해시됩니다.
- `POST /api/v1/auth/login`이 `Admin:TokenLifetimeMinutes` 동안 유효한 HS256 토큰을
  반환합니다.
- 토큰은 브라우저 `localStorage`에 저장되고 `Authorization: Bearer`로 전송됩니다.
- `X-API-Key`는 SHA-256 다이제스트가 `Admin:ApiKeySha256`에 있을 때 자동화용으로
  허용됩니다.

대시보드 토큰은 **MQTT 자격 증명이 아닙니다**. REST API만 인가합니다.

### 부트스트랩 비밀번호

비밀번호가 설정되지 않으면:

- 브로커가 무작위 비밀번호를 생성하고,
- 메모리에서 해시하며,
- 평문을 경고 로그에 한 번 씁니다.

비밀번호는 디스크에 저장되지 않습니다. 비밀번호 없이 재시작하면 새로 생성됩니다.
안정적인 운영을 위해 `NEXTMQTT_ADMIN_PASSWORD` 또는 `Admin:PasswordHash`를
설정하십시오.

### 토큰 보안

- `NEXTMQTT_ADMIN_TOKEN_SECRET`을 모든 노드에 같은 무작위 값으로 설정합니다.
- 토큰은 Bearer 자격 증명입니다. 대시보드에는 HTTPS를 사용하십시오.
- 보안이 중요한 환경에서는 `TokenLifetimeMinutes`를 줄이십시오.
- 로그아웃은 브라우저의 사본만 지웁니다. 모든 토큰을 무효화하려면 토큰 시크릿을
  교체하십시오.

## 위협 모델 메모

- 클러스터 시크릿은 노드를 인증하며 클라이언트용이 아닙니다. 이미지와 로그에 넣지
  마십시오.
- MQTT 페이로드는 불투명한 바이트입니다. 페이로드 형식 표시자는 구조적 정확성만
  검증합니다.
- 연결과 발행 속도 제한은 원격 주소별, 클라이언트별 토큰 버킷(`RateLimits`)입니다.
  적대적인 네트워크에서는 에지 제한과 함께 사용하십시오.
- 선택적 클라이언트 인증서 검증은 플랫폼 기본값을 따릅니다. 상호 TLS 정책은 배포마다
  정하십시오.
