# Inno.Rendering

[Rendering 索引](README.md) · [Runtime](Inno.Rendering.Runtime.md) · [ShaderGraph](Inno.Rendering.ShaderGraph.md)

`Inno.Rendering` 是 Project/Plugin 脚本面对的通用渲染 API。它不引用 Scene，也不定义 Camera、Light、MeshRenderer、PBR 参数、Render Queue 或固定 Pass Tag。

## 公开契约

| 分类 | API | 语义 |
| --- | --- | --- |
| 请求 | `RenderRequest`, `RenderTarget`, `RenderViewport`, `RenderFrameData` | 将目标、尺寸、可选 Pipeline 与 Plugin 自有帧数据提交给 Runtime。 |
| 内容作用域 | `RenderContentId`, `RenderContentReference`, `RenderContentScope` | Host 显式选择的有序、frame-scoped 内容根；不预设 Scene、World 或 Document 类型。 |
| 请求生产 | `RenderRequestProvider`, `RenderRequestProviderContext`, `RenderRequestProviderExtensionAttribute` | Plugin 每帧自动产生请求的 reload-safe TypeRegistry 扩展入口；Context 提供显式 content、capability、完整主表面尺寸与 Host 选定的主呈现 viewport，不预设 Camera。 |
| Pipeline | `RenderPipelineAsset`, `RenderPipeline`, `RenderPipelineContext` | Stable Type ID + 原生配置状态，以及每请求建图入口。 |
| Feature | `RenderPipelineFeature`, `RenderFeatureContext`, `RenderFeatureConfiguration` | 有序、可重载的额外建图扩展。 |
| 发现 | `RenderPipelineExtensionAttribute`, `RenderFeatureExtensionAttribute` | TypeCache 候选 generation 的稳定身份。 |
| Shader | `ShaderAsset`, `ShaderDefinition`, `ShaderPassDefinition`, `ShaderTechniqueDefinition` | 通用 GPU Program、开放 Contract 与 Role 映射。 |
| 材质 | `MaterialAsset`, `MaterialValue`, `MaterialPropertyBlock`, `MaterialPassResolver` | 稳定属性、Keyword、Metadata 与能力感知 Technique 解析。 |
| 资源 | `TextureAsset`, `GeometryAsset`, `RenderTexture`, `IRenderResourceService`, `IRenderFrameUploadService` | 后端无关资产、持久资源、异步预热与当前帧流式 Buffer。 |
| 目标产物 | `IRenderTargetArtifactProvider`, `RenderTargetArtifactStatus` | 以 `Ready`、`Pending`、`Unavailable`、`Failed` 精确表达无源码 Shader/Texture 目标产物状态。 |
| 诊断 | `IRenderDiagnosticSink`, `RenderDiagnostic` | 发布并在条件恢复后解析当前 Rendering 问题，不把状态诊断伪装成带调用栈的普通 Log。 |
| 全局 | `GraphicsSettings`, `RenderFrameStatistics` | 当前 capability、默认 Pipeline 与只读统计。 |

## Shader → Technique → Material → Pipeline

Shader Pass 只有稳定名称和通用 fixed-function state；状态覆盖 primitive topology、front-face、cull、depth、blend、color mask 与 multisampling，stencil 则保留为 draw/pass 级动态状态。Technique 由渲染提供者声明开放 `ShaderContractId`，并把开放 `ShaderPassRoleId` 映射到具体 Pass。Pipeline 使用自己的协议解析材质：

```csharp
MaterialPassResolution? selection = MaterialPassResolver.Resolve(
    material,
    new ShaderContractId("sample.sprite"),
    new ShaderPassRoleId("sample.draw"),
    context.capabilities);
```

另一个 Plugin 可以使用完全不同的 Contract、Role、Metadata、排序和资源布局，内核不需要修改。

解析不是“内建所有模型再用开关启用”。Resolver 只做四件通用工作：按 Plugin 提供的 Contract 找 Technique、按设备能力过滤、尊重 Material 显式 Technique、按 Plugin 提供的 Role 找 Pass。它不知道 sprite、PBR、shadow 或 post process；这些名字、属性、排序和 pass 组合都由 Pipeline/Plugin 拥有。解析结果会被确定性缓存和验证，不会在每个 draw 上用 CLR 反射猜测语义。

Material/Geometry 是可选帮助层，不是强制执行路径。纹理 `MaterialValue` 同时保存后端中立 `RenderSamplerState`，因此 filter/address mode 不由 Runtime 写死；BGFX 的 texture/sampler 组合绑定不会伪装成不可用的独立 Material Sampler property。Buffer 属于 Pipeline/Pass 显式资源接口，不被 Material helper 隐式拥有。低级 Pipeline 可以通过 `IRenderResourceService.AcquireBuffer`、`AcquireTexture`、`AcquireKtxTexture`、`AcquireGraphicsPipeline` 和 `AcquireComputePipeline` 提交自己的目标二进制与资源描述，再直接使用 `RenderCommandEncoder` 绑定和录制；这些入口仍只返回后端中立 opaque handle。

