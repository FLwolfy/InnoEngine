# Inno.Adapter.Text

[Text 索引](README.md) · [Runtime adapter catalog](../runtime/Inno.Adapter.md)

`ITextBackendFactory` 与 `TextBackend` 是 Composition 使用的中立选择协议。Host 依 `AdapterSelection` 选实现并为 Session 创建后端；游戏脚本只见 `Inno.Text` 契约。新增排版后端不改变 Text API。

当前 `TextBackend.FreeTypeHarfBuzz` 由默认 catalog 提供。工厂的 `CreateBackend` 每次返回调用方独占的 `ITextBackend`；`LoadFont` 返回 generation-local `TextFontHandle`，`ReleaseFont`/`Dispose` 逆序清理。`Shape` 和 `Rasterize` 不能接受上一后端代际的 handle。实现者不得把原生指针放入持久资产或脚本导出。

## 公开边界

| API | 用途 |
| --- | --- |
| `TextBackend` | 选择内置 FreeType/HarfBuzz 实现；不是脚本导出。 |
| `ITextBackendFactory.CreateBackend` | Composition 在创建 Session 时取得独占 backend。 |

扩展后端仍须实现 [Text Service](Inno.Text.md) 的 `ITextBackend`，返回不可持久化的 face handle。Host 应在 Session 停止时调用 `Dispose`；原生依赖不向上层传播。无效 face 和后端故障必须在调用点报告。
