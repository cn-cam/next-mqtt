# CLI 참조

`nextmqtt` CLI는 공유 코덱으로 만든 작은 MQTT 3.1.1/5.0 클라이언트입니다. 스모크
테스트, 합성 부하, 비밀번호 해시 생성에 사용합니다.

```bash
dotnet run --project src/NextMqtt.Cli -- <command> [options]
```

게시된 바이너리에서는 같은 명령을 `nextmqtt`로 사용할 수 있습니다.

## 명령

| 명령 | 용도 |
| --- | --- |
| `pub` | 메시지 하나 발행 |
| `sub` | 구독하고 메시지 출력 |
| `bench` | 다중 클라이언트 발행 벤치마크 |
| `hash-password` | 설정용 PBKDF2 해시 생성 |
| `version` | CLI 버전 출력 |

## 연결 옵션

| 옵션 | 기본값 | 설명 |
| --- | --- | --- |
| `--host` | `localhost` | 브로커 호스트 |
| `--port` | 1883(`--tls`면 8883) | 브로커 포트 |
| `--tls` | off | TLS 사용 |
| `--insecure` | off | TLS 인증서 검증 생략(테스트 전용) |
| `--username` | — | MQTT 사용자 이름 |
| `--password` | — | MQTT 비밀번호 또는 JWT |
| `--client-id` | 자동 생성 | 클라이언트 ID |
| `--protocol` | `5` | `5` 또는 `3.1.1` |
| `--session-expiry` | 0 | MQTT 5 세션 만료(초) |
| `--resume` | off | clean start를 끄고 세션 재개 |

## 발행

```bash
nextmqtt pub --topic demo/hello --message "hello" --qos 1
nextmqtt pub --topic firmware/device-1 --file firmware.bin --qos 1
printf 'from stdin' | nextmqtt pub --topic demo/stdin --stdin
nextmqtt pub --topic demo/retained --message value --retain
```

| 옵션 | 설명 |
| --- | --- |
| `--topic` | 필수. 와일드카드는 브로커가 거부 |
| `--message` | UTF-8 페이로드 |
| `--file` | 원시 파일 페이로드 |
| `--stdin` | 표준 입력에서 페이로드 읽기 |
| `--qos` | 0, 1, 2 |
| `--retain` | 마지막 값으로 저장 |

QoS 1은 PUBACK을 기다리고 QoS 2는 PUBREC/PUBREL/PUBCOMP를 수행합니다. 성공이 아닌
이유 코드는 종료 상태 1을 반환합니다.

## 구독

```bash
nextmqtt sub --topic 'sensors/#' --qos 1
nextmqtt sub --topic 'alerts/#,audit/#' --count 10
```

| 옵션 | 설명 |
| --- | --- |
| `--topic` | 필수. 쉼표로 구분한 필터 지원 |
| `--qos` | 요청 QoS |
| `--count` | N개 후 중지. 0은 Ctrl+C까지 실행 |

구독자는 QoS 1/2를 확인하고 토픽, QoS, retain 플래그, 바이트 수, UTF-8 또는 Base64
페이로드를 출력합니다.

## 요청과 응답

```bash
nextmqtt request --topic svc/echo --payload '{"ping":1}' --timeout 5
```

응답 토픽을 구독하고 MQTT 5 `Response Topic`과 `Correlation Data`를 붙여 요청을
발행한 뒤, correlation data가 일치하는 첫 응답을 출력합니다. `--protocol 5`(기본)가
필요합니다.

| 옵션 | 기본값 | 설명 |
| --- | --- | --- |
| `--topic` | – | 요청 토픽(필수) |
| `--payload` | 빈 값 | 요청 페이로드 텍스트 |
| `--response-topic` | `/reply/<client>/<guid>` | 응답이 사용해야 할 토픽 |
| `--correlation` | 무작위 GUID | 대조할 correlation 텍스트 |
| `--timeout` | 5 | 응답을 기다리는 초 |
| `--qos` | 0 | 요청과 구독의 QoS |

시간 안에 응답이 없으면 종료 코드 1입니다.

## 벤치마크

```bash
nextmqtt bench --clients 20 --messages 1000 --size 128 --qos 1
```

| 옵션 | 기본값 | 설명 |
| --- | --- | --- |
| `--clients` | 10 | 동시 연결 수 |
| `--messages` | 1000 | 클라이언트당 메시지 수 |
| `--size` | 64 | 페이로드 바이트 |
| `--qos` | 0 | 발행 QoS |
| `--topic` | `bench` | 토픽 접두사. 각 클라이언트는 `<topic>/<index>` 사용 |

벤치마크는 경과 시간, 메시지/초, MiB/초, 실패 수를 출력합니다. 합성 발행 벤치마크이며
완전한 적합성이나 용량 테스트가 아닙니다. 격리된 환경에서 실행하고 브로커 CPU와 드롭
지표를 관찰하십시오.

### 연결 벤치마크

```bash
nextmqtt bench-connections --connections 5000 --hold 5
```

| 옵션 | 기본값 | 설명 |
| --- | --- | --- |
| `--connections` | 1000 | 병렬로 여는 연결 수 |
| `--hold` | 5 | 열어 둘 초(0은 즉시 닫기) |

수락된 연결 수, 수락 속도(conn/s), 실패 수, 유지 시간 후 종료를 출력합니다. 연결 실패가
있으면 종료 코드가 0이 아닙니다. 한 호스트에서 약 1만 연결을 넘으면 클라이언트의 임시
포트 범위를 늘리거나 여러 생성기를 실행하십시오. 그렇지 않으면 브로커가 아니라
클라이언트가 한계가 됩니다.

## 비밀번호 해시

```bash
nextmqtt hash-password --password 'correct horse battery staple'
# --password를 생략하면 표준 입력에서 읽음
```

출력:

```text
pbkdf2-sha256$210000$<salt-base64>$<hash-base64>
```

값을 `Authentication:Users[].PasswordHash` 또는 `Admin:PasswordHash`에 사용합니다.
부트스트랩 관리자에는 `NEXTMQTT_ADMIN_PASSWORD`를 권장합니다.

## 종료 코드

| 코드 | 의미 |
| --- | --- |
| 0 | 성공 |
| 1 | 브로커 거부, 연결 실패, 벤치마크 실패 |
| 2 | 잘못된 명령줄 |
| 130 | 중단(Ctrl+C) |

## TLS 예

```bash
nextmqtt sub --host mqtt.example.com --port 8883 --tls --topic demo/#
nextmqtt pub --host mqtt.example.com --port 8883 --tls --insecure \
  --topic demo/test --message hello --qos 1
```

`--insecure`는 로컬 자체 서명 인증서 전용입니다.
