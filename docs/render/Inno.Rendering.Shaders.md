# Inno.Rendering.Shaders

[Rendering 索引](README.md) · [Wiki 首页](../README.md) · [BGFX 工具链](../build/Inno.Build.Toolchains.Bgfx.Tools.md)

## 职责与当前状态

内置 Shader 创作层。目前已经实现语言中立的源码函数接口、多实现分析快照、函数端口推导、图节点降低、typed region 与 stage。
依赖通用 Graph、Serialization、Diagnostics、Execution、TypeRegistry 和 Rendering 的 GPU 阶段契约；不直接引用
BGFX、Rendering.Assets、Scene、Editor 或 2D 插件。
它不创建 GPU 对象，Rendering 运行时也不反向引用它。

`.ishader` 图 importer、函数源码模块和 typed program 已进入实际资产编译链，Rendering2D 与 ImGui 图产物已在 Metal 启动中使用。
[Shader Editor](../editor/Inno.Editor.Panel.ShaderEditor.md) 已接入，但高级工作流、Target 扩展、安全帧原子发布与完整 legacy 清理仍未完成。
当前实现不能作为完整交付；已验证行为和具体缺口以[实施记录](../issues/2026-09-11-unified-shader-implementation.md)为准。

## 公开 API

| 类型 | 入口及语义 |
| --- | --- |
| `ShaderSourceType` | `Atomic`、`ArrayOf`、`Structure`、`Storage` 创建不可变类型；`id`、`elementType`、`elementCount`、`fields`、`storage` 描述布局；`IsEquivalentTo` 同时比较名称、布局及资源访问契约 |
| `ShaderStorageType` | `Buffer(element, access)` / `Image(format, access, dimension, array)`；只读 `valueType/access/format/dimension/array/isImage`；复用 Rendering 的 `RenderStorageAccess`，不复制一套访问枚举 |
| `ShaderSourceField` | `(name, type)` 构造一个不可变结构成员；公开同名只读属性 |
| `ShaderSourceParameterDirection` | `Input`、`Output`、`InputOutput`，区别函数值流向 |
| `ShaderSourceParameter` | `(name, type, direction)`；参数以名称匹配，不能按位置偷偷接回改名端口 |
| `ShaderSourceFunction` | `(name, returnType, parameters, location)`；`HasSameInterface` 校验替代实现的返回值、参数名、方向、顺序和类型，允许实现函数名不同 |
| `ShaderSourcePosition` | `assetPath`、从 1 开始的 `line`/`column`，指向原始文件 |
| `ShaderSourceDiagnostic` | `code`、`severity`、`message`、`location`，不保留异常或解析器实例 |
| `ShaderSourceFile` | `assetPath` 与不可变 `text` 输入快照 |
| `IShaderSourceResolver` | `ReadInclude(includingFile, include)`；调用者必须接到当前资产候选、挂载权限及依赖记录 |
| `ShaderSourceRequest` | 冻结 `source`、`entryPoint`、`resolver` 和 `defines`；语言不从当前 GPU API 猜测 |
| `ShaderSourceAnalysis` | `function`、`dependencies`、`diagnostics`、`succeeded`；成功仅表示接口分析无错误，不表示 GPU 编译通过 |
| `IShaderSourceFrontend` | `languageId`、`Analyze(request)`；一种语言的词法/声明分析扩展，不能创建 GPU 资源 |
| `ShaderSourceFrontendCatalog` | 构造时捕获唯一语言 ID 的集合；`languageIds`、`Analyze(languageId, request)`；缺失前端明确失败 |
| `ShaderSourceFrontendExtensionAttribute` | 标记参与共同 TypeRegistry 发现的前端实现 |
| `ShaderSourceFrontendRegistry` | `(TypeCatalog)` 绑定 owner；`languageIds`、`Analyze` 在共同 operation scope 下工作；`Build` 与 `DisposeSnapshot` 接入候选、回滚和退休 |
| `ShaderSourceNodeDefinition` | 从函数接口创建 `inno.shader.source` 的 `GraphNodeDefinition`；`function`、`GetPorts(node)` 返回缓存的端口快照 |
| `ShaderSourceImplementationRequest` | `(implementationId, languageId, source)`；键由 owner 明确区分 Adapter/Target/variant，不从文件名推断 |
| `ShaderSourceImplementationAnalysis` | `implementationId`、`languageId`、`sourcePath`、`entryPoint`、`defines`、`sources`、`includes`、`analysis`、`contentHash`；`CreateSourceRequest()` 只通过冻结文件和 include 边重建输入，不触碰活动 resolver |
| `ShaderSourceInclude` | `includingFile`、`include`、`resolvedPath`，保留 include 别名和挂载解析结果，而不是编译时重新猜路径 |
| `ShaderSourceModuleAnalysis` | `implementations` 保留成功和失败配置；`function` 仅在全部接口一致时提供；`diagnostics` 和 `succeeded` 不表示 native 编译成功 |
| `ShaderIrValue` | builder 分配的 `index` 和完整 `type`；不是 GPU register，不能跨 builder 混用 |
| `ShaderIrOperation` | 值运算：Input、Constant、Construct、Extract、Add/Subtract/Multiply/Divide/Minimum/Maximum、Equal/LessThan、Select、SourceCall；资源：TextureSample/TextureSampleLevel、StorageLoad/StorageStore/StorageAtomicAdd、Discard；控制流：RegionInput、Branch、Loop |
| `ShaderIrInstruction` | 不可变 `operation/inputs/outputs/regions`；操作数据为 `inputName/constantBits/memberIndex/source`；`hasSideEffects` 包括保守的源码调用、内存访问和控制流，不能任意删除或移动 |
| `ShaderIrBlock` | 按求值顺序保存 `instructions` 和具名 `outputs`，不持有 builder、前端或 resolver |
| `ShaderIrBuilder` | `Input`；float/int/uint/bool `Constant`；`Binary/Select/Construct/Extract/Call/Build`；`Sample/SampleLevel/LoadStorage/StoreStorage/AtomicAddStorage/Discard`；`Branch/Loop`。全部验证类型与词法作用域，冻结后不保留构造 callback |
| `ShaderIrStageInput` / `ShaderIrInputKind` | `id`、`type`、`kind`、`semantic`、`location`；描述 VertexAttribute、Varying、Uniform、SampledTexture、Storage、Builtin |
| `ShaderIrStageOutput` / `ShaderIrOutputKind` | `id`、`kind`、`semantic`、`location`；描述 ClipPosition、Varying、Color、Depth |
| `ShaderIrStage` | `(stage, body, inputs, outputs, threadsX, threadsY, threadsZ)`；复制接口、验证输入/输出类型及唯一绑定，禁止跨阶段错误接口；`contentHash` 覆盖阶段语义、嵌套区域、源码与布局，不包含画布位置 |
| `ShaderNodePort` | `id`、完整 `type`、`direction`、`required`，与 Editor 绘制类型分离 |
| `IShaderNodeCompiler` | `definitionId`、`GetPorts(context)`、`Lower(context)`；公共算法不集中判断具体节点类型 |
| `ShaderNodeCompilerExtensionAttribute` | 标记参与共同 TypeRegistry 发现的节点编译实现 |
| `ShaderNodeDescriptionContext` | `nodeId`、`definitionId`、`sourceModule`、`implementationId`、`stageInput`；`Read<T>(id, defaultValue)` 使用完整 owner SerializationContext；禁止跨调用保留 |
| `ShaderNodeLoweringContext` | `description`、`builder`、`inputs`、`Input(id)`；只在当前降低调用中使用 |
| `ShaderGraphLoweringRequest` | 捕获图的独立中立副本、具名 `outputs`、`implementationId`、冻结 `sourceModules` 和 `stageInputs` |
| `ShaderGraphDiagnostic` | `code`、`severity`、`message`、可空 `nodeId` / `portId`；不保存旧 provider 或异常对象 |
| `ShaderGraphLoweringResult` | `block`、`diagnostics`、`succeeded`；失败没有半成品 region |
| `ShaderNodeCompilerCatalog` | `(compilers)` 验证唯一 definition ID；`definitionIds`、`Lower(request, serialization, context, cancellationToken)` |
| `ShaderNodeCompilerRegistry` | `(TypeCatalog)`、`definitionIds`、`Lower`；整个 region 使用同一代际 operation，候选回滚/退休复用 TypeRegistry |
| `ShaderConstantNodeCompiler` | `inno.shader.constant`；原生属性 `type` 为 float/int/uint/bool，`value` 必须使用对应原生数值类型 |
| `ShaderBinaryNodeCompiler` | `inno.shader.binary`；`type` 与 `operation`，端口 left/right/value；支持 add/subtract/multiply/divide/minimum/maximum/equal/less-than |
| `ShaderConstructNodeCompiler` | `inno.shader.construct`；向量/矩阵 `type`，按序 `component.N` 输入和 value 输出 |
| `ShaderStageInputNodeCompiler` | `inno.shader.stage-input`；从 Target 捕获的输入绑定产生 value，不接受 native 变量名 |
| `ShaderSelectNodeCompiler` | `inno.shader.select`；`type`、condition/true/false/value 端口，不能用它隐藏上游副作用 |
| `ShaderSourceNodeCompiler` | `inno.shader.source`；使用已冻结模块生成 return/out/inout 与结构成员，显式拒绝聚合和成员同时连接 |
| `ShaderRerouteNodeCompiler` | `inno.shader.reroute`；强类型 value 输入/输出透传，不添加指令或改变副作用顺序 |
| `ShaderGraphDocument` | `Create/ReadDefinition` 保存和读取图的材质/Pass 契约；`Encode/Decode/Read<T>` 使用 owner SerializationRegistry/Context。`definitionKey/outputDefinitionId/stageKey/settingsKey` 为原生图协议键；不能传入临时拼接的引用上下文 |
| `ShaderGraphStageSettings` | `pass/stage/outputs/threadsX/threadsY/threadsZ`；在 output node 中持久化完整阶段和 Compute 工作组配置 |
| `ShaderGraphOutput` | `id/kind/semantic/location`；输出端口名、GPU 目标类别与显式位置，不保存 native 表达式 |
| `ShaderGraphInputSettings` | `id/type/kind/semantic/location` 与 `CreateBinding()`；未完成设置可保存，创建不可变 binding 时严格校验 |
| `ShaderGraphType` | `id/element/length/fieldNames/fieldTypes`；storage 的 `isStorage/isImage/storageElement/access/format/dimension/isArray`；`CreateType()` 拒绝矛盾描述并生成不可变类型 |
| `ShaderGraphTemplates` | `CreateRaster(serialization, context)` 创建有效默认图，不新增第二种 Shader 资产格式 |
| `ShaderGraphBindings` | `ChangeInput(graph, nodeId, settings, serialization, context)` 返回独立候选，原子调整输入节点与暴露参数契约，不修改传入图；未完成输入保留为可序列化内容，调用者将整个候选记录为一次 History 修改 |
| `ShaderGraphBindings.RemoveNodes(graph, nodeIds, serialization, context)` | 返回删除候选，不修改原图；输出节点连同阶段内容删除，清理失去最后所有者的 Pass/绑定及已删除 Pass 的 Technique 映射；共享参数保留默认值并更新阶段可见性，其他未完成内容不做全图清洗 |
| `ShaderGraphProgramCompiler` | `(ShaderNodeCompilerRegistry)` 与 `Lower(...)`；验证图级定义、阶段归属与接口，按 Pass 降低，不编译原生 GPU 程序 |
| `ShaderGraphPass` | `(name, stages)`；保存有序阶段快照，公开只读 `name/stages` |
| `ShaderGraphProgramResult` | 只读 `passes/diagnostics/succeeded`；成功降低不表示 Adapter 编译或 GPU 发布成功 |

