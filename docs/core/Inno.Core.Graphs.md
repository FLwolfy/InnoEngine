# Inno.Core.Graphs

[上一页：Storage](Inno.Core.Storage.md) · [Core 索引](README.md) · [Wiki 首页](../README.md) · [下一页：Rendering](../render/README.md)

`Inno.Core.Graphs` 是不依赖 Rendering、Scene、Assets、Editor 或 ImGui 的通用图模型。它只持久化稳定 ID、中立 JSON 值、节点位置和连接，不保存 CLR `Type`、extension 实例或 runtime delegate。因此节点插件卸载后，文档及连线仍能完整保留。

## 职责与边界

- `GraphDocument` 拥有有序 `GraphNodeRecord`、`GraphEdgeRecord` 和中立 metadata。
- `GraphNodeId`、`GraphEdgeId`、`GraphPortId` 是稳定字符串 ID，不依赖进程内 hash。
- `GraphNodeDefinition.GetPorts` 支持根据节点中立数据生成动态端口。
- `GraphValidator` 检查方向、类型转换、端口容量、必填输入、缺失 endpoint，并复用 Core Storage DependencyGraph 检查确定性有向循环。
- Missing Node 是 warning：文档保持可编辑；真正缺失的物理 node/port endpoint 才是 error。

该项目只私有引用 `Inno.Scripting.Api` 以声明逻辑脚本 API `InnoEngine.Graphs`。ShaderGraph 是上层消费者，Core Graph 不知道 Shader 语义。

## 公开 API

| API | 作用 |
| --- | --- |
| `GraphDocument` | 增删节点/边、查询节点、保存中立 metadata。 |
| `GraphNodeRecord` | 保存 definition ID、位置和稳定 property JSON。 |
| `GraphEdgeRecord` / `GraphEndpoint` | 保存 output 到 input 的稳定连接。 |
| `GraphSerializedValue` | 验证、规范化并按类型读写一个 JSON 值。 |
| `GraphNodeDefinition` | reload-scoped 节点定义与动态端口扩展点。 |
| `[GraphNodeExtension(id)]` | Project 脚本节点发现协议。 |
| `IGraphNodeDefinitionResolver` | 通过 Stable ID 查询当前 generation 候选快照。 |
| `IGraphTypeConversion` | 声明有方向的隐式类型转换。 |
| `GraphValidator.Validate` | 生成确定顺序的结构化诊断。 |

## 常见工作流

```csharp
GraphDocument graph = new();
GraphNodeRecord node = new(new GraphNodeId("constant-1"), "math.float");
node.position = new GraphPosition(120f, 80f);
node.SetValue("value", GraphSerializedValue.From(0.5f));
graph.AddNode(node);

GraphValidationResult validation = GraphValidator.Validate(graph, activeDefinitions);
if (!validation.isValid)
{
    // Map stable diagnostics back to the graph canvas.
}
```

## 生命周期、错误与热重载

`GraphDocument` 可以跨 extension generation 存活；`GraphNodeDefinition` 和 resolver 只能属于当前候选快照。Registry 切换失败时继续使用上一份 resolver。Missing Node 不删除节点、属性或连线，脚本扩展重新可用后再次验证即可恢复。

当前稳定行为没有旧 schema reader、migration、former ID 或兼容 alias。图资产 writer/reader 将由具体上层资产项目负责。