`ShaderPropertyDefinition.bindingKind` 用 `ShaderPropertyBindingKind` 明确区分 `Uniform`、`SampledTexture`、`StorageTexture` 与 `StorageBuffer`；storage binding 还通过 `RenderStorageAccess` 声明 Read、Write 或 ReadWrite。数值默认推断为 Uniform，纹理默认推断为 SampledTexture，Buffer 默认推断为 StorageBuffer，但资产可以显式声明。Shader IR、Pass-local `ShaderInterface`、编译 artifact 与 Runtime binding descriptor 会保留同一个绑定契约。Material 只拥有 Uniform 与 SampledTexture 值；StorageTexture/StorageBuffer 必须由 Pipeline 在对应 Pass 中显式获取、向 RenderGraph 声明，并通过 `BindStorageTexture`/`BindBuffer` 绑定，避免把帧级 UAV 错误持久化到材质资产。

## Pipeline 示例骨架

```csharp
[RenderPipelineExtension("sample.pipeline")]
public sealed class SamplePipeline : RenderPipeline
{
    public override void Build(RenderPipelineContext context)
    {
        SampleFrameData data = context.request.data.Get<SampleFrameData>(
            new RenderDataChannelId("sample.frame"));
        var phase = new RenderPhaseId("sample.draw");
        context.graph.AddRasterPass("Sample Draw", phase, data,
            static (frame, pass) => frame.Record(pass.commands))
            .UseColorAttachment(context.outputTexture, 0, RenderLoadAction.Clear)
            .HasSideEffect();
    }
}
```

`SampleFrameData` 可以描述 Canvas、tile map、Scene 快照、体素、光线追踪输入或其他任意模型。它只在当前帧和当前 Plugin generation 有效；持久资产仅保存 Stable ID、Persistent ID 与 Inno 序列化属性 bytes。

`RenderPipelineContext.preservePresentationTarget` 是跨模型颜色合成契约。同一 target 的首个成功请求或互不重叠区域可以初始化自己的颜色；后续覆盖相同像素的请求必须 Load/Preserve 已有颜色。它不指定 alpha、2D、3D 或 UI 语义，具体混合状态仍由 Pipeline 决定。请求只有在建图成功后才占据 presentation region，因此一个坏 Plugin 层不会迫使后续健康层读取未初始化输出。

Plugin 的生产入口不依赖 Host Service Locator：

```csharp
[RenderRequestProviderExtension("sample.viewport")]
public sealed class SampleRequestProvider : RenderRequestProvider
{
    public override void Submit(RenderRequestProviderContext context)
    {
        IReadOnlyList<MyWorld> worlds = context.content.GetValues<MyWorld>();
        context.requests.Submit(new RenderRequest(
            "Sample View",
            RenderTarget.backbuffer,
            context.primaryPresentationViewport));
    }
}
```

`RenderContentScope` 由应用组合根在帧边界建立。Rendering Runtime 只调用 Host 提供的中立 callback，因此不引用 Scene；Plugin Provider 只消费 `context.content`，不扫描全局 Scene Manager。`primaryPresentationSize` 表示完整物理表面，`primaryPresentationViewport` 表示实际游戏内容区域；面向 Player backbuffer 的模型应使用后者，才能统一支持 letterbox、pillarbox 与未来的显示适配策略。内容对象不得跨帧或跨 Plugin generation 保留，Provider 必须在提交前把需要的数据复制进 immutable frame snapshot。Host 没有提供内容或 callback 失败时使用空 scope，并产生结构化诊断而不破坏当前帧。

逐帧 Sprite 顶点、粒子或实例数据使用 `context.uploads.UploadBuffer(...)`。它返回 opaque `RenderBufferSlice`，可直接交给 `RenderCommandEncoder.BindVertexBuffer`、`BindIndexBuffer`、`BindInstanceBuffer` 或 Storage `BindBuffer`，不暴露持久 Buffer handle，也不允许跨帧缓存。

长期存在的动态图集、画布或 simulation texture 可通过 `IRenderResourceService.UpdateTexture(texture, region, data)` 原位更新局部矩形，不需要重建资源。通用 GPU→CPU 结果通过 `ReadTextureAsync` 返回不可变 `RenderTextureReadbackResult`；调用取消只停止该等待并安全回收 pending transfer。Readback texture 必须以 `RenderTextureUsage.Readback` 创建，Pipeline 自己决定何时 Copy/Blit 生产结果，因此 API 不内建 Picking、截图或任何领域语义。

## 官方 2D Plugin 如何组合内置 API

引擎本体没有隐藏的 2D Renderer。`Inno.Rendering.2D` 完全以 Plugin 身份组合 Scene、Asset、Settings、Mathematics、Editor Viewport 与本项目公开的 backend-neutral Rendering API：