Registry 的 protected override 复用 `TypeRegistry` 契约；Registry 本身封闭，语言通过接口与发现标记组合，端口呈现仍属于 Editor。

## 脚本扩展边界

本项目唯一的 `Properties/ScriptingApi.cs` 显式导出节点/前端扩展契约、描述对象和 typed IR，逻辑命名空间为
`InnoEditor.Rendering.Shaders`，全部是 Editor scope。Registry、后台编译 owner 和 Adapter 实现不暴露给普通脚本。
运行时脚本不能引用这些创作类型；Player 仍只需要运行时接口和预编译产物。
图读写工具 `ShaderGraphDocument`、`ShaderGraphBindings`、`ShaderGraphTemplates` 已导出到 Editor 脚本，
配合宿主提供的 SerializationRegistry 与完整引用上下文使用。脚本不能构造 Registry 或捕获 converter generation。
自定义节点绘制通过独立 `Inno.Editor.Shaders` 的 `ShaderNodeDrawContext.Read/Write` 使用中立数据，与编译扩展独立。

## Target 与模板

| API | 契约 |
| --- | --- |
| `ShaderTargetAttribute`、`ShaderTarget.id/Expand(context, cancellationToken)` | 插件将领域输出展开为已有阶段图；Pass 声明能力、Contract/Role 和绑定；不生成原生 Shader 字符串 |
| `ShaderTargetContext.document/serialization/references` | 独立原图副本、借用的 owner converter 和完整引用上下文，只在调用期间有效 |
| `ShaderTargetRegistry(types).ids/Expand/Dispose` | 宿主的 generation-scoped 发现与调用；Missing Target 明确失败且不改原图 |
| `ShaderGraphDocument.targetKey/ReadTarget/SetTarget` | 持久化稳定 Target ID；未指定领域 Target 的图显式创作通用阶段 |
| `ShaderGraphTemplateAttribute`、`ShaderGraphTemplate.id/displayName/Create` | 插件贡献 File Browser Shader 创建模板 |
| `ShaderGraphTemplateInfo(id, displayName)` | 不含 provider 的菜单快照 |
| `ShaderGraphTemplateRegistry(types).templates/Create/Dispose` | 按稳定 ID 创建独立图，重复 ID 或缺失模板明确失败 |

