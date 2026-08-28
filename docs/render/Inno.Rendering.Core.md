# Inno.Rendering.Core

[Rendering 索引](README.md) · [Wiki 首页](../README.md) · [上一页：Core Graphs](../core/Inno.Core.Graphs.md)

`Inno.Rendering.Core` 定义后端中立设备能力、资源描述、帧内 Handle、RenderGraph 和命令编码契约。它是 Pipeline、Feature、自定义 Pass 与 BGFX 后端之间唯一允许共享的底层协议。

## 依赖与初始化

项目没有运行时初始化 Manager，只私有引用 `Inno.Core.Scripting` 生成逻辑 namespace `InnoEngine.Rendering`。实际设备在上层 Rendering Layer 的帧安全点创建，并把不可变 `GraphicsCapabilities` 交给每帧 `RenderGraphBuilder`。

## 公开 API

| 分类 | API | 说明 |
| --- | --- | --- |
| 能力 | `GraphicsCapabilities`, `GraphicsLimits`, `GraphicsFeature` | 查询 Compute、Storage、格式、View 上限和坐标约定。 |
| 资源 | `RenderTextureDescriptor`, `RenderBufferDescriptor`, `RenderTextureContainer` | 显式声明尺寸、格式、用途、采样与容量；KTX 是当前便携目标容器。 |
| Handle | `RenderTextureHandle`, `RenderBufferHandle` | 仅在所属 graph generation 有效。 |
| 阶段 | `RenderPhaseId`, `BuiltinRenderPhases` | 开放 Stable ID 与 before/after 拓扑约束。 |
| Pass | `RasterPassBuilder`, `ComputePassBuilder`, `CopyPassBuilder` | 声明读写、Attachment mip/array layer、UAV、side effect 和顺序；Raster/Compute 都可显式设置 view/projection。 |
| 编译 | `RenderGraphCompileResult`, `CompiledRenderGraph` | 验证、裁剪、拓扑排序、别名分配和可执行结果。 |
| 后端 | `IRenderGraphBackend`, `RenderCommandEncoder` | 后端实现 View/Encoder；Project 脚本不接触原生 handle。 |

## 常见工作流

```csharp
RenderTextureHandle hdr = graph.CreateTexture(
    "Camera HDR",
    new RenderTextureDescriptor(
        width,
        height,
        RenderTextureFormat.RGBA16Float,
        RenderTextureUsage.ColorAttachment | RenderTextureUsage.Sampled));

graph.AddRasterPass("Opaque", BuiltinRenderPhases.opaque, drawList,
    static (list, context) => list.Record(context.commands))
    .UseColorAttachment(hdr, 0, RenderLoadAction.Clear);

graph.MarkOutput(hdr);
RenderGraphCompileResult result = graph.Compile();
```

Graph 编译会拒绝未初始化读取、同 Pass 冲突、Attachment 用途/尺寸/子资源错误、能力不满足、循环和 View 超限。没有输出消费且无 side effect 的 Pass 会被裁剪；生命周期不重叠且 descriptor 完全相同的临时资源可共享 physical slot。Texture array 的单层 Attachment 让 CSM、cube face 和分层预览不需要泄漏后端 framebuffer API。

## 生命周期与失败语义

- Builder、PassData、执行回调和 graph handle 只属于当前帧 generation。
- 跨 Camera、历史纹理和 Swapchain 必须以 opaque persistent handle 显式 Import。
- `IRenderDevice.CreateTexture(RenderTextureContainer, ...)` 在帧安全点接收已验证 KTX；资产和公开材质仍不保存设备 handle。
- `CompiledRenderGraph.Execute` 保证已开始 Pass 和 Graph 的完整 unwind；录制与清理同时失败时抛 `AggregateException`。
- 编译失败只返回结构化诊断，不执行部分 graph。上层 Pipeline 应保留 last-good Pipeline/GPU 状态。
- `IRenderGraphBackend.EndGraph` 不负责额外 present；全帧唯一提交由 Rendering Layer 的 `OnAfterRender` 协调。

当前 API 没有 legacy RenderGraph、schema version 或兼容层。
