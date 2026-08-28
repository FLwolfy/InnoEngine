# Inno.Rendering.ShaderGraph

[返回 Rendering 索引](README.md) · [Wiki 首页](../README.md) · [通用 Graph](../core/Inno.Core.Graphs.md) · [Shader 资产链](Inno.Rendering.Assets.md)

## 职责与边界

`Inno.Rendering.ShaderGraph` 把中立 `GraphDocument` 解释为 Shader 语义，并生成与手写 `.ishader` 完全相同的 `ShaderDefinition`、`ShaderIRModule`、shaderc 与目标 artifact 链。它依赖 `Inno.Core.Graphs` 和 `Inno.Rendering`；Rendering 不反向引用本项目，通用 Graph 也不知道 Shader、ImGui 或 Editor。

`ShaderGraphAsset` 派生自 `ShaderAsset`，因此 Material 可以直接引用 `.ishadergraph`。Surface Graph 自动生成 `ForwardLitClustered`、`ForwardLit`、GBuffer、DepthOnly、ShadowCaster 与 Picking；前两者分别消费 GPU cluster list 与 CPU uniform light fallback，并通过 Pass-local Shader Interface 共存于同一 IR。VertexFragment 生成通用 Raster Pass；Compute 生成 Storage Buffer kernel。

## 公开 API

| API | 语义 |
| --- | --- |
| `ShaderGraphTarget`, `ShaderGraphOutputKind`, `ShaderValueType` | Graph 目标、输出协议和静态值类型 |
| `ShaderGraphAsset`, `ShaderGraphDocumentData`, `ShaderGraphDocumentCodec` | 严格当前格式 JSON、稳定 Node/Port ID 与可编辑文档 |
| `BuiltinShaderNodes` | 常量、数学、材质属性、纹理、空间输入及 Surface/Vertex/Fragment/Compute 输出节点 ID |
| `ShaderNodeExtensionAttribute`, `ShaderNodeDefinition` | Project 脚本节点的 Stable ID 和派生入口 |
| `ShaderNodeEmitContext`, `ShaderValue` | 类型检查后的输入、表达式、statement、property 和 semantic 发射接口 |
| `ShaderNodeRegistry` | built-in + TypeCache 扩展的候选构建、原子切换与 generation |
| `ShaderGraphCompiler`, `ShaderGraphCompileResult` | Graph 验证、节点映射诊断与共享 Shader IR 生成 |

## 脚本节点示例

```csharp
using System.Collections.Generic;
using InnoEngine.Graphs;
using InnoEngine.Rendering;
using InnoEngine.Rendering.ShaderGraph;

[ShaderNodeExtension("sample.color")]
public sealed class SampleColorNode : ShaderNodeDefinition
{
    public SampleColorNode()
        : base("sample.color", "Sample Color", "Sample", ShaderStage.Fragment) { }

    public override IReadOnlyList<GraphPortDefinition> GetPorts(GraphNodeRecord node)
        => new[] { new GraphPortDefinition(new GraphPortId("value"), "Color",
            ShaderGraphValueTypes.GetId(ShaderValueType.Color), GraphPortDirection.Output) };

    public override void Emit(ShaderNodeEmitContext context)
        => context.SetOutput(new GraphPortId("value"),
            new ShaderValue(ShaderValueType.Color, "vec4(1.0, 0.2, 0.1, 1.0)", context.node.id));
}
```

缺失节点不会删除 record 或 edge；编译返回 `SHADER_GRAPH_MISSING_NODE` 并保留 last-good 预览。Registry generation 不能被长期对象、delegate 或 CLR `Type` 固定。

## 相邻页面

- [Inno.Core.Graphs](../core/Inno.Core.Graphs.md)：不含渲染语义的文档、验证与连接。
- [Inno.Rendering.Assets](Inno.Rendering.Assets.md)：统一 IR、shaderc 与目标 artifact。
- [Inno.Editor.Graph](../editor/Inno.Editor.Graph.md)：通用画布、History 与文档控制器。
- [ShaderGraph Panel](../editor/Inno.Editor.Panel.ShaderGraph.md)：Editor 交互与预览。