Target 在导入时先展开，随后捕获其引入的源码与资产依赖，进入同一公共 IR 和 Adapter 编译链。
Authoring Artifact 同时保存原图与展开图；Export/Editor 读取原图，编译读取展开图。目标生成的节点位置不影响语义指纹。
Target 与 Template 的 provider 只存活于 TypeRegistry snapshot，公共菜单快照只含字符串；运行时不解释 Target 或图。

```csharp
using System.Collections.Generic;
using InnoEngine.Graphs;
using InnoEditor.Rendering.Shaders;

[ShaderNodeCompilerExtension]
public sealed class HalfNodeCompiler : IShaderNodeCompiler
{
    public string definitionId => "project.shader.half";
    public IReadOnlyList<ShaderNodePort> GetPorts(ShaderNodeDescriptionContext context)
        => [new("value", ShaderSourceType.Atomic("float"), GraphPortDirection.Output)];
    public IReadOnlyDictionary<string, ShaderIrValue> Lower(ShaderNodeLoweringContext context)
        => new Dictionary<string, ShaderIrValue> { ["value"] = context.builder.Constant(0.5f) };
}
```

以上代码放入 `.editor.cs`。已验证真实脚本编译与生成 IDE 项目的裁剪引用均只向 Editor 暴露该接口；
这是 API 可扩展性验证，不代替 Shader Editor 中自定义节点的完整绘制、创建、热重载和卸载验收。

