# Inno.Editor.Panel.MaterialGraph

[返回 Editor 索引](README.md) · [Wiki 首页](../README.md) · [Editor Graph](Inno.Editor.Graph.md) · [MaterialGraph](../render/Inno.Rendering.MaterialGraph.md)

Material Graph Panel 用专业深色双栏布局直接编辑当前选中的普通 `.imaterial`：左侧是 Inspector 风格的 Material/Properties 区，右侧是该材质唯一的节点画布。常用界面只保留 Save、Shader 和当前节点值；Technique 与重建映射收进 Advanced，错误诊断只在需要处理时出现。紫色只用于主操作、Output 与当前选择。

新建材质会先创建唯一 `Material Output`；选择 Shader 后按 reflection 建立一一对应的属性节点和连线。现有 `.imaterial` 第一次打开时直接从其 Shader、Technique 和属性生成图；保存后图文档内嵌回同一个 MaterialAsset。更换 Shader 或重置映射会作为一次 History 事务原子替换文档，并保留稳定 ID 与类型兼容的值。

Asset Browser 选中或双击 `.imaterial` 会确定性地显示它的一对一 Material Graph，不需要 New/Open Selected/路径三套并行状态。切换或新建前若当前图为 dirty，面板提供 Save and Continue、Discard、Cancel；Discard、关闭和保存失败都会恢复 Shader、Technique、属性及内嵌图快照。

Panel 只保存文档稳定 ID、资产路径和画布视图参数。重载期间文档由 `GraphEditorModule` 持有，缺失资产不会被空白文档覆盖。
