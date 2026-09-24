# UI API

[Wiki 首页](../README.md) · [Text](../text/README.md) · [Rendering](../render/README.md)

UI 是内建的 retained-mode 文档与交互基础服务；它接收 RML、输入、字体和命名纹理，输出后端中立的帧几何及事件，不持有图形设备。将几何上传、混合并显示到游戏画面的策略由 [Inno.Canvas](../plugins/Inno.Canvas.md) Plugin 实现。

| 项目 | 职责 |
| --- | --- |
| [Inno.UI](Inno.UI.md) | 文档、Context、帧与事件的脚本契约 |
| [Inno.UI.Assets](Inno.UI.Assets.md) | `.rml` 导入 |
| [Inno.UI.Runtime](Inno.UI.Runtime.md) | 每 Session 的 Context、输入与 artifact 生命周期 |
| [Inno.Adapter.UI](Inno.Adapter.UI.md) | 中立 backend factory/selection |
| [Inno.Adapter.UI.RmlUi](Inno.Adapter.UI.RmlUi.md) | RmlUi 后端与原生转换 |
| [Inno.UI.Tests](Inno.UI.Tests.md) | 导入、DOM、输入、帧与生命周期回归 |

脚本使用逻辑 namespace `InnoEngine.UI`。RmlUi Native 类型不得跨出 adapter。