## 注册调用

Catalog 与 Registry 另有 `AnalyzeModule(implementations)`。Registry 在整个多实现分析期间持有同一 operation scope，
不能在两个语言实现之间切换 generation。缺失语言返回可恢复诊断；空候选集或重复 implementation key 属于调用错误。

## 组合示例

```csharp
using Inno.Rendering.Shaders;
using Inno.Build.Toolchains.Bgfx.Tools;

static ShaderSourceAnalysis Inspect(IShaderSourceResolver candidateSources)
{
    var frontends = new ShaderSourceFrontendCatalog([new BgfxShaderSourceFrontend()]);
    var source = new ShaderSourceFile("project::Functions/Tint.ishadersource",
        "vec4 Tint(vec4 color, float strength) { return color * strength; }");
    return frontends.Analyze("inno.shader-language.bgfx-sc",
        new ShaderSourceRequest(source, "Tint", candidateSources));
}
```

BGFX 类只出现在工具链组合处。其他前端实现 `IShaderSourceFrontend` 并用不同稳定语言 ID 注册，
不需要向公共编译器添加语言分支。

## 多实现分析与依赖快照

- 调用者列出需要一致校验的 Adapter 实现及条件编译配置；公共层不翻译 native 代码，也不擅自枚举未知宏组合。
- 返回类型、参数名、方向、次序和聚合布局必须一致；函数的私有实现名允许不同。
- `AnalyzeModule` 用受控 resolver 捕获全部成功读取的源；同一路径在一次分析中返回不同内容会失败。
- 前端成功结果中声明但未通过 resolver 读取的依赖会失败，避免稍后编译偷偷读另一个文件版本。
- `contentHash` 覆盖语言、入口、源路径、全部源文本、include 解析边及按 key 排序的 defines，忽略实现集合的枚举顺序。
  它只是输入指纹，**不是**完整编译缓存 key；图语义、Target、工具链身份和资源布局仍须纳入最终编译 key。
