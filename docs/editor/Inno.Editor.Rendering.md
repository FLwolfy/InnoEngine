# Inno.Editor.Rendering

[Editor 索引](README.md) · [Rendering](../render/README.md) · [Scene View](Inno.Editor.Panel.SceneView.md) · [Game View](Inno.Editor.Panel.GameView.md)

`Inno.Editor.Rendering` 是 Editor viewport 与任意 Plugin 渲染模型之间的后端中立合成边界。Viewport kind 只表示“Scene View”“Game View”或自定义预览等用途，不再等同于某一种 2D/3D 渲染器。Editor 不知道 Camera、Scene snapshot、Picking buffer、Render Path 或材质世界观。

## Contributor 与 Composition 协议

| API | 说明 |
| --- | --- |
| `EditorViewportKindId` | 开放 viewport 用途，例如 `inno.editor.viewport.scene`。 |
| `EditorViewportContributorExtensionAttribute` | 声明 Stable ID、kind、合成顺序与交互控制优先级的热重载入口。 |
| `EditorViewportContributor` | 一个渲染模型对 viewport 的可选贡献；负责 participation、frame data、Pipeline，以及可选导航/工具/pointer。 |
| `EditorViewportContribution` | 单个模型提供的 frame data、可选 Pipeline、共享目标格式与 manipulation space。 |
| `EditorViewportLayer` | Host 接受后的中立模型层；只含 Stable ID、Pipeline、frame data 与 order。 |
| `EditorViewportComposition` | 一个 viewport 的非空层集合；按 `order`、Contributor Stable ID 确定性排序并冻结结构。 |
| `EditorViewportContext` | 当前 Editor、交互服务、viewport ID、物理尺寸、导航状态、显式内容作用域与呈现偏好。 |
| `EditorViewportNavigationState` | Host 持有的 position/rotation、正交/透视参数、pivot、focus distance、移动速度与 Planar/Orbit/Fly 模式。 |
| `EditorViewportNavigationProfile` | 当前交互控制者声明的 Pan、Zoom、Orbit、Fly、Frame Selection 能力与边界。 |
| `RenderContentScope` | Host 显式选择的有序、frame-scoped 内容集合；Contributor 不扫描全局 Loaded Scene。 |
| `EditorViewportPresentation` | Host 提供的呈现偏好；当前包含线性背景色。 |
| `EditorViewportManipulationSpace` | 控制者可选提供的本帧精确 view/projection，供 Transform 工具使用。 |
| `EditorViewportOutput` | Host 拥有的 opaque `ImGuiTextureHandle` 输出。 |
| `EditorRenderingModule` | Contributor generation、参与判断、控制者选择、Composition、Submit/Draw/Release 与逐 Contributor 异常隔离。 |

同一种 kind 可以同时有多个 Contributor。`EditorRenderingModule` 每帧询问全部候选的 `CanContribute`，将成功结果组成一个 `EditorViewportComposition`，再让 Host 把每层作为普通 `RenderRequest` 提交到同一 `RenderTexture`。低 order 先绘制，高 order 后叠加；相同 order 使用 Stable ID 排序，因此热重载、文件枚举顺序和反射顺序不会改变画面层次。

同一目标上与已成功层发生像素重叠的后续请求，会收到 `RenderPipelineContext.preservePresentationTarget=true`。该 Pipeline 必须 Load/Preserve 已有颜色，不能再次清屏。互不重叠的 split-screen viewport 可以分别初始化自己的区域；建图失败的层会回滚，不会占据目标，也不会阻止其他 Contributor。所有层必须使用同一 presentation format，否则仅隔离格式不一致的 Contributor。

渲染顺序和交互所有权是两个正交维度。`controllerPriority` 只选择一个 Contributor 负责导航、Toolbar、Pointer 与 Gizmo manipulation space，不改变 layer order。这样 3D 可以作为底层和交互控制者，2D 可以作为 overlay；也允许 2D 在纯 2D 内容中同时承担两者。错误、导航和 target 状态均按稳定 `viewportId` 隔离，多开同 kind viewport 不会串状态。

