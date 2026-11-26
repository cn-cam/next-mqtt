# FAQ

## NextMQTT는 기존 제품의 포크나 재구현인가요?

아니요. OASIS MQTT 3.1.1과 5.0 명세의 독립적인 클린룸 구현입니다. 다른 MQTT 브로커의
소스 코드, 문서 문구, 설정 형식, 로고, 상표가 포함되지 않으며 제휴나 인증을 주장하지
않습니다.

## 운영에 사용할 수 있나요?

프로토콜, 인증, ACL, 클러스터 경로는 단위 테스트와 루프백 통합 테스트로 검증되고,
컨테이너 이미지는 CI에서 빌드됩니다. 내구성은 선택 사항입니다. retained, 영속 세션,
세션 복제본을 디스크에 저장할 수 있고, 아웃바운드 inflight 확인은 클라이언트가
재전송합니다. 시스템 오브 레코드로 쓰기 전에 파일 저장소를 켜고 스토리지를 계획하십시오.

## 왜 .NET인가요?

.NET 런타임은 성숙한 비동기 소켓, `System.IO.Pipelines`, 고성능 UTF-8과 암호 기본 요소,
단일 파일 배포 모델을 제공합니다. 브로커와 API는 VM이나 외부 데이터베이스 없이 한
프로세스에서 실행됩니다.

## 어떤 MQTT 클라이언트가 동작하나요?

준수하는 MQTT 3.1.1 또는 5.0 클라이언트 라이브러리라면 모두 됩니다. 연결 대상:

- `mqtt://host:1883`
- `mqtts://host:8883`(TLS 활성 시)
- `ws://host:8083/mqtt`
- `wss://host:8084/mqtt`(HTTPS 설정 시)

동봉된 `nextmqtt` CLI도 클라이언트이며 스모크 테스트에 유용합니다.

## 한 노드가 몇 연결을 처리하나요?

페이로드 크기, QoS, 구독 수, 하드웨어에 달려 있습니다. 설계 목표는 적당한 CPU로
노드당 수만 개의 유휴 또는 저빈도 클라이언트입니다. `nextmqtt bench`로 자신의 워크로드를
측정하고 오프라인 큐와 retained 페이로드의 메모리를 관찰하십시오. 코드에 고정 연결
상한은 없습니다.

## 클러스터가 exactly-once를 보장하나요?

아니요. MQTT QoS는 클라이언트와 브로커 사이에 적용됩니다. 노드 간 전달은 중복 제거
캐시가 있는 최선 노력 HTTP입니다. 피어 장애 시 그쪽으로 간 메시지는 유실될 수 있습니다.
로컬 QoS 1/2 의미는 유지됩니다.

## 공유 구독을 노드 간에 쓸 수 있나요?

예. 노드는 heartbeat에서 공유 그룹을 광고하고, 각 메시지는 클러스터 전체 그룹의 멤버
하나에 배정됩니다. 노드마다 한 번이 아닙니다. [클러스터](/ko/clustering)를 참고하십시오.

## 상태는 어디에 저장되나요?

기본은 메모리입니다. `Storage:Retained:Mode=File`, `Storage:Session:Mode=File`,
`Storage:Replicas:Mode=File`을 설정하면 해당 데이터를 볼륨의 저널이나 스냅숏에
저장합니다. 데이터베이스나 WAL은 없습니다. 파일 저장소를 켜지 않으면 재시작 시 세션,
큐, retained가 사라집니다. 설정과 시크릿은 환경 변수, Compose 파일, Kubernetes
오브젝트에 있습니다.

## 관리자 비밀번호를 재설정하려면?

`NEXTMQTT_ADMIN_PASSWORD`를 설정하고 재시작합니다. `Admin:PasswordHash`를 쓰면 새
해시를 만듭니다:

```bash
dotnet run --project src/NextMqtt.Cli -- hash-password --password 'new-password'
```

## 재시작 후 대시보드 토큰이 왜 무효가 되나요?

`NEXTMQTT_ADMIN_TOKEN_SECRET`이 없어 브로커가 임시 서명 키를 생성했기 때문입니다.
모든 노드에 고정 시크릿을 설정하십시오.

## 익명을 끈 뒤 클라이언트가 연결되지 않는 이유는?

최소 하나의 MQTT 자격 증명 소스가 필요합니다:

- `Authentication:Users`의 로컬 사용자, 또는
- `Authentication:Jwt:Secret`의 JWT 시크릿.

대시보드 관리자 비밀번호는 MQTT 자격 증명이 아닙니다.

## SUBSCRIBE가 `0x87`을 반환하는 이유는?

ACL이 필터를 거부했습니다. 구독 규칙은 요청 필터를 *포함*해야 합니다.
[인증과 ACL](/ko/authentication)을 참고하십시오.

## retained 메시지가 사라진 이유는?

- 빈 페이로드로 발행되어 retained 상태가 삭제됨.
- 메시지 만료가 지남.
- 다른 피어가 죽어 있는 노드에서 발행되어 복제되지 않음.
- 이후 발행이 대체함.

## 다른 오리진에서 대시보드를 쓸 수 있나요?

`Server:Http:AllowedOrigins`에 오리진을 추가합니다. Vite 개발 서버는 기본적으로
`http://localhost:5173`입니다. CORS는 인증을 대신하지 않습니다.

## 컨테이너 포트는 어떻게 설정하나요?

환경 변수를 사용합니다. 예:

```bash
NEXTMQTT_SERVER__HTTP__PORT=9090
NEXTMQTT_SERVER__TCP__PORT=11883
```

Docker/Kubernetes에서 같은 컨테이너 포트를 게시하는 것을 잊지 마십시오.

## 텔레메트리나 라이선스 키가 필요한가요?

텔레메트리 없음, 라이선스 서버 없음, 런타임 외부 의존성 없음입니다. 프로젝트는
Apache-2.0입니다.

## 보안 문제는 어떻게 보고하나요?

공개 이슈가 아니라 GitHub 비공개 취약점 보고를 사용합니다.
[SECURITY.md](https://github.com/cn-cam/next-mqtt/blob/main/SECURITY.md)를 참고하십시오.
