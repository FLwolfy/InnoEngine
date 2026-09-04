## 总结结论

直说：**这套引擎绝对不是“狗屎架构”**。它的核心设计明显高于普通个人引擎，尤其是程序集热重载、候选事务、资产 Artifact/CAS、Editor 扩展、History、Play Mode 隔离、Rendering Core 等部分，已经有成熟商业引擎的思路。

但它目前也**没有达到“零妥协、一步到位、可正式发布游戏”的程度**。

最准确的评价是：

| 维度 | 评价 |
|---|---|
| 核心架构方向 | 很现代，约 8.5/10 |
| 热重载与扩展体系 | 非常强，约 9/10 |
| Rendering 抽象 | 很先进，约 8.5/10 |
| API 易用性 | 仍有明显摩擦，约 6.5/10 |
| 代码可维护性 | 项目边界清晰，但内部巨型类偏多，约 7/10 |
| Play Mode | 设计成熟，约 8/10 |
| Asset / Plugin | 基础优秀，部分执行路径有伸缩性问题，约 7.5/10 |
| Game Export / Player | 目前仍是 MVP，约 4.5/10 |
| 测试 | 数量和覆盖不错，但缺发布级 E2E，约 7.5/10 |

总体上，我会给当前状态 **7/10**。核心可以继续演进，不需要推倒重写；但 Export/Player、全局状态、边界约束和 Build Pipeline 必须再经历一次系统性收口。

---

# 最高优先级问题

## 1. 导出游戏中的纹理运行路径实际上可能不可用

这是目前最严重的真实功能问题。

纹理导入器把原始 PNG/JPG 等写入 Artifact：

[TextureAssetImporter.cs](/Users/aaronliao/Dev/GameEngineDev/InnoEngine/src/rendering/Inno.Rendering.Assets/Importing/TextureAssetImporter.cs:41)

但游戏导出明确只创建空的 `Sources/<source-id>` 身份目录，不复制源文件：

[AssetLoader.cs](/Users/aaronliao/Dev/GameEngineDev/InnoEngine/src/assets/Inno.Assets.Pipeline/AssetLoader.cs:1439)

Player 运行时预热纹理，却仍然从物理 source path 调用 `texturec`：

[RenderAssetPrewarmService.cs](/Users/aaronliao/Dev/GameEngineDev/InnoEngine/src/rendering/Inno.Rendering.Runtime/RenderAssetPrewarmService.cs:117)

因此导出后：

```text
TextureAsset runtimePayload 有原始图片 bytes
        ↓
RenderAssetPrewarmService 不使用它
        ↓
尝试读取空 Sources 目录下的 PNG/JPG
        ↓
texturec 失败
```

现有测试没有发现它，是因为测试里的假纹理编译器完全忽略了 `sourcePath`：

[EmptyRenderingKernelTests.cs](/Users/aaronliao/Dev/GameEngineDev/InnoEngine/tests/Inno.Rendering.Runtime.Tests/EmptyRenderingKernelTests.cs:954)

这应当作为 P0 修复。正确方向不是临时把裸资源重新复制进 Player，而是：

- Export 阶段执行 target-specific texture build。
- 生成目标格式 KTX/平台纹理 Artifact。
- Player 只加载编译后的 Artifact。
- Editor Development 模式才允许 runtime compiler。

---

## 2. 明确禁止的 `InternalsVisibleTo` 仍存在，而且不只是测试

当前有 11 处 `InternalsVisibleTo`，其中多处是生产程序集之间的耦合，例如：

- [Inno.Rendering.Core/AssemblyInfo.cs](/Users/aaronliao/Dev/GameEngineDev/InnoEngine/src/rendering/Inno.Rendering.Core/Properties/AssemblyInfo.cs:3)
- [Inno.Rendering/AssemblyInfo.cs](/Users/aaronliao/Dev/GameEngineDev/InnoEngine/src/rendering/Inno.Rendering/Properties/AssemblyInfo.cs:3)
- [Inno.Assets.Pipeline/AssemblyInfo.cs](/Users/aaronliao/Dev/GameEngineDev/InnoEngine/src/assets/Inno.Assets.Pipeline/Properties/AssemblyInfo.cs:3)
- [Inno.Scene/AssemblyInfo.cs](/Users/aaronliao/Dev/GameEngineDev/InnoEngine/src/engine/Inno.Scene/Properties/AssemblyInfo.cs:3)

