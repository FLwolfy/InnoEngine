# Inno.Rendering.ShaderGraph

[Rendering 索引](README.md) · [Core Graphs](../core/Inno.Core.Graphs.md) · [Shader 资产链](Inno.Rendering.Assets.md)

`Inno.Rendering.ShaderGraph` 是通用 Shader IR 前端。生产注册表默认没有任何节点：常量、采样、Sprite、材质模型、光照和 Program Output 都由 Project/Plugin 注册。

## 核心模型

- `ShaderGraphAsset` 只保存中立 `GraphDocument`，通过 Inno 原生序列化持久化。
- `ShaderNodeDefinition` 声明端口、允许阶段和类型化发射。
- `ShaderGraphProgramNodeDefinition` 是唯一 Program Output；它决定产生 Raster/Compute Pass、Technique、Contract、Role、源码和资源接口。
- `ShaderGraphCompiler` 验证 Missing Node、连接、类型转换、阶段合法性和输出数量，使用 Core Storage DependencyGraph 生成确定性节点拓扑，然后生成普通 `ShaderIRModule`。
- `ShaderNodeRegistry` 以 Stable ID 原子切换 Plugin generation；默认 `definitions` 为空。

## Plugin 节点示例

```csharp
[ShaderNodeExtension("sample.constant")]
public sealed class ConstantNode : ShaderNodeDefinition
{
    public ConstantNode()
        : base("sample.constant", "Constant", "Sample", ShaderStage.Fragment) { }

    public override IReadOnlyList<GraphPortDefinition> GetPorts(GraphNodeRecord node)
        => new[]
        {
            new GraphPortDefinition(
                new GraphPortId("value"),
                "Value",
                ShaderGraphValueTypes.GetId(ShaderValueType.Color),
                GraphPortDirection.Output)
        };

    public override void Emit(ShaderNodeEmitContext context)
        => context.SetOutput(
            new GraphPortId("value"),
            new ShaderValue(
                ShaderValueType.Color,
                "vec4(1.0, 0.2, 0.1, 1.0)",
                context.node.id));
}
```

Program Output 派生类在 `BuildProgram(ShaderGraphProgramContext)` 中调用 `Emit(stage)`，读取自己定义的开放 semantic，并构建任意 `ShaderDefinition`/`ShaderIRPass`。内核不会自动生成任何特定 Pass。

Missing Node 会保留 node record、端口值和 edge；候选编译失败不会破坏 last-good 预览。Graph 不保存 CLR `Type`、运行时实例或 JSON DOM。
