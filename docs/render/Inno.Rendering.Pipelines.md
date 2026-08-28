# Inno.Rendering.Pipelines

[返回 Rendering 索引](README.md) · [Wiki 首页](../README.md) · [上一页：Rendering Assets](Inno.Rendering.Assets.md) · [下一页：BGFX 后端](Inno.Rendering.Bgfx.md)

## 职责与边界

`Inno.Rendering.Pipelines` 实现内置 `UniversalRenderPipeline`，负责把 `RenderWorldSnapshot`、Camera 请求、质量配置和设备 capability 编译成显式 RenderGraph。它只依赖 `Inno.Rendering` 与 `Inno.Rendering.Core`，不知道 BGFX、Native、Editor、Importer 或 ShaderGraph。

当前稳定实现同时生成 Forward+ 与 Deferred 图，两条路径共享场景快照、材质 Pass tag、方向光阴影、天空、透明队列和后处理。具体 Shader program、GPU mesh cache 与 draw/dispatch 提交由 `IRenderPipelineExecutor` 完成，因此 Pipeline 本身不持有 GPU handle，也不会固定脚本 ALC。

## 初始化与执行顺序

1. 帧开始时由宿主创建唯一 `RenderWorldSnapshot`。
2. 每个 Camera/Editor View 创建独立 `RenderRequest` 与 `BuiltinRenderResources`。
3. `UniversalRenderPipeline.Build` 先裁剪/排序，再声明 CSM、cluster、主光照、透明与后处理 Pass。
4. Feature 在同一 Builder 上添加自己的依赖和阶段约束。
5. RenderGraph 编译后，由设备执行层把 `RenderPipelineOperation` 转换为缓存 Shader、资源绑定和 `RenderCommandEncoder` 调用。

管线只调用 operation executor，不直接 present；所有 View 完成后仍由帧宿主统一执行一次 `IRenderDevice.EndFrame()`。

## 公开 API

| API | 说明 |
| --- | --- |
| `UniversalRenderPipeline` | Stable ID 为 `inno.pipeline.universal` 的内置双路径 Pipeline。 |
| `UniversalRenderPipeline.ResolvePath` | 根据 capability 把不可执行的 Deferred 安全降级为 Forward+。 |
| `UniversalRenderPipeline.SupportsDeferred` | 检查 MRT、语义 GBuffer 格式和 Depth 能力。 |
| `BuiltinPipelineOperations` | Built-in executor 使用的稳定 Scene/Fullscreen/Compute operation ID。 |
| `RenderingLayer.TryActivatePipelineAsset` | 仅在帧边界构建完整 Pipeline/Feature candidate，成功后原子切换，失败保留 last-good generation。 |

## Forward+ 图

- 有 Compute + StorageBuffer 且至少两个 Compute binding、并且存在局部光时，生成 16×16 screen tile × 24 个对数 depth slice 的 cluster grid 与 light-index buffer；Compute 以 view-space 深度和投影光球对每个 cluster 做保守相交测试。
- 能力不足时不生成非法 Compute Pass，改由 executor 使用 CPU light list，并发布 `RENDER_PIPELINE_FORWARD_CPU_LIGHTS`。
- Cluster 可用时 Opaque/Transparent 请求开放 tag `ForwardLitClustered`，ShaderGraph PBR 从当前像素的 cluster list 读取局部光；手写 Shader 若只实现 `ForwardLit`，executor 会自动选择该经典 Pass。能力不足时同一 Shader artifact 直接使用 `ForwardLit`，不要求 Storage Buffer。
- Scene Color 保持线性；HDR 优先 `RGBA16Float`，其次 `RG11B10Float`，最后明确诊断并降至 `RGBA8`。

## Deferred 图

GBuffer 语义固定而物理格式 capability-aware：

| Attachment | 语义 | 当前格式选择 |
| --- | --- | --- |
| GBuffer0 | BaseColor / Metallic | `RGBA8` |
| GBuffer1 | Normal / Roughness | `RGB10A2`，否则 `RGBA8` |
| GBuffer2 | Emissive / AO | `RGBA16Float`，否则 `RGBA8` |
| Depth | Depth / Stencil | `Depth24Stencil8`，否则 `Depth32Float` |

Deferred 不满足三个 color attachment 或 depth 能力时，当前 View 自动构建 Forward+ 图并发布 `RENDER_PIPELINE_DEFERRED_FALLBACK`，不会生成部分 GBuffer 或黑帧。

## 阴影、天空与后处理

- 首个启用阴影的 Directional Light 使用 1–4 层 texture array；每个 cascade 是独立 RenderGraph Pass 和独立 array-layer Attachment。
- `CameraClearMode.Sky` 添加 Sky fullscreen operation；其他模式保留明确的 color clear 行为。
- Bloom 通过半分辨率 downsample/upsample 资源链完成；关闭 Bloom 时相关资源与 Pass 不存在。
- Tone Mapping 始终是最终 operation，携带 exposure；Bloom 开启时使用独立 `ToneMapBloom` program 合成过滤结果，关闭时不创建或绑定 Bloom 资源。Backbuffer 以 side effect 保活，离屏目标通过 imported `cameraTarget` Attachment 保活。

## 错误、生命周期与热重载

- RenderWorld、Culling Results、Operation、Graph handle 和回调都只属于当前帧。
- 排序使用 queue、相机距离和 persistent component ID，结果在相同输入下稳定。
- Pipeline 不保存 CLR `Type`、脚本 delegate 或 BGFX handle；reload candidate 可以在帧边界原子替换。
- Shader/Program、Mesh Buffer、draw/dispatch/copy、材质 uniform/texture、方向光 CSM 与局部光绑定均由 `DefaultRenderPipelineExecutor` 在帧安全点准备并通过中立 Encoder 提交。

## 相邻页面

- [Inno.Rendering](Inno.Rendering.md)：Scene 组件、RenderWorld 与 Pipeline 扩展契约。
- [Inno.Rendering.Core](Inno.Rendering.Core.md)：RenderGraph 验证、资源与命令边界。
- [Inno.Rendering.Assets](Inno.Rendering.Assets.md)：统一 Shader IR 和目标平台产物。
- [Inno.Rendering.Bgfx](Inno.Rendering.Bgfx.md)：BGFX View、Framebuffer 与帧提交。
