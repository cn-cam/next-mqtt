# CLI

```bash
dotnet run --project src/NextMqtt.Cli -- <command> [options]
```

## 接続オプション

| オプション | 既定 | 説明 |
| --- | --- | --- |
| `--host` | localhost | Broker |
| `--port` | 1883 / 8883 | ポート |
| `--tls` | off | TLS |
| `--insecure` | off | 証明書検証スキップ |
| `--username` / `--password` | — | 認証 |
| `--client-id` | 自動 | クライアント ID |
| `--protocol` | 5 | 5 / 3.1.1 |
| `--session-expiry` | 0 | MQTT 5 セッション期限 |
| `--resume` | off | clean start 無効 |

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

`--count 0` は Ctrl+C まで継続。

## Benchmark

```bash
nextmqtt bench --clients 20 --messages 1000 --size 128 --qos 1
```

| オプション | 既定 |
| --- | --- |
| `--clients` | 10 |
| `--messages` | 1000 |
| `--size` | 64 |
| `--qos` | 0 |
| `--topic` | bench |

合成 publish ベンチマークであり、完全な適合性/容量試験ではありません。

## パスワードハッシュ

```bash
nextmqtt hash-password --password 'correct horse battery staple'
```

`pbkdf2-sha256$...` を `Authentication:Users[].PasswordHash` または
`Admin:PasswordHash` に使用します。

## 終了コード

| コード | 意味 |
| --- | --- |
| 0 | 成功 |
| 1 | 拒否/接続失敗 |
| 2 | 引数エラー |
| 130 | Ctrl+C |

## TLS

```bash
nextmqtt sub --host mqtt.example.com --port 8883 --tls --topic demo/#
```