```text
SceneWorld
  → SceneRenderContent.CreateScope
  → RenderContentScope<GameScene>
  → Rendering2DSceneScope
  → Rendering2DSceneSystem.Capture
  → Rendering2DFrameCollector
  → RenderFrameData
  → RenderRequest
  → Rendering2DPipeline.Build
  → RenderGraph raster pass
  → RenderCommandEncoder
  → IRenderDevice
  → BGFX/Metal、BGFX/D3D 等具体后端
```

`Camera2D`、`SpriteRenderer2D`、`TilemapRenderer2D` 与 `Light2D` 都是普通 `GameBehavior`。它们只保存 Scene 可序列化数据及统一的 `enabled` 生命周期，不接触 BGFX handle、View ID、native pointer 或 Editor service。

像素密度只有一个项目级 owner：`Rendering2DProjectSettings.defaultPixelsPerUnit`。Pixel-perfect Camera 与未显式覆盖密度的 Sprite 都读取该设置；Camera 不再重复保存 PPU。`SpriteRenderer2D.pixelsPerUnit` 仍是合理的资源级覆盖，因为不同图集可能采用不同的 authoring density，它不属于 Camera 投影设置。

`Rendering2DSceneSystem` 是每个 Scene 的 2D extraction owner，而不是 GPU renderer。它持有 Camera、Drawable 与 Light 的结构索引：Scene 对象或 Component 结构没有变化时，所有 Camera 复用同一不可变对象列表对应的索引；Transform、颜色、材质等普通属性仍在构建当前帧快照时读取，所以属性修改不要求重扫 Scene。这个 Scene-owned cache 使 Plugin 不需要每个 Camera、每帧遍历全部 GameObject，也保证 Plugin disable、移除或 generation retirement 时有一个明确位置释放所有 Plugin Component 引用。

`Rendering2DSceneScope` 只收集显式包含 `Rendering2DSceneSystem` 的 Scene，并跳过没有选择 2D 模型的 Scene；同一 Host scope 因而可以并存纯 3D、纯 2D 和混合 Scene。一个 Scene 中出现多个 2D system 仍是所有权错误，会被明确拒绝。系统存在但 `enabled=false` 时，`Capture` 立即清空并返回空 snapshot：Scene View 仍把它视为已安装的 2D authoring model，由自己的 Editor Camera 保留网格、导航和重新启用后的连续编辑位置，但不会提取 Scene 中的 Camera、Sprite、Tilemap 或 Light；Game View contributor 不参与，Player backbuffer 也不提交 2D request。Remove 则表示 Scene 完全退出 2D 模型，Scene View 也不再获得 2D contributor。重新启用后下一次 `Capture` 从当前 Scene 结构重建索引。

`Rendering2DFrameCollector` 读取 scope、Camera 和项目 2D Settings，计算正交 view/projection、camera bounds、layer/culling、Light 快照、Sprite/Tilemap quad、排序键、batch、CPU picking 数据与诊断，并把结果冻结在 Plugin-owned `RenderFrameData` channel 中。Scene View Contributor 使用独立 Editor Camera，并在同一个 frame snapshot 中返回 view/projection 和 picking；Game View Contributor 使用 Scene 的 Base/Overlay Camera stack；Player 的 `Rendering2DRequestProvider` 则直接使用 Host 计算好的 `primaryPresentationViewport` 提交 backbuffer request。2D Editor 层使用稳定 order `1000`，会叠加在未来低 order 的 3D 底层之上；Pipeline 在 `preservePresentationTarget` 为 true 时加载已有 presentation color。

`Rendering2DPipeline.Build` 只消费 immutable frame data。它通过 `IRenderResourceService` 解析开放的 shader contract/material role，通过 `IRenderFrameUploadService` 上传当前帧 vertex/index slices，再用 `RenderGraphBuilder.AddRasterPass` 声明 attachment、load/store、view/projection 和 side effect。真正的资源创建、依赖排序、pass culling、command replay 与 platform backend 都由引擎完成；2D Plugin 从未引用 `Inno.Native.Bgfx`。因此未来 3D、矢量、UI 或自定义渲染 Plugin 可以复用同一底座，却不需要继承或修改 2D 世界观。

## 热重载与失败隔离

- Pipeline/Feature 候选只在帧边界发布，失败保留 last-good generation。
- `IRenderTargetArtifactProvider` 不使用布尔值混合“正在编译”和“部署缺失”。`Pending` 是 Editor 首次异步编译的正常状态，不发布 Error；`Unavailable` 表示当前部署确实没有请求产物；`Failed` 表示生产已失败且 Provider 已发布具体诊断；`Ready` 保证返回值可立即使用。
- Runtime 在 `Pending`/`Failed` 时继续使用 last-good GPU Program 或 Texture；恢复成功后通过 `IRenderDiagnosticSink.Resolve` 清理旧状态，不让已修复问题永久残留在 Console。
- `RenderFrameData`、Graph handle、回调和 `RenderPipelineContext` 不得跨帧缓存。
- 普通艺术参数使用 Material value；只有接口、控制流或状态变化才应成为静态 Keyword 变体。
- Project/Plugin API 中不存在 BGFX 类型。需要的后端能力通过 `GraphicsCapabilities` 查询。
