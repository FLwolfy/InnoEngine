# Inno.Rendering

[返回 Rendering 索引](README.md) · [Wiki 首页](../README.md) · [后端中立核心](Inno.Rendering.Core.md) · [内置 Pipelines](Inno.Rendering.Pipelines.md) · [BGFX 后端](Inno.Rendering.Bgfx.md)

## 职责与边界

`Inno.Rendering` 是艺术家、游戏脚本和 Pipeline 作者面对的运行时契约。它提供 Camera、Light、MeshRenderer、Shader、Material、RenderTexture、RenderRequest 和可组合 Pipeline/Feature API，但不暴露 BGFX handle、View ID、原生指针或 BGFX 枚举。

该程序集单向依赖 Scene、Assets Core、Mathematics 与 `Inno.Rendering.Core`。Scene 不反向引用 Rendering，因此渲染组件以普通 `GameComponent` 扩展 Scene。ShaderGraph 和具体 Pipeline 将依赖本项目，本项目不会反向依赖它们。

## 初始化与生命周期

宿主先创建 `IRenderDevice`，再从活动 `RenderPipelineAsset` 的 Stable Type ID 解析 Pipeline 扩展。每个 Camera 或 Editor 视口产生一个 `RenderRequest`；Pipeline 在当前帧的 `RenderGraphBuilder` 上声明资源与 Pass。Feature 只保存中立配置，每帧创建回调，不能把运行时委托、CLR `Type` 或 GPU 对象写入资产。

设备宿主在帧安全点更新 `GraphicsSettings` 的内部状态，脚本通过 `GraphicsSettings.capabilities`、`pipelineAsset` 和 `frameStatistics` 读取只读快照。切换 Pipeline 后应在帧边界原子替换并释放旧实例。

## 公开 API

| API | 语义 |
| --- | --- |
| `GraphicsSettings`, `RenderFrameStatistics` | 当前能力、活动 Pipeline 与只读帧统计 |
| `RenderPipelineAsset`, `RenderQualitySettings`, `RenderFeatureConfiguration` | Pipeline Stable Type ID、默认路径、质量和有序 Feature 配置 |
| `RenderPipeline`, `RenderPipelineFeature` | 每视图建图入口与可组合功能扩展基类 |
| `RenderPipelineExtensionAttribute`, `RenderFeatureExtensionAttribute` | TypeRegistry 使用的稳定扩展身份 |
| `RenderPipelineContext`, `RenderFeatureContext`, `BuiltinRenderResources` | 当前帧/视图上下文、诊断与类型化标准资源 |
| `RenderWorldSnapshot`, `RenderCullingResults`, `RenderObjectData`, `RenderLightData` | 帧级 Scene 快照、视锥裁剪与确定性 Opaque/Transparent 排序 |
| `RenderPipelineOperation`, `RenderTextureBinding`, `RenderBufferBinding`, `RenderUniformBinding` | 用稳定绑定名提交 Scene/Fullscreen/Compute 资源与类型化 uniform，不暴露后端 handle |
| `RenderView`, `RenderRequest`, `RenderTarget`, `RenderTexture` | 相机参数、渲染请求、可选 selected object ID 和后端中立输出目标 |
| `RenderPicking` | 稳定对象 ID 的 GPU RGBA 编码，以及 Scene View 无回读 bounds-ray 选择回退 |
| `Camera`, `DirectionalLight`, `PointLight`, `SpotLight`, `MeshRenderer` | Scene 渲染组件；Camera 可覆盖 Pipeline 默认 Render Path |
| `ShaderAsset`, `ShaderDefinition`, `ShaderPassDefinition` | 可派生 Shader 资产、Pass tag/state/source 定义 |
| `BuiltinShaderPassTags`, `BuiltinShaderMetadataTags` | 开放 Pass 协议常量；`PipelineOperation` metadata 把 Shader Pass 注册到稳定 Operation ID |
| `ShaderPropertyDefinition`, `ShaderKeywordDefinition`, `ShaderPropertyId` | 稳定属性与静态变体契约 |
| `MaterialAsset`, `MaterialValue`, `MaterialPropertyBlock` | 持久材质值和逐绘制临时覆盖 |
| `TextureAsset`, `MeshAsset` | 不持有 GPU handle 的资源资产对象；Mesh 同时保存导入后的对象空间 bounds |

## 常见工作流

```csharp
using Inno.Rendering;
using Inno.Rendering.Core;

[RenderFeatureExtension("sample.outline")]
public sealed class OutlineFeature : RenderPipelineFeature
{
    /// <inheritdoc />
    public override void AddRenderPasses(RenderFeatureContext context)
    {
        context.graph
            .AddRasterPass("Outline", BuiltinRenderPhases.postProcessing, 0, static (_, _) => { })
            .UseColorAttachment(
                context.resources.sceneColor,
                0,
                RenderLoadAction.Load)
            .ReadTexture(context.resources.sceneDepth)
            .HasSideEffect();
    }
}
```

Feature 应以稳定阶段、`before`/`after` 和资源依赖表达顺序。不要保存 `RenderGraphBuilder`、generation-scoped handle 或当前帧回调。

Fullscreen/Compute Feature 的 `.ishader` Pass 可声明 `"tags": { "PipelineOperation": "project.effect" }`。Editor 只在完整目标 artifact 编译成功后安装该 ID；Feature 通过 `IRenderPipelineExecutor` 准备并执行 operation，失败时旧 operation program 继续可用。

## 错误、热重载与注意事项

- `RenderRequest` 会拒绝无效尺寸、近远裁剪面和未初始化矩阵。
- `RenderRequest.selectedObjectId` 只传递稳定 renderer identity；Feature 可据此绘制选中轮廓，不保存 Scene 对象或 GPU handle。
- Pipeline/Feature 候选失败时，宿主应保留 last-good Registry 与 Pipeline；成功候选只在帧边界切换。
- Material 持久化使用 `ShaderPropertyId` 的字符串值；运行时哈希不能写回资产。
- `MeshRenderer` 的有序材质槽通过隐藏的中立序列化属性进入 Scene/Prefab；Inspector 和脚本仍使用只读 `materials` 与显式 `SetMaterial(s)`，不会暴露持久化实现。
- Camera、Light 和 MeshRenderer 不直接创建 GPU 资源，资产 generation 切换不会固定旧脚本 ALC。
- Shader 编译、Importer 与 CPU artifact 已由 [Inno.Rendering.Assets](Inno.Rendering.Assets.md) 落地；Forward+/Deferred 图由 [Inno.Rendering.Pipelines](Inno.Rendering.Pipelines.md) 实现。

## 相邻页面

- [Inno.Rendering.Core](Inno.Rendering.Core.md)：RenderGraph 与命令边界。
- [Inno.Rendering.Assets](Inno.Rendering.Assets.md)：统一 Shader IR、Importer 与离线产物。
- [Inno.Rendering.Pipelines](Inno.Rendering.Pipelines.md)：RenderWorld、Forward+/Deferred 与后处理图。
- [Inno.Rendering.Bgfx](Inno.Rendering.Bgfx.md)：唯一 BGFX 适配层。
- [Inno.Engine.Scene](../engine/Inno.Engine.Scene.md)：GameComponent 和 Scene 生命周期。
