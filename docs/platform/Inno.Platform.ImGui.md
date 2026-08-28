# Inno.Platform.ImGui

[Platform 索引](README.md) · [Inno.Platform](Inno.Platform.md) · [Wiki 首页](../README.md)

`Inno.Platform.ImGui` 把 Dear ImGui context、SDL3 输入、渲染和可选 multi-viewport 集成到 `PlatformApplication`。业务层通过公开扩展方法创建/销毁 context，不接触 Platform 的 internal native event。

## 创建与销毁

```csharp
PlatformImGuiContext imgui = application.CreateImGuiContext(
    window,
    ImGuiContextFlags.EnableDocking |
    ImGuiContextFlags.EnableViewports |
    ImGuiContextFlags.EnableSmoothResize);

try
{
    _ = imgui.RenderFrame(DrawEditor);
}
finally
{
    application.DestroyImGuiContext(window);
}
```

每个 Platform window 最多关联一个 context。`CreateImGuiContext` 可接收 `IPlatformImGuiRenderer`；省略时继续使用轻量 SDL renderer，Editor 则注入 BGFX renderer。`DestroyImGuiContext(window)` 会释放 renderer、viewport backend、cursor、字体资源和 native ImGui context。

`IPlatformImGuiRenderer` 公开主窗口 draw-data、detached viewport create/resize/render/present/destroy 与 `supportsViewports`。跨模块纹理只使用 `ImGuiTextureHandle`，Platform public/protected API 不泄漏任何 BGFX handle。

## PlatformImGuiContext

| 成员 | 说明 |
| --- | --- |
| `RegisterFontStyle(style, path, size)` | 首帧前注册或替换某个 Regular/Bold/Italic 组合对应的字体文件。 |
| `SetIniFile(string?)` | 在首帧前设置 layout 文件；`null`/空白禁用持久化。 |
| `RenderFrame(Action)` | 开始一帧、执行绘制、提交主 viewport 和 detached viewports，返回 `ImDrawData` 指针。 |
| `Dispose()` | 释放所有 native/unmanaged 资源；重复调用安全。 |

`SetIniFile` 会持有 UTF-8 文件名直到 native context 被销毁，避免 Dear ImGui 保存 layout 时访问失效内存。Editor Host 使用 `<Project>/editor.ini`，所以不同 Project 拥有独立布局。

## 字体样式

内置 JetBrains Mono 会自动注册 `Regular`、`Bold`、`Italic`、`Bold | Italic` 四个 face。绘制代码通过 scope 切换，scope 结束后自动恢复原字体：

```csharp
using (ImGuiFont.PushStyle(ImGuiFontStyle.Bold | ImGuiFontStyle.Italic))
{
    ImGui.TextUnformatted("Highlighted");
}
```

`ImGuiFontStyle` 是 flags enum，因此组合方式不需要为 `BoldItalic` 再增加一个特殊枚举值。`IsAvailable(style)` 只检查精确 face；`PushStyle(style)` 在精确 face 缺失时依次回退到 Bold、Italic、Regular。

字体公开 API 是完全 safe 的：调用方不会接触 `ImFont*`、`ImGuiContext*` 或 `unsafe` context。原生指针只在 Platform 的 private/internal adapter 中用于定位 context 和调用 Dear ImGui。

Host 也可以在第一帧前替换任一 face：

```csharp
imgui.RegisterFontStyle(
    ImGuiFontStyle.Bold,
    Path.Combine(projectRoot, "Editor", "Fonts", "ProjectBold.ttf"),
    16f);
```

注册会把当前内置 icon glyph 合并到新 face，因此切换字体后 `ImGuiIcon` 仍可使用。跨 ImGui context 持有 scope 或在另一个 context 中 Dispose 会抛出异常，以防 Pop 错误的 font stack。

## 生命周期约束

- `SetIniFile` 必须早于第一次 `RenderFrame`；之后调用会抛出 `InvalidOperationException`。
- `RegisterFontStyle` 同样必须早于第一次 `RenderFrame`；字体路径不存在或 size 非正数会明确失败。
- `RenderFrame` 的回调必须同步完成，不得保存当前 `ImDrawData` 供后续帧使用。
- multi-viewport 与 smooth resize 由 context flags 控制；未开启时不会创建相关后端。smooth resize 重绘期间，SDL 可能在原生标题栏移动或边框拉伸中发送临时 `WindowMouseLeave`；后端把 pending leave 绑定到来源 window，并在同一 window 的 live-resize lock 释放前保持最后一个有效鼠标位置，避免所有 ImGui hover feedback 在 expose 帧中闪烁。真正离开窗口、进入其他 viewport 或结束 resize 后仍会正常清理 hover。
- context 与创建它的 `PlatformApplication`/window 同生命周期，销毁顺序应为 ImGui、Shell、window、application。
