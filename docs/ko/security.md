# 보안

## 신뢰 경계

| 경계 | 보호 |
| --- | --- |
| MQTT 클라이언트 → Broker | TLS, 비밀번호/JWT, ACL |
| 브라우저 → UI | Bearer/API Key, HTTPS |
| 노드 → 노드 | HMAC-SHA256, 공유 키 |
| 운영자 → 컨테이너 | UID 10001, 비루트 |

## 체크리스트

### 자격 증명

- 강한 관리 비밀번호
- 안정적인 토큰 서명 키
- `.env`, 인증서, API 키를 커밋하지 않음
- 유출 시 즉시 교체

### MQTT

- 익명 비활성화
- ACL 기본 거부 + 명시 규칙
- 외부 네트워크 TLS
- inflight/큐/패킷 제한
- 엣지에서 속도 제한

### UI / 클러스터

- HTTPS와 소스 IP 제한
- 토큰 유효 기간 단축
- `/metrics` 보호
- `/internal/cluster/*` 비공개
- 클러스터 키 16자 이상, 환경별 고유
- NTP 동기화

### 컨테이너

- UID 10001
- `allowPrivilegeEscalation: false`
- 모든 capabilities 제거
- 리소스 제한
- 읽기 전용 루트 + 쓰기 가능 `/tmp`

## 인증

- PBKDF2-SHA256, 16바이트 salt, 210,000회
- JWT는 HS256만, exp/nbf/iss/aud/sub 검증
- 관리 토큰은 별도 키
- API 키는 SHA-256 다이제스트

MQTT JWT, 클러스터, 관리 키를 재사용하지 마세요.

## DoS

패킷 크기, inflight, 큐, 송신 채널, CONNECT 타임아웃, keep-alive는 제한됩니다.
IP별 연결 제한과 메시지 속도 제한은 아직 없습니다. 로드 밸런서, NetworkPolicy,
리소스 제한으로 보완하세요.

## 신고

<https://github.com/cn-cam/next-mqtt/security/advisories/new>

공개 이슈 대신 비공개 취약점 신고를 사용하세요.
