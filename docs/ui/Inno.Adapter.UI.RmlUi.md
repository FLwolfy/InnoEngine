# Inno.Adapter.UI.RmlUi

[UI 索引](README.md) · [Native UI](../native/Inno.Native.UI.md)

默认 UI backend 将中立 Context、显式 `inno.ui-language.rml` 文本、字体、输入与纹理映射到 RmlUi 6.0 原生桥。RML 识别和 `.rml` importer 位于独立 `Inno.Adapter.UI.RmlUi.Authoring`，通用 UI service/asset 层只处理语言 ID 与文本。

原生侧只手写窄 C++ 语义 facade 与 RmlUi 适配逻辑；BGCS Cpp2C 生成 C ABI，BGCS 再生成 C# function-table binding。RmlUi 类型、回调与渲染热路径都留在 native adapter 内，managed 边界只传递中立 DTO、稳定句柄和增量资源。进程级初始化、线程归属和字体内存集中在显式 `RmlUiProcessHost`。逻辑字体 family 在每个 backend 内映射到 content-addressed 物理 family，因此并存 Session 的同名不同内容不会冲突。`Render` 仅在资源变化时复制 mesh/texture；成功 `FinishFrame` 后确认 delta，失败可重试。

## 使用与生命周期

```csharp
using Inno.Adapter.UI.RmlUi;
using Inno.UI;

using var backend = new RmlUiBackend();
UiContextHandle context = backend.CreateContext(new UiContextOptions("HUD", 800, 600));
UiDocumentHandle document = backend.LoadDocument(context, new UiDocumentSource(markup));
backend.ShowDocument(context, document);
backend.Update(context, input);
UiRenderFrame frame = backend.Render(context);
backend.DestroyContext(context);
```

`markup` 与 `input` 由调用方提供；该代码只适用于 Host/adapter 层，脚本使用 `InnoEngine.UI.UI`。Context/Document handle 在进程内不复用；无效或已退休 handle 抛错。Context 销毁时清理 DOM 和像素，因 RmlUi 的全局字体缓存可能在 `Rml::Shutdown` 再调用渲染接口，极小的接口壳保留至最后一个 UI backend 停止。实现不允许外部文件/网络读取隐式绕过 Asset Pipeline。参见 [UI Runtime](Inno.UI.Runtime.md)。
