# Text API

[Wiki 首页](../README.md) · [UI](../ui/README.md) · [Runtime](../runtime/README.md)

Text 是内建、后端中立的排版基础服务，不规定字体如何进入场景或如何绘制。字形 shaping 和 rasterization 由每个 Runtime Session 的服务持有；Canvas 等上层系统消费结果并选择渲染方式。

| 项目 | 职责 |
| --- | --- |
| [Inno.Text](Inno.Text.md) | FontAsset、排版值类型、服务契约和脚本门面 |
| [Inno.Text.Assets](Inno.Text.Assets.md) | OpenType 字体导入 |
| [Inno.Text.Runtime](Inno.Text.Runtime.md) | 每 Session 的字体 artifact 与后端生命周期 |
| [Inno.Adapter.Text](Inno.Adapter.Text.md) | 中立 backend factory/selection |
| [Inno.Adapter.Text.FreeTypeHarfBuzz](Inno.Adapter.Text.FreeTypeHarfBuzz.md) | FreeType + HarfBuzz 实现 |
| [Inno.Text.Tests](Inno.Text.Tests.md) | 字体导入、排版和原生桥接回归 |

脚本使用逻辑 namespace `InnoEngine.Text`；Native ABI 与 backend 不导出到脚本。
