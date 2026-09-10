# Inno.Rendering.MaterialGraph

[返回 Rendering 索引](README.md) · [Wiki 首页](../README.md) · [Rendering Assets](Inno.Rendering.Assets.md) · [Editor Material Graph](../editor/Inno.Editor.Panel.MaterialGraph.md)

`Inno.Rendering.MaterialGraph` 是普通 `.imaterial` / `MaterialAsset` 的节点化数据映射前端。一个 Material 只有一份运行时状态和一张内嵌 authoring graph，不再要求用户维护第二个图资产。它不会生成完整 Shader、Pass、Technique 或渲染模型；Shader 仍由唯一的 Shader Asset/IR/目标编译链负责。

## 资产与求值

- `MaterialGraphDocumentStore` 将图文档保存在普通 `MaterialAsset` 的开放 metadata 中；运行时只消费同一对象已经求值得到的 Shader、Technique、关键词和属性。
- 图中只有一个 `Material Output` 和按 Shader reflection 创建的强类型 Value 节点。
- Output 端口只暴露 `ShaderPropertyBindingOwner.Material` 的属性；RenderPass 所有的纹理、Buffer 和状态不会伪装成材质参数。
- `MaterialGraphEvaluator` 验证端口类型、输出数量与 Technique，然后以稳定 Property ID 原子替换材质值。
- 更换 Shader 时会重建映射，并按稳定 Property ID 保留类型兼容的现有值。

## 持久化

`.imaterial` 仍由唯一的 `inno.rendering.material` importer 读写。图文档通过通用 `GraphDocumentCodec` 确定性编码并随同一材质保存；保存前必须先通过求值验证。运行时无需第二套材质绑定或 Shader 编译路径。

## 边界

通用 Graph 不引用 Rendering 或 ImGui。Rendering Core 不引用 MaterialGraph 或 Editor Graph。MaterialGraph 只依赖公开 Material、Shader reflection、Asset Pipeline 与通用 Graph 契约。