这不仅违反你的硬性要求，也说明某些项目拆分只是“程序集上分开了”，真实协议边界还没有完全独立。

文档甚至写着 Editor 不使用 `InternalsVisibleTo`，与当前代码不一致：

[Inno.Editor.Core.md](/Users/aaronliao/Dev/GameEngineDev/InnoEngine/docs/editor/Inno.Editor.Core.md:68)

应该通过以下方式移除，而不是简单把 internal 改 public：

- 把共享协议移动到真正的 contract assembly。
- 把必须由实现方拥有的逻辑移动到实现方项目。
- 使用公开但最小化的 capability/interface。
- 测试通过公开行为、独立 test adapter 或反射验证 internal，不建立 friend assembly。

---

# Game Export 仍不是发布级系统

## 3. Player 构建依赖完整引擎源码仓库

内置 Publisher 每次 Export 都执行 `dotnet publish`：

[DotnetGamePlayerPublisher.cs](/Users/aaronliao/Dev/GameEngineDev/InnoEngine/src/editor/Inno.Editor.Exporting/Building/DotnetGamePlayerPublisher.cs:41)

而且通过查找 `InnoEngine.sln` 判断是否处于源码 checkout：

[DotnetGamePlayerPublisher.cs](/Users/aaronliao/Dev/GameEngineDev/InnoEngine/src/editor/Inno.Editor.Exporting/Building/DotnetGamePlayerPublisher.cs:192)

`GameExportRequest` 甚至公开要求调用者传入 `playerProjectPath`：

[GameExportRequest.cs](/Users/aaronliao/Dev/GameEngineDev/InnoEngine/src/editor/Inno.Editor.Exporting/Building/GameExportRequest.cs:33)

这是明显的内部构建细节泄漏。正式 Editor 安装包脱离源码仓库后，这套 API 就不成立。

现代方案应当是：

- Editor 安装时携带不可变的 `BuildSupport/<target>` 包。
- 包含 Player template、runtime、native libraries、toolchain manifest。
- Player binary 按 engine/toolchain fingerprint 缓存。
- 每次游戏导出只组合缓存 Player、脚本、目标 Artifact 和 Manifest。
- `GameExportRequest` 不应知道 `.csproj`。

---

## 4. 正式游戏仍携带 shaderc、texturec、Shader include 和源内容

Publisher 会把 BGFX 工具目录、`bgfx_shader.sh`、`bgfx_compute.sh` 复制进游戏：

[DotnetGamePlayerPublisher.cs](/Users/aaronliao/Dev/GameEngineDev/InnoEngine/src/editor/Inno.Editor.Exporting/Building/DotnetGamePlayerPublisher.cs:163)

Player 启动后创建运行时 Shader/Texture compiler：

[GamePlayerHost.cs](/Users/aaronliao/Dev/GameEngineDev/InnoEngine/src/engine/Inno.Player/GamePlayerHost.cs:91)

Shader source importer 还重复输出 `runtime` 和 `source`：

[ShaderSourceAssetImporter.cs](/Users/aaronliao/Dev/GameEngineDev/InnoEngine/src/rendering/Inno.Rendering.Assets/Importing/ShaderSourceAssetImporter.cs:29)

这会造成：

- 游戏第一次使用 Shader/Texture 时编译和卡顿。
- 发布包携带编译工具。
- Shader source 和原始纹理仍存在于 CAS，只是文件名被哈希化。
- 构建结果依赖最终用户环境。
- Artifact 体积存在重复内容。

内容寻址不是加密。现在的 Artifact 能隐藏原始目录结构，但不能保护资源内容。

正式模式应在 Export 阶段完成所有平台相关编译；Player 中的 runtime compiler 应只属于 Development Player。

---

## 5. 导出的“快照”不是整个项目的一致性快照

Asset 自身的导出在 Loader gate 内是稳定的，这是优点。但 Game Export 依次、分开读取：

1. Asset Catalog/Artifact；
2. Project Settings；
3. Plugin Catalog；
4. runtime assemblies；
5. Player；

见 [GameExportService.cs](/Users/aaronliao/Dev/GameEngineDev/InnoEngine/src/editor/Inno.Editor.Exporting/Building/GameExportService.cs:85)。

没有一个统一 generation token 或 lease。脚本、Plugin、Settings 或 Asset 在中途切换时，有机会组成混合代际 Build。