- `ShaderIrStage.contentHash` 覆盖降低后的类型、值/副作用顺序、所有嵌套区域、绑定和源码输入；移动节点不改变它。
  它同样不是完整缓存 key，调用者还必须加入工具链身份、能力、优化和变体配置。
- 源码错误不能吞掉共同的 retirement barrier；缺失语言恢复后可重新分析原始中立输入，无须丢弃函数引用。

## Typed IR 的当前边界

当前 typed region、嵌套分支/计数循环与 stage 已由 ShaderGraphProgramCompiler 组合，并接到 `.ishader` importer。
完整 Target、资源/控制流节点 UI 和发布原子性仍需收口，不能把底层 IR 能力视为全部创作体验已完成。
常量保存 IEEE/整数位模式而不是 native 字符串；输入使用中立名称；聚合保留完整成员类型；参数按语义名称检查后，
调用操作按函数声明顺序传参，返回值在前、out/inout 在后。`Build` 冻结快照，之后继续编辑 builder 不改变旧结果。

```csharp
using System.Collections.Generic;
using Inno.Rendering.Shaders;

static ShaderIrBlock BuildBrightness()
{
    var builder = new ShaderIrBuilder();
    ShaderIrValue brightness = builder.Input("brightness", ShaderSourceType.Atomic("float"));
    ShaderIrValue result = builder.Binary(ShaderIrOperation.Multiply, brightness, builder.Constant(2f));
    return builder.Build(new Dictionary<string, ShaderIrValue> { ["brightness"] = result });
}
```

