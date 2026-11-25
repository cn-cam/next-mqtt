# CLI

```bash
dotnet run --project src/NextMqtt.Cli -- <command> [options]
```

## 연결 옵션

| 옵션 | 기본 | 설명 |
| --- | --- | --- |
| `--host` | localhost | Broker |
| `--port` | 1883 / 8883 | 포트 |
| `--tls` | off | TLS |
| `--insecure` | off | 인증서 검증 생략 |
| `--username` / `--password` | — | 인증 |
| `--client-id` | 자동 | 클라이언트 ID |
| `--protocol` | 5 | 5 / 3.1.1 |
| `--session-expiry` | 0 | MQTT 5 세션 만료 |
| `--resume` | off | clean start 비활성 |

## Publish

```bash
nextmqtt pub --topic demo/hello --message hello --qos 1
nextmqtt pub --topic firmware/device-1 --file firmware.bin --qos 1
printf 'stdin' | nextmqtt pub --topic demo/stdin --stdin
nextmqtt pub --topic demo/retained --message value --retain
```

## Subscribe

```bash
nextmqtt sub --topic 'sensors/#' --qos 1
nextmqtt sub --topic 'alerts/#,audit/#' --count 10
```

`--count 0`은 Ctrl+C까지 계속합니다.

## 요청/응답

```bash
nextmqtt request --topic svc/echo --payload '{"ping":1}' --timeout 5
```

응답 토픽을 구독한 뒤 MQTT 5의 `Response Topic`과 `Correlation Data`를 붙여
발행하고, 상관 데이터가 일치하는 첫 응답을 출력합니다. MQTT 5가 필요합니다.

| 옵션 | 기본 | 설명 |
| --- | --- | --- |
| `--topic` | – | 요청 토픽 (필수) |
| `--payload` | 빈 값 | 요청 페이로드 |
| `--response-topic` | `/reply/<client>/<guid>` | 응답 토픽 |
| `--correlation` | 임의 GUID | 비교할 상관 데이터 |
| `--timeout` | 5 | 응답 대기 초 |
| `--qos` | 0 | 발행/구독 QoS |

응답이 없으면 종료 코드 1.
## Benchmark

```bash
nextmqtt bench --clients 20 --messages 1000 --size 128 --qos 1
```

| 옵션 | 기본 |
| --- | --- |
| `--clients` | 10 |
| `--messages` | 1000 |
| `--size` | 64 |
| `--qos` | 0 |
| `--topic` | bench |

합성 publish 벤치마크이며 전체 적합성/용량 시험은 아닙니다.

### 연결 벤치마크

```bash
nextmqtt bench-connections --connections 5000 --hold 5
```

| 옵션 | 기본 | 설명 |
| --- | --- | --- |
| `--connections` | 1000 | 병렬로 여는 연결 수 |
| `--hold` | 5 | 연결 유지 시간(초), 0이면 즉시 종료 |

연결 수, 초당 연결 수립률, 실패 수를 출력하고 유지 시간이 끝나면 모두 닫습니다.
하나라도 실패하면 종료 코드가 0이 아닙니다. 한 호스트에서 약 1만 연결을 넘으면
클라이언트의 임시 포트 범위를 늘리거나 생성 호스트를 여러 대 사용하세요. 그렇지
않으면 병목이 브로커가 아니라 클라이언트가 됩니다.

## 비밀번호 해시

```bash
nextmqtt hash-password --password 'correct horse battery staple'
```

`pbkdf2-sha256$...`를 `Authentication:Users[].PasswordHash` 또는
`Admin:PasswordHash`에 사용합니다.

## 종료 코드

| 코드 | 의미 |
| --- | --- |
| 0 | 성공 |
| 1 | 거부/연결 실패 |
| 2 | 인자 오류 |
| 130 | Ctrl+C |

## TLS

```bash
nextmqtt sub --host mqtt.example.com --port 8883 --tls --topic demo/#
```