Plugin Export 同样先取文件列表，再直接读取物理文件：

[PluginExportService.cs](/Users/aaronliao/Dev/GameEngineDev/InnoEngine/src/assets/Inno.Plugins.Authoring/PluginExportService.cs:119)

文件在枚举与读取之间被外部修改时，ZIP 可能包含混合状态。文档中“一次稳定快照”的说法比实际实现更强。

需要引入类似：

```text
ProjectBuildSnapshotLease
├── Asset catalog generation
├── Plugin generation
├── Script generation
├── Settings generation
├── Rendering target artifacts
└── Toolchain/build-support fingerprint
```

整个构建结束前，对应 generation 不能被回收。

---

## 6. Export 会阻塞 Editor，Plugin Export 还把整个项目读进内存

`ExportAsync` 在第一次 `await` 之前已经同步完成 Asset 导出、Settings、Manifest 和程序集复制：

[GameExportService.cs](/Users/aaronliao/Dev/GameEngineDev/InnoEngine/src/editor/Inno.Editor.Exporting/Building/GameExportService.cs:85)

它从 Editor 的 `OnUpdate` 直接启动：

[ExportWindowModule.cs](/Users/aaronliao/Dev/GameEngineDev/InnoEngine/src/editor/Inno.Editor.Exporting/Runtime/ExportWindowModule.cs:173)

因此大项目会卡主线程。

Plugin Export 更明显：

- 所有项目文件读成 `byte[]`。
- 所有 embedded dependency ZIP 先完整生成进内存。
- 最后才统一写 ZIP。

见 [PluginExportService.cs](/Users/aaronliao/Dev/GameEngineDev/InnoEngine/src/assets/Inno.Plugins.Authoring/PluginExportService.cs:147)。

应该改成短暂捕获快照、后台流式写包、阶段化进度与完整 cancellation。

---

## 7. 缺少真正的 Build Profile

当前 Export 窗口只有 application ID、产品名、启动 Scene、目标和输出目录。

还缺少正式构建系统通常必需的：

- 持久化 Build Profile；
- Development/Release；
- Scene 列表及包含策略；
- 图形后端/能力目标；
- Debug symbols；
- 资源压缩策略；
- macOS signing、entitlements、notarization；
- Windows icon、manifest、版本资源；
- 构建报告和复现 fingerprint；
- target prerequisite validation；
- 增量构建缓存。

所以它现在更像“可演示的 Player 打包器”，还不是完整游戏构建系统。

---

# 核心架构问题

## 8. 全局静态 Manager 太多

`Shell` 是 singleton，并按固定顺序初始化大量静态 Manager：

[Shell.cs](/Users/aaronliao/Dev/GameEngineDev/InnoEngine/src/core/Inno.Core.Framework/Application/Shell.cs:122)

包括 Asset、Plugin、Settings、Serialization、Assembly、TypeCache、Job、Identity、Log 等。

优点是游戏脚本 API 很简单；代价是：

- 一个进程只能有一个 Engine World/Project。
- 测试需要关闭并行。
- 依赖关系隐藏在全局初始化顺序中。
- Headless build、批处理、多项目 Editor、独立 Preview World 会越来越困难。
- 静态 event 更容易延长 collectible ALC 生命周期。

多个测试项目已经明确关闭并行，这正是全局状态的外部表现。

不建议一次性 DI 化所有游戏 API。更合理的是：

- 内部建立实例化 `EngineHost/ProjectSession/RuntimeWorld`。
- Unity 风格静态 API 作为当前 session 的薄 facade。
- Editor、Player、Build Worker 分别拥有独立 Host。
- 允许未来在同一进程创建 Preview/Test World。

另外，`Inno.Core.Framework` 实际上是高层 composition root，却位于 `core` 下并反向引用 Assets/Plugins：

[Inno.Core.Framework.csproj](/Users/aaronliao/Dev/GameEngineDev/InnoEngine/src/core/Inno.Core.Framework/Inno.Core.Framework.csproj:9)

功能没有错，但命名和层级会误导依赖认知；它更接近 `Inno.Runtime.Hosting`。

---

## 9. Serialization 的严格 class converter 策略过度消耗 API 易用性

根对象可以默认序列化 `ISerializable`，但所有嵌套 class 都传入：

```csharp
allowDefaultObject: false
```

