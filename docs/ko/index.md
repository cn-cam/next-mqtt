---
layout: home
title: 차세대 MQTT 플랫폼

hero:
  name: NextMQTT
  text: 차세대 MQTT 플랫폼
  tagline: 클라우드, 엣지, 실시간 데이터를 위한 .NET 네이티브 MQTT 플랫폼. 클러스터, 관리 API, 대시보드 내장.
  image:
    src: /logo.svg
    alt: NextMQTT
  actions:
    - theme: brand
      text: 시작하기
      link: /ko/getting-started
    - theme: alt
      text: 아키텍처
      link: /ko/architecture
    - theme: alt
      text: GitHub
      link: https://github.com/cn-cam/next-mqtt

features:
  - icon: 📡
    title: MQTT 3.1.1 / 5.0
    details: TCP, TLS, WebSocket, WSS. QoS 0/1/2, retained, will, 공유 구독, 토픽 별칭, 세션 만료.
  - icon: 🧩
    title: 통합과 플러그인
    details: Webhook, NATS, Kafka, RabbitMQ 브리지(유계 큐)와 인증·인가를 교체하는 자체 플러그인 호스트.
  - icon: 🔐
    title: 강화 가능한 보안
    details: PBKDF2, HS256 JWT, MQTT 5 확장 인증, 토픽 ACL, 관리 토큰, API 키, 키 경로를 지정하는 TLS.
  - icon: 🔁
    title: 자가 복구 클러스터
    details: 서명된 gossip, 노드 간 라우팅, 클러스터 전체 공유 구독, 참여 시 retained 동기화, 정지 노드 자동 제거.
  - icon: 🧭
    title: 운영 우선
    details: 클라이언트, 구독, retained, publish 콘솔, 플러그인·로그 화면, 클러스터 토폴로지, Prometheus 메트릭.
  - icon: 📦
    title: 단순한 배포
    details: 멀티 아키텍처 이미지, Compose, Kustomize, Helm. 프로브와 오토스케일링.
  - icon: 🧼
    title: 독립 구현
    details: 클린룸, Apache-2.0. 타사 Broker의 소스, 설정 형식, 로고, 상표를 포함하지 않습니다.
---

## 최근 추가

- **TLS를 명확하게**: 인증서, 개인 키 경로, 기대 도메인을 따로 설정합니다. 중간 인증서를 전송하고, 도메인을 포함하지 않는 인증서는 시작 시 경고합니다.
- **클러스터 자가 복구**: 참여 노드는 retained를 피어에서 가져오고, 멤버 변화 시 제한을 즉시 재계산하며, 무응답 노드는 실제로 제거됩니다.
- **통합은 라우팅 규칙 뒤에**: `drop` 으로 버려진 메시지는 Webhook이나 브리지에 가지 않고, `rewrite` 된 토픽이 하위로 전달됩니다.
- **대시보드의 플러그인과 로그**: 플러그인 호스트와 유계 로그 버퍼를 화면에서 볼 수 있습니다.
- **문서 게이트**: `scripts/docs-coverage.ps1` 이 주석 없는 멤버에서 빌드를 실패시킵니다.


## MQTT를 위한 새로운 기반

NextMQTT는 .NET 표준화 팀을 위해 단일 프로세스, 명시적 리소스 한도, 운영 도구를
한 번에 제공합니다.

- **.NET 10 단일 프로세스**. 외부 데이터베이스나 메시지 미들웨어 불필요.
- **전 계층 관측성**. 연결, 세션, inflight, 구독, retained, 클러스터.
- **예측 가능한 비동기 파이프라인**. `System.IO.Pipelines`와 유계 채널.
- **CI로 검증되는 배포 자산**. 위키 초안이 아닌 테스트된 매니페스트와 차트.

## 한눈에 보기

```text
MQTT 클라이언트 ──TCP/TLS──┐
브라우저 클라이언트 ──WS/WSS──┤
                             ├─► 리스너 ─► 코덱 ─► 세션 ─► 토픽 색인
관리 UI ──HTTPS──────────────┘                        │
                                                      ├─► retained 저장소
                                                      └─► 클러스터 라우터
```

컴포넌트 모델, 데이터 구조, 장애 동작은 [아키텍처](/ko/architecture)를, 바로
시작하려면 [시작하기](/ko/getting-started)를 참고하세요.

## 독립성

NextMQTT는 상용 MQTT Broker와 제휴, 보증, 파생 관계가 없습니다. 타사 Broker의
소스, 문서, 설정 형식, 로고, 상표를 포함하지 않습니다.
