# CLI reference

The `nextmqtt` CLI is a small MQTT 3.1.1/5.0 client built on the shared codec.
It is intended for smoke tests, synthetic load and password-hash generation.

```bash
dotnet run --project src/NextMqtt.Cli -- <command> [options]
```

Published binaries expose the same commands as `nextmqtt`.

## Commands

| Command | Purpose |
| --- | --- |
| `pub` | Publish one message |
| `sub` | Subscribe and print messages |
| `bench` | Run a multi-client publish benchmark |
| `hash-password` | Generate a PBKDF2 hash for config |
| `version` | Print the CLI version |

## Connection options

| Option | Default | Description |
| --- | --- | --- |
| `--host` | `localhost` | Broker host |
| `--port` | 1883 (8883 with `--tls`) | Broker port |
| `--tls` | off | Use TLS |
| `--insecure` | off | Skip TLS certificate validation (testing only) |
| `--username` | — | MQTT username |
| `--password` | — | MQTT password or JWT |
| `--client-id` | generated | Client identifier |
| `--protocol` | `5` | `5` or `3.1.1` |
| `--session-expiry` | 0 | MQTT 5 session expiry in seconds |
| `--resume` | off | Disable clean start to resume a session |

## Publish

```bash
nextmqtt pub --topic demo/hello --message "hello" --qos 1
nextmqtt pub --topic firmware/device-1 --file firmware.bin --qos 1
printf 'from stdin' | nextmqtt pub --topic demo/stdin --stdin
nextmqtt pub --topic demo/retained --message value --retain
```

| Option | Description |
| --- | --- |
| `--topic` | Required; wildcards are rejected by the broker |
| `--message` | UTF-8 payload |
| `--file` | Raw file payload |
| `--stdin` | Read the payload from standard input |
| `--qos` | 0, 1 or 2 |
| `--retain` | Store as the retained value |

QoS 1 waits for PUBACK; QoS 2 performs PUBREC/PUBREL/PUBCOMP. Non-success
reason codes exit with status 1.

## Subscribe

```bash
nextmqtt sub --topic 'sensors/#' --qos 1
nextmqtt sub --topic 'alerts/#,audit/#' --count 10
```

| Option | Description |
| --- | --- |
| `--topic` | Required; comma-separated filters supported |
| `--qos` | Requested QoS |
| `--count` | Stop after N messages; 0 means run until Ctrl+C |

The subscriber acknowledges QoS 1/2 messages and prints topic, QoS, retain
flag, byte count and a UTF-8 or base64 payload.

## Benchmark

```bash
nextmqtt bench --clients 20 --messages 1000 --size 128 --qos 1
```

| Option | Default | Description |
| --- | --- | --- |
| `--clients` | 10 | Concurrent connections |
| `--messages` | 1000 | Messages per client |
| `--size` | 64 | Payload bytes |
| `--qos` | 0 | Publish QoS |
| `--topic` | `bench` | Topic prefix; each client uses `<topic>/<index>` |

The benchmark prints elapsed time, messages/second, MiB/second and failures. It
is a synthetic publisher benchmark, not a full conformance or capacity test.
Run it in an isolated environment and watch broker CPU and drop metrics.

## Password hashing

```bash
nextmqtt hash-password --password 'correct horse battery staple'
# or omit --password to read from stdin
```

Output:

```text
pbkdf2-sha256$210000$<salt-base64>$<hash-base64>
```

Use the value as `Authentication:Users[].PasswordHash` or
`Admin:PasswordHash`. Prefer `NEXTMQTT_ADMIN_PASSWORD` for the bootstrap admin.

## Exit codes

| Code | Meaning |
| --- | --- |
| 0 | Success |
| 1 | Broker rejection, connection failure or benchmark failures |
| 2 | Invalid command line |
| 130 | Interrupted (Ctrl+C) |

## Examples with TLS

```bash
nextmqtt sub --host mqtt.example.com --port 8883 --tls --topic demo/#
nextmqtt pub --host mqtt.example.com --port 8883 --tls --insecure \
  --topic demo/test --message hello --qos 1
```

`--insecure` is only for local self-signed certificates.
