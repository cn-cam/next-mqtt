# FAQ

## 기존 제품의 포크인가요?

아니요. OASIS MQTT 3.1.1/5.0의 독립적인 클린룸 구현입니다. 타사 Broker의 소스,
설정 형식, 문서, 로고, 상표를 포함하지 않습니다.

## 운영에 사용할 수 있나요?

프로토콜, 인증, ACL, 클러스터는 테스트되었고 CI에서 이미지를 검증합니다. 내구성은
선택 사항입니다. retained, 영속 세션, 세션 복제본을 디스크에 저장할 수 있고, 아웃바운드
inflight 확인은 클라이언트가 재전송합니다. 시스템 오브 레코드로 쓰기 전에 파일 저장소를
켜고 볼륨을 계획하십시오.

## 어떤 클라이언트를 지원하나요?

MQTT 3.1.1/5.0 규격 클라이언트:

- `mqtt://host:1883`
- `mqtts://host:8883`
- `ws://host:8083/mqtt`
- `wss://host:8084/mqtt`

## 클러스터가 exactly-once인가요?

아니요. MQTT QoS는 클라이언트와 Broker 사이입니다. 노드 간 전달은 최선 노력
HTTP이며 중복 제거 캐시를 사용합니다.

## 공유 구독이 클러스터 전역인가요?

클러스터 전체에서 조정됩니다. 메시지마다 공유 그룹의 노드 하나가 선택되므로 같은 그룹의
다른 노드로 중복 전달되지 않습니다.

## 상태는 어디에 저장되나요?

기본은 메모리입니다. `Storage:Retained:Mode=File`, `Storage:Session:Mode=File`,
`Storage:Replicas:Mode=File`을 설정하면 해당 데이터를 볼륨의 저널이나 스냅숏에
저장합니다. 데이터베이스나 WAL은 없습니다. 파일 저장소를 켜지 않으면 재시작 시 세션,
큐, retained가 사라집니다.

## 관리 비밀번호를 잊었습니다

`NEXTMQTT_ADMIN_PASSWORD`를 설정하고 재시작하세요.

## 재시작 후 UI 토큰이 무효입니다

`NEXTMQTT_ADMIN_TOKEN_SECRET`이 없어 임시 키가 생성되었기 때문입니다. 고정 값을
설정하세요.

## 익명을 끄면 연결이 안 됩니다

`Authentication:Users` 또는 JWT를 설정하세요. 관리 비밀번호는 MQTT 자격 증명이
아닙니다.

## SUBSCRIBE가 0x87입니다

ACL이 필터를 거부했습니다. 구독 규칙은 요청 필터를 포함해야 합니다.

## retained가 사라졌습니다

빈 payload 삭제, 만료, 복제 대상 노드 중단, 덮어쓰기 중 하나입니다.

## 보안 신고

공개 이슈 대신 GitHub private vulnerability reporting을 사용하세요.
