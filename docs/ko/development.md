# 개발

## 요구 사항

| 도구 | 버전 |
| --- | --- |
| .NET SDK | 10.0.100+ |
| Node.js | 22+ (24 권장) |
| Docker | 선택 |

## 빌드 / 테스트

```bash
dotnet restore NextMqtt.slnx
dotnet build NextMqtt.slnx -c Release
dotnet test NextMqtt.slnx -c Release
```

| 프로젝트 | 범위 |
| --- | --- |
| Core.Tests | codec, 잘못된 패킷, topic, trie |
| Broker.Tests | session, queue, retained, auth, ACL, cluster |
| Server.Tests | 실제 TCP 통합 |

현재 211개 테스트 통과(Core 63, Broker 105, Server 43).

## 대시보드

```bash
cd web
npm install
npm run dev
npm run typecheck
npm run build
```

Vite가 `/api`, `/healthz`, `/readyz`, `/metrics`를 `:8080`으로 프록시합니다.

## 문서

```bash
cd docs
npm install
npm run dev
npm run build
```

GitHub Pages는 `DOCS_BASE=/next-mqtt/`.

## 구조

```text
src/NextMqtt.Core       codec / topic
src/NextMqtt.Broker     session / routing / auth / cluster
src/NextMqtt.Server     listener / API / UI
src/NextMqtt.Cli        pub / sub / bench / hash
tests/                  단위/통합
web/                    Vue 3
docs/                   4개 언어 문서
deploy/                 Docker / K8s / Helm
```

## 프로토콜 변경

1. `Core/Protocol` 수정
2. encoder/decoder, 플래그/속성 검증
3. 왕복/잘못된 입력 테스트
4. `ClientConnection` / `BrokerSession` 연결
5. TCP 통합 테스트
6. `docs/protocol.md`와 CHANGELOG 갱신

## 규칙

- `net10.0`, nullable, implicit usings
- file-scoped namespace, 4 spaces
- 비동기 `Async` 접미사 + CancellationToken
- `.Result` / `.Wait()` / `Thread.Sleep` 금지
- 유계 큐, 드롭은 메트릭
- UI는 `<script setup lang="ts">`

## 컴플라이언스

```bash
./scripts/compliance-check.sh
./scripts/compliance-check.ps1
```

금지 브랜드 token을 런타임 생성하여 파일명, 내용, 이력, 브랜치, 태그를 검사합니다.
CI에서 강제됩니다.
