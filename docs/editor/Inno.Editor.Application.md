# Inno.Editor.Application

[Editor 索引](README.md) · [Editor Scripting](Inno.Editor.Scripting.md) · [Wiki 首页](../README.md)

`Inno.Editor.Application` 是 Editor 可执行入口。它只组合 Platform、EngineHost/Edit RuntimeSession、ImGui runtime、Scripting、Build 和 Editor features；Panel/Module/Action/Setting 等实例仍由 Attribute 自动发现。

## 启动参数

`Program` 只接受且必须接受一个位置参数：要打开的 project directory。缺少参数或存在多余参数时打印 usage 并返回 exit code `2`；产品代码不读取当前工作目录、机器专属路径或隐式的 InnoProject 默认值。

```text
Inno.Editor.Application /path/to/InnoProject
```

Editor 当前不需要额外的 InnoEngine project descriptor。目录本身就是当前项目边界：

- 已有目录会原位打开，不会覆盖 Assets。
- 不存在或空目录会被创建，Application Composition Root 再创建所需的 `Assets` / `Library` 结构并启动实例化 authoring 与 scripting services。
- 如果传入路径指向普通文件，构造会抛出 `IOException`。

未来如果需要引擎版本、Package 列表或 Project GUID，可在目录内增加独立 descriptor；不应让 Editor 解析 InnoEngine 自身的 `.csproj` 作为游戏项目格式。

## internal EditorHost

`EditorHost` 是 Application 内部的启动实现，不属于公开 API。它依次构造 Platform、Window、EngineHost、Edit RuntimeSession、authoring services、ImGui context 与 Editor runtime；启动失败与正常 `Dispose` 共用幂等资源栈，并按 Editor → ImGui → Session → EngineHost → Window → Platform 的逆序释放。

`editor.ini`、`Settings.Editor.inno`、`Settings.Project.inno`、`Settings.Build.inno`、Editor boot log、Assets、Plugins 与脚本产物都以 `projectDirectory` 为根目录。`editor.ini` 只保存 ImGui layout 与 Module/Panel 状态；Editor 偏好通过 SerializationRegistry 写入 `Settings.Editor.inno`；runtime 项目协议和团队共享的导出默认值分别写入 `Settings.Project.inno` 与 `Settings.Build.inno`。三个设置文档共享一个 Settings frontend，但保持独立的生命周期和部署边界。

Module/Panel 状态只使用 `editor.ini` 中由 Attribute ID 确定的具名可读 section，没有独立 Workspace 文档或第二个状态 ID。

主窗口请求关闭后，internal host 会在停止 Module、卸载 Scene 和销毁 ImGui context 之前强制执行一次项目 layout 保存。顺序固定为：捕获全部有状态 Module/Panel → 捕获最新 ImGui layout → flush 并原子替换 `editor.ini`。即使运行期间的两秒节流尚未到期，正常退出也不会丢失最后一次打开的 Scene setup。

`editor.ini` 写入失败会发布 `Project State Persistence` Diagnostic 并记录一次完整 Log。EditorLayer 以一秒间隔继续尝试，即使 ImGui layout 没有再次变化也不会遗留无法恢复的失败状态；成功保存后只清除 Diagnostic，历史 Log 保留。

## EditorLayer 边界

`EditorLayer` 持有 `PlatformImGuiContext`、`ImGuiEditorRuntime` 与稳定的 `EditorPlayModeLoop`。Play loop 作为 host service 注入 extension runtime；Layer 每帧推进一次完整 Play `RuntimeSession.Tick`，并为 Editor update、draw 与快捷键建立当前 presentation execution scope，但不知道 Play 状态机、Scene session、Asset、菜单或脚本编译状态。

Editor 启动阶段在 LogRouter 可用之前产生的诊断写入 `<Project>/Logs/EditorBoot.log`；EngineHost 初始化后的轮转日志写入同一目录。项目根目录不生成独立日志文件。

每帧安全点顺序为：推进完整 Play Session tick → 在当前 Edit/Play execution scope 中更新 `EditorFrame` 与 Module transition → 重新解析 transition 后的 scope 并绘制统一主菜单、中央 Toolbar、自动发现 Panel 与 Modal → 执行 Rendering frame。Update 与 Draw 分别获取 scope，因此 Preparing 在 Update 中提交为 Playing 后，同一帧 Draw 已经指向 Play world，不会出现一帧 Edit/Play 混合。Exit Action 生效后下一帧不再模拟，并在 transition 安全点释放 Play Scene、Session 与 History。脚本编译弹窗位于 `Inno.Editor.Scripting`，由 internal `EditorScripting` module 驱动真实编译阶段进度；Application 不包含 Scene action、恢复算法或 ScriptManager 状态机。

internal `EditorRenderingHostService` 只负责组合通用 Render Runtime、BGFX 设备与 ImGui contributor，并作为 Scripting reload participant 协调 Pipeline/Feature generation。它不提供任何 Camera、Light、PBR 或固定 Scene 语义；没有活动 Viewport/Pipeline Plugin 时 Editor 仍正常运行，Scene/Game View 只显示无活动 rendering provider 的诊断。

启动时 `EditorHost` 会检查发现的 Panel 数量；为零会立即抛出明确异常，避免再次出现“窗口正常但内容完全为空”的静默失败。
