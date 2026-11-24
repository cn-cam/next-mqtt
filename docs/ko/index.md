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
    details: TCP, TLS, WebSocket, WSS. QoS 0/1/2, retained, will, 공유 구독, 세션 만료.
  - icon: 🧭
    title: 운영 우선
    details: 클라이언트, 구독, retained, publish 콘솔, 클러스터 토폴로지, Prometheus 메트릭.
  - icon: 🧩
    title: 코디네이터 없는 클러스터
    details: 서명된 gossip 멤버십과 노드 간 라우팅. 단일 노드에서 시작해 무중단 확장.
  - icon: 📦
    title: 단순한 배포
    details: 멀티 아키텍처 이미지, Compose, Kustomize, Helm. 프로브와 오토스케일링.
  - icon: 🔐
    title: 강화 가능한 보안
    details: PBKDF2, HS256 JWT, 토픽 ACL, 관리 토큰, API 키, TLS.
  - icon: 🧼
    title: 독립 구현
    details: 클린룸, Apache-2.0. 타사 Broker의 소스, 설정 형식, 로고, 상표를 포함하지 않습니다.
---

## MQTT를 위한 새로운 기반

NextMQTT는 .NET 표준화 팀을 위해 단일 프로세스, 명시적 리소스 한도, 운영 도구를
한 번에 제공합니다.

- **.NET 10 단일 프로세스**. 외부 데이터베이스나 메시지 미들웨어 불필요.
- **전 계층 관측성**. 연결, 세션, inflight, 구독, retained, 클러스터.
- **예측 가능한 비동기 파이프라인**. `System.IO.Pipelines`와 유계 채널.
- **CI로 검증되는 배포 자산**.

[아키텍처](/ko/architecture)와 [시작하기](/ko/getting-started)를 참고하세요.

## 독립성

NextMQTT는 상용 MQTT Broker와 제휴, 보증, 파생 관계가 없습니다. 타사 Broker의
소스, 문서, 설정 형식, 로고, 상표를 포함하지 않습니다.
