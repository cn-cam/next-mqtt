# プロトコル対応

NextMQTT は OASIS 仕様の MQTT 3.1.1（プロトコルレベル 4）と MQTT 5.0（レベル 5）を
実装しています。

## トランスポート

| 種別 | 既定ポート | 備考 |
| --- | --- | --- |
| TCP | 1883 | 平文 MQTT |
| TLS | 8883 | 任意。証明書が必要 |
| WebSocket | 8083（`/mqtt`） | 要求時にサブプロトコル `mqtt` を交渉 |
| セキュア WebSocket | 8084（`/mqtt`） | 任意の Kestrel HTTPS エンドポイント |

WebSocket クライアントは `ws://host:8083/mqtt` または
`wss://host:8084/mqtt` に接続します。同じ HTTP エンドポイントがダッシュボードと
REST API を提供します。

## 制御パケット

| パケット | 3.1.1 | 5.0 | 備考 |
| --- | --- | --- | --- |
| CONNECT | ✅ | ✅ | ユーザー名/パスワード、遺言、clean start、セッション期限 |
| CONNACK | ✅ | ✅ | Session present、理由コード、サーバープロパティ |
| PUBLISH | ✅ | ✅ | QoS 0/1/2、retain、DUP、受信と送信のトピックエイリアス |
| PUBACK | ✅ | ✅ | 5.0 は理由コードとプロパティ |
| PUBREC / PUBREL / PUBCOMP | ✅ | ✅ | QoS 2 ハンドシェイク全体 |
| SUBSCRIBE | ✅ | ✅ | ワイルドカード、共有購読、購読識別子 |
| SUBACK | ✅ | ✅ | フィルタごとの理由コード |
| UNSUBSCRIBE / UNSUBACK | ✅ | ✅ | 5.0 はフィルタごとの理由コード |
| PINGREQ / PINGRESP | ✅ | ✅ | keep-alive |
| DISCONNECT | ✅ | ✅ | 理由コード、セッション期限の更新、切断時遺言 |
| AUTH | — | ✅ | 差し替え可能なプロバイダーとのチャレンジ/レスポンス。組み込み `nextmqtt-jwt` |

## 機能

### セッション

- Clean start と永続セッション。
- MQTT 5 のセッション期限 interval（DISCONNECT での更新を含む）。
- セッション乗っ取り：前の接続は DISCONNECT `0x8E` を受け取ります。
- 別ノードへの `CleanStart=false` 再接続では、所有者からセッションを引き継ぐか
  レプリカから復元します。PUBREL 待ちの受信 QoS 2 はセッションと一緒に移動します。
- 永続セッションのオフラインキュー（`Broker:MaxQueuedMessages` で有界）。
- 長さ 0 の ID にはブローカーがクライアント ID を割り当てます
  （`AssignedClientIdentifier` で返却）。

### 購読

- `+` と `#` のワイルドカード、先頭 `$` の除外規則。
- 共有購読：`$share/{group}/{filter}`。各メッセージはクラスタ全体のグループ内の
  1 メンバーに割り当てられます。
- MQTT 5 オプション：`noLocal`、`retainAsPublished`、retain handling 0/1/2。
- 一致した配信に購読識別子を付与。
- 重複購読はクライアント単位で集約し、一致する最大 QoS で 1 回配信します。

### メッセージ

- retained メッセージ。空ペイロードでの削除を含みます。
- retained とライブメッセージのメッセージ期限 interval。
- 遺言遅延付きの遺言。正常な DISCONNECT で抑制されます（理由 `0x04` で明示的に
  発行することも可能）。
- MQTT 5 のユーザープロパティ、content type、response topic、correlation data は
  エンドツーエンドで保持されます。
- 受信と送信のトピックエイリアス。`Broker:MaxTopicAlias` とクライアントの
  Topic Alias Maximum で制限されます。
- `Broker:MaxPacketSize` によるリスナーごとの最大パケット。
- CONNACK で Receive Maximum を広告し、セッションごとに受信 QoS 2 の未確認数を
  制限します。

### keep-alive

クライアントは keep-alive interval の 1.5 倍以内に何かの制御パケットを送る必要が
あります。超過するとブローカーは理由 `0x8D`（MQTT 5）で接続を閉じます。keep-alive
`0` はチェックを無効にします。

## 制限

| 制限 | 既定 | オプション |
| --- | --- | --- |
| パケットサイズ | 1 MiB | `Broker:MaxPacketSize` |
| セッションごとの inflight | 32 | `Broker:MaxInflightMessages` |
| オフラインキュー | 1,000 メッセージ | `Broker:MaxQueuedMessages` |
| セッションごとの購読数 | 1,000 | `Broker:MaxSubscriptionsPerSession` |
| トピックエイリアス | 32 | `Broker:MaxTopicAlias` |
| クライアント ID 長 | 256 | `Broker:MaxClientIdLength` |
| CONNECT タイムアウト | 10 秒 | `Broker:ConnectTimeout` |
| 再送間隔 | 10 秒 | `Broker:RetransmitInterval` |

MQTT 自体の上限も適用されます：残り長 268,435,455 バイト、UTF-8 フィールドあたり
65,535 バイト、同時に使えるパケット ID は 65,535 個です。

## エラー処理

不正なパケットは接続を閉じます。MQTT 5 では、ブローカーは次のような具体的な理由で
DISCONNECT を送ろうとします：

| 理由 | 原因 |
| --- | --- |
| `0x81` Malformed packet | 不正なフラグ、切り詰められたフィールド、不正な UTF-8 |
| `0x82` Protocol error | 不正な状態遷移、パケット ID 0 |
| `0x8F` Topic filter invalid | SUBSCRIBE の不正なフィルタ |
| `0x90` Topic name invalid | PUBLISH の空トピックまたはワイルドカード |
| `0x94` Topic alias invalid | 交渉範囲外のエイリアス |
| `0x95` Packet too large | 設定上限を超えるパケット |
| `0x87` Not authorized | ACL による公開/購読の拒否 |
| `0x8D` Keep-alive timeout | keep-alive の 1.5 倍以内にパケットなし |

MQTT 3.1.1 のクライアントは接続失敗時に最も近い CONNACK 戻り値を受け取り、
プロトコルエラーでは切断されます。

## 非目標（現行リリース）

- MQTT over QUIC。
- ノード間の順序保証や exactly-once。クラスタ転送はベストエフォートです。
- 他社ブローカーのプラグインや拡張パッケージとのランタイム互換。
- 非対称 JWT 署名。受け付けるのは HS256 のみです。

予定は[ロードマップ](/ja/roadmap)を参照してください。
