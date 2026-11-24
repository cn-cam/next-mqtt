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
