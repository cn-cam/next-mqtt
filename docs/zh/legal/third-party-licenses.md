# 第三方许可

NextMQTT 运行时目前**没有第三方 NuGet 依赖**。Broker、协议编解码与 HTTP API
只使用 .NET 10 平台。

测试与构建工具（不随运行时分发）：

| 组件 | 版本 | 许可 | 范围 |
| --- | --- | --- | --- |
| xunit | 2.9.x | Apache-2.0 | 仅测试 |
| xunit.runner.visualstudio | 3.1.x | Apache-2.0 | 仅测试 |
| Microsoft.NET.Test.Sdk | 18.x | MIT | 仅测试 |
| coverlet.collector | 10.x | MIT | 仅测试 |
| Vue | 3.5.x | MIT | 控制台 |
| Vue Router | 4.x | MIT | 控制台 |
| Pinia | 3.x | MIT | 控制台 |
| Vite | 7.x | MIT | 控制台构建 |
| VitePress | 1.6.x | MIT | 文档站 |

容器基础镜像与 Node 镜像遵循各自许可，未 vendored 到本仓库。

新增依赖时必须更新此表，并确认与 Apache-2.0 兼容。
