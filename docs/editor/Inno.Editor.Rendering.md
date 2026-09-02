# Inno.Editor.Rendering

[Editor 索引](README.md) · [Rendering](../render/README.md) · [Scene View](Inno.Editor.Panel.SceneView.md) · [Game View](Inno.Editor.Panel.GameView.md)

`Inno.Editor.Rendering` 是 Editor viewport 与任意 Plugin 渲染模型之间的后端中立桥。Editor 不知道 Camera、Scene snapshot、Picking buffer、Render Path 或材质世界观。

## Provider 协议

| API | 说明 |
| --- | --- |
| `EditorViewportKindId` | 开放 viewport 用途，例如 `inno.editor.viewport.scene` 或 Plugin 自定义预览。 |
| `EditorViewportProviderExtensionAttribute` | Stable ID、kind 与优先级的热重载发现入口。 |
| `EditorViewportProvider` | 先声明导航 Profile，再构建提交；也可提供工具栏和归一化 pointer 处理。 |
| `EditorViewportContext` | 当前 Editor、交互服务、viewport ID、物理尺寸、导航状态、显式内容作用域与呈现偏好。 |
| `EditorViewportNavigationState` | Host 持有的 position/rotation、正交/透视参数、pivot、focus distance、移动速度与 Planar/Orbit/Fly 模式；不属于任何 Scene Camera。 |
| `EditorViewportNavigationProfile` | Provider 每帧独立声明 Pan、Zoom、Orbit、Fly、Frame Selection 能力，以及世界 Up、灵敏度/范围和可选选择 bounds。 |
| `RenderContentScope` | Rendering 公共层提供的显式有序内容集合与 active content；Editor 与运行时 Provider 共用，插件不需要扫描全局 Loaded Scene。 |
| `EditorViewportPresentation` | 宿主提供的呈现偏好；当前包含线性背景色，不规定 Pipeline 如何实现清屏。 |
| `EditorViewportManipulationSpace` | 可选的本帧精确 view/projection 与正交标记，供宿主 Transform 工具使用。 |
| `EditorViewportSubmission` | Plugin 提供的 `RenderFrameData`、可选 Pipeline、目标格式、优先级与 manipulation space。 |
| `EditorViewportRequest`, `EditorViewportOutput` | Host 请求与 opaque `ImGuiTextureHandle` 输出。 |
| `EditorRenderingModule` | Provider generation、Submit/Draw/Release 与异常隔离。 |

`EditorRenderingModule` 还会通过 `EditorContext.statistics` 发布后端中立快照：最后完成的
RenderFrame view/draw/dispatch/cull 数，以及每个已提交 viewport 的 kind、Provider Stable ID、
状态、目标尺寸、Pipeline Stable ID、format 和 priority。统计只含稳定 ID 与字符串，不让
Stats Panel 引用 Scene/Game Panel、具体 Pipeline 或 Plugin 类型；任何其他 Editor feature 也能
使用同一 `EditorStatistics.Publish` 协议贡献自己的分组。

Plugin 示例：

```csharp
[EditorViewportProviderExtension(
    "sample.scene-provider",
    "inno.editor.viewport.scene")]
public sealed class SampleSceneProvider : EditorViewportProvider
{
    public override EditorViewportNavigationProfile ConfigureNavigation(
        EditorViewportContext context)
    {
        if (!context.navigation.isInitialized)
            context.navigation.ConfigurePerspective(
                new Vector3(0f, 2f, -8f),
                Quaternion.identity,
                60f,
                0.01f,
                1000f);
        return new EditorViewportNavigationProfile(
            new EditorViewportNavigationProfileId("sample.scene-navigation"),
            EditorViewportNavigationCapabilities.Pan
                | EditorViewportNavigationCapabilities.Zoom
                | EditorViewportNavigationCapabilities.Orbit
                | EditorViewportNavigationCapabilities.Fly
                | EditorViewportNavigationCapabilities.FrameSelection,
            EditorViewportNavigationMode.Orbit);
    }

    public override EditorViewportSubmission Build(EditorViewportContext context)
    {
        var data = new RenderFrameData();
        data.Set(
            new RenderDataChannelId("sample.scene"),
            BuildFrame(
                context.navigation,
                context.content,
                context.presentation.backgroundColor));
        return new EditorViewportSubmission(
            data,
            LoadPipeline(),
            manipulationSpace: new EditorViewportManipulationSpace(
                frame.viewMatrix,
                frame.projectionMatrix,
                frame.isOrthographic));
    }
}
```

Host 创建或 resize `RenderTexture`，提交普通 `RenderRequest`，再将 GPU texture 注册为 `ImGuiTextureHandle`。不存在 CPU readback，也不向 Panel 暴露 BGFX handle。Provider 构建失败只影响对应 viewport；无 Provider 时显示 “No active rendering provider”，Editor 其他功能继续运行。

导航状态由 Host 按稳定 viewport ID 保存，因此 Plugin reload 不会重置视图，也不会让 Host 长期持有 Plugin Camera 类型。每帧顺序固定为：Host 设置 `content`/presentation → Provider `ConfigureNavigation` → Scene View 处理输入 → Provider `Build` → 提交 `RenderRequest`。所以滚轮、Orbit、Fly 和 Frame Selection 的结果会在同一帧进入 immutable snapshot，不再产生一帧延迟。

`RenderContentScope` 是类型擦除但带稳定 ID 的当前帧边界。Provider 可用 `GetValues<T>()` 取得它理解的 Scene/Document 类型；`RenderContentReference.value` 不得跨帧或跨 generation 保存。Scene/Game 的默认背景色属于 `Editor/Appearance/Viewports`，通过 `context.presentation` 传入 Provider。Provider 可以将它用作 clear color，也可以在自己的语义确实需要时选择其他合成方式。

`manipulationSpace` 完全可选，也不引入 Camera/2D/3D 概念。Scene View 只在 Provider 提供它时绘制 ImGuizmo；矩阵必须来自同一个 immutable frame snapshot，避免视口画面、Picking 与 Gizmo 使用不同相机状态。一次连续拖拽只在释放时通过 `SceneEdits` 的三个最小 Transform property payload 组成一个 History transaction，因此 Undo/Redo 原子恢复 position、rotation 与 scale，并且不捕获 Plugin delegate 或 runtime `Type`。

## Editor 目标产物编译

`EditorRenderTargetArtifactProvider` 是 authoring 边界：它根据 Asset `contentVersion` 异步编译 Shader 与 Texture，并只向 Rendering Runtime 暴露无源码目标产物。首次请求会返回 `RenderTargetArtifactStatus.Pending`；这表示工作已排队，不是产物丢失，因此启动 Editor 时不会产生 `RENDER_SHADER_TARGET_UNAVAILABLE`。只有不可变部署中确实没有文件时才返回 `Unavailable`，编译器明确失败时则返回 `Failed` 并发布精确工具链诊断。

每个缓存项保留 last-good artifact。新候选编译期间继续返回 `Ready` 和 last-good；候选失败时也不破坏已工作的 GPU 资源。诊断按完整 code/source/message/severity 去重，并以 code/source 作为可恢复状态范围进入 `DiagnosticHub`；同一文件的多条编译诊断不会互相覆盖，成功重编译、资源恢复或 Provider Dispose 时会显式解析并清除。Console 因而显示 `Diagnostic` 的真实 Asset 位置，不再显示 `EditorRenderDiagnosticSink.Publish` 之类没有排障价值的 Log 调用栈。
