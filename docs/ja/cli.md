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

## リクエスト/応答

```bash
nextmqtt request --topic svc/echo --payload '{"ping":1}' --timeout 5
```

応答トピックを購読し、MQTT 5 の `Response Topic` と `Correlation Data` を付けて
公開し、相関データが一致する最初の応答を表示します。MQTT 5 が必要です。

| オプション | 既定 | 説明 |
| --- | --- | --- |
| `--topic` | – | リクエストトピック（必須） |
| `--payload` | 空 | リクエストのペイロード |
| `--response-topic` | `/reply/<client>/<guid>` | 応答トピック |
| `--correlation` | ランダム GUID | 照合する相関データ |
| `--timeout` | 5 | 応答待ちの秒数 |
| `--qos` | 0 | 公開と購読の QoS |

応答が無い場合は終了コード 1。
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

### 接続ベンチマーク

```bash
nextmqtt bench-connections --connections 5000 --hold 5
```

| オプション | 既定 | 説明 |
| --- | --- | --- |
| `--connections` | 1000 | 並列に張る接続数 |
| `--hold` | 5 | 接続を保持する秒数（0 で即時切断） |

接続数、毎秒の接続確立数、失敗数を出力し、保持時間の後にすべて切断します。1 件
でも失敗すると終了コードは非ゼロです。1 ホストで約 1 万接続を超える場合は、
クライアントの一時ポート範囲を広げるか複数の生成ホストを使ってください。そう
しないとボトルネックはブローカーではなくクライアント側になります。

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