见 [ValuePipeline.cs](/Users/aaronliao/Dev/GameEngineDev/InnoEngine/src/core/Inno.Core.Serialization/Internal/ValuePipeline.cs:194)。

因此即使是 sealed、准确声明类型、实现 `ISerializable` 的普通嵌套对象，也必须编写 Converter：

[ValuePipeline.cs](/Users/aaronliao/Dev/GameEngineDev/InnoEngine/src/core/Inno.Core.Serialization/Internal/ValuePipeline.cs:134)

这就是之前 `GameRuntimePlugin` 报错的根本原因。

这种严格策略是有意识的，不是随手写坏；但它产生的安全收益不足以抵消：

- 大量重复 Converter。
- 错误直到运行 Export 才出现。
- `[RequiresSerializationConverter]` 的语义被削弱，因为实际上所有嵌套 class 都需要 Converter。
- 新增一个普通嵌套 DTO 就可能破坏序列化。

更好的契约是：

- sealed/exact concrete `ISerializable` 默认递归处理；
- 多态、引用身份、对象图、自定义布局才要求 Converter；
- `[RequiresSerializationConverter]` 保持真正的强制语义；
- Registry 构建时验证完整 serializable type graph，避免运行时首次失败。

---

## 10. 项目拆得很细，但内部存在多个巨型职责中心

典型文件：

- `AssetLoader.cs`：2922 行
- `ModuleHost.cs`：1430 行
- `AssetPipeline.cs`：1328 行
- `FileBrowserPanel.cs`：1306 行
- `RenderResourceService.cs`：1207 行
- `EditorSceneWorkspace.cs`：1165 行
- `EditorExtensionCatalog.cs`：1016 行
- `RenderGraphCompiler.cs`：1007 行
- `EditorHistory.cs`：965 行
- `ScriptCompiler.cs`：893 行
- `ScriptManager.cs`：878 行

行数本身不是罪，但其中部分类型同时负责：

- 生命周期；
- IO；
- candidate/transaction；
- cache；
- registry；
- diagnostics；
- rollback；
- observer；
- threading。

这说明“程序集拆分”已经很好，但“程序集内部的领域拆分”还没完全跟上。

最需要拆的是 `AssetLoader`、`AssetPipeline`、`ScriptManager`、`EditorSceneWorkspace` 和 `RenderResourceService`。应该按稳定职责拆 service，而不是机械按 `Helper` 拆碎。

---

## 11. 观察者异常被静默吞掉

例如 AssetPipeline observer：

[AssetPipeline.cs](/Users/aaronliao/Dev/GameEngineDev/InnoEngine/src/assets/Inno.Assets/AssetPipeline.cs:925)

AssetLoader reload observer：

[AssetLoader.cs](/Users/aaronliao/Dev/GameEngineDev/InnoEngine/src/assets/Inno.Assets.Pipeline/AssetLoader.cs:2196)

Plugin candidate observer 也采用类似方式。

“扩展异常不能回滚已经提交的事务”是正确的，但不能等于“什么都不记录”。否则插件或面板失效时，用户只会看到功能没有发生。

建议统一建立 extension-failure sink：

- 隔离异常；
- 记录 extension ID、generation、callback phase；
- 去重并限流；
- 在 Logging/Diagnostics Panel 可见；
- 必要时 quarantine 当前 extension。

---

## 12. Plugin 每 500ms 在主线程遍历整个目录树

[PluginEnvironment.cs](/Users/aaronliao/Dev/GameEngineDev/InnoEngine/src/assets/Inno.Plugins.Authoring/PluginEnvironment.cs:20) 每 500ms 执行一次完整目录 fingerprint：

[PluginEnvironment.cs](/Users/aaronliao/Dev/GameEngineDev/InnoEngine/src/assets/Inno.Plugins.Authoring/PluginEnvironment.cs:437)

然后可能同步 Scan、解压、验证和准备 Asset mount。由于 `PluginEnvironment.Update()` 在 `Shell.Tick()` 中执行，插件规模增大后会造成周期性帧尖峰。

另外，导出 Plugin 会把所有 active Plugin 都声明为 dependency：

[PluginExportService.cs](/Users/aaronliao/Dev/GameEngineDev/InnoEngine/src/assets/Inno.Plugins.Authoring/PluginExportService.cs:91)

