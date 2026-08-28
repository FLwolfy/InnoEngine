# Inno.Rendering.Bgfx

[返回 Rendering 索引](README.md) · [Wiki 首页](../README.md) · [艺术家 API](Inno.Rendering.md) · [后端中立核心](Inno.Rendering.Core.md)

## 职责与边界

`Inno.Rendering.Bgfx` 是唯一允许引用 `Inno.Native.Bgfx` 的托管渲染程序集。它把 `IRenderDevice`、Compiled RenderGraph、资源描述和 `RenderCommandEncoder` 映射为 BGFX 设备、View、Encoder、Framebuffer 和延迟销毁队列。

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
| `BgfxDevice` | BGFX 设备所有权、能力映射、帧边界、RenderGraph 执行、KTX/普通纹理、Buffer、Program 与延迟销毁 |

`BgfxCapabilityMapper` 与 `BgfxCommandEncoder` 是内部实现，不属于稳定脚本契约。

## 当前实现行为

- 编译后的逻辑 Pass 按拓扑顺序映射到 BGFX View，并使用 `set_view_order` 固定执行顺序。
- Raster attachment 在 Pass 开始时组成临时 Framebuffer；离开 Graph 后进入延迟销毁队列。
- `BeginFrame` 会先重置上一帧实际使用过的 BGFX View，再处理到期销毁；直接 shutdown 也执行同一重置，从而解除 View 对 framebuffer/program 的跨帧引用。
- Pipeline 创建时将阶段 Shader 的生命周期所有权交给 BGFX Program；Pipeline 热替换与关闭只销毁 Program，避免后端异步命令尚未消费时提前释放 Shader。
- 同一物理别名槽只创建一个 transient texture；跨帧纹理必须通过 `PersistentTextureHandle` 导入。
- 所有 BGFX 字符串 API 使用显式 UTF-8 字节长度，避免绑定层把负长度解释成超大拷贝。
- Noop 后端不执行无意义的 backbuffer reset，但仍更新逻辑尺寸并推进 frame。
- `CopyTexture`、Program、Vertex/Index/Storage Buffer、Draw、Dispatch、uniform 与 KTX texture container 已映射；所有外部 API 仍只使用 Core opaque handle。

## 失败与资源安全

- 非 API thread、未开启帧、嵌套 Graph/Encoder、跨 generation handle 和 frame 前未结束 Encoder 都会抛出明确异常。
- GPU 资源不会在 finalizer 或 Asset 回调线程销毁；销毁请求按 BGFX 帧号延迟处理。
- Graph 执行异常仍由 Core 的 complete-unwind 契约依次结束 Encoder 与 Graph。

## 相邻页面

- [Inno.Rendering.Core](Inno.Rendering.Core.md)：设备接口、资源描述和 RenderGraph 编译规则。
- [Inno.Rendering](Inno.Rendering.md)：脚本、艺术家与 Pipeline 扩展 API。
