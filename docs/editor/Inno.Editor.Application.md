# Inno.Editor.Application

[Editor 索引](README.md) · [Editor Scripting](Inno.Editor.Scripting.md) · [Wiki 首页](../README.md)

`Inno.Editor.Application` 是 Editor 可执行入口。它只组合 Platform、Shell、ImGui runtime、Scripting、Global feature 和六个独立 Panel feature；Panel/Module/Action/Setting 等实例仍由 Attribute 自动发现。

## 启动参数

`Program` 只接受且必须接受一个位置参数：要打开的 project directory。缺少参数或存在多余参数时打印 usage 并返回 exit code `2`；产品代码不读取当前工作目录、机器专属路径或隐式的 InnoProject 默认值。

```text
Inno.Editor.Application /path/to/InnoProject
```

Editor 当前不需要额外的 InnoEngine project descriptor。目录本身就是当前项目边界：

- 已有目录会原位打开，不会覆盖 Assets。
- 不存在或空目录会被创建，Shell 和 ScriptManager 再创建所需的 `Assets` / `Library` 结构与 IDE 工程。
- 如果传入路径指向普通文件，构造会抛出 `IOException`。

未来如果需要引擎版本、Package 列表或 Project GUID，可在目录内增加独立 descriptor；不应让 Editor 解析 InnoEngine 自身的 `.csproj` 作为游戏项目格式。

## internal EditorHost

`EditorHost` 是 Application 内部的启动实现，不属于公开 API。`EditorHost.Create(projectDirectory)` 依次构造 Platform、Window、Shell、ImGui context、EditorContext 与 EditorLayer；每个成功阶段立即登记清理动作，全部验证通过后才返回 host。启动失败与正常 `Dispose` 共用同一个幂等资源栈，严格按 Layer/overlay → ImGui → Shell → Window → Platform 的逆序释放；单项清理异常会记录到 boot log，但不会遮蔽原始启动异常或阻止后续清理。

`editor.ini`、`EditorSettings.json`、`ProjectSettings.inno`、Editor boot log、Assets、Plugins 与脚本产物都以 `projectDirectory` 为根目录。`editor.ini` 只保存 ImGui layout 与 Module/Panel 状态；Editor 外观、图标与缩放由 `Inno.Editor.Settings` 写入 `EditorSettings.json`；Layer、Tag 与 Plugin/runtime 的强类型项目协议由 `Inno.Core.Settings` 写入 `ProjectSettings.inno`。三个文档各有单一所有者。

Module/Panel 状态只使用 `editor.ini` 中由 Attribute ID 确定的具名可读 section，没有独立 Workspace 文档或第二个状态 ID。

主窗口请求关闭后，internal host 会在停止 Module、卸载 Scene 和销毁 ImGui context 之前强制执行一次项目 layout 保存。顺序固定为：捕获全部有状态 Module/Panel → 捕获最新 ImGui layout → flush 并原子替换 `editor.ini`。即使运行期间的两秒节流尚未到期，正常退出也不会丢失最后一次打开的 Scene setup。

`editor.ini` 写入失败会发布 `Project State Persistence` Diagnostic 并记录一次完整 Log。EditorLayer 以一秒间隔继续尝试，即使 ImGui layout 没有再次变化也不会遗留无法恢复的失败状态；成功保存后只清除 Diagnostic，历史 Log 保留。

## EditorLayer 边界

`EditorLayer` 持有 `PlatformImGuiContext`、`ImGuiEditorRuntime` 与稳定的 `EditorPlayModeLoop`。Play loop 作为 host service 注入 extension runtime；Layer 只在 fixed/update/late 三个对应 callback 转发时间，不知道 Play 状态机、Scene session、Asset、菜单或脚本编译状态。

Editor 启动阶段在 Shell 日志系统可用之前产生的诊断写入 `<Project>/Logs/EditorBoot.log`；Shell 初始化后的轮转日志写入同一目录，并使用 `log_<timestamp>.log` 文件名。项目根目录不生成独立日志文件。

每帧安全点顺序为：fixed Play callback → variable Play callback → 更新 `EditorFrame` 和 Module transition → late Play callback → 绘制统一主菜单/中央 Toolbar 与自动发现 Panel → flush deferred Action → 绘制统一 Modal。进入或退出状态由 Module transition 先提交，late callback 只在状态已经是 `Playing` 时执行；Exit Action 生效后下一帧 fixed/update 也不会继续模拟。脚本编译弹窗位于 `Inno.Editor.Scripting`，由 internal `EditorScripting` module 驱动真实编译阶段进度；Application 不包含 Scene action、恢复算法或 ScriptManager 状态机。

internal `EditorRenderingHostService` 只负责组合通用 Render Runtime、BGFX 设备与 ImGui contributor，并作为 Scripting reload participant 协调 Pipeline/Feature generation。它不提供任何 Camera、Light、PBR 或固定 Scene 语义；没有活动 Viewport/Pipeline Plugin 时 Editor 仍正常运行，Scene/Game View 只显示无活动 rendering provider 的诊断。

启动时 `EditorHost` 会检查发现的 Panel 数量；为零会立即抛出明确异常，避免再次出现“窗口正常但内容完全为空”的静默失败。
