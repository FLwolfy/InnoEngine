# Inno.Rendering.Bgfx

[返回 Rendering 索引](README.md) · [Wiki 首页](../README.md) · [后端中立 API](Inno.Rendering.md)

## 职责与边界

`Inno.Rendering.Bgfx` 是唯一允许引用 `Inno.Native.Bgfx` 的运行时托管渲染程序集。它把 `IRenderDevice`、Compiled RenderGraph、资源描述和 `RenderCommandEncoder` 映射为 BGFX 设备、View、Encoder、Framebuffer 和延迟销毁队列。shaderc/texturec 只存在于 Build Toolchain，不进入运行时 Adapter 或 Player。

其公开 API 仍保持后端中立：`BgfxDeviceOptions` 接受 `GraphicsBackend` 与 `IPlatformWindow`，`BgfxDevice` 返回 `GraphicsCapabilities`、帧号和 Rendering 层 opaque handle。原生 handle 只存在于程序集内部。

## 初始化顺序

1. 在 BGFX API thread 创建 `BgfxDevice`；进程中同时只能存在一个实例。
   传入 `IPlatformWindow` 时，初始 backbuffer 直接使用其物理 `pixelWidth`/`pixelHeight`，避免 HiDPI 窗口在首帧进行一次逻辑尺寸到 drawable 尺寸的重复 reset。
2. 每帧调用 `BeginFrame`，处理 resize 与到期资源释放。
3. 编译一个或多个 RenderGraph 后调用 `Execute`。
4. 所有 Encoder 结束后调用一次 `EndFrame`；该方法是唯一的 `bgfx.frame` 提交点。
5. 在相同 API thread 调用 `Dispose`，释放存活资源并执行 `bgfx.shutdown`。

`BgfxDevice` 通过进程设备 lease 明确表达 BGFX 的真实平台约束：一个进程同一时间只能拥有一个
活动图形设备。第二次并发创建会在进入 native API 前失败；前一个设备完成 shutdown 后可以创建
下一个普通设备。Dear ImGui 的 viewport/window/renderer 路由则按 `ImGuiContext` 分区，不再依赖
一个可被后创建 Host 覆盖的全局 backend map。因此当前能力是“多个 Runtime/Host 可隔离，但同一
进程只允许一个活动图形 Host”，而不是虚假声明同进程多 GPU device 支持。

Noop 测试可启用 `forceSingleThreaded`。BGFX 的该模式是进程级一次性配置，同一进程 shutdown 后不能创建第二个单线程设备；实现会明确抛出异常，避免原生 fatal 或挂起。生产窗口后端不应开启此测试选项。

## 公开 API

| API | 语义 |
| --- | --- |
| `BgfxDeviceOptions` | 后端偏好、窗口、backbuffer、VSync/sRGB、延迟销毁帧数和 Noop 单线程测试设置 |
| `BgfxDevice` | BGFX 设备所有权、能力映射、帧边界、RenderGraph 执行、帧命令计数、KTX/普通纹理、Buffer、Program 与延迟销毁 |

