# Inno.Rendering.Runtime

[Rendering 索引](README.md) · [公开 API](Inno.Rendering.md) · [BGFX 后端](Inno.Rendering.Bgfx.md)

`Inno.Rendering.Runtime` 负责通用帧调度，不包含任何具体 Pipeline。它把 `Layer` 三段渲染生命周期、请求队列、Pipeline/Feature generation、GPU 资源缓存和 ImGui 等 frame-final contributor 组合起来。

## 初始化与帧顺序

```text
OnBeforeRender
  ├─ finish any committed Pipeline/Feature generation transition
  ├─ IRenderDevice.BeginFrame
  ├─ GPU resource update / deferred destroy
  └─ 接收当前帧 RenderRequest
OnRender
  └─ 调用 TypeRegistry 发现的 RenderRequestProvider，并接受 Host 提交
OnAfterRender
  ├─ 按 priority/name 将全部请求构建进一个全帧 Graph
  ├─ 将 ImGui 等 contributor 追加到同一个 Graph
  ├─ 全帧只编译、分配 View 并执行一次 Graph
  ├─ 确认所有 Encoder 结束
  └─ IRenderDevice.EndFrame（唯一一次）
```

## 公开 API

| API | 说明 |
| --- | --- |
| `RenderRuntimeLayer` | 唯一设备帧拥有者与 `IRenderRequestSink` 实现。 |
| `RenderTargetRegistry` | 在帧安全点创建、resize、导入和释放离屏目标。 |
| `IRenderFrameGraphContributor` | 在用户请求后向同一帧贡献 Graph，例如 ImGui。 |

Project/Plugin 不需要获得 Runtime 实例。实现 `[RenderRequestProviderExtension(id)]` 后，Provider 会随 TypeCache candidate 一起发现、排序、恢复和原子切换，并在 `OnRender` 通过公开 `RenderRequestProviderContext.requests` 提交零到多个请求。单个 Provider 抛异常只隔离该 Provider；其他请求和 Editor 合成继续运行。

Runtime 通过活动 TypeCache 创建 Pipeline 和 Feature 候选。同一 TypeCache generation 内的候选构造、配置恢复或建图失败只产生诊断，不替换该资产的 last-good generation。Editor 脚本重载把 Runtime 注册为统一 reload participant：候选 TypeCache 与 Asset Catalog 准备完成后，Runtime 会先构造并恢复所有当前活动 Pipeline/Feature；只有全部成功才切换，后续任一 participant 失败时恢复旧实例，完整提交后才释放旧实例。这样 Pipeline、Feature、Asset 和 Assembly generation 不会出现部分发布。Host 直接重建 TypeCache 而未使用 Editor 协调器时，Runtime 仍会在下一帧清理退休 generation，避免固定 collectible ALC。无 Pipeline 时请求发布 `RENDER_PIPELINE_UNAVAILABLE`，Editor 和 ImGui 仍继续提交。

## 资源与代际

- `RenderResourceService` 以资产 Persistent ID、内容状态和设备 generation 缓存 Texture、Geometry、Program 与 Material 绑定。
- Provider 可按 Stable Resource ID + revision 原子获取原始 Graphics/Compute Pipeline；候选创建失败不会销毁旧 handle，因此预编译程序不依赖 Material helper 或运行时 shaderc。
- 资源替换和销毁只发生在帧安全点；旧资源延迟释放。
- Runtime 只在活动 generation 与尚未完成的 reload transaction 中短暂持有 Pipeline/Feature 实例；持久身份只使用 Stable ID 和中立配置 bytes。提交后旧实例释放，回滚后候选实例释放。
- Shader 与纹理目标编译器由 Host 注入。Runtime 不引用 BGFX 工具或选择平台 profile；没有编译器时低级 GPU 路径和预编译资源仍可运行，源资产解析会给出明确诊断。
- shaderc/texturec 只在后台预热任务中运行。`PrewarmMaterial`、`PrewarmTexture` 与首次 Resolve 只登记候选；完成结果在后续 `BeginFrame` 安全点发布，失败保留 CPU artifact 与 GPU Program/Texture 的 last-good，不阻塞当前帧。
- `IRenderFrameUploadService` 用可复用动态页处理当前帧 Vertex/Index/Storage 数据；页按布局复用，闲置后回收，返回的 slice 跨帧使用会被拒绝。
- `IRenderResourceService.UpdateTexture` 在帧安全点验证并提交持久纹理局部更新，适合动态图集和持续变化的纹理，不替换 handle。
- `IRenderResourceService.ReadTextureAsync` 建立 generation-scoped pending transfer；Runtime 在后续 `BeginFrame` 轮询设备完成，异步恢复等待者。取消和 Runtime 关闭都会通知设备释放 pending readback，不进行 CPU busy wait。
- 多请求共享一个设备帧和一个 Graph；请求/Contributor 通过 name scope 隔离同名 Pass，单个建图失败由 mutation scope 回滚。累计 Pass 超过 `maxViews` 时拒绝新增候选并给出明确诊断。
- 显式调用 `AllowParallelRecording` 的独立 Pass callback 可在 worker 上并行生成中立 command list；Runtime/后端仍按全帧 Graph 拓扑串行回放并只调用一次 `EndFrame`。
- `GraphicsSettings.frameStatistics` 汇总全帧 Graph 的实际 View、后端报告的 draw/dispatch 与真实裁剪 Pass 数。
