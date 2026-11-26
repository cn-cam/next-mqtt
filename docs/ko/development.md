# 개발

## 도구 체인

| 도구 | 버전 |
| --- | --- |
| .NET SDK | 10.0.100+ |
| Node.js | 22+(24 권장) |
| npm | 10+ |
| Docker | 선택 사항. 이미지와 클러스터 테스트용 |
| Helm | 선택 사항. 차트 렌더링용 |

`global.json`은 SDK 기능 밴드를 고정하고 로컬 개발에서 더 새로운 메이저 SDK를
허용합니다.

## 빌드와 테스트

```bash
dotnet restore NextMqtt.slnx
dotnet build NextMqtt.slnx -c Release
dotnet test NextMqtt.slnx -c Release
```

테스트 스위트:

| 프로젝트 | 범위 |
| --- | --- |
| `NextMqtt.Core.Tests` | 코덱 왕복, 잘못된 패킷, 토픽 일치, 트라이 |
| `NextMqtt.Broker.Tests` | 세션, 큐, retained, 인증, ACL, 클러스터 라우팅 |
| `NextMqtt.Server.Tests` | 공유 코덱을 사용한 실제 루프백 TCP 통합 테스트 |

통합 테스트는 임시 포트에서 브로커를 시작하고 CONNECT, QoS 0/1/2, retained, 유언,
영속 세션, 인계, ACL, 인증을 검증합니다.

## 대시보드

```bash
cd web
npm install
npm run dev        # http://localhost:5173
npm run typecheck
npm run build
```

Vite는 `/api`, `/healthz`, `/readyz`, `/metrics`를 `http://localhost:8080`으로
프록시합니다.

UI는 Vue 3 + TypeScript이며 다음을 사용합니다:

- 직접 작성한 CSS(컴포넌트 프레임워크 없음)
- 작은 SVG 아이콘 세트
- 가벼운 반응형 i18n 계층(영어와 중국어)
- WebSocket 대시보드 채널 대신 폴링 composable

## 문서

```bash
cd docs
npm install
npm run dev        # http://localhost:5173(VitePress 기본)
npm run build
```

GitHub Pages용으로 빌드할 때는 `DOCS_BASE=/next-mqtt/`를 설정합니다.

## 저장소 구조

```text
src/NextMqtt.Core        코덱과 토픽 기본 요소(의존성 없음)
src/NextMqtt.Broker      세션, 라우팅, 인증, ACL, retained, 클러스터
src/NextMqtt.Server      호스트, 리스너, REST API, 정적 대시보드
src/NextMqtt.Cli         pub/sub/bench/hash 명령
tests/                   단위 및 통합 테스트
web/                     Vue 3 대시보드
docs/                    VitePress 문서
deploy/                  Docker, Compose, Kubernetes, Helm
scripts/                 컴플라이언스와 유지 관리 스크립트
```

## 프로토콜 기능 추가

1. `src/NextMqtt.Core/Protocol`의 패킷 모델을 추가하거나 확장합니다.
2. 플래그와 속성 검증을 포함해 디코더와 인코더를 갱신합니다.
3. 왕복 테스트와 잘못된 입력 테스트를 추가합니다.
4. 동작을 `ClientConnection` 또는 `BrokerSession`에 연결합니다.
5. `NextMqtt.Server.Tests`에 루프백 통합 테스트를 추가합니다.
6. `docs/protocol.md`와 `CHANGELOG.md`를 갱신합니다.

자명하지 않은 동작은 주석에 관련 OASIS MQTT 명세 절을 인용하십시오.

## 규칙

- `net10.0`, nullable 활성, implicit usings.
- 파일 범위 네임스페이스, 4칸 들여쓰기, `_camelCase` 필드.
- 비동기 메서드는 `Async`로 끝나고 `CancellationToken`을 받습니다.
- 요청 경로에서 `.Result`, `.Wait()`, `Thread.Sleep`을 쓰지 않습니다.
- 유계 큐와 채널을 쓰고 드롭을 지표로 드러냅니다.
- `sealed` 클래스와 불변 옵션을 선호합니다.
- 대시보드 컴포넌트는 `<script setup lang="ts">`와 타입 지정 props를 사용합니다.

## 컴플라이언스 게이트

모든 커밋은 다음을 통과해야 합니다:

```bash
./scripts/compliance-check.sh     # Linux/macOS/Git Bash
./scripts/compliance-check.ps1    # Windows
```

스캐너는 금지 브랜드 토큰을 실행 시 조립하고 파일 이름, 추적·미추적 내용, 커밋
메시지, 브랜치, 태그를 검사합니다. CI 필수 작업이기도 합니다.

주석, 테스트, 문서에도 예외는 없습니다. 용어가 충돌하면 이름을 바꾸십시오.

## 런타임 의존성

core, broker, server에는 **서드파티 NuGet 의존성이 없습니다**. Kafka와 RabbitMQ
브리지는 ADR에서 승인된 고정 버전의 `Confluent.Kafka`와 `RabbitMQ.Client`를
사용합니다. 그 외 의존성을 추가하려면 `docs/adr/`의 아키텍처 결정 기록과
`docs/legal/third-party-licenses.md` 갱신이 필요합니다.

## 커밋 스타일

Conventional Commits, 명령형, 72자 이내 제목:

```text
feat(broker): support message expiry on retained delivery
fix(web): keep the session table stable while polling
docs: document the cluster secret rotation caveat
```

각 커밋은 빌드와 테스트를 통과해야 합니다. 풀 리퀘스트에는 동작 변경의 테스트와 문서
갱신을 포함하십시오.

## 수동 검증 레시피

```bash
# 브로커 시작
dotnet run --project src/NextMqtt.Server

# 한 셸에서 구독
dotnet run --project src/NextMqtt.Cli -- sub --topic test/# --count 1

# 다른 셸에서 발행
dotnet run --project src/NextMqtt.Cli -- pub --topic test/hello --message hi --qos 1
```

클러스터:

```bash
docker compose -f deploy/compose/docker-compose.cluster.yml up -d --build
# healthy를 기다린 뒤 포트 1883과 1884로 두 CLI 명령 실행
```

## 코드 리뷰 체크리스트

- [ ] 금지 브랜드 토큰 없음
- [ ] 프로토콜 변경이 인코더/디코더와 통합 테스트로 검증됨
- [ ] 유계 큐와 타임아웃 유지
- [ ] 오류가 올바른 MQTT 이유 코드로 매핑됨
- [ ] 대시보드 타입이 API 계약과 일치
- [ ] 문서, 변경 기록, 예제 갱신
- [ ] 비밀 정보나 개인 토큰이 커밋되지 않음
