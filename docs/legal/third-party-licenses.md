# Third-party licences

NextMQTT's runtime currently has **no third-party NuGet dependencies**. The
broker, protocol codec and HTTP API use only the .NET 10 platform.

Test and build tooling (not distributed with the runtime):

| Component | Version | Licence | Scope |
| --- | --- | --- | --- |
| xunit | 2.9.x | Apache-2.0 | Tests only |
| xunit.runner.visualstudio | 3.1.x | Apache-2.0 | Tests only |
| Microsoft.NET.Test.Sdk | 18.x | MIT | Tests only |
| coverlet.collector | 10.x | MIT | Tests only |
| Vue | 3.5.x | MIT | Dashboard |
| Vue Router | 4.x | MIT | Dashboard |
| Pinia | 3.x | MIT | Dashboard |
| Vite | 8.x | MIT | Dashboard build |
| VitePress | 1.6.x | MIT | Documentation site |

The container base images (`mcr.microsoft.com/dotnet/...`, `node:...`, `nginx`
if used) are distributed under their respective licences and are not vendored
into this repository.

When adding a dependency, update this table and confirm the licence is
compatible with Apache-2.0.