“已安装/已激活”不等于“当前项目实际依赖”。这容易让包依赖膨胀。应从 Script assembly dependency、Asset dependency closure、Settings ownership 得出真实依赖闭包。

---

# Play Mode 评价

Play Mode 整体设计是成功的：

- 明确的 Editing/Entering/Playing/Exiting 状态机；
- Scene snapshot 与独立 runtime copy；
- History isolation；
- 运行异常自动请求恢复；
- 恢复失败可重试；
- 日志 session 分离；
- 编译、Scene、Interactions 都通过窄接口注入。

[EditorPlayModeModule.cs](/Users/aaronliao/Dev/GameEngineDev/InnoEngine/src/editor/Inno.Editor.PlayMode/Runtime/EditorPlayModeModule.cs:12)

但有两个需要收口的点。

### 编译请求没有 generation ticket

`RequestCompilation()` 返回 `void`，调用方只能轮询全局 `state` 和 `lastCompilation`：

[IEditorScriptCompilation.cs](/Users/aaronliao/Dev/GameEngineDev/InnoEngine/src/editor/Inno.Editor.Scripting/Runtime/IEditorScriptCompilation.cs:19)

这不能证明“当前结果正是我请求的那一代”。Play Mode 本身甚至不主动请求 fresh compilation，只等待当前状态：

[EditorPlayModeModule.cs](/Users/aaronliao/Dev/GameEngineDev/InnoEngine/src/editor/Inno.Editor.PlayMode/Runtime/EditorPlayModeModule.cs:115)

应让请求返回 `CompilationTicket`，包含 generation ID、Task、result 和 pin/release。Export 与 Play Mode 都复用这个协议。

### enabled 生命周期不是 Unity 的立即语义

`GameBehavior.enabled` 只修改字段：

[GameBehavior.cs](/Users/aaronliao/Dev/GameEngineDev/InnoEngine/src/engine/Inno.Scene/Core/GameBehavior.cs:17)

`OnEnable/OnDisable` 要到下一次生命周期 `Prepare` 才触发：

[SceneLifecycle.cs](/Users/aaronliao/Dev/GameEngineDev/InnoEngine/src/engine/Inno.Scene/Lifecycle/SceneLifecycle.cs:19)

这不是错误，但与 Unity 的即时 callback 语义不同。必须明确决定并写入 API 契约，否则用户会凭 Unity 经验产生错误预期。

---

# 构建与规范没有真正自动化

当前 58 个 production project 中：

- 49 个生成 XML 文档；
- 只有 33 个同时严格启用 `CS1572/CS1573/CS1591`；
- 9 个没有启用 XML 输出；
- `Inno.Core.Framework` 甚至显式屏蔽 `CS1573/CS1591`：

[Inno.Core.Framework.csproj](/Users/aaronliao/Dev/GameEngineDev/InnoEngine/src/core/Inno.Core.Framework/Inno.Core.Framework.csproj:6)

根级 [Directory.Build.props](/Users/aaronliao/Dev/GameEngineDev/InnoEngine/src/Directory.Build.props:1) 只处理 Inno metadata，没有统一：

- target framework；
- nullable；
- implicit usings；
- XML docs；
- warnings as errors；
- deterministic build；
- analyzer；
- language version。

同时没有：

- `global.json` 固定 SDK；
- `Directory.Packages.props` 集中包版本；
- architecture tests 检查依赖方向；
- 自动禁止 `InternalsVisibleTo`；
- 自动验证只有 Bgfx 能引用 Native.Bgfx；
- 自动检查 Scripting API 和 ProjectReference 边界。

你的 `AGENTS.md` 规则非常完整，但目前主要依赖人和 AI 自律。`InternalsVisibleTo` 和 XML 配置漂移已经证明：**没有机器执行的规则最终都会失效。**

---

# Player 还有一个数据路径问题

Player 在读取 Manifest 之前，就用进程文件名建立持久目录：

[GamePlayerHost.cs](/Users/aaronliao/Dev/GameEngineDev/InnoEngine/src/engine/Inno.Player/GamePlayerHost.cs:51)

路径是：

```text
LocalApplicationData/InnoEngine/<executable-name>
```

而不是稳定的 `applicationId`。

这意味着：

- 重命名 exe/product 后找不到原存档；
- 不同应用使用相同产品名可能冲突；
- `applicationId` 没有真正承担平台身份职责。

持久目录必须由 Manifest 中的稳定 application ID 决定。

---

