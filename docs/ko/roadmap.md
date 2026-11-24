# 로드맵

방향성이며 약속이 아닙니다.

## 0.1 — 기반 (완료)

- MQTT 3.1.1/5.0, TCP/TLS/WS/WSS
- QoS 0/1/2, retained, will, session expiry
- 공유 구독, MQTT 5 구독 옵션
- 비밀번호/JWT/익명 인증, ACL
- gossip 클러스터, 서명된 전달
- REST API, Vue 3 UI
- CLI, Docker, Compose, Kubernetes, Helm
- Prometheus, 헬스 체크

## 0.2 — 영구 저장과 운영

- [x] 추가 전용 로그 기반 retained 저장소 (0.4.0)
- [x] 시작 시 및 파일 증가 시 compaction (0.4.0)
- [ ] 플러그형 세션 저장소
- [ ] SQLite / PostgreSQL
- [ ] 세션 백업/복구
- [ ] 우아한 drain
- [x] IP 단위 연결 속도 제한 (0.3.0)
- [x] 클라이언트 단위 메시지 속도 할당량 (0.3.0)
- [ ] 토픽 단위 할당량
- [ ] OpenTelemetry

## 0.3 — 프로토콜 심화

- [ ] 강화 인증 AUTH
- [ ] 아웃바운드 토픽 별칭
- [ ] request/response 도우미
- [ ] MQTT over QUIC
- [ ] 전역 공유 구독
- [ ] retained 충돌 해결

## 0.4 — 통합

- [ ] Webhook bridge
- [ ] Kafka / NATS bridge
- [ ] 규칙 엔진
- [ ] .NET 플러그인 SDK
- [ ] 스키마 검증
- [ ] 멀티테넌시

## 비목표

- 타사 Broker 전용 플러그인 API 호환
- 타사 설정/대시보드 재사용
- 다른 구현의 소스/테스트/문서 복제
- 벤더 인증/보증 주장

보안 수정이 기능보다 우선합니다.
