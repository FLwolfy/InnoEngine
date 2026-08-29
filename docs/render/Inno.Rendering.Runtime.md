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
  └─ Project、Plugin、Scene/Game Panel 提交请求
OnAfterRender
  ├─ 按 priority/name 构建并执行每个请求
  ├─ 构建 ImGui 等 contributor graph
  ├─ 确认所有 Encoder 结束
  └─ IRenderDevice.EndFrame（唯一一次）
```

## 公开 API

| API | 说明 |
| --- | --- |
| `RenderRuntimeLayer` | 唯一设备帧拥有者与 `IRenderRequestSink` 实现。 |
| `RenderTargetRegistry` | 在帧安全点创建、resize、导入和释放离屏目标。 |
| `IRenderFrameGraphContributor` | 在用户请求后向同一帧贡献 Graph，例如 ImGui。 |

Runtime 通过活动 TypeCache 创建 Pipeline 和 Feature 候选。同一 TypeCache generation 内的候选构造、配置恢复或建图失败只产生诊断，不替换该资产的 last-good generation。Editor 脚本重载把 Runtime 注册为统一 reload participant：候选 TypeCache 与 Asset Catalog 准备完成后，Runtime 会先构造并恢复所有当前活动 Pipeline/Feature；只有全部成功才切换，后续任一 participant 失败时恢复旧实例，完整提交后才释放旧实例。这样 Pipeline、Feature、Asset 和 Assembly generation 不会出现部分发布。Host 直接重建 TypeCache 而未使用 Editor 协调器时，Runtime 仍会在下一帧清理退休 generation，避免固定 collectible ALC。无 Pipeline 时请求发布 `RENDER_PIPELINE_UNAVAILABLE`，Editor 和 ImGui 仍继续提交。

## 资源与代际

- `RenderResourceService` 以资产 Persistent ID、内容状态和设备 generation 缓存 Texture、Geometry、Program 与 Material 绑定。
- Provider 可按 Stable Resource ID + revision 原子获取原始 Graphics/Compute Pipeline；候选创建失败不会销毁旧 handle，因此预编译程序不依赖 Material helper 或运行时 shaderc。
- 资源替换和销毁只发生在帧安全点；旧资源延迟释放。
- Runtime 只在活动 generation 与尚未完成的 reload transaction 中短暂持有 Pipeline/Feature 实例；持久身份只使用 Stable ID 和中立配置 bytes。提交后旧实例释放，回滚后候选实例释放。
- Shader 与纹理目标编译器由 Host 注入。Runtime 不引用 BGFX 工具或选择平台 profile；没有编译器时低级 GPU 路径和预编译资源仍可运行，源资产解析会给出明确诊断。
- 多请求共享一个设备帧；累计 Pass 超过 `maxViews` 时拒绝对应请求并给出明确诊断。
- `GraphicsSettings.frameStatistics` 汇总实际执行 View、后端报告的 draw/dispatch，以及各请求和 contributor graph 的真实裁剪 Pass 数，不再用占位零值覆盖统计。
