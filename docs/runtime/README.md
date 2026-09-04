# Runtime API

[Wiki 首页](../README.md) · [Scene](../scene/README.md) · [Build](../build/README.md)

| 项目 | 职责 |
| --- | --- |
| [Inno.Runtime](Inno.Runtime.md) | EngineHost、RuntimeSession、runtime manifest、content deployment 与 execution context |
| [Inno.Player](Inno.Player.md) | 最小 Player Composition Root；没有稳定 library API |

`EngineHost` 持有应用级实例服务，`RuntimeSession` 持有 Edit/Play/Player 状态。脚本静态门面不拥有真实状态，无 execution scope 时明确失败。