# 测试结论

我执行了整个 `InnoEngine.sln` 的测试：

- 总计 756 个测试；
- 首次全量运行：755 通过，1 失败；
- 失败位于 Script Compiler 的 Roslyn metadata namespace 加载，抛出内部 `NullReferenceException`：
  [ScriptCompiler.cs](/Users/aaronliao/Dev/GameEngineDev/InnoEngine/src/editor/Inno.Editor.Scripting/Compilation/ScriptCompiler.cs:272)
- 单独重跑失败测试：通过；
- 单独重跑完整 Scripting 测试项目：143/143 全部通过。

所以当前不是稳定的功能失败，但存在一次可复现记录的**非确定性测试/编译环境问题**。脚本编译属于关键链路，不应简单当成 Roslyn 偶发问题忽略。

另一方面，当前 Game Export 测试主要使用 Fake Player Publisher：

[GameExportServiceTests.cs](/Users/aaronliao/Dev/GameEngineDev/InnoEngine/tests/Inno.Editor.Exporting.Tests/GameExportServiceTests.cs:227)

还缺：

- 真正 `dotnet publish` 当前平台 Player；
- 启动导出的 Player；
- 加载 Scene；
- 加载纹理与 Shader；
- 至少渲染若干帧；
- 检查退出码和日志；
- Windows x64 的目标机 CI 验证。

正因为缺这个 E2E，纹理 source-path 问题没有被发现。

---

# 做得非常好的部分

这些部分建议保留架构方向，不要推倒：

- ModuleHost 的 collectible ALC、候选加载、prepare/activate/rollback/complete、卸载监控。
- TypeCache/Registry 的 generation snapshot 与原子切换。
- Asset Catalog、persistent ID、content-addressed Artifact、runtime source-free catalog。
- Plugin 和 Project 共用同一 Asset Database，没有创建第二套插件资产系统。
- Editor Extension 的发现、构造注入、稳定 ID、quarantine、状态恢复。
- History 的协议化 payload、预算和 reload-safe handler。
- Rendering Core 的后端中立、开放 ID、RenderGraph hazard/culling/aliasing。
- ShaderGraph 与 handwritten shader 共用编译链的方向。
- Play Mode 的 Scene/History 隔离。
- GameBehavior/GameSystem 统一 lifecycle。
- Scripting API 的显式导出和逻辑 namespace。
- 没有发现随处散落的 TODO、HACK、临时 fallback 或 legacy compatibility 分支。

这些足以说明你的系统不是堆功能，而是在认真构建一个可演进的平台。

---

# 建议整改顺序

## 第一阶段：修正确性

1. 修复导出 Player 的纹理 Artifact 加载。
2. 增加真实 Player 启动与渲染 E2E。
3. 持久目录改用 application ID。
4. 查明 Scripting 测试的非确定性 Roslyn 异常。
5. 删除全部生产和测试 `InternalsVisibleTo`。

## 第二阶段：建立真正的 Build Pipeline

1. `ProjectBuildSnapshotLease`。
2. 持久化 `GameBuildProfile`。
3. 每个平台独立 BuildSupport 包。
4. Shader/Texture 离线目标编译。
5. Build cache、fingerprint、report。
6. Signing/notarization/package stages。
7. Player 不再依赖源码 checkout，不再携带编译工具。

## 第三阶段：强制架构规则

1. 根级 `Directory.Build.props`。
2. `global.json`。
3. `Directory.Packages.props`。
4. Architecture tests/analyzer。
5. XML 警告统一为 error。
6. CI 检查依赖方向和禁止 friend assembly。

## 第四阶段：降低长期维护成本

1. 把 static Manager 收口到实例化 Host，保留静态 facade。
2. 拆分 `AssetLoader`、`ScriptManager`、`RenderResourceService` 等巨型职责中心。
3. Serialization 支持 sealed concrete nested object 默认处理。
4. 编译 API 改为 ticket/generation lease。
5. 统一 extension failure diagnostics。
6. Plugin 使用 watcher/后台 fingerprint，并计算真实依赖闭包。

最终判断：**核心值得继续建设，而且很有潜力；现在最大的问题不是底层设计差，而是发布链仍停留在“功能刚打通”，同时少数边界规则没有自动执行。把 Export/Player 和机器可验证的架构规则补齐后，这套系统才真正能称为现代、无明显妥协的引擎架构。**

