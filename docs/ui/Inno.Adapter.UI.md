# Inno.Adapter.UI

[UI 索引](README.md) · [Runtime adapter catalog](../runtime/Inno.Adapter.md)

`IUiBackendFactory`、`UiBackendId`、`UiBackendProvider` 与 `UiBackendCatalog` 是 Composition 层的开放式后端选择协议。默认 catalog 注册 RmlUi provider；第三方可以注册自己的稳定 ID/provider，而不修改引擎枚举，也不要求游戏脚本引用原生库。

`UiBackendId.rmlUi` 只是内置 provider 的 ID，不是白名单。工厂的 `CreateBackend` 每次返回 Session 独占 `IUiBackend`；backend 必须声明 `implementationId` 与 `UiBackendCapabilities`，并在 `DestroyContext`/`Dispose` 完成所属资源退休。

## 公开边界

| API | 用途 |
| --- | --- |
| `UiBackendId`、`UiBackendProvider`、`UiBackendCatalog` | 开放式实现注册与不可变 composition snapshot；不是脚本导出。 |
| `IUiBackendFactory.CreateBackend` | Composition 在创建 Session 时取得独占 backend。 |

扩展实现只需符合 [IUiService](Inno.UI.md) 下方的后端中立帧与事件协议。它不可将 native context、RmlUi 元素或 GPU handle 写入资产或 Scene。Host 在 Session 停止时关闭 backend；后端创建失败应阻止 Session 启动，而非回退到空 UI。
