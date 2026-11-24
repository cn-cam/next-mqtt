# 開発

## 要件

| ツール | バージョン |
| --- | --- |
| .NET SDK | 10.0.100+ |
| Node.js | 22+（24 推奨） |
| Docker | 任意 |

## ビルド / テスト

```bash
dotnet restore NextMqtt.slnx
dotnet build NextMqtt.slnx -c Release
dotnet test NextMqtt.slnx -c Release
```

| プロジェクト | 範囲 |
| --- | --- |
| Core.Tests | codec、不正パケット、topic、trie |
| Broker.Tests | session、queue、retained、auth、ACL、cluster |
| Server.Tests | 実 TCP 統合 |

現在 126 テスト。

## ダッシュボード

```bash
cd web
npm install
npm run dev
npm run typecheck
npm run build
```

Vite が `/api`、`/healthz`、`/readyz`、`/metrics` を `:8080` にプロキシします。

## ドキュメント

```bash
cd docs
npm install
npm run dev
npm run build
```

GitHub Pages では `DOCS_BASE=/next-mqtt/`。

## 構成

```text
src/NextMqtt.Core       codec / topic
src/NextMqtt.Broker     session / routing / auth / cluster
src/NextMqtt.Server     listener / API / UI
src/NextMqtt.Cli        pub / sub / bench / hash
tests/                  単体・統合
web/                    Vue 3
docs/                   4 言語ドキュメント
deploy/                 Docker / K8s / Helm
```

## プロトコル変更

1. `Core/Protocol` を更新
2. encoder/decoder、フラグ/プロパティ検証
3. 往復・不正入力テスト
4. `ClientConnection` / `BrokerSession` に接続
5. TCP 統合テスト
6. `docs/protocol.md` と CHANGELOG 更新

## 規約

- `net10.0`、nullable、implicit usings
- file-scoped namespace、4 スペース
- 非同期は `Async` 接尾辞 + CancellationToken
- `.Result` / `.Wait()` / `Thread.Sleep` 禁止
- 有界キュー、ドロップはメトリクス
- UI は `<script setup lang="ts">`

## コンプライアンス

```bash
./scripts/compliance-check.sh
./scripts/compliance-check.ps1
```

禁止ブランド token を実行時生成して、ファイル名・内容・履歴・ブランチ・タグを検査。
CI で強制されます。
