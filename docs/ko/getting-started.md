# 시작하기

## 요구 사항

| 방식 | 요구 사항 |
| --- | --- |
| 컨테이너 | Docker 24+ |
| 소스 | .NET SDK 10.0.100+, Node.js 22+ |
| Kubernetes | 1.28+, Ingress 선택 |

## Docker

```bash
docker run -d --name nextmqtt \
  -p 1883:1883 -p 8080:8080 -p 8083:8083 \
  -e NEXTMQTT_ADMIN_PASSWORD='change-me-now' \
  ghcr.io/cn-cam/next-mqtt:latest
```

Docker Hub에서도 받을 수 있습니다:

```bash
docker pull ooooooooocam/next-mqtt:latest
```

`http://localhost:8080`에서 `admin`과 설정한 비밀번호로 로그인합니다.

::: warning 개발 기본값
기본 설정은 익명 접속과 느슨한 ACL을 허용합니다. 공개 전에
`NEXTMQTT_ALLOW_ANONYMOUS=false`, `NEXTMQTT_ACL__DEFAULTPOLICY=Deny`를 설정하세요.
:::

비밀번호를 설정하지 않으면 시작 로그에 생성된 비밀번호가 출력됩니다.

## Compose

```bash
cp .env.example .env
docker compose up -d
```

3노드 클러스터:

```bash
docker compose -f deploy/compose/docker-compose.cluster.yml up -d
```

## 소스에서 실행

```bash
dotnet run --project src/NextMqtt.Server
```

대시보드 개발:

```bash
cd web
npm install
npm run dev
```

운영형 로컬 실행:

```bash
cd web && npm run build && cd ..
dotnet run --project src/NextMqtt.Server
```

## 첫 메시지

```bash
# 터미널 1
dotnet run --project src/NextMqtt.Cli -- sub --topic demo/# --qos 1

# 터미널 2
dotnet run --project src/NextMqtt.Cli -- pub --topic demo/hello --message hello --qos 1
```

MQTT 3.1.1/5.0 클라이언트로 `mqtt://localhost:1883` 또는
`ws://localhost:8083/mqtt`에 연결할 수 있습니다.

## 포트

| 포트 | 용도 |
| --- | --- |
| 1883 | MQTT TCP |
| 8883 | MQTT TLS |
| 8083 | MQTT WebSocket `/mqtt` |
| 8084 | MQTT WSS |
| 8080 | API, 대시보드, `/metrics`, `/healthz` |

## 다음 단계

- [기능 목록](/ko/features)
- [설정](/ko/configuration)
- [인증과 ACL](/ko/authentication)
- [배포](/ko/deployment)
