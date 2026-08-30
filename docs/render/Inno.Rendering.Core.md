# Inno.Rendering.Core

[Rendering 索引](README.md) · [Wiki 首页](../README.md) · [Runtime](Inno.Rendering.Runtime.md)

`Inno.Rendering.Core` 是完全后端中立的可编程 GPU 协议。它只知道资源、能力、Pass 依赖和命令，不知道场景、相机、材质语义或任何具体图形算法。

## 公开 API

| 分类 | API | 说明 |
| --- | --- | --- |
| 能力 | `GraphicsCapabilities`, `GraphicsLimits`, `GraphicsFeature`, `GraphicsBackend` | 当前设备功能、按维度采样/Attachment/Storage read/write 格式、坐标约定与 View 上限。 |
| 开放 ID | `RenderPhaseId`, `RenderResourceId`, `RenderBindingId`, `RenderDataChannelId` | 调用方拥有的稳定协议 ID；内核没有内建阶段或资源名。 |
| Graph 资源 | `RenderTextureHandle`, `RenderBufferHandle`, descriptor、region 与 usage | 仅在所属 graph generation 内有效。 |
| 帧上传 | `RenderBufferUploadDescriptor`, `RenderBufferSlice` | 当前帧动态 Vertex/Index/Storage 数据的 opaque range。 |
| 持久资源 | `PersistentTextureHandle`, `PersistentBufferHandle`, Pipeline handle、`RenderTextureRegion` 与 descriptor | 由设备 generation 拥有，不得写入资产；纹理可局部更新。 |
| 异步回读 | `RenderTextureReadbackHandle`, `RenderTextureReadbackResult` | 后端中立的完整 mip GPU→CPU 传输与轮询结果。 |
| Pass | `RasterPassBuilder`, `ComputePassBuilder`, `CopyPassBuilder` | 显式采样读取、Storage Read/Write/ReadWrite、Attachment、copy、side effect、before/after 与可选并行录制。 |
| 命令 | `RenderCommandEncoder` | Direct/Indexed/Instanced/Indirect/Procedural draw、dispatch、copy/blit、绑定和状态。 |
| 编译 | `RenderGraphBuilder`, `RenderGraphNameScope`, `RenderGraphCompileResult`, `CompiledRenderGraph` | 命名隔离、验证、裁剪、拓扑排序、生命周期和别名分配。 |
| 后端 | `IRenderDevice`, `IRenderGraphBackend`, `RenderPresentationSize`, `RenderDeviceFrameCounters` | 设备与编译图执行边界、主呈现目标物理尺寸，以及当前帧后端实际提交的 draw/dispatch 数。 |

`GraphicsBackend.Metal`、`Direct3D12` 等值只描述“Host 当前选择了哪个图形 API family”。它不是 Metal/DX 原生 API，也不会要求 Pipeline 写两套实现。正常启动由 BGFX 选择平台默认 backend；Host 可通过非脚本的设备启动选项显式偏好另一个 backend。Pipeline 通常只查询 `GraphicsFeature`、format 和 limits，只有确实存在 backend 差异时才读取该 identity。Shader 仍维护同一份 `.sc`/Shader IR；离线编译器根据目标平台与 backend identity 选择 Metal、DX 或 Vulkan profile/产物。Plugin 和项目脚本不参与 BGFX handle、View ID 或原生 shader language 适配。

## 最小工作流

```csharp
var phase = new RenderPhaseId("sample.compose");
RenderTextureHandle output = graph.CreateTexture(
    "Output",
    new RenderTextureDescriptor(
        width,
        height,
        RenderTextureFormat.RGBA8Srgb,
        RenderTextureUsage.ColorAttachment | RenderTextureUsage.Sampled));

graph.AddRasterPass("Compose", phase, passData,
    static (data, context) => data.Record(context.commands))
    .UseColorAttachment(output, 0, RenderLoadAction.Clear);

graph.MarkOutput(output);
RenderGraphCompileResult result = graph.Compile();
```

阶段名、颜色含义和 `passData` 类型全部属于调用方。Graph 验证未初始化读取、discard 后读取、同 Pass hazard、operation-specific usage、格式/子资源、TextureBlit/BufferCopy/Compute 等能力、循环和 View 上限；无消费者且无 side effect 的 Pass 会裁剪，生命周期不重叠且 descriptor 相同的临时资源可别名。被 `MarkOutput` 的资源必须在最终状态包含已存储内容。

`RenderGraphCompileResult.culledPassCount` 返回本次成功编译真正移除的 Pass 数。`IRenderDevice.frameCounters` 返回自当前 `BeginFrame` 起后端实际提交的 draw/dispatch；不支持计数的替代后端可以返回零，但不得伪造命令。

