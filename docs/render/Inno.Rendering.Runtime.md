# Inno.Rendering.Runtime

[Rendering 索引](README.md) · [公开 API](Inno.Rendering.md) · [BGFX 后端](Inno.Rendering.Bgfx.md)

`Inno.Rendering.Runtime` 负责通用帧调度，不包含任何具体 Pipeline。它把 `Layer` 三段渲染生命周期、请求队列、Pipeline/Feature generation、GPU 资源缓存和 ImGui 等 frame-final contributor 组合起来。

## 初始化与帧顺序

```text
OnBeforeRender
  ├─ finish any committed Pipeline/Feature generation transition
  ├─ IRenderDevice.BeginFrame
  ├─ GPU resource update / deferred destroy
  ├─ 捕获完整主表面与 Host 选定的 content viewport
  └─ 接收当前帧 RenderRequest
OnRender
  └─ 调用 TypeRegistry 发现的 RenderRequestProvider，并接受 Host 提交
OnAfterRender
  ├─ content viewport 未覆盖完整主表面时先清除黑色背景
  ├─ 按 priority/name 将全部请求构建进一个全帧 Graph
  ├─ 跟踪成功请求覆盖的 presentation region，并要求后续重叠层保留已有颜色
  ├─ 将 ImGui 等 contributor 追加到同一个 Graph
  ├─ 全帧只编译、分配 View 并执行一次 Graph
  ├─ 确认所有 Encoder 结束
  └─ IRenderDevice.EndFrame（唯一一次）
```

## 公开 API

| API | 说明 |
| --- | --- |
| `RenderRuntimeLayer` | 唯一设备帧拥有者与 `IRenderRequestSink` 实现。 |
| `RenderRuntimeLayer.EnterExecutionScope()` | 把当前 Runtime 的 Graphics 脚本门面绑定到当前异步执行流；返回的 scope 必须按嵌套顺序释放。 |
| `RenderTargetRegistry` | 在帧安全点创建、resize、导入和释放离屏目标；被替换的目标会跨一个完整提交帧退役，避免已录制的 UI/呈现命令持有失效句柄。 |
| `IRenderFrameGraphContributor` | 在用户请求后向同一帧贡献 Graph，例如 ImGui。 |

Project/Plugin 不需要获得 Runtime 实例。实现 `[RenderRequestProviderExtension(id)]` 后，Provider 会随 TypeCache candidate 一起发现、排序、恢复和原子切换，并在 `OnRender` 通过公开 `RenderRequestProviderContext.requests` 提交零到多个请求。应用组合根可给 Runtime 提供 `RenderContentScope` callback 和主呈现 viewport callback；Context 将同一个显式、frame-scoped 内容集合与 content viewport 交给全部 Provider，Runtime 本身仍不知道 Scene、World 或具体适配策略。viewport callback 缺失时使用完整表面，返回越界区域时产生结构化诊断并安全恢复为完整表面。单个 Provider 抛异常只隔离该 Provider；其他请求和 Editor 合成继续运行。

Runtime 通过活动 TypeCache 创建 Pipeline 和 Feature 候选。同一 TypeCache generation 内的候选构造、配置恢复或建图失败只产生诊断，不替换该资产的 last-good generation。Editor 脚本重载把 Runtime 注册为统一 reload participant：候选 TypeCache 与 Asset Catalog 准备完成后，Runtime 会先构造并恢复所有当前活动 Pipeline/Feature；只有全部成功才切换，后续任一 participant 失败时恢复旧实例，完整提交后才释放旧实例。这样 Pipeline、Feature、Asset 和 Assembly generation 不会出现部分发布。

扩展缺席不是候选构造失败。若候选 TypeCache 已经不包含资产引用的 Pipeline Stable ID，或不包含任一已启用 Feature Stable ID，Runtime 会提交一个显式 unavailable generation：旧 Pipeline、Feature 与 Request Provider 在提交后释放，资产配置继续保留 Stable ID，但不再执行旧 Plugin 代码。此状态与“Editor 在 Plugin 缺失时冷启动”完全一致；Editor Viewport Contributor registry 同步移除对应模型，Scene reload 把 Plugin Component/System 保存为 Missing。相同 Stable ID 回归后，Runtime 会在同一 reload transaction 内重新构建被跟踪的资产。只有扩展类型仍存在而构造、配置或状态恢复失败时，才视为坏候选并保留 last-good。Host 直接重建 TypeCache 而未使用 Editor 协调器时，Runtime 仍会在下一帧清理退休 generation，避免固定 collectible ALC。无 Pipeline 时不执行该请求，Editor 和 ImGui 仍继续提交。

## 多模型 Presentation 合成

