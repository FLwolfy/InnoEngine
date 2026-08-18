# Inno.Editor.Scripting

[Editor 索引](README.md) · [Core Scripting](../core/Inno.Core.Scripting.md) · [Assemblies](../core/Inno.Core.Assemblies.md) · [Wiki 首页](../README.md)

`Inno.Editor.Scripting` 管理一个 Project 的 C# 源文件、IDE 工程和脚本程序集代际。它只存在于 Editor；Runtime 通过 `Inno.Core.Assemblies` 接收已编译模块。

## 输入与输出

```text
<Project>/
├─ Assets/**/*.cs                 # GameScripts
├─ Assets/**/*.editor.cs          # EditorScripts
├─ Assets/Plugins/**/*.dll        # Runtime plugin
├─ Assets/Plugins/**/*.editor.dll # Editor-only plugin
├─ Library/ScriptApi/             # 裁剪 reference assemblies
│  └─ <profile>/<fingerprint>/
│     ├─ Runtime/              # 运行时 Roslyn 使用的真实类型身份参考集
│     └─ IDE/                  # 仅包含 InnoEngine.* / InnoEditor.* 的逻辑 API facade
├─ Library/ScriptAssemblies/      # 最近若干代实际脚本 DLL/PDB
├─ Library/IDE/                   # IDE bin/obj
│  ├─ Analyzers/                  # 项目本地的脚本 API 边界 Analyzer
│  └─ ScriptApiMaps/              # 逻辑 namespace 到实现 namespace 的生成映射
├─ Inno.GameScripts.csproj
├─ Inno.EditorScripts.csproj
└─ InnoProject.sln
```

`Inno.GameScripts` 只能看到 Runtime profile；`Inno.EditorScripts` 同时看到 Runtime、Editor profile，并引用 GameScripts。

## 脚本 API 如何生效

1. `ScriptApiCatalog` 读取各引擎项目唯一的 `Properties/ScriptingApi.cs` assembly attributes。
2. 每个 profile 生成两组用途严格分离的 metadata-only reference assembly。
3. IDE 工程只引用 `Inno.ScriptApi.Runtime.dll` 和可选的 `Inno.ScriptApi.Editor.dll`。这些 facade 在 metadata 中真正定义 `InnoEngine.*` / `InnoEditor.*` 类型，因此显式 `using` 会被 IDE 实际使用，而不是触发生成器的标记。
4. IDE csproj 不引用真实的 `Inno.Core.*`、`Inno.Engine.*` 或 `Inno.Assets.*` DLL；未导出类型与实现 namespace 都无法解析。
5. Editor 内的运行时 Roslyn 编译使用另一组保留真实 CLR 类型身份的裁剪参考集。它仅在内存中把逻辑 `using` 转换为声明的实现 namespace。
6. facade 同时生成同名 XML documentation，将已导出类型和成员的 `summary`、`param`、`returns` 与 `see` 重写到逻辑 namespace。
7. 最终热重载脚本 DLL 直接引用真实引擎类型；IDE facade 只用于代码模型与 IDE 诊断，永远不作为脚本运行时依赖加载。

这意味着 Rider 不会再因为某个项目被标为 Runtime 而看到该程序集全部 public API。比如导出 `StableTypeIdAttribute` 后，脚本仍无法解析 `TypeCacheManager`。

## ScriptManagerOptions

| 属性 | 默认值 | 说明 |
| --- | --- | --- |
| `required projectRootDirectory` | 无 | 包含 Assets 与 Library 的 Project 根目录。 |
| `autoCompile` | `true` | `Start()` 后是否创建首次编译请求，并在文件变化后标记脚本 dirty。 |
| `debounceMilliseconds` | `250` | dirty 文件在允许编译前必须保持不变的时间。 |
| `retainedCompilationGenerations` | `3` | 在 `Library/ScriptAssemblies` 中保留的最近编译代数，必须至少为 1。 |

## ScriptManager

