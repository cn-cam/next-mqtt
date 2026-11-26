# 開発

## ツールチェーン

| ツール | バージョン |
| --- | --- |
| .NET SDK | 10.0.100+ |
| Node.js | 22+（24 推奨） |
| npm | 10+ |
| Docker | 任意。イメージとクラスタのテスト用 |
| Helm | 任意。チャートのレンダリング用 |

`global.json` は SDK の機能バンドを固定し、ローカル開発ではより新しいメジャー SDK を
許可します。

## ビルドとテスト

```bash
dotnet restore NextMqtt.slnx
dotnet build NextMqtt.slnx -c Release
dotnet test NextMqtt.slnx -c Release
```

テストスイート：

| プロジェクト | 範囲 |
| --- | --- |
| `NextMqtt.Core.Tests` | コーデックの往復、不正パケット、トピック一致、Trie |
| `NextMqtt.Broker.Tests` | セッション、キュー、retained、認証、ACL、クラスタルーティング |
| `NextMqtt.Server.Tests` | 共有コーデックを使った実際のループバック TCP 統合テスト |

統合テストは一時ポートでブローカーを起動し、CONNECT、QoS 0/1/2、retained、遺言、
永続セッション、乗っ取り、ACL、認証を検証します。

## ダッシュボード

```bash
cd web
npm install
npm run dev        # http://localhost:5173
npm run typecheck
npm run build
```

Vite は `/api`、`/healthz`、`/readyz`、`/metrics` を `http://localhost:8080` へ
プロキシします。

UI は Vue 3 + TypeScript で、次を使います：

- 手書き CSS（コンポーネントフレームワークなし）
- 小さな SVG アイコンセット
- 軽量なリアクティブ i18n 層（英語と中国語）
- WebSocket のダッシュボードチャネルではなくポーリング composable

## ドキュメント

```bash
cd docs
npm install
npm run dev        # http://localhost:5173（VitePress の既定）
npm run build
```

GitHub Pages 向けにビルドするときは `DOCS_BASE=/next-mqtt/` を設定します。

## リポジトリ構成

```text
src/NextMqtt.Core        コーデックとトピックの基本（依存なし）
src/NextMqtt.Broker      セッション、ルーティング、認証、ACL、retained、クラスタ
src/NextMqtt.Server      ホスト、リスナー、REST API、静的ダッシュボード
src/NextMqtt.Cli         pub/sub/bench/hash コマンド
tests/                   単体テストと統合テスト
web/                     Vue 3 ダッシュボード
docs/                    VitePress ドキュメント
deploy/                  Docker、Compose、Kubernetes、Helm
scripts/                 コンプライアンスと保守スクリプト
```

## プロトコル機能の追加

1. `src/NextMqtt.Core/Protocol` のパケットモデルを追加または拡張します。
2. フラグとプロパティの検証を含めてデコーダーとエンコーダーを更新します。
3. 往復テストと不正入力テストを追加します。
4. 挙動を `ClientConnection` または `BrokerSession` に組み込みます。
5. `NextMqtt.Server.Tests` にループバック統合テストを追加します。
6. `docs/protocol.md` と `CHANGELOG.md` を更新します。

自明でない挙動では、コメントに関連する OASIS MQTT 仕様の節を引用してください。

## 規約

- `net10.0`、nullable 有効、implicit usings。
- ファイルスコープ名前空間、4 スペースインデント、`_camelCase` フィールド。
- 非同期メソッドは `Async` で終わり、`CancellationToken` を受け取ります。
- リクエスト経路で `.Result`、`.Wait()`、`Thread.Sleep` を使いません。
- 有界キューとチャネルを使い、破棄をメトリクスに表します。
- `sealed` クラスと不変オプションを優先します。
- ダッシュボードコンポーネントは `<script setup lang="ts">` と型付き props を使います。

## コンプライアンスゲート

すべてのコミットは次を通す必要があります：

```bash
./scripts/compliance-check.sh     # Linux/macOS/Git Bash
./scripts/compliance-check.ps1    # Windows
```

スキャナーは禁止ブランドトークンを実行時に組み立て、ファイル名、追跡・未追跡の内容、
コミットメッセージ、ブランチ、タグを検査します。CI の必須ジョブでもあります。

コメント、テスト、ドキュメントにも除外はありません。用語が衝突する場合は改名して
ください。

## 実行時依存

core、broker、server には**サードパーティの NuGet 依存はありません**。Kafka と
RabbitMQ ブリッジは ADR で承認された固定バージョンの `Confluent.Kafka` と
`RabbitMQ.Client` を使います。それ以外の依存を追加するには `docs/adr/` の
アーキテクチャ決定記録と `docs/legal/third-party-licenses.md` の更新が必要です。

## コミットスタイル

Conventional Commits、命令形、72 文字以内の件名：

```text
feat(broker): support message expiry on retained delivery
fix(web): keep the session table stable while polling
docs: document the cluster secret rotation caveat
```

各コミットはビルドとテストを通すべきです。プルリクエストには挙動変更のテストと
ドキュメント更新を含めてください。

## 手動検証レシピ

```bash
# ブローカーを起動
dotnet run --project src/NextMqtt.Server

# 1 つのシェルで購読
dotnet run --project src/NextMqtt.Cli -- sub --topic test/# --count 1

# 別のシェルで公開
dotnet run --project src/NextMqtt.Cli -- pub --topic test/hello --message hi --qos 1
```

クラスタ：

```bash
docker compose -f deploy/compose/docker-compose.cluster.yml up -d --build
# healthy を待ち、ポート 1883 と 1884 に対して 2 つの CLI コマンドを実行
```

## コードレビューチェックリスト

- [ ] 禁止ブランドトークンがない
- [ ] プロトコル変更がエンコーダー/デコーダーと統合テストで覆われている
- [ ] 有界キューとタイムアウトが保たれている
- [ ] エラーが適切な MQTT 理由コードにマップされている
- [ ] ダッシュボードの型が API 契約と一致している
- [ ] ドキュメント、変更履歴、例が更新されている
- [ ] 秘密情報や個人トークンがコミットされていない
