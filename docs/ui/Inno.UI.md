# Inno.UI

[UI 索引](README.md) · [Text](../text/Inno.Text.md)

`IUiService` / `UI` 门面提供 Context 生命周期、语言标记的文本文件加载、纯文本/内容片段/attribute/class 变更、字体及 RGBA8 纹理注册、`Update`、`Render` 与事件提取。核心不知道 RML。`UiRenderFrame` 只发布 mesh/texture 增量、稳定 handle、释放请求与有序 draw commands，供渲染 Plugin 持久缓存。

每个 Context 的生命周期由调用方明确拥有。无活动 `UiExecutionContext` 时门面失败；持有服务的 Runtime/Provider 在其 owner 生命周期中关闭 Context。唯一 `Properties/ScriptingApi.cs` 限定脚本导出类型；`IUiBackend` 和 RmlUi ABI 不导出。基础层不依赖 Scene、Rendering 或 Editor。

## 公开类型与工作流

| 类型 | 语义 |
| --- | --- |
| `UiContextOptions`、`UiContextHandle`、`UiDocumentHandle` | 独立 surface 的尺寸/density 与不透明代际句柄；调用方负责关闭。 |
| `UiDocumentAsset`、`UiDocumentSource` | 导入 RML 或内存 RML；后者适用于运行期生成内容。 |
| `UiTextureData`、`UiFrameTexture` | 只接受紧密排列 RGBA8；帧纹理含 ID、revision 和像素副本。 |
| `UiVertex`、`UiMeshUpdate`、`UiTextureUpdate`、`UiDrawCommand`、`UiRenderFrame` | 后端中立资源增量与绘制命令；不是图形设备 handle。 |
| `UiEventType`、`UiEvent` | 更新后排队的 DOM 事件，包含文档 handle 与元素 ID。 |
| `UI`、`IUiService`、`UiExecutionContext` | 文档生命周期、DOM 更改、字体/纹理注册、Update/Render/DrainEvents。 |
| `IUiBackend`、`UiInputSnapshot` | Runtime/adapter 边界，不属于脚本导出。 |

```csharp
using InnoEngine.UI;

static UiDocumentHandle Show(UiContextHandle context, UiDocumentAsset source)
{
    UiDocumentHandle document = UI.LoadDocument(context, source);
    UI.ShowDocument(context, document);
    return document;
}
```

上述调用需位于 UI Session scope；先 `Update` 再 `DrainEvents`/`Render`。Context 关闭后句柄失效，跨 Session/Plugin reload 不持久化；持久状态应保存 Asset ID 与稳定业务 ID。UI 不承诺任意外部文件路径加载：当前 RML 可使用内联 RCSS，以及由调用方注册的命名纹理。字体 collection 的 UI 注册当前仅支持 face 0，Text shaping 本身支持显式 faceIndex。
