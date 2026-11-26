# CLI リファレンス

`nextmqtt` CLI は共有コーデックで作られた小さな MQTT 3.1.1/5.0 クライアントです。
スモークテスト、合成負荷、パスワードハッシュ生成のためのものです。

```bash
dotnet run --project src/NextMqtt.Cli -- <command> [options]
```

公開バイナリでは同じコマンドを `nextmqtt` として使えます。

## コマンド

| コマンド | 用途 |
| --- | --- |
| `pub` | 1 メッセージを公開 |
| `sub` | 購読してメッセージを表示 |
| `bench` | マルチクライアントの公開ベンチマーク |
| `hash-password` | 設定用の PBKDF2 ハッシュを生成 |
| `version` | CLI のバージョンを表示 |

## 接続オプション

| オプション | 既定 | 説明 |
| --- | --- | --- |
| `--host` | `localhost` | ブローカーのホスト |
| `--port` | 1883（`--tls` 時は 8883） | ブローカーのポート |
| `--tls` | off | TLS を使う |
| `--insecure` | off | TLS 証明書検証を省略（テスト専用） |
| `--username` | — | MQTT ユーザー名 |
| `--password` | — | MQTT パスワードまたは JWT |
| `--client-id` | 自動生成 | クライアント ID |
| `--protocol` | `5` | `5` または `3.1.1` |
| `--session-expiry` | 0 | MQTT 5 のセッション期限（秒） |
| `--resume` | off | clean start を無効にしてセッションを再開 |

## 公開

```bash
nextmqtt pub --topic demo/hello --message "hello" --qos 1
nextmqtt pub --topic firmware/device-1 --file firmware.bin --qos 1
printf 'from stdin' | nextmqtt pub --topic demo/stdin --stdin
nextmqtt pub --topic demo/retained --message value --retain
```

| オプション | 説明 |
| --- | --- |
| `--topic` | 必須。ワイルドカードはブローカーに拒否されます |
| `--message` | UTF-8 ペイロード |
| `--file` | 生ファイルのペイロード |
| `--stdin` | 標準入力からペイロードを読む |
| `--qos` | 0、1、2 |
| `--retain` | 最後の値として保存 |

QoS 1 は PUBACK を待ち、QoS 2 は PUBREC/PUBREL/PUBCOMP を実行します。成功以外の
理由コードでは終了ステータス 1 になります。

## 購読

```bash
nextmqtt sub --topic 'sensors/#' --qos 1
nextmqtt sub --topic 'alerts/#,audit/#' --count 10
```

| オプション | 説明 |
| --- | --- |
| `--topic` | 必須。カンマ区切りのフィルタに対応 |
| `--qos` | 要求 QoS |
| `--count` | N 件で停止。0 は Ctrl+C まで実行 |

購読者は QoS 1/2 を確認し、トピック、QoS、retain フラグ、バイト数、UTF-8 または
Base64 のペイロードを表示します。

## リクエストと応答

```bash
nextmqtt request --topic svc/echo --payload '{"ping":1}' --timeout 5
```

応答トピックを購読し、MQTT 5 の `Response Topic` と `Correlation Data` を付けて
リクエストを公開し、correlation data が一致する最初の応答を表示します。
`--protocol 5`（既定）が必要です。

| オプション | 既定 | 説明 |
| --- | --- | --- |
| `--topic` | – | リクエストトピック（必須） |
| `--payload` | 空 | リクエストのペイロード文字列 |
| `--response-topic` | `/reply/<client>/<guid>` | 応答が使うべきトピック |
| `--correlation` | ランダム GUID | 照合する correlation 文字列 |
| `--timeout` | 5 | 応答を待つ秒数 |
| `--qos` | 0 | リクエストと購読の QoS |

時間内に応答がなければ終了コード 1 です。

## ベンチマーク

```bash
nextmqtt bench --clients 20 --messages 1000 --size 128 --qos 1
```

| オプション | 既定 | 説明 |
| --- | --- | --- |
| `--clients` | 10 | 同時接続数 |
| `--messages` | 1000 | クライアントごとのメッセージ数 |
| `--size` | 64 | ペイロードのバイト数 |
| `--qos` | 0 | 公開 QoS |
| `--topic` | `bench` | トピック接頭辞。各クライアントは `<topic>/<index>` |

ベンチマークは経過時間、メッセージ/秒、MiB/秒、失敗数を表示します。合成の公開
ベンチマークであり、完全な適合性テストや容量テストではありません。隔離された環境で
実行し、ブローカーの CPU と破棄メトリクスを監視してください。

### 接続ベンチマーク

```bash
nextmqtt bench-connections --connections 5000 --hold 5
```

| オプション | 既定 | 説明 |
| --- | --- | --- |
| `--connections` | 1000 | 並列に開く接続数 |
| `--hold` | 5 | 開いたままにする秒数（0 は即時クローズ） |

受け付けられた接続数、受け付け速度（conn/s）、失敗数、保持時間後のクローズを表示
します。接続に失敗があると終了コードは非ゼロです。1 ホストから約 1 万接続を超える
場合は、クライアントの一時ポート範囲を広げるか複数の生成器を使ってください。そう
しないとブローカーではなくクライアントが上限になります。

## パスワードハッシュ

```bash
nextmqtt hash-password --password 'correct horse battery staple'
# --password を省略すると標準入力から読む
```

出力：

```text
pbkdf2-sha256$210000$<salt-base64>$<hash-base64>
```

値を `Authentication:Users[].PasswordHash` または `Admin:PasswordHash` に使います。
ブートストラップ管理者には `NEXTMQTT_ADMIN_PASSWORD` を推奨します。

## 終了コード

| コード | 意味 |
| --- | --- |
| 0 | 成功 |
| 1 | ブローカーの拒否、接続失敗、ベンチマークの失敗 |
| 2 | コマンドラインが不正 |
| 130 | 中断（Ctrl+C） |

## TLS の例

```bash
nextmqtt sub --host mqtt.example.com --port 8883 --tls --topic demo/#
nextmqtt pub --host mqtt.example.com --port 8883 --tls --insecure \
  --topic demo/test --message hello --qos 1
```

`--insecure` はローカルの自己署名証明書専用です。
