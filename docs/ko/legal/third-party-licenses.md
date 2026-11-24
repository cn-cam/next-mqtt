# 서드파티 라이선스

NextMQTT 런타임에는 서드파티 NuGet 의존성이 없습니다. Broker, codec, HTTP API는
.NET 10 플랫폼만 사용합니다.

테스트/빌드 도구:

| 구성 요소 | 버전 | 라이선스 | 범위 |
| --- | --- | --- | --- |
| xunit | 2.9.x | Apache-2.0 | 테스트 |
| xunit.runner.visualstudio | 3.1.x | Apache-2.0 | 테스트 |
| Microsoft.NET.Test.Sdk | 18.x | MIT | 테스트 |
| coverlet.collector | 10.x | MIT | 테스트 |
| Vue | 3.5.x | MIT | UI |
| Vue Router | 4.x | MIT | UI |
| Pinia | 3.x | MIT | UI |
| Vite | 7.x | MIT | UI 빌드 |
| VitePress | 1.6.x | MIT | 문서 |

컨테이너/Node 기본 이미지는 각 라이선스를 따릅니다.

의존성 추가 시 이 표를 갱신하고 Apache-2.0 호환성을 확인하세요.