| 成员 | 说明 |
| --- | --- |
| `isCompiling` | 当前是否持有编译 gate。 |
| `isCompilationPending` | 是否有 source/plugin 变化等待宿主消费。 |
| `compilationProgress` | 已完成编译工作单元的比例，范围为 0 到 1。 |
| `compilationStatus` | 当前编译阶段的短说明。 |
| `lastCompilation` | 最近一次完整 Game/Editor 编译结果。 |
| `CompilationCompleted` | 每次完整编译后触发。 |
| `Start()` | 创建目录、生成 IDE 文件、启动 watcher，并按选项请求编译。 |
| `RequestCompile()` | 只标记 dirty 并重置静默计时，不启动编译。 |
| `TryCompilePending(out task)` | 静默时间满足后消费 dirty 请求并启动一次编译。宿主可在 focus 安全点调用。 |
| `CompileAsync(...)` | 编译 GameScripts 和 EditorScripts；任一失败都不产生待激活代际。 |
| `ApplyPendingReload()` | 在主线程安全点加载首次代际，或事务式替换当前代际。 |
| `GenerateProjectFiles()` | 重新生成两个 csproj 和 solution。 |
| `Dispose()` | 停止 watcher、取消任务并卸载活动脚本模块。 |

典型宿主流程：

```csharp
using var scripts = new ScriptManager(new ScriptManagerOptions
{
    projectRootDirectory = projectRoot
});

scripts.Start();

// Call only while the editor owns focus.
if (editorWindow.isFocused && scripts.TryCompilePending(out Task<ScriptCompilationResult>? task))
{
    ScriptCompilationResult result = await task;
    if (result.success)
        scripts.ApplyPendingReload();
}
```

当前 Editor Host 只在主窗口或 detached viewport 重新获得 focus、且 watcher 静默期结束后消费请求。文件监听线程始终只写 dirty 状态。

编译开始后交互立即锁定，固定宽度 modal 也会立即进入 `120 ms` 淡入，不再使用延迟显示阈值。即使编译在一帧内完成，窗口仍会完成淡入、至少保持 `350 ms`，再用 `140 ms` 淡出。进度由项目生成、源码解析、API Analyzer、编译诊断、Emit 与 Reload 准备等真实完成项计算；Roslyn 的单次诊断或 Emit 没有内部百分比回调，因此条形图会在该工作项执行期间停留，完成后再推进，而不会用计时器伪造中间进度。编译、候选验证与热重载全部完成后才进入淡出阶段。

生成的 IDE 工程会把 `Library/**` 排除出可见项目树，并把 Analyzer 与 API map 标记为不可见；它们仍参与构建，但 Rider 中只保留 `Assets` 与 `Dependencies`。facade reference 还会显式绑定同代 XML documentation，确保逻辑 `InnoEngine.*` 类型 hover 能读取从实现 API 迁移来的注释。

内置脚本 API 当前不注入 global using。脚本必须显式引用逻辑 namespace，例如：

```csharp
using InnoEngine.Core;
using InnoEngine.Scene;
using InnoEngine.Serialization;

public sealed class PlayerController : GameBehavior
{
    [SerializableProperty]
    public float speed { get; set; } = 5f;

    protected override void Update()
    {
        float frameMovement = speed * Time.deltaTime;
    }
}
```

`GameBehavior.Update/FixedUpdate/LateUpdate` 与 `GameSystem.OnUpdate/OnFixedUpdate/OnLateUpdate` 都是无参扩展点。帧间隔统一从 `InnoEngine.Core.Time.deltaTime` 或 `Time.fixedDeltaTime` 获取，避免不同生命周期 API 重复传递同一份全局时钟状态。

`StableTypeId` 不是脚本类型的必填 attribute。未声明时使用固定脚本程序集名和完整类型名生成确定性 ID；只有当类型或 namespace 需要重命名且仍要恢复旧 Scene/Prefab 状态时，才需要显式固定它。

脚本编译还会为 `[SerializableProperty]` 自动补充同一类型内的源码声明顺序。field 与 property 可以交错书写，Inspector 与序列化结果不会再把所有 field 强制排到 property 前面；脚本作者不需要手动填写 `order`。

Editor 的进程内 Debug 脚本编译会定义 `DEBUG` 与 `TRACE`，与生成 IDE 工程的 Debug configuration 保持一致。因此 `Log.Debug(...)` 不会因为运行时 Roslyn 缺少条件编译符号而被意外移除。

```csharp
using InnoEngine.Reflection;

[StableTypeId("1b26f2aa-c7dd-4a61-b226-f9763bfd3eca")]
public sealed class RenamablePlayerController : GameBehavior
{
}
```

直接写实现 namespace 时，IDE 因为根本没有真实引擎程序集参考而无法解析 `Inno`；边界 Analyzer 可同时给出更具体的改写建议：

