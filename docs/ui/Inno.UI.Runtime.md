# Inno.UI.Runtime

[UI 索引](README.md) · [Runtime](../runtime/README.md)

`UiRuntimeFactory` 以 `inno.runtime.ui` 注册 Session Subsystem（order `-700`），依赖 Input 与 Text。`UiRuntime` 在帧开始捕获 Session 的 `InputSnapshot` 并绑定脚本 scope；加载 RML/font artifact，管理后端与 Context。Session 释放时 Context 和字体 lease 随 owner 一起退休，不跨 Edit/Play Session 共享可变状态。

公开构造入口 `UiRuntime(IUiBackend, IAssetArtifactLookup)` 转交后端所有权。Runtime 只根据 backend 的 implementation ID、document language 与 capability 做中立校验，不出现具体实现名。字体 faceIndex 先按导入 metadata 验证，再按 capability 决定是否可用；不支持时抛出 `UiCapabilityUnavailableException`。UI 只依赖 Input 帧序，不再伪依赖独立 Text runtime。

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
