# Inno.Editor.Application

[Editor 索引](README.md) · [Editor Scripting](Inno.Editor.Scripting.md) · [Wiki 首页](../README.md)

`Inno.Editor.Application` 是 Editor 可执行入口。它只组合 Platform、Shell、ImGui 与一个 `EditorRuntime`；Assets、Scene、Diagnostics、Scripting feature 全部由 Attribute 自动发现。

## 启动参数

`Program` 不再提供 `--generate-project` 等命令分支。第一个位置参数是要打开的 project directory；未提供时使用当前工作目录。

```text
Inno.Editor.Application /path/to/InnoProject
```

Editor 当前不需要额外的 InnoEngine project descriptor。目录本身就是 v1 的项目边界：

- 已有目录会原位打开，不会覆盖 Assets。
- 不存在或空目录会被创建，Shell 和 ScriptManager 再创建所需的 `Assets` / `Library` 结构与 IDE 工程。
- 如果传入路径指向普通文件，构造会抛出 `IOException`。

未来如果需要引擎版本、Package 列表或 Project GUID，可在目录内增加独立 descriptor；不应让 Editor 解析 InnoEngine 自身的 `.csproj` 作为游戏项目格式。

## EditorHost

```csharp
using EditorHost host = new(projectDirectory);
return host.Run();
```

| 成员 | 说明 |
| --- | --- |
| `EditorHost(string projectDirectory)` | 规范化并创建项目目录，然后初始化 Shell、Scripts 和 UI。 |
| `projectDirectory` | 已规范化的项目绝对路径。 |
| `Run()` | 运行主循环直到主窗口或应用请求退出。 |
| `Dispose()` | 停止脚本监听，卸载 Shell/ImGui/Platform 资源。 |

`editor.ini`、Editor boot log、Assets 与脚本产物都以 `projectDirectory` 为根目录，不再依赖编译时硬编码路径。

## EditorLayer 边界

`EditorLayer` 只持有 `PlatformImGuiContext` 与 `EditorRuntime`。它把 Layer 的 Attach/LateUpdate/Detach 和按键事件转交给 Runtime，不知道 Scene、Asset、Log、菜单或脚本编译状态。

每帧安全点顺序为：Runtime 更新 Module → 绘制统一主菜单与自动发现 Panel → flush deferred Action → 绘制统一 Modal。脚本编译弹窗位于 `Inno.Editor.Scripting`，由 `ScriptingModule` 驱动真实进度并通过 Modal renderer 固定在主 viewport 中心。Application 不包含 Scene action、Asset 类型判断、context menu 排列或 ScriptManager 状态机。
