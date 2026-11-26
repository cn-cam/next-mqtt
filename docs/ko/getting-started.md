# 시작하기

NextMQTT는 HTTP API와 대시보드를 내장한 단일 브로커 프로세스입니다. 이 페이지에서는
몇 분 만에 브로커를 실행하고 첫 MQTT 교환까지 진행합니다.

## 요건

| 형태 | 요건 |
| --- | --- |
| 컨테이너 | Docker 24+(Compose v2 선택) |
| 소스에서 | .NET SDK 10.0.100+, Node.js 22+(24 권장) |
| Kubernetes | Kubernetes 1.28+, Ingress 컨트롤러(선택) |
| 이미지 빌드 | Docker BuildKit |

## Docker로 실행

```bash
docker run -d --name nextmqtt \
  -p 1883:1883 \
  -p 8080:8080 \
  -p 8083:8083 \
  -e NEXTMQTT_ADMIN_PASSWORD='change-me-now' \
  ghcr.io/cn-cam/next-mqtt:latest
```

Docker Hub 미러도 게시되어 있습니다:

```bash
docker pull oooooooooocam/next-mqtt:latest
```

<http://localhost:8080>을 열고 지정한 비밀번호로 `admin`에 로그인합니다. 대시보드는
브로커가 직접 제공하므로 별도 웹 서버가 필요하지 않습니다.

::: warning 개발용 기본값
동봉된 설정은 빠른 시작이 되도록 익명 MQTT와 느슨한 ACL을 허용합니다. 포트 1883이나
8080을 신뢰할 수 없는 네트워크에 노출하기 전에
[보안 가이드](/ko/security)를 따르고 다음을 설정하십시오:

```bash
NEXTMQTT_ALLOW_ANONYMOUS=false
NEXTMQTT_ACL__DEFAULTPOLICY=Deny
```
:::

관리자 비밀번호를 설정하지 않으면 브로커가 시작 시 생성해 로그에 씁니다:

```bash
docker logs nextmqtt | grep "bootstrap password"
```

## Compose로 실행

단일 노드:

```bash
cp .env.example .env
# .env를 편집한 뒤:
docker compose up -d
```

3노드 클러스터:

```bash
docker compose -f deploy/compose/docker-compose.cluster.yml up -d
```

| 노드 | MQTT | 대시보드 |
| --- | --- | --- |
| node-a | `localhost:1883` | <http://localhost:8080> |
| node-b | `localhost:1884` | <http://localhost:8081> |
| node-c | `localhost:1885` | <http://localhost:8082> |

한 노드에서 발행하고 다른 노드에서 구독하면 클러스터 라우팅을 확인할 수 있습니다.

## 소스에서 실행

```bash
dotnet run --project src/NextMqtt.Server
```

`dotnet run`은 대시보드 번들을 빌드하지 않습니다. UI 개발에는:

```bash
cd web
npm install
npm run dev        # http://localhost:5173, /api를 :8080으로 프록시
```

운영에 가까운 로컬 실행은 번들을 먼저 빌드합니다:

```bash
cd web && npm run build && cd ..
dotnet run --project src/NextMqtt.Server
```

서버는 개발 중 `web/dist`를 자동으로 읽고 `dotnet publish` 시 `wwwroot`로 복사합니다.

## 첫 메시지

MQTT 5와 MQTT 3.1.1을 지원하는 동봉 CLI를 사용합니다:

```bash
# 터미널 1
dotnet run --project src/NextMqtt.Cli -- sub --topic demo/# --qos 1

# 터미널 2
dotnet run --project src/NextMqtt.Cli -- pub --topic demo/hello --message "hello" --qos 1
```

구독자는 토픽, QoS, retain 플래그, 페이로드를 출력합니다. 발행 명령에 `--retain`을
추가하고 구독자를 끊은 뒤 다시 구독하면 retained 저장소에서 즉시 전달됩니다.

아무 MQTT 3.1.1/5.0 클라이언트 라이브러리로 `mqtt://localhost:1883` 또는
`ws://localhost:8083/mqtt`에 연결할 수도 있습니다.

## 포트

| 포트 | 리스너 | 설정 |
| --- | --- | --- |
| 1883 | MQTT over TCP | `Server:Tcp` |
| 8883 | MQTT over TLS | `Server:Tls`(기본 비활성) |
| 8083 | `/mqtt`의 MQTT over WebSocket | `Server:Http:WebSocketPort` |
| 8084 | MQTT over 보안 WebSocket | `Server:Http:TlsPort` |
| 8080 | REST API, 대시보드, `/metrics`, `/healthz` | `Server:Http:Port` |

모든 리스너는 기본적으로 `0.0.0.0`에 바인딩합니다. 컨테이너에서는 실제로 필요한
포트만 게시하십시오.

## 대시보드 둘러보기

| 페이지 | 표시 내용 |
| --- | --- |
| Overview | 클라이언트/세션/구독 게이지, 발행 처리량, 브로커 카운터, 노드 목록 |
| Clients | 온라인 및 영속 오프라인 세션, 클라이언트 연결 끊기 |
| Subscriptions | 필터, 공유 그룹, QoS, MQTT 5 구독 옵션 |
| Retained | 저장 메시지, 페이로드 크기, 만료, 삭제 |
| Publish | 평문 또는 Base64 페이로드를 브로커로 발행 |
| Cluster | 멤버십, 노드 상태, 토폴로지 |
| Settings | 적용 중인 리스너와 한도, 테마, 언어, 토큰 작업 |

대시보드는 몇 초마다 API를 폴링합니다. 헤더에 마지막 갱신 시각이 표시됩니다.

## 다음 단계

- [설정](/ko/configuration) — 모든 옵션과 환경 변수
- [인증과 ACL](/ko/authentication) — 사용자, JWT, 토픽 규칙
- [배포](/ko/deployment) — Docker, Compose, Kubernetes, Helm
- [클러스터](/ko/clustering) — 3노드 구성과 라우팅 모델
- [운영](/ko/operations) — 지표, 상태 확인, 문제 해결
