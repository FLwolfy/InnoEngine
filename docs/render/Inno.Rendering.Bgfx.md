# Inno.Rendering.Bgfx

[返回 Rendering 索引](README.md) · [Wiki 首页](../README.md) · [艺术家 API](Inno.Rendering.md) · [后端中立核心](Inno.Rendering.Core.md)

## 职责与边界

`Inno.Rendering.Bgfx` 是唯一允许引用 `Inno.Native.Bgfx` 与 `Inno.Native.Bgfx.Tools` 的托管渲染程序集。它把 `IRenderDevice`、Compiled RenderGraph、资源描述和 `RenderCommandEncoder` 映射为 BGFX 设备、View、Encoder、Framebuffer 和延迟销毁队列，并拥有 shaderc/texturec 的平台 profile 与进程调用。

其公开 API 仍保持后端中立：`BgfxDeviceOptions` 接受 `GraphicsBackend` 与 `PlatformWindow`，`BgfxDevice` 返回 `GraphicsCapabilities`、帧号和 Core 层 opaque handle。原生 handle 只存在于程序集内部。

## 初始化顺序

1. 在 BGFX API thread 创建 `BgfxDevice`；进程中同时只能存在一个实例。
2. 每帧调用 `BeginFrame`，处理 resize 与到期资源释放。
3. 编译一个或多个 RenderGraph 后调用 `Execute`。
4. 所有 Encoder 结束后调用一次 `EndFrame`；该方法是唯一的 `bgfx.frame` 提交点。
5. 在相同 API thread 调用 `Dispose`，释放存活资源并执行 `bgfx.shutdown`。

Noop 测试可启用 `forceSingleThreaded`。BGFX 的该模式是进程级一次性配置，同一进程 shutdown 后不能创建第二个单线程设备；实现会明确抛出异常，避免原生 fatal 或挂起。生产窗口后端不应开启此测试选项。

## 公开 API

| API | 语义 |
| --- | --- |
| `BgfxDeviceOptions` | 后端偏好、窗口、backbuffer、VSync/sRGB、延迟销毁帧数和 Noop 单线程测试设置 |
| `BgfxDevice` | BGFX 设备所有权、能力映射、帧边界、RenderGraph 执行、帧命令计数、KTX/普通纹理、Buffer、Program 与延迟销毁 |
| `BgfxShadercToolchain`, `BgfxShaderTargetPlatform` | 将通用 Shader IR 编译到 D3D/Metal/Vulkan/OpenGL profile；同一 `.sc` 跨目标复用 |
| `BgfxTextureTargetCompiler` | 使用 texturec 生成 Runtime 可上传的 KTX 目标产物 |

`BgfxCapabilityMapper` 与 `BgfxCommandEncoder` 是内部实现，不属于稳定脚本契约。

Metal、D3D、Vulkan 等 BGFX renderer 不要求分别维护业务 Shader：同一 Shader IR/`.sc` 由 `BgfxShadercToolchain` 根据目标 profile 生成不同 artifact。这里的“公开底层 API”指后端中立的 Buffer、Texture、Pipeline、Draw/Dispatch 命令，不是把 BGFX handle 或 Metal API 暴露给 Plugin。

若未来完全替换 BGFX，应新增另一个 `IRenderDevice`/`IRenderGraphBackend`、资源映射和 Shader compiler backend；RenderGraph、Material、Pipeline、Plugin 与 Scene 语义无需修改。需要诚实区分的一点是：手写 `.sc` 是 BGFX shaderc 方言，虽然能跨 BGFX 的 Metal/D3D/Vulkan renderer，但新的非 BGFX 后端仍需提供 `.sc` 转换层，或要求手写 Shader 也先进入更高层 Shader IR。该成本被限制在编译/后端程序集，不会扩散到用户 Pipeline API。

## 当前实现行为