Runtime 不把一次请求假定为整个 target 的唯一 owner。请求仍按 `priority` 与名称确定性排序；每个请求成功完成 Pipeline 建图后，Runtime 才把它的 `RenderTarget + RenderViewport` 记录为已呈现区域。后续请求若写入同一 target 的重叠区域，`RenderPipelineContext.preservePresentationTarget` 为 true，Pipeline 必须使用 Load/Preserve 语义，而不能清除此前模型的颜色。区域不相交时该值保持 false，所以 split-screen 的每个区域都能独立清屏。

该协议只声明跨 Pipeline 的 presentation color 所有权，不向 Core 引入 2D、3D、Camera 或 Scene。Editor 可以把多个 `EditorViewportContributor` 的层提交到同一离屏 target；Player 也可以用普通 `RenderRequest` 构建相同组合。建图异常会通过 Graph mutation scope 回滚，并且失败请求不会登记 presentation region，因此后续有效模型可以正常初始化目标。当前协议支持 3D 底图加 2D/UI overlay；需要跨独立模型共享并读写同一 depth buffer 时，应在 Rendering 公共层新增显式、后端中立的 depth composition contract，不能依靠隐式附件或 Plugin 互相引用。

## 资源与代际

- `RenderResourceService` 以资产 Persistent ID、内容状态和设备 generation 缓存 Texture、Geometry、Program 与 Material 绑定。
- Provider 可按 Stable Resource ID + revision 原子获取原始 Graphics/Compute Pipeline；候选创建失败不会销毁旧 handle，因此预编译程序不依赖 Material helper 或运行时 shaderc。
- 资源替换和销毁只发生在帧安全点；旧资源延迟释放。
- Runtime 只在活动 generation 与尚未完成的 reload transaction 中短暂持有 Pipeline/Feature 实例；持久身份只使用 Stable ID 和中立配置 bytes。提交后旧实例释放，回滚后候选实例释放。
- Plugin 移除会同时退休 Plugin-owned Pipeline、Feature、Request Provider 与 Editor Viewport Contributor；不会通过 rendering last-good 把已经退出 TypeCache 的 Plugin 类型继续固定在旧 collectible ALC 中。
- Shader 与纹理目标编译器由 Host 注入。Runtime 不引用 BGFX 工具或选择平台 profile；没有编译器时低级 GPU 路径和预编译资源仍可运行，源资产解析会给出明确诊断。
- shaderc/texturec 只在后台预热任务中运行。`PrewarmMaterial`、`PrewarmTexture` 与首次 Resolve 只登记候选；完成结果在后续 `BeginFrame` 安全点发布，失败保留 CPU artifact 与 GPU Program/Texture 的 last-good，不阻塞当前帧。
- `IRenderFrameUploadService` 用可复用动态页处理当前帧 Vertex/Index/Storage 数据；页按布局复用，闲置后回收，返回的 slice 跨帧使用会被拒绝。
- `IRenderResourceService.UpdateTexture` 在帧安全点验证并提交持久纹理局部更新，适合动态图集和持续变化的纹理，不替换 handle。
- `IRenderResourceService.ReadTextureAsync` 建立 generation-scoped pending transfer；Runtime 在后续 `BeginFrame` 轮询设备完成，异步恢复等待者。取消和 Runtime 关闭都会通知设备释放 pending readback，不进行 CPU busy wait。
- 多请求共享一个设备帧和一个 Graph；请求/Contributor 通过 name scope 隔离同名 Pass，单个建图失败由 mutation scope 回滚。累计 Pass 超过 `maxViews` 时拒绝新增候选并给出明确诊断。
- 显式调用 `AllowParallelRecording` 的独立 Pass callback 可在 worker 上并行生成中立 command list；Runtime/后端仍按全帧 Graph 拓扑串行回放并只调用一次 `EndFrame`。
- `GraphicsSettings.frameStatistics` 汇总全帧 Graph 的实际 View、后端报告的 draw/dispatch 与真实裁剪 Pass 数。

## Graphics execution context

`GraphicsSettings` 保留面向 Project/Plugin Script 的 Unity 风格静态调用形式，但不再保存任何
process-global 可变状态。每个 `RenderRuntimeLayer` 拥有独立的 capabilities、default pipeline 和
last-frame statistics；`EnterExecutionScope()` 只把该实例状态绑定到当前 `AsyncLocal` 执行流。
Editor/Player 组合根在本帧 authoring、simulation、request collection 与 render 期间进入 scope，
退出后立即释放。两个 Runtime 可以嵌套或并行存在而不会覆盖对方；没有活动 scope 时只读属性
返回 `null`，写入 default pipeline 会明确失败。引擎内部仍直接使用实例状态，不反向依赖脚本门面。

Reload transaction 在提交后会清空 previous pipeline、request provider 和 pending/current request
快照。完成的 transaction 即使被外部诊断对象暂时保留，也不再包含旧 generation 的 `Type`、
实例或 delegate；这条约束与 Scene Missing 占位共同保证退休 Plugin ALC 可回收。
