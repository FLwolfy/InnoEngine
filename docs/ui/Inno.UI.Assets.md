# Inno.UI.Assets

[UI 索引](README.md) · [Assets](../assets/README.md)

`UiDocumentImporter` 通过语言前端分析 `.rml` 源，输出 `runtime` artifact；RML 前端验证根元素并解析内联 `@font-face`。字体文件成为标准资产依赖，文档产物保存隔离后的字体族和 face 声明。`UiDocumentAsset` 通过标准资产身份和 Missing 语义进入 Player；导入器不执行 DOM 渲染或图形上传。

## 公开契约与工作流

`UiDocumentImporter : AssetImporter<UiDocumentAsset>` 的公开 `supportedExtensions` 为 `.rml`；受保护的 `ImportAsync` 使用现有 Asset Pipeline 写入资产，调用方不需要另一套 UI 文件索引。将 `Hud.rml` 放入 Project `Assets` 后，在有效 Asset scope 中加载 `UiDocumentAsset`，再交给 [Inno.UI](Inno.UI.md) 的 `UI.LoadDocument`。安装在 Plugin 中的 RML 仍经同一 importer 进入只读 mount。

空文件、无 RML 根节点、无效字体引用和非法 UTF-8 在导入时报告错误；Missing 资产不会被替换为默认空文档。RCSS 可内联在 RML 中；本 importer 不负责读取任意外部 CSS、网络图片或系统字体，也不属于 Player runtime closure。
