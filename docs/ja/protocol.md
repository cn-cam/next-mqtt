# プロトコル対応

OASIS MQTT 3.1.1（level 4）と 5.0（level 5）を実装しています。

## トランスポート

| 種別 | 既定ポート | 備考 |
| --- | --- | --- |
| TCP | 1883 | 平文 |
| TLS | 8883 | 証明書が必要 |
| WebSocket | 8083 `/mqtt` | サブプロトコル `mqtt` |
| WSS | 8084 `/mqtt` | HTTPS 証明書 |

## パケット

| パケット | 3.1.1 | 5.0 |
| --- | --- | --- |
| CONNECT / CONNACK | ✅ | ✅ |
| PUBLISH | ✅ | ✅ |
| PUBACK/PUBREC/PUBREL/PUBCOMP | ✅ | ✅ |
| SUBSCRIBE / SUBACK | ✅ | ✅ |
| UNSUBSCRIBE / UNSUBACK | ✅ | ✅ |
| PINGREQ / PINGRESP | ✅ | ✅ |
| DISCONNECT | ✅ | ✅ |
| AUTH | — | ⚠️ `0x8C` |

## 機能

- clean start、永続セッション、セッション期限、乗っ取り
- オフラインキュー（有界）
- retained、メッセージ期限、will、will delay
- `+`/`#`、`$` 先頭ルール
- `$share/{group}/{filter}`（ノード内ラウンドロビン）
- noLocal / retainAsPublished / retain handling
- ユーザープロパティ、content type、response topic、correlation data
- 受信トピックエイリアス
- keep-alive 1.5 倍

## 制限

| 制限 | 既定 |
| --- | --- |
| 最大パケット | 1 MiB |
| inflight | 32 |
| オフラインキュー | 1000 |
| セッション購読数 | 1000 |
| トピックエイリアス | 32 |
| CONNECT タイムアウト | 10 秒 |
| 再送間隔 | 10 秒 |

## エラー

| コード | 原因 |
| --- | --- |
| `0x81` | 不正パケット/UTF-8 |
| `0x82` | プロトコル違反 |
| `0x8F` | 不正フィルタ |
| `0x90` | 不正トピック |
| `0x94` | 不正エイリアス |
| `0x95` | パケット過大 |
| `0x87` | ACL 拒否 |
| `0x8D` | keep-alive 超過 |

## 非目標

拡張認証、送信エイリアス、QUIC、グローバル共有サブスクリプション、永続化、
ブリッジ、ルールエンジン、プラグイン SDK。