- 运算不做隐式数值转换；`Multiply` 是逐分量乘法，不暗示矩阵线性代数乘法。
- 矩阵构造按列排列标量；矩阵 `Extract` 取列向量。结构与数组保留精确布局、次序和长度。
- `Select` 只选择已经求出的值，不是分支，也不能借它抑制另一侧的源码副作用。
- `Branch` 才是真正条件执行：两个回调输出名和类型必须完全一致，只有选中分支的指令执行，合并值才对外可见。
- `Loop` 的 uint 次数在进入循环前求值；每次迭代提供索引和具名 carried state，输出同时替换状态，零次返回初值。
  允许空输出/空状态的纯副作用区域；禁止将资源句柄当普通合并值。嵌套 builder 在回调返回后关闭，不能编辑父层或使用兄弟层的未合并值。
- `Sample` 使用 fragment 隐式导数；其他阶段必须用 `SampleLevel`。`Discard` 仅用于 Fragment，阶段检查递归进入所有嵌套区域。
- Storage buffer 使用 uint 下标；2D image 使用 int2，array/3D 使用 int3；图 IR 检查元素、格式及读写权限。
  `StorageLoad` 也保留内存顺序，不能把写入前后的两次 load 合并；原子加返回原值，只允许 read-write int/uint buffer。
- 所有源码调用保守视作有副作用；即使返回值无人使用，也保留原始调用顺序。本批次不做 DCE 或跨调用重排。
- 图降低按稳定拓扑顺序执行，独立节点以文档顺序排序；不删除未使用的源码调用。输入索引线性建立，不按每个节点全表搜索连线。
- Stage 支持显式 raster/compute 接口、uniform/采样纹理、storage、MRT 与工作组；图 artifact 冻结源码依赖并调用 typed 编译链。
  完整 Target、图级资源/控制节点接线和定义/产物原子发布仍未验收。
- 旧完整 SC 字符串创作入口已被统一图链取代；本轮还需完成最终内部 Shader 解耦和产物清理验收，详见[实施记录](../issues/2026-09-12-shader-authoring-execution.md)。

### 图输入默认值

`ShaderGraphLiteral.Zero(type)` 为支持的标量、向量、浮点矩阵、结构体和固定数组创建精确位模式默认值。
`type` 保存完整类型，`scalarBits` 以声明/矩阵列顺序保存 IEEE float、int、uint、bool 的原始位；`GetScalarTypes()` 校验形状，`Emit(builder, expectedType)` 发出同一公共 IR。
`ShaderGraphDocument.inputDefaultPrefix + portId` 绑定到稳定输入名，而非端口位置。连线优先、旧默认值保留但不求值；未连接类型不匹配返回定位到端口的 `SHADER_GRAPH_DEFAULT_TYPE`。
不能给 storage、opaque texture 或效果顺序令牌生成数值默认值。类型变更要显式修复，不做隐式转换。该类型仅导出给 Editor 创作脚本。

## 端口与生命周期

- 输入为 `input.<name>`，输出为 `output.<name>`，非 void 返回值为 `return`。`inout` 生成两个端口。
- `void` 只用于无返回值；参数、结构体成员和数组元素均不能使用它，也不能用 alias/嵌套数组绕过值类型检查。
- 结构体保留聚合端口，同时提供 `.member` 端口；展开/折叠不改变边的身份。数组保留固定元素类型和长度。
- 聚合端口类型标识包含完整布局的确定性 hash，不能把不同长度数组当作同一种图类型。
- lowering 已验证聚合与成员冲突、按名称发现失效连接和缺失编译器恢复；原图不被修改。悬挂边的 Editor 修复界面尚未实现。
- Catalog 是 owner 持有的不可变注册快照，不是全局单例。独立组合时调用者负责 provider 生命周期；
  `ShaderSourceFrontendRegistry` 则使用共享 TypeRegistry 发现、候选冲突回滚和 provider 退休，解析期间
  持有共同 operation scope。现有测试覆盖该注册闭环，不等于真实 collectible 插件、Editor 文档和 GPU 发布的完整热卸载验收。
- 持久化只能保存稳定 ID 与中立值，不能保存 frontend、resolver、图定义对象或解析器。
