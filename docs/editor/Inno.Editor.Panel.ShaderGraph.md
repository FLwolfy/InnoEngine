# Inno.Editor.Panel.ShaderGraph

[返回 Editor 索引](README.md) · [Wiki 首页](../README.md) · [Editor Graph](Inno.Editor.Graph.md) · [ShaderGraph](../render/Inno.Rendering.ShaderGraph.md)

该 Panel 组合 `GraphEditorModule`、`ShaderNodeRegistry` 与统一 ShaderGraph compiler，提供 pan/zoom、多选框选、连接/重连、搜索创建、复制粘贴、分组/注释/reroute 表现、保存、dirty 标记、预览和 Node/source 诊断定位。Missing Node 保留原文档与连线，失败预览保留 last-good artifact。

Graph 修改统一进入 `EditorInteractions.history` 的中立文档 operation；连续拖动使用稳定 merge key，结构修改不合并。`editor.ini` 仅保存打开文档、active document、pan/zoom 与预览设置，不保存节点选择、Undo、编译结果或 GPU 资源。

Panel 类型是 internal feature，没有额外稳定公共 API。可扩展语义来自 [Inno.Rendering.ShaderGraph](../render/Inno.Rendering.ShaderGraph.md)，通用编辑操作来自 [Inno.Editor.Graph](Inno.Editor.Graph.md)。
