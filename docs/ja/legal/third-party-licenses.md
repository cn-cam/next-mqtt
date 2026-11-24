# サードパーティライセンス

NextMQTT のランタイムにサードパーティ NuGet 依存はありません。Broker、codec、
HTTP API は .NET 10 プラットフォームのみを使用します。

テスト/ビルドツール：

| コンポーネント | バージョン | ライセンス | 範囲 |
| --- | --- | --- | --- |
| xunit | 2.9.x | Apache-2.0 | テスト |
| xunit.runner.visualstudio | 3.1.x | Apache-2.0 | テスト |
| Microsoft.NET.Test.Sdk | 18.x | MIT | テスト |
| coverlet.collector | 10.x | MIT | テスト |
| Vue | 3.5.x | MIT | UI |
| Vue Router | 4.x | MIT | UI |
| Pinia | 3.x | MIT | UI |
| Vite | 7.x | MIT | UI ビルド |
| VitePress | 1.6.x | MIT | ドキュメント |

コンテナ/Node のベースイメージは各ライセンスに従います。

依存追加時はこの表を更新し、Apache-2.0 との互換性を確認してください。