Shader 与纹理目标产物分别由 `Inno.Build.Toolchains.Bgfx` 和 `Inno.Build.Toolchains.Bgfx.Tools` 生成；这里不公开编译工具链 API。

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
- `CopyTexture`/`BlitTexture`、2D/3D/Cube texture、完整/局部 texture update、异步 texture readback、Program、Vertex/Index/Storage Buffer、Storage Texture、普通/Indexed/Instanced/Indirect/Procedural Draw、Dispatch、uniform 与 KTX texture container 已映射；所有外部 API 仍只使用 Rendering opaque handle。
- Storage Texture 通过 `encoder_set_image` 绑定。BGFX 的 `TextureImageRead`/`TextureImageWrite` 格式位分别映射为 Core 的 access-specific capability，`RenderStorageAccess.ReadWrite` 要求两者同时成立；graphics program 明确拒绝 BGFX 无法表达的 storage binding，compute program 才接受 StorageTexture/StorageBuffer slot。
- BGFX capability 会映射 sampled format、2D/Cube Array、3D、StorageTexture、UInt32 Index、Instancing、VertexID、Half/10:10:10:2 vertex、Alpha-to-Coverage、SwapChain 等中立 feature；通用 RenderGraph 和直接资源/命令入口都会拒绝不支持组合，Plugin 可以根据同一 snapshot 明确降级。
- BGFX `TextureReadBack` 映射为 `GraphicsFeature.TextureReadback`。Readback 资源使用 BGFX transfer flags，`read_texture` 返回的目标 frame 到达前由后端持有 unmanaged buffer；完成或取消后在 API thread 安全释放。当前契约读取完整 mip，且拒绝 multisample/attachment/storage 混用；调用方先显式 Copy/Blit 到 readback texture。
- `UpdateTextureRegion` 分别映射 2D、3D 与 Cube update API，并在进入 native call 前校验 mip texel bounds、层/face 与精确 byte count；持久 handle 和设备 generation 保持不变。
- `Draw` 不会在缺少 Vertex Buffer 时隐式转成 procedural；调用方必须使用 `DrawProcedural`。Indirect Draw 会先提交当前 Vertex/Index range，无 Vertex Buffer 时要求 ProceduralDraw capability。
- 每次直接或间接 draw/dispatch 成功交给 BGFX Encoder 后更新 `frameCounters`；`BeginFrame` 原子清零，因此 Runtime 读取的是本帧真实提交量。
- shaderc/profile 和 texturec 不位于通用 Assets、Runtime 或 Player；Build 在导出时生成目标产物，Player 只消费已经冻结的 KTX 与 Shader 二进制。

## 失败与资源安全

- 非 API thread、未开启帧、嵌套 Graph/Encoder、跨 generation handle 和 frame 前未结束 Encoder 都会抛出明确异常。
- GPU 资源不会在 finalizer 或 Asset 回调线程销毁；销毁请求按 BGFX 帧号延迟处理。
- Detached window surface 的 resize 与 destroy 同样进入统一延迟销毁队列，不会在仍可能被 GPU 使用时直接销毁旧 framebuffer。
- `BgfxDevice.Dispose` 在调用 native shutdown 前采集 managed texture、buffer、pipeline、surface、graph 与 deferred queue 的闭包状态；即使发现遗漏也始终完成 native shutdown、readback buffer 清理和 process lease 释放，随后把遗漏作为 Inno 所有权错误明确报告。诊断不能把设备留在半关闭状态。
- 开发环境的 native loader 会按 SHA-256 内容身份把 `.lib` 当前产物同步到应用输出目录后再加载，避免重新构建 BGFX 后仍运行旧 dylib；已部署 Player 没有源码 checkout 时继续只使用 Support Pack 内的冻结产物。

双平台 Rendering CI 固定验证 Windows x64 与 macOS arm64 runner 架构，并在真实 Editor 冒烟日志中断言 D3D11/D3D12 或 Metal 后端、约定帧数和完整关闭；原生 BGFX 输出与 Editor boot log 会一同作为诊断 artifact 上传。

- Inno 会在设备关闭前释放 ImGui、Render Runtime、offscreen target、detached surface 与全部托管 handle，并排空延迟销毁队列。HiDPI 主窗口从首个 native frame 起使用物理 drawable 尺寸，不再通过首次 resize 修正。
- Objective-C `retainCount` 不能区分 Inno/BGFX 所有权与 Metal framework/driver 的内部 retain；BGFX 官方示例也能复现原检查的误报，见 [bkaradzic/bgfx#3642](https://github.com/bkaradzic/bgfx/issues/3642)。当前 vendor patch 保留真实 `release()`，移除这一不可证明的计数断言，并以 Inno 的确定性 managed resource closure、BGFX handle 销毁顺序和完整 native shutdown 作为可验证不变量。这不是日志过滤：真实资源表未清空会在关闭前被记录，并在 native runtime 与 process lease 已安全释放后明确抛出。macOS ARM64 Debug Editor 的 120-frame smoke 已验证无 `RefCount is`、无 BGFX Fatal 且出现 `BGFX Shutdown complete`。
- Graph 执行异常仍由 Core 的 complete-unwind 契约依次结束 Encoder 与 Graph。

## 相邻页面

- [Inno.Rendering](Inno.Rendering.md)：设备接口、资源描述、RenderGraph、脚本与 Pipeline 扩展 API。