Runtime 会把所有 `RenderRequest` 与 frame-final contributor 合并到同一个 Graph。每个生产者使用 `BeginNameScope`，因此都可自然使用 `Visible`、`Compose` 等局部名称，最终诊断名仍全帧唯一。生产者失败时 `BeginMutationScope` 会同时回滚其 Pass、临时资源和 output 标记，不污染其他请求。

`RenderBufferSlice` 由上层当前帧上传服务创建；公开属性只暴露元素范围和 usage，Backing Buffer 仍为 Core 内部实现。Command Encoder 在绑定时同时验证 usage 与 frame index，旧帧 slice 不会悄悄引用已被复用的动态页。

Pass callback 默认使用 `RenderPassRecordingMode.Serial`。调用 `.AllowParallelRecording()` 后，Core 可以在受控 worker 上把该 callback 录制进隔离的后端中立 command list，再严格按编译后的 Graph 顺序回放给真实后端。该 opt-in 不改变 GPU/View 顺序；调用者必须保证 PassData 和 callback 访问的数据可并发读取。未 opt-in 的 Pass、资源生命周期和所有 backend API thread 规则保持串行。

Compute Pass 必须准确声明资源访问方向：

```csharp
graph.AddComputePass("Process", new RenderPhaseId("sample.process"), data,
        static (value, context) => value.Record(context.commands))
    .ReadStorageTexture(inputImage)
    .WriteStorageTexture(outputImage)
    .ReadStorageBuffer(parameters)
    .ReadWriteStorageBuffer(counters)
    .HasSideEffect();
```

`ReadTexture`/`ReadBuffer` 表示普通 shader read（例如 sampled texture），`ReadStorageTexture`/`ReadStorageBuffer` 表示无序 storage binding。两者不能混用来绕过 usage 与 hazard 验证。`GraphicsCapabilities.SupportsStorage(format, access)` 分别检查 Read、Write 或两者；只支持 image write 的后端不会被误报成支持 read-write。

`RenderTextureDescriptor` 通过 `RenderTextureDimension.Texture2D/Texture3D/Cube` 描述 2D/数组、Volume 与 Cubemap/Cubemap Array；没有 Scene、天空盒或 PBR 语义。`depth` 只属于 Volume，`arrayLayers` 在 Cube 中表示 Cubemap 数量。`GetSubresourceLayerCount(mip)` 返回当前 mip 可寻址的 2D layer、3D Z slice 或扁平化 cube face 数，上传中的 cube face 使用 `cubeLayer * 6 + face`。Graph 与后端共同校验 sampled format、Texture2DArray、Texture3D、TextureCubeArray、尺寸、mip/layer 及 MSAA 约束。

持久纹理更新分成完整 subresource `UpdateTexture` 与矩形 `UpdateTextureRegion`。Region 明确携带 mip、x/y/z、width/height/depth；数据必须紧密打包且字节数精确匹配格式。异步回读通过 `BeginTextureReadback`、`TryGetTextureReadback`、`CancelTextureReadback` 表达，纹理必须显式声明 `RenderTextureUsage.Readback` 且为单采样传输资源。渲染目标或 storage 结果应先由 Plugin 通过 Graph Copy/Blit 写入 readback texture，避免 Core 暗中插入同步或格式转换。

`RenderVertexSemantic` 覆盖 Position/Normal/Tangent/Bitangent、四组 Color、八组 Texture Coordinate 与 Skinning 通道。`RenderVertexAttribute.byteOffset` 与 `RenderVertexLayout.stride` 可显式描述 attribute 间隙、对齐和尾部 padding；省略 offset/stride 时才自动紧密排列。布局的等价性与缓存 key 同时包含 resolved attribute 和 stride，尾部 padding 不会被错误合并。可移植格式覆盖 float、half、normalized/integer byte、normalized/integer short 与 10:10:10:2。`Draw` 明确要求已绑定 Vertex Buffer；无 Vertex Buffer 的提交必须显式调用 `DrawProcedural`，不能静默改变命令语义。Indirect Draw 会提交当前 Vertex/Index binding；无 Vertex Buffer 时同样按 Procedural Draw capability 验证。Half、10:10:10:2、UInt32 Index、Instancing、Procedural Draw 与 Alpha-to-Coverage 都会先检查独立 capability。

## 生命周期与错误

- Builder、PassData、callback 和 graph handle 只存活于当前帧。
- 跨帧资源必须通过持久 handle 显式 Import；资产对象永远不保存 GPU handle。
- `CompiledRenderGraph.Execute` 在异常时完整结束已开始的 Pass 与 Graph。
- 能力不足必须返回结构化诊断或由 Plugin 明确降级，不能静默生成部分图。
- `IRenderDevice.EndFrame` 由 Runtime 每帧只调用一次。
