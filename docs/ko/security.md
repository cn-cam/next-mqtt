# 보안 모델

NextMQTT는 독립적인 클린룸 구현입니다. 보안 보고와 강화 질문은 저장소의 비공개
권고 채널로 받습니다.

## 신뢰 경계

| 경계 | 보호 |
| --- | --- |
| MQTT 클라이언트 → 브로커 | 선택적 TLS, 비밀번호/JWT 인증, 토픽 ACL |
| 브라우저 → 대시보드 | Bearer 토큰 또는 API 키, 선택적 HTTPS |
| 노드 → 노드 | HMAC-SHA256 서명 요청, 공유 시크릿, 선택적 HTTPS |
| 운영자 → 컨테이너 | 비루트 UID 10001, 케이퍼빌리티 제거, 선택적 읽기 전용 루트 |
| 빌드 → 릴리스 | CI 컴플라이언스 스캔, CodeQL, 고정 의존성, OCI 출처 |

## 강화 체크리스트

### 자격 증명

- [ ] 강한 `NEXTMQTT_ADMIN_PASSWORD` 또는 미리 해시한 `Admin:PasswordHash` 설정
- [ ] `NEXTMQTT_ADMIN_TOKEN_SECRET`을 모든 노드가 공유하는 무작위 값으로 설정
- [ ] `.env`, 인증서, 개인 키, API 키를 커밋하지 않음
- [ ] 노출이 의심되면 관리자 비밀번호와 토큰 시크릿 교체
- [ ] 가능하면 하드웨어 보호 시크릿 저장소 사용

시크릿을 생성합니다:

```bash
openssl rand -base64 32
```

### MQTT 접근

- [ ] 익명 접근 비활성화
- [ ] 기본 거부로 두고 명시적 ACL 규칙 추가
- [ ] 신뢰 네트워크 밖의 클라이언트에는 TLS 요구
- [ ] 메모리 예산에 맞는 세션 한도 설정
- [ ] 연결 속도 제한을 강제하는 게이트웨이 고려

### 대시보드

- [ ] HTTPS와 네트워크 제한 뒤에 배치
- [ ] 토큰 가치가 높으면 `Admin:TokenLifetimeMinutes` 단축
- [ ] 노출 시 `Admin:MetricsRequireAuth`로 `/metrics` 보호
- [ ] `/internal/cluster/*`를 공개하지 않음
- [ ] CORS를 자신의 대시보드 오리진으로 제한

### 클러스터

- [ ] 환경마다 고유한 16자 이상 시크릿 사용
- [ ] 시계를 NTP로 5분 이내 동기화
- [ ] 사설 네트워크 밖에서는 HTTPS Advertise URL 사용
- [ ] ingress에서 내부 클러스터 경로 차단
- [ ] 유지 보수 시간에 시크릿 교체

### 컨테이너와 Kubernetes

- [ ] UID 10001로 실행(이미지 기본값)
- [ ] `allowPrivilegeEscalation: false`와 모든 케이퍼빌리티 제거 유지
- [ ] CPU/메모리 제한 설정
- [ ] 읽기 전용 루트와 쓰기 가능한 `/tmp`, `/app/data` 사용
- [ ] NetworkPolicy 또는 더 엄격한 egress 규칙 적용
- [ ] 자격 증명에 `existingSecret` / 외부 시크릿 오퍼레이터 사용

## 인증 세부

### 비밀번호 해시

PBKDF2-SHA256, 무작위 16바이트 salt, 기본 210,000회 반복. 인코딩된 해시가 반복 횟수를
포함하며 검증은 상수 시간 비교를 씁니다.

### JWT

HS256만 받습니다. 기본으로 `exp`가 필수이고 `nbf`, `iss`, `aud`를 검증하며 `sub`
클레임이 주체가 됩니다. 공유 시크릿은 `Authentication:Jwt:Secret`으로 설정합니다.

관리자 토큰 시크릿, 클러스터 시크릿, JWT 시크릿을 재사용하지 마십시오.

### 관리자 토큰

대시보드 토큰은 `Admin:TokenSecret`으로 서명한 HS256 JWT입니다. `admin` 역할을 가지며
REST API 미들웨어만 받습니다. API 키는 SHA-256 다이제스트로 대조하며 관리자 토큰과
같은 힘을 가진 Bearer 자격 증명으로 취급합니다.

## 데이터 처리

- 페이로드는 불투명한 바이트이며 기본적으로 로그에 남기지 않습니다.
- MQTT 사용자 속성과 메타데이터는 보존됩니다. 토픽 이름이나 사용자 속성에 비밀을 넣지
  마십시오.
- 대시보드는 토픽 이름과 페이로드 크기를 보여 주며 내용은 보여 주지 않습니다.
- 세션, 큐, retained는 메모리에 있고 설정에 따라 파일 저장소에도 들어갑니다. 크래시
  덤프나 데이터 볼륨에 포함될 수 있으므로 접근을 제한하십시오.
- 클러스터 중복 제거 캐시는 최대 10분간 메시지 ID를 보관합니다.

## 서비스 거부 고려

브로커는 패킷 크기, inflight 윈도, 오프라인 큐, 송신 채널을 제한하고 CONNECT 제한
시간과 keep-alive를 강제합니다. 원격 주소별 연결 속도 제한, 클라이언트별 발행 속도
제한, 토픽 쿼터는 `RateLimits`에서 사용할 수 있지만 기본은 꺼져 있습니다. 다음을 함께
사용하십시오:

- 네트워크 정책과 로드 밸런서 제한
- 에지에서의 연결 속도 제한
- 큰 페이로드를 받기 전의 인증
- 컨테이너 리소스 제한

## 취약점 보고

GitHub 비공개 취약점 보고를 사용합니다:

<https://github.com/cn-cam/next-mqtt/security/advisories/new>

버전, 배포 형태, 재현 절차, 영향을 포함하십시오. 패치되지 않은 취약점을 공개
이슈로 열지 마십시오.

## 독립성과 상표

이 프로젝트에는 다른 MQTT 브로커 제품의 소스 코드, 문서 문구, 설정 형식, 로고,
상표가 포함되지 않습니다. 브랜드 이름 규칙은 CI에서 강제됩니다.
[AGENTS.md](https://github.com/cn-cam/next-mqtt/blob/main/AGENTS.md)와
[TRADEMARKS.md](https://github.com/cn-cam/next-mqtt/blob/main/TRADEMARKS.md)를
참고하십시오.
