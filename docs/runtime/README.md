# Runtime API

[Wiki 首页](../README.md) · [Scene](../scene/README.md) · [Build](../build/README.md)

| 项目 | 职责 |
| --- | --- |
| [Inno.Runtime.Contracts](Inno.Runtime.Contracts.md) | 后端/Scene 中立的 Subsystem 模板、factory、descriptor 与帧协议 |
| [Inno.Runtime.Generators](Inno.Runtime.Generators.md) | 构建期强类型 catalog 生成，禁止 runtime 扫描 |
| [Inno.Engine.Default](Inno.Engine.Default.md) | 默认发行的逐领域声明与 Editor/Player 共用装配 |
| [Inno.Runtime](Inno.Runtime.md) | EngineHost、RuntimeSession、runtime manifest、content deployment 与 execution context |
| [Inno.Adapter](Inno.Adapter.md) | 全部 runtime adapter family 的统一 catalog 与中立 backend selection |
| [Inno.Adapter.Default](Inno.Adapter.Default.md) | Player 使用的默认 runtime implementation catalog |
| [Inno.Adapter.Authoring.Default](Inno.Adapter.Authoring.Default.md) | Editor 使用的默认 authoring/presentation catalog |
| [Inno.Shell](Inno.Shell.md) | Player 与 Editor 共用的 application/window/input/render/frame lifecycle |
| [Inno.Player](Inno.Player.md) | 最小 Player Composition Root；没有稳定 library API |

`EngineHost` 持有应用级实例服务，`RuntimeSession` 持有 Edit/Play/Player 状态。脚本静态门面不拥有真实状态，无 execution scope 时明确失败。
