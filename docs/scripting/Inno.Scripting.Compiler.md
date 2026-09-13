# Inno.Scripting.Compiler

## Authoring 标注与 Player 边界

编译器根据 ScriptingApiScope.Authoring 的导出清单，在语义绑定后、目标 Player references 绑定前移除纯创作标注使用、自定义派生标注类及相关 using。规则由清单与继承符号推导，不引用 Editor.Annotations/Serialization/Rendering2D，也没有具体属性类型白名单。创作编译与 IDE 保留完整标注 API，Player IL 不保留 Editor 程序集引用。普通 SerializableProperty 不在此 scope，继续持久化。

[Scripting 索引](README.md) · [API](Inno.Scripting.Api.md) · [Reload](Inno.Scripting.Reload.md)

## 职责与边界

Compiler 拥有 Roslyn、裁剪 reference assemblies、logical namespace analyzer、source fingerprint、runtime/editor artifact 和编译进度。它不拥有 active assembly generation，也不进入 Player。

## 公开 API

- `ScriptCompiler`, `ScriptCompilerOptions`：一次 fresh compilation 的入口与 project context；`CompileAuthoringGenerationAsync` 生成 Runtime + Editor 的可激活候选，`CompileRuntimeDeploymentAsync(targetRuntimeDirectory, ...)` 只生成 Player 所需 Runtime closure，并将生成结果绑定到指定 Support Pack 的真实运行时程序集。
- `ScriptCompilationResult`, `ScriptCompilationProgress`, `ScriptCompilationStageTiming`：确定性结果、阶段和 timing。
- `ScriptDiagnostic`, `ScriptDiagnosticSeverity`：源码定位诊断。
- `ScriptSourceAsset`, `ScriptAssemblyDefinitionAsset`, `ScriptAssemblyScope`：由 common Asset Pipeline 导入的脚本模型。
- `LogicalScriptingApiAnalyzer`：拒绝实现 namespace、global using 和未导出 API。

同一裁剪 reference 规则用于 authoring generation、runtime deployment 与 IDE project。Runtime deployment 先用当前裁剪 API 和 analyzer 验证逻辑 namespace、可见性与脚本规则，再把改写后的源码直接针对目标 Support Pack 的 `Inno.*` 实现程序集编译；目标程序集内容指纹属于增量缓存键，因此更换或修改 Pack 不会复用不兼容脚本产物。IDE 投影只为 Project 自己的 Runtime、Editor 和显式 `.iasmdef` assembly 生成工程；Plugin source 是引擎管理的安装内容，只通过最近一次成功编译 generation 的 DLL 进入用户工程引用，绝不会生成或保留 `Inno.Plugin.*.csproj`。Game Build 只调用 runtime deployment 入口，不解析 Editor API reference、不编译 `.editor.cs`，也不生成 `Inno.EditorScripts.dll`。取消或失败结果没有 activation artifact；缓存命中仍重放诊断。

Shader 创作扩展通过 [`InnoEditor.Rendering.Shaders`](../render/Inno.Rendering.Shaders.md) 的逐类型 Editor scope 清单进入同一规则，
不在 Compiler 中增加 Shader 类型或程序集白名单。新增集成用例实际编译节点扩展，并读取生成 IDE reference 的公开 metadata：
Editor 可见 `IShaderNodeCompiler`，Runtime 不可见；Runtime 脚本直接引用该命名空间必须编译失败。

Project `Assets` 的 `~` 目录是普通 authoring content，其中的 `.cs` 与 `.iasmdef` 正常进入 authoring generation 和 IDE project；runtime deployment 始终剔除该子树。只读 `.iplugin` Mount 中的 `~` 目录才是 `.isample`，其脚本在显式 `Import Sample` 到 Project 前不会进入 Plugin assembly。

Compiler 为每个产物写入 `Inno.AssetSource` assembly metadata。Project assembly 写入 `project`，Plugin assembly 写入 manifest Plugin ID；`Assets.LocalPath` 以此解析同源资源，使业务源码在 Project 开发态和 `.iplugin` 安装态保持完全一致。

脚本编译、generation 激活和 IDE 投影是有顺序但不同的责任。只有完整 Runtime + Editor + Plugin 候选编译成功后才能激活；IDE project 在激活成功后生成。IDE 文件写入失败只发布 `INNO-IDE-PROJECTION` Warning，不允许回滚或阻止已经验证成功的运行 generation。

实现与逻辑 reference 均从公开成员的 nullable metadata 生成可空类型，包括参数、返回值、字段、属性、事件、数组元素与嵌套泛型实参。生成期间的 `NullabilityInfoContext` 在结束时释放，不进入跨代缓存；这些注解属于已有公共契约内容指纹，改变注解会产生新的 API 缓存身份。不能在调用方用 `null!` 掩盖投影遗失的可空契约。