Plugin 示例：

```csharp
using Inno.Editor.Rendering;
using Inno.Rendering;

[EditorViewportContributorExtension(
    "sample.scene-model",
    "inno.editor.viewport.scene",
    order: 0,
    controllerPriority: 100)]
public sealed class SampleSceneContributor : EditorViewportContributor
{
    public override bool CanContribute(EditorViewportContext context)
        => context.content.contents.Count > 0;

    public override EditorViewportContribution Build(EditorViewportContext context)
    {
        var data = new RenderFrameData();
        data.Set(new RenderDataChannelId("sample.scene"), BuildFrame(context));
        return new EditorViewportContribution(data, LoadPipeline());
    }
}
```

导航状态由 Host 按稳定 viewport ID 保存，因此 Plugin reload 不会重置视图，也不会让 Host 长期持有 Plugin Camera 类型。每帧顺序固定为：Host 设置 content/presentation → 全部 Contributor 判断参与 → 选择控制者并配置导航 → Panel 处理输入 → 全部参与者 Build → 冻结 Composition → 提交有序 RenderRequest。滚轮、Orbit、Fly 和 Frame Selection 的结果会在同一帧进入 snapshot。

`RenderContentScope` 是类型擦除但带 Stable ID 的当前帧边界。Contributor 可用 `GetValues<T>()` 取得它理解的 Scene/Document 类型；`RenderContentReference.value` 不得跨帧或跨 generation 保存。每个渲染模型自行判断哪些 Scene 选择了该模型。因此同一个 scope 可以同时包含纯 3D Scene、纯 2D Scene，以及同时挂载两种 extraction system 的混合 Scene；缺少某个模型的 system 只表示该模型跳过此 Scene，不会令整个 viewport 失败。

`manipulationSpace` 完全可选，也不向 Host 引入 Camera/2D/3D 概念。只有控制者提供的 manipulation space 会被接受；矩阵必须来自该控制者同一帧提交的 snapshot，避免画面、Picking 与 Gizmo 使用不同相机状态。一次连续拖拽只在释放时通过 `SceneEdits` 的最小 Transform payload 组成一个 History transaction，不捕获 Plugin delegate 或 runtime `Type`。

Host 创建或 resize `RenderTexture`，再将 GPU texture 注册为 `ImGuiTextureHandle`。不存在 CPU readback，也不向 Panel 暴露 BGFX handle。单个 Contributor 的 participation、导航、Build、Toolbar 或 Pointer 异常只隔离该 Contributor；没有任何适用 Contributor 时显示居中的不可用状态，Editor 其他功能继续运行。

## Editor 目标产物编译

`EditorRenderTargetArtifactProvider` 是 authoring 边界：它根据 Asset `contentVersion` 异步编译 Shader 与 Texture，并只向 Rendering Runtime 暴露无源码目标产物。首次请求会返回 `RenderTargetArtifactStatus.Pending`；这表示工作已排队，不是产物丢失，因此启动 Editor 时不会产生 `RENDER_SHADER_TARGET_UNAVAILABLE`。只有不可变部署中确实没有文件时才返回 `Unavailable`，编译器明确失败时则返回 `Failed` 并发布精确工具链诊断。

每个缓存项保留 last-good artifact。新候选编译期间继续返回 `Ready` 和 last-good；候选失败时也不破坏已工作的 GPU 资源。诊断按完整 code/source/message/severity 去重，并以 code/source 作为可恢复状态范围进入 `DiagnosticHub`；同一文件的多条编译诊断不会互相覆盖，成功重编译、资源恢复或 Contributor Dispose 时会显式解析并清除。Console 因而显示 `Diagnostic` 的真实 Asset 位置，不再显示没有排障价值的日志调用栈。