```text
INNO2001: Use scripting namespace 'InnoEngine.Scene' instead of implementation namespace 'Inno.Engine.Scene'
```

新增 Rendering 等模块时，只需在该项目唯一的 `Properties/ScriptingApi.cs` 声明 `ScriptingApiNamespace("InnoEngine.Rendering", ...)` 和逐类型 exports。下一次生成 IDE 工程/编译时映射会自动进入两个 profile，不需要改 ScriptCompiler 中央名单。

## 编译结果与诊断

- `ScriptCompilationResult.success`：两个程序集是否全部成功。
- `diagnostics`：`ScriptDiagnostic` 列表。
- `outputDirectory`：本代输出目录。
- `ScriptDiagnostic`：包含 `id`、`severity`、`message`、`filePath`、`line`、`column`。
- `ScriptDiagnosticSeverity`：`Info`、`Warning`、`Error`。

失败时旧代际继续运行，`ApplyPendingReload()` 返回 `false`。

Editor Host 不再为成功编译写入 Info 日志；Warning、Error 和源位置诊断保持不变。

## Editor 与 Scene 生命周期

当前 Editor Layer 负责编辑 Scene，但不会调用 `SceneManager.Update()`，因此在 Inspector 中切换 `GameBehavior.enabled` 只修改被序列化的编辑状态，不会在 Edit Mode 执行 `Awake`、`OnEnable`、`OnDisable`、`Start` 或 `Update`。这些回调需要 Scene 进入由 Runtime `GameLayer` 驱动的正常生命周期后才会执行。

在正常生命周期中，启用 active Behavior 会调用 `OnEnable`，禁用会调用 `OnDisable`；`OnDestroy` 只在已经进入过 Runtime 生命周期的实例被移除、GameObject 被销毁或 Scene 被卸载时调用。它不是禁用通知。未来 Editor Play Mode 应通过独立的 Runtime Scene/session 驱动这些回调，而不应让 Inspector 的 Edit Mode 操作直接冒充 Play Mode。

`Library/ScriptAssemblies/<number>` 中的数字是持久递增的编译 generation，而不是内存对象编号。每代目录保存该次 GameScripts/EditorScripts 的 DLL、PDB 与复制后的插件，只占用磁盘空间；`ScriptManager` 启动时会从现有最大编号继续，并自动删除超过 `retainedCompilationGenerations` 的旧目录。活动程序集会先由 `AssemblyManager` shadow copy 到自己的 generation 目录，因此清理这些编译输入不会卸载正在运行的脚本。旧脚本是否仍占用内存由 collectible `AssemblyLoadContext` 的协作式卸载状态决定，与这些目录是否存在无关。

generation 只是 Editor 编译事务的运行编号，不是 Scene/Prefab schema，也不会写入任何资产或序列化数据。类型状态迁移使用 Stable Type ID；Importer 持久缓存兼容性使用 importer 自己的显式 `version`。重新启动 Editor 后，只需从磁盘上仍存在的最大 generation 继续编号，编号跳跃或旧目录被删除都不影响资产兼容性。

## Code Analysis

逻辑脚本 API Analyzer 已直接归入 `Inno.Editor.Scripting/CodeAnalysis`，使用统一的 `Inno.Editor.Scripting` namespace，不再存在独立的 `Inno.Editor.Scripting.CodeAnalysis` 程序集。运行时 Roslyn 编译直接创建 `LogicalScriptingApiAnalyzer`；IDE 工程生成器把当前 Scripting 程序集复制到 `Library/IDE/Analyzers`，因此 IDE 与运行时共享同一套 `INNO2001`、`INNO2002` 规则实现。

`CodeAnalysis/Documentation/AnalyzerReleases.Shipped.md` 与 `AnalyzerReleases.Unshipped.md` 是 Roslyn 规则版本记录，被明确归类在 Analyzer 子目录中，并作为构建输入保留。

## 热重载边界

成功代际通过 `AssemblyManager.BeginReload()` 准备，Scene 层通过 `SceneReloadService` 捕获并迁移脚本 Component/System。成功后提交并卸载旧 ALC；失败时同时回滚 Scene 结构和程序集 catalog。

无法自动迁移的内容仍包括用户 static 状态、后台线程、第三方事件订阅和外部裸对象引用。脚本应在 `OnDisable()` 释放这些资源。