- 编译后的逻辑 Pass 按拓扑顺序映射到 BGFX View，并使用 `set_view_order` 固定执行顺序。
- Raster attachment 在 Pass 开始时组成临时 Framebuffer；离开 Graph 后进入延迟销毁队列。
- `BeginFrame` 会先重置上一帧实际使用过的 BGFX View，再处理到期销毁；直接 shutdown 也执行同一重置，从而解除 View 对 framebuffer/program 的跨帧引用。
- 阶段 Shader 在 Program 创建成功后立即把唯一剩余引用交给 Program；Pipeline 生命周期只延迟销毁 Program，由 BGFX 按后端安全顺序释放关联 Shader，避免并行维护第二套阶段资源所有权。
- 同一物理别名槽只创建一个 transient texture；跨帧纹理必须通过 `PersistentTextureHandle` 导入。
- 所有 BGFX 字符串 API 使用显式 UTF-8 字节长度，避免绑定层把负长度解释成超大拷贝。
- Noop 后端不执行无意义的 backbuffer reset，但仍更新逻辑尺寸并推进 frame。
- `CopyTexture`/`BlitTexture`、2D/3D/Cube texture、Program、Vertex/Index/Storage Buffer、Storage Texture、普通/Indexed/Instanced/Indirect/Procedural Draw、Dispatch、uniform 与 KTX texture container 已映射；所有外部 API 仍只使用 Core opaque handle。
- Storage Texture 通过 `encoder_set_image` 绑定。BGFX 的 `TextureImageRead`/`TextureImageWrite` 格式位分别映射为 Core 的 access-specific capability，`RenderStorageAccess.ReadWrite` 要求两者同时成立；graphics program 明确拒绝 BGFX 无法表达的 storage binding，compute program 才接受 StorageTexture/StorageBuffer slot。
- BGFX capability 会映射 sampled format、2D/Cube Array、3D、StorageTexture、UInt32 Index、Instancing、VertexID、Half/10:10:10:2 vertex、Alpha-to-Coverage、SwapChain 等中立 feature；Core Graph 和直接资源/命令入口都会拒绝不支持组合，Plugin 可以根据同一 snapshot 明确降级。
- `Draw` 不会在缺少 Vertex Buffer 时隐式转成 procedural；调用方必须使用 `DrawProcedural`。Indirect Draw 会先提交当前 Vertex/Index range，无 Vertex Buffer 时要求 ProceduralDraw capability。
- 每次直接或间接 draw/dispatch 成功交给 BGFX Encoder 后更新 `frameCounters`；`BeginFrame` 原子清零，因此 Runtime 读取的是本帧真实提交量。
- shaderc/profile 和 texturec 不再位于通用 Assets 或 Runtime；Editor 显式注入 BGFX 实现，Player 可换成预编译 artifact provider。

## 失败与资源安全

- 非 API thread、未开启帧、嵌套 Graph/Encoder、跨 generation handle 和 frame 前未结束 Encoder 都会抛出明确异常。
- GPU 资源不会在 finalizer 或 Asset 回调线程销毁；销毁请求按 BGFX 帧号延迟处理。

双平台 Rendering CI 固定验证 Windows x64 与 macOS arm64 runner 架构，并在真实 Editor 冒烟日志中断言 D3D11/D3D12 或 Metal 后端、约定帧数和完整关闭；原生 BGFX 输出与 Editor boot log 会一同作为诊断 artifact 上传。
- BGFX Debug Metal 后端当前可能在正常 shutdown 时输出 `RefCount` 警告；其上游问题 [bkaradzic/bgfx#3642](https://github.com/bkaradzic/bgfx/issues/3642) 仍处于开放状态。CI 不把该上游固定引用计数诊断误判为 Inno 资源泄漏，但仍要求进程成功、无 BGFX Fatal、无 Host teardown failure 且出现 `BGFX Shutdown complete`。
- Graph 执行异常仍由 Core 的 complete-unwind 契约依次结束 Encoder 与 Graph。

## 相邻页面

- [Inno.Rendering.Core](Inno.Rendering.Core.md)：设备接口、资源描述和 RenderGraph 编译规则。
- [Inno.Rendering](Inno.Rendering.md)：脚本、艺术家与 Pipeline 扩展 API。
