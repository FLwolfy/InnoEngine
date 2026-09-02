# Inno.Scripting.Compiler

[Scripting 索引](README.md) · [API](Inno.Scripting.Api.md) · [Reload](Inno.Scripting.Reload.md)

## 职责与边界

Compiler 拥有 Roslyn、裁剪 reference assemblies、logical namespace analyzer、source fingerprint、runtime/editor artifact 和编译进度。它不拥有 active assembly generation，也不进入 Player。

## 公开 API

- `ScriptCompiler`, `ScriptCompilerOptions`：一次 fresh compilation 的入口与 project context；`CompileAuthoringGenerationAsync` 生成 Runtime + Editor 的可激活候选，`CompileRuntimeDeploymentAsync` 只生成 Player 所需 Runtime closure。
- `ScriptCompilationResult`, `ScriptCompilationProgress`, `ScriptCompilationStageTiming`：确定性结果、阶段和 timing。
- `ScriptDiagnostic`, `ScriptDiagnosticSeverity`：源码定位诊断。
- `ScriptSourceAsset`, `ScriptAssemblyDefinitionAsset`, `ScriptAssemblyScope`：由 common Asset Pipeline 导入的脚本模型。
- `LogicalScriptingApiAnalyzer`：拒绝实现 namespace、global using 和未导出 API。

同一裁剪 reference 规则用于 authoring generation、runtime deployment 与 IDE project。IDE 投影只为 Project 自己的 Runtime、Editor 和显式 `.iasmdef` assembly 生成工程；Plugin source 是引擎管理的安装内容，只通过最近一次成功编译 generation 的 DLL 进入用户工程引用，绝不会生成或保留 `Inno.Plugin.*.csproj`。Game Build 只调用 runtime deployment 入口，不解析 Editor API reference、不编译 `.editor.cs`，也不生成 `Inno.EditorScripts.dll`。取消或失败结果没有 activation artifact；缓存命中仍重放诊断。

脚本编译、generation 激活和 IDE 投影是有顺序但不同的责任。只有完整 Runtime + Editor + Plugin 候选编译成功后才能激活；IDE project 在激活成功后生成。IDE 文件写入失败只发布 `INNO-IDE-PROJECTION` Warning，不允许回滚或阻止已经验证成功的运行 generation。
