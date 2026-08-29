# Inno.Rendering

[Rendering 索引](README.md) · [后端中立核心](Inno.Rendering.Core.md) · [Runtime](Inno.Rendering.Runtime.md) · [ShaderGraph](Inno.Rendering.ShaderGraph.md)

`Inno.Rendering` 是 Project/Plugin 脚本面对的通用渲染 API。它不引用 Scene，也不定义 Camera、Light、MeshRenderer、PBR 参数、Render Queue 或固定 Pass Tag。

## 公开契约

| 分类 | API | 语义 |
| --- | --- | --- |
| 请求 | `RenderRequest`, `RenderTarget`, `RenderViewport`, `RenderFrameData` | 将目标、尺寸、可选 Pipeline 与 Plugin 自有帧数据提交给 Runtime。 |
| 请求生产 | `RenderRequestProvider`, `RenderRequestProviderContext`, `RenderRequestProviderExtensionAttribute` | Plugin 每帧自动产生请求的 reload-safe TypeRegistry 扩展入口。 |
| Pipeline | `RenderPipelineAsset`, `RenderPipeline`, `RenderPipelineContext` | Stable Type ID + 原生配置状态，以及每请求建图入口。 |
| Feature | `RenderPipelineFeature`, `RenderFeatureContext`, `RenderFeatureConfiguration` | 有序、可重载的额外建图扩展。 |
| 发现 | `RenderPipelineExtensionAttribute`, `RenderFeatureExtensionAttribute` | TypeCache 候选 generation 的稳定身份。 |
| Shader | `ShaderAsset`, `ShaderDefinition`, `ShaderPassDefinition`, `ShaderTechniqueDefinition` | 通用 GPU Program、开放 Contract 与 Role 映射。 |
| 材质 | `MaterialAsset`, `MaterialValue`, `MaterialPropertyBlock`, `MaterialPassResolver` | 稳定属性、Keyword、Metadata 与能力感知 Technique 解析。 |
| 资源 | `TextureAsset`, `GeometryAsset`, `RenderTexture`, `IRenderResourceService`, `IRenderFrameUploadService` | 后端无关资产、持久资源、异步预热与当前帧流式 Buffer。 |
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

Plugin 的生产入口不依赖 Host Service Locator：

```csharp
[RenderRequestProviderExtension("sample.viewport")]
public sealed class SampleRequestProvider : RenderRequestProvider
{
    public override void Submit(RenderRequestProviderContext context)
    {
        context.requests.Submit(new RenderRequest(
            "Sample View",
            RenderTarget.backbuffer,
            new RenderViewport(0, 0, 1280, 720)));
    }
}
```

逐帧 Sprite 顶点、粒子或实例数据使用 `context.uploads.UploadBuffer(...)`。它返回 opaque `RenderBufferSlice`，可直接交给 `RenderCommandEncoder.BindVertexBuffer`、`BindIndexBuffer`、`BindInstanceBuffer` 或 Storage `BindBuffer`，不暴露持久 Buffer handle，也不允许跨帧缓存。

长期存在的动态图集、画布或 simulation texture 可通过 `IRenderResourceService.UpdateTexture(texture, region, data)` 原位更新局部矩形，不需要重建资源。通用 GPU→CPU 结果通过 `ReadTextureAsync` 返回不可变 `RenderTextureReadbackResult`；调用取消只停止该等待并安全回收 pending transfer。Readback texture 必须以 `RenderTextureUsage.Readback` 创建，Pipeline 自己决定何时 Copy/Blit 生产结果，因此 API 不内建 Picking、截图或任何领域语义。

## 热重载与失败隔离

- Pipeline/Feature 候选只在帧边界发布，失败保留 last-good generation。
- `RenderFrameData`、Graph handle、回调和 `RenderPipelineContext` 不得跨帧缓存。
- 普通艺术参数使用 Material value；只有接口、控制流或状态变化才应成为静态 Keyword 变体。
- Project/Plugin API 中不存在 BGFX 类型。需要的后端能力通过 `GraphicsCapabilities` 查询。
