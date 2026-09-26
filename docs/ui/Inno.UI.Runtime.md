# Inno.UI.Runtime

[UI 索引](README.md) · [Runtime](../runtime/README.md)

`UiRuntimeFactory` 以 `inno.runtime.ui` 注册 Session Subsystem（order `-700`），依赖 Input 与 Text。`UiRuntime` 管理导入文档的字体 artifact、后端与 Context，并允许每个 Context 接收独立 `UiInputSnapshot`。Canvas 先由实际 RenderView 路由输入，再显式更新对应 Context；不会向所有世界画布广播窗口鼠标。文档关闭或 Context 销毁时，最后一个使用者释放对应字体 lease；Session 释放时清理剩余资源。

公开构造入口 `UiRuntime(IUiBackend, IAssetArtifactLookup)` 转交后端所有权。Runtime 只根据 backend 的 implementation ID、document language 与 capability 做中立校验，不出现具体实现名。它在加载导入文档前注册该文档声明的字体，以文档内容版本隔离字体族；同一字体 artifact 被多个 Context 使用时只保留一份 lease。UI 只依赖 Input 帧序，不再伪依赖独立 Text runtime。

## 生命周期与扩展点

Factory 的公开 `descriptor` 声明 Input/Text 依赖，`Create(RuntimeSubsystemContext)` 为每个 Session 构造 Runtime。`UiRuntime` 的公开方法与 [IUiService](Inno.UI.md) 一一对应：先 `CreateContext`，然后 `LoadDocument` / `ShowDocument`，每帧 `Update`、`DrainEvents`、`Render`，最终 `CloseDocument` / `DestroyContext`。`OnBeginFrame` 捕获 Input 快照，`OnStop` 释放 backend 及字体 artifact lease；它们只是 RuntimeSubsystem 覆写，不属于脚本 API。

```csharp
using Inno.UI;

using IDisposable scope = runtime.EnterExecutionScope();
UiContextHandle context = UI.CreateContext(new UiContextOptions("HUD", 1280, 720));
UiDocumentHandle document = UI.LoadDocument(context, source);
UI.ShowDocument(context, document);
```

此段供 Host 控制 Session 的场景使用；普通脚本在活动帧内调用 `UI` 门面。跨 Session 的 Context/Document handle 不能复用；持久配置只保存资产身份与业务数据。
