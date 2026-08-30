# Inno.Editor.Rendering

[Editor 索引](README.md) · [Rendering](../render/README.md) · [Scene View](Inno.Editor.Panel.SceneView.md) · [Game View](Inno.Editor.Panel.GameView.md)

`Inno.Editor.Rendering` 是 Editor viewport 与任意 Plugin 渲染模型之间的后端中立桥。Editor 不知道 Camera、Scene snapshot、Picking buffer、Render Path 或材质世界观。

## Provider 协议

| API | 说明 |
| --- | --- |
| `EditorViewportKindId` | 开放 viewport 用途，例如 `inno.editor.viewport.scene` 或 Plugin 自定义预览。 |
| `EditorViewportProviderExtensionAttribute` | Stable ID、kind 与优先级的热重载发现入口。 |
| `EditorViewportProvider` | 构建提交、绘制工具栏和处理归一化 pointer。 |
| `EditorViewportContext` | 当前 Editor、交互服务、viewport ID 与物理尺寸。 |
| `EditorViewportManipulationSpace` | 可选的本帧精确 view/projection 与正交标记，供宿主 Transform 工具使用。 |
| `EditorViewportSubmission` | Plugin 提供的 `RenderFrameData`、可选 Pipeline、目标格式、优先级与 manipulation space。 |
| `EditorViewportRequest`, `EditorViewportOutput` | Host 请求与 opaque `ImGuiTextureHandle` 输出。 |
| `EditorRenderingModule` | Provider generation、Submit/Draw/Release 与异常隔离。 |

Plugin 示例：

```csharp
[EditorViewportProviderExtension(
    "sample.scene-provider",
    "inno.editor.viewport.scene")]
public sealed class SampleSceneProvider : EditorViewportProvider
{
    public override EditorViewportSubmission Build(EditorViewportContext context)
    {
        var data = new RenderFrameData();
        data.Set(new RenderDataChannelId("sample.scene"), BuildFrame(context));
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

`manipulationSpace` 完全可选，也不引入 Camera/2D/3D 概念。Scene View 只在 Provider 提供它时绘制 ImGuizmo；矩阵必须来自同一个 immutable frame snapshot，避免视口画面、Picking 与 Gizmo 使用不同相机状态。一次连续拖拽只在释放时通过 `SceneEdits` 的三个最小 Transform property payload 组成一个 History transaction，因此 Undo/Redo 原子恢复 position、rotation 与 scale，并且不捕获 Plugin delegate 或 runtime `Type`。
