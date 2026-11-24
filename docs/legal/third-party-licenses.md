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
| Vite | 7.x | MIT | Dashboard build |
| VitePress | 1.6.x | MIT | Documentation site |

## Dashboard runtime bundles

The dashboard is a static bundle; the packages below are compiled into it and
therefore distributed with the broker image and the release archives.

| Component | Version | Licence | Purpose |
| --- | --- | --- | --- |
| Vue | 3.5.x | MIT | UI framework |
| Vue Router | 4.x | MIT | Client-side routing |
| Pinia | 3.x | MIT | Session and UI state |
| mqtt (MQTT.js) | 5.16.x | MIT | Browser MQTT 3.1.1 / 5.0 client used by the debugger |
| mqtt-packet | 9.x | MIT | Packet encoding and decoding |
| readable-stream | 4.x | MIT | Stream implementation |
| ws | 8.x | MIT | WebSocket transport (browser build) |
| worker-timers | 8.x | MIT | Keep-alive timers that survive background-tab throttling |
| buffer / process / events | 6.x / 0.11.x / 3.x | MIT | Browser polyfills |
| ieee754 | 1.2.x | BSD-3-Clause | Numeric helpers used by `buffer` |
| split2 | 4.x | ISC | Stream framing |
| lru-cache | 10.x | ISC | Cache used by the client |

Every remaining transitive package in `web/package-lock.json` is licensed under
MIT, ISC, BSD-2-Clause, BSD-3-Clause or 0BSD, all of which are compatible with
Apache-2.0. `typescript` (Apache-2.0) and the Vite toolchain run at build time
only and are not redistributed.

The container base images (`mcr.microsoft.com/dotnet/...`, `node:...`, `nginx`
if used) are distributed under their respective licences and are not vendored
into this repository.

When adding a dependency, update this table and confirm the licence is
compatible with Apache-2.0.
