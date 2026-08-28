# InnoEngine 开发规范（AGENTS）

## 1. 项目风格目标
- 保持当前仓库一致的可读性与可扩展性。
- 优先保持层次清晰、边界明确、低耦合。
- 所有新增注释默认使用英文（尤其是公开 API）。

## 2. 命名规范
- 文件名与主类型名保持一致。
- 默认命名空间与目录层级保持一致；`src/editor` 使用第 13 节定义的项目级命名空间规则。
- 类型名使用 `PascalCase`。
- 接口以 `I` 前缀。
- 成员参数使用语义化 `camelCase`。
- 私有字段使用 `m_` 前缀。
- 常量：`C_` 前缀（如 `C_MAX_COUNT`）或语义清晰的 `readonly static` 命名。
- 代码中的 Unity 风格 API（现有的如 `transform`、`scene`、`active`、`name`）保持周边兼容。

## 3. 成员声明组织（重要）
- 在类中按访问级别及角色分组，尽量放在类顶部：
  1. 常量（const / static）
  2. 字段（m_）
  3. 构造函数
  4. 公共属性 / 方法
  5. 受保护/内部成员
  6. 私有方法与工具函数
- 一个类内同作用域同类型成员应尽量集中放置，避免在类中散落。
- “public 成员优先”：先写公开成员，再写受限成员。

## 4. 封装与职责边界
- 公开 API 以最小必要暴露原则实现。
- 组合优先、继承次之。
- 通过注册器 / 工厂 / 抽象接口解耦。

## 5. 注释规范（必须英文）
- 所有对外公开成员（`public`）以及可由外部派生类型重写的 `protected` 成员必须具有完整的英文 XML 注释：
  - 类型与成员必须包含有实际说明意义的 `/// <summary>`，不得只重复成员名称。
  - 每个参数必须有对应的 `/// <param>`；每个泛型参数必须有对应的 `/// <typeparam>`。
  - 非 `void` 方法必须包含 `/// <returns>`，并说明返回值语义以及失败/空值状态。
  - 对调用者可观察的重要异常必须使用 `/// <exception>` 说明触发条件。
  - 重写或实现成员可以使用 `/// <inheritdoc />`，但新增的约束、异常或语义必须在当前成员补充说明。
- 启用 XML 文档输出的项目应将 `CS1572`、`CS1573` 和 `CS1591` 视为编译错误，防止无效参数标签、遗漏参数说明或缺少公开成员注释。
- 关键的 `internal` API 如果对行为有关键影响，也建议补齐 XML 注释。
- 禁止中文注释；临时注释（如 TODO）避免长期留存。
- 复杂逻辑可添加完整英文短句注释。

## 6. 错误处理
- 参数校验优先：`ArgumentNullException.ThrowIfNull`、清晰的 `ArgumentException`。
- 安全失败与异常失败分离。
- 对关键边界返回明确异常信息。

## 7. 命名与清晰度（Transform / Identity 相关约定）
- 对齐上层语义，保持现有 API 命名风格。
- 本地变量避免与属性名重合，减少 shadowing 与可读性冲突。
- 属性值更新后应保持内部缓存一致性，避免重复实时全量计算造成不必要代价。

## 8. 测试边界
- 除非我明确要求，否则不改 tests 部分。
- 测试只在变更公共行为契约且你明确要求时调整。

## 9. 编译安全约束
- 每次对话结束前，当前改动目标范围内（非 tests 以外）不应引入可见编译错误。
- 发现潜在编译风险时，要在提交说明中显式标注。

## 10. 目录边界
- `src/core`, `src/engine`, `src/assets`, `src/render`, `src/editor`, `src/platform`, `build`, `tests`
- 新文件尽量放置在匹配现有分层与职责目录。

## 11. Wiki 文档维护
- API Wiki 统一位于根目录 `docs/`，入口为 `docs/README.md`。
- Wiki 目录优先映射源码分层：`docs/core`、`docs/assets`、`docs/engine`、`docs/render`、`docs/editor`、`docs/platform`；每个分类必须有 `README.md` 索引。
- 默认每个 `.csproj` 对应一个独立 Markdown 项目页，文件名使用完整项目名，例如 `docs/core/Inno.Core.Reflection.md`。
- 项目页至少包含：职责与边界、依赖/初始化顺序、所有 `public` API、面向派生实现者的重要 `protected` 扩展点、常见工作流、可编译风格示例、错误/生命周期/热重载注意事项、相邻页面导航。
- API 表格与示例必须以当前源码为依据；不得把 `internal` 实现描述成稳定公开契约。若解释内部机制，应明确标注其非公开性质。
- 新增、删除、重命名或改变公开 API 行为时，在同一变更中同步对应项目页和分类索引。新增项目时同步创建项目页并加入 `docs/README.md` 的覆盖状态。
- 多页之间使用相对 Markdown 链接；每个项目页顶部至少提供分类索引和 Wiki 首页/相邻页面入口。移动页面时必须修复所有入站链接。
- Wiki 正文默认使用中文以便项目查阅；API 名称、代码、代码注释和公开 XML 注释保持英文。不要复制大段源码，用小而完整的示例解释组合方式。
- 文档应区分“当前稳定行为”“内部实现细节”“未来规划”，不得把规划写成已经存在的 API。
- 续写前先从对应目录运行公开类型/成员检索并阅读相关 `.csproj` 依赖；完成后检查 Markdown 链接、页面索引和源码签名是否一致。

## 12. Scripting API 清单
- 参与脚本 API 的每个项目只允许一个 `Properties/ScriptingApi.cs`，不得把导出 attribute 分散到业务源码或集中到一个反向依赖所有模块的清单项目。
- 使用 `ScriptingApiExport` 逐类型显式导出；禁止恢复按程序集暴露全部 public API 的 metadata/property 机制。
- 使用稳定脚本分组名（如 `InnoEngine.Scene`、`InnoEngine.Mathematics`、`InnoEditor.Inspection`）和 `ScriptingApiNamespace` 映射真实 CLR namespace。
- 新增模块（如 Rendering）只修改自己的 `Properties/ScriptingApi.cs`，不得在 Editor 编译器中维护中央程序集/type 白名单。
- 运行时编译和 IDE project 必须共用同一组裁剪 reference assemblies；修改清单后需同时验证两条路径。
- 完全禁止 compilation-wide/global using，包括手写指令、MSBuild `Using` item、隐式导入和通过 metadata 注入。所有源码与脚本必须在使用它们的文件中显式声明普通 `using`。
- 脚本必须使用逻辑 namespace（如 `using InnoEngine.Scene;`），不得直接使用实现侧 `Inno.*` namespace。

## 13. Editor 项目组织与引用边界
- `src/editor` 中每个项目的业务源码统一使用与 `.csproj`/程序集名称完全相同的命名空间；功能目录只负责组织文件，不追加到命名空间。例如 `Inno.Editor.Inspection/PropertyDrawing` 中的类型仍使用 `namespace Inno.Editor.Inspection;`。
- 可复用的 InspectionDrawer、PropertyDrawer、Registry 与 serialized property renderer 统一属于 `Inno.Editor.Inspection`；业务 Panel 只在自身项目中实现具体 Drawer，不得为了扩展检查显示而引用 `Inno.Editor.Panel.Inspector`。
- 唯一命名空间例外是 `Inno.Editor.ImGui/Widgets`：其中所有类型使用 `namespace Inno.Editor.ImGui.ImGuiWidget;`。
- `Inno.Editor.ImGui/Widgets` 只允许 `ImGuiWidget.*.cs` 文件。Widget 的 presentation、options、result 与私有状态应收口到对应的 `ImGuiWidget.<Feature>.cs`，不得创建独立的 Widget helper 文件。
- Editor 项目内部按可独立理解的功能建立目录（如 `Interactions`、`Documents`、`Zoom`、`PropertyDrawing`）；同一功能内的 Action、Menu、DragDrop、Runtime 与 Presentation 不得仅按类型角色机械拆成多个细碎目录。禁止使用含义模糊的 `Internal` 目录，访问级别由 C# 声明表达，不由目录名表达。
- Editor `.csproj` 的 `ProjectReference` 必须按公开 API 边界分组：第一个 `ItemGroup` 只放未出现在任何 public/protected API 中的实现依赖，并逐项设置 `PrivateAssets="compile"`；第二个 `ItemGroup` 只放公开签名、公开基类或公开接口实际泄漏的依赖。没有使用的引用应直接删除。
- `ProjectReference` 是否公开必须根据真实 API 签名判断，不能因为运行时会使用某程序集就默认向下游传递。调整公开类型、基类、参数、返回值或属性后，应同步复核引用分组。

## 14. Editor History 与 Workspace 状态
- 可逆 Editor 数据修改统一进入 `EditorInteractions.history`；不要在 Panel 中维护第二套 Undo 栈，也不要把简单 `inverse Action` 当作通用模型。
- Feature Module 先完成领域修改，再用 `RecordApplied(name, EditorHistoryChange)` 记录；History payload 只能保存 stable protocol kind、persistent ID、Stable Type ID、路径、索引、标量和中立序列化 bytes，禁止捕获 runtime 对象、插件 `Type`、extension 实例或来自 collectible ALC 的委托。
- 每个 reload-safe 协议必须声明 `[EditorHistoryHandler(kind)]`。Handler 的 `Query` 只检查当前 generation 可用性；`Apply` 必须在失败时回滚本次部分修改。Handler Registry 与其他 Editor Registry 一起候选构建和原子切换。
- `RecordValue`、委托式 `Execute` 与派生 `EditorHistoryOperation` 只允许 Host-only 兼容流程；这些 runtime-bound entry 会在 extension generation 改变时截断，不得用于 EditorScripts 或 Scene/Asset 等长期记录。
- 稳定 `mergeKey` 只用于同一个逻辑值的连续输入；布尔开关、创建、删除和排序不得合并。多步骤修改使用 `BeginTransaction`，但每个 child 仍必须独立原子化。
- Undo/Redo 失败时必须保持操作位于原栈，禁止移动指针或覆盖新状态。新操作必须释放 Redo 分支；被淘汰或清除的 operation 必须释放其文件、对象或插件代际引用。
- 大 payload 使用 `EditorHistoryOptions` 自动溢出到 `<Project>/Library/Editor/History`；History 受 entry、resident bytes 与 disk bytes 三重预算限制，缓存不进入 `editor.ini`、Asset metadata 或 Scene 序列化。
- 保存、打开、选择等纯工作流操作默认不进入数据 Undo；它们只有在确实修改项目数据时才记录对应的数据部分。
- 跨启动的项目语义状态直接属于 Module/Panel；它们使用 Attribute 中必填、稳定且全局唯一的 ID，并通过 protected `Capture(EditorState)` / `Restore(EditorState)` hooks 参与持久化。扩展只调用参数对象的 `Get` / `Set`，不得在公开或 protected API 中暴露 JSON 实现。未 override Capture 的类型不进入状态 IO；不得恢复独立 Workspace interface、reader/writer 或第二个状态 ID。持久值只允许可重新解析的中立数据。
- `editor.ini` 是统一且可读的项目级 Editor settings 文档：标准 ImGui section 保存 layout；每个 Module/Panel 分别使用 `[InnoEditor][Module.<id>]` / `[InnoEditor][Panel.<id>]`；Panel 开关使用 `[InnoEditor][Panels]`。禁止用 Base64 或单一 opaque payload 包装全部 Workspace。Undo 栈、dirty Scene 内容、runtime 引用和编译中间态不得持久化。
- Editor Selection 是当前 session 的瞬时交互状态，不得写入 `editor.ini`。Workspace 可以保存可独立解释的导航位置、已打开文档和 active document，但启动后不得自动恢复 Asset、Scene、GameObject、Component 或 System selection。
- Editor 正常退出时必须先捕获全部有状态的 Module/Panel，再捕获最新 ImGui layout，最后在 Module 停止和 Scene 卸载前强制原子写入一次完整 `editor.ini`。运行期间仍可节流保存，但不能把它当作退出保存的替代品。
- Editor Scene 修改统一进入 `Inno.Editor.Scene.SceneEdits`。普通属性只保存单 property bytes；Component/System 保存 element identity/type/index/state；GameObject 删除保存最小 subtree；层级只保存受影响 placement；禁止为小修改序列化或恢复完整 Scene。
- Module/Panel 状态恢复必须容忍缺失 Asset、损坏 payload 和脚本类型尚未进入 TypeCache。候选未完整准备好前不得破坏当前可编辑状态。
- 有状态的 Module/Panel 必须先成功执行一次 protected `Restore`，之后才允许 `Capture` 覆盖磁盘 section。扩展 Registry 在启动或脚本激活期间可能重入刷新；恢复协调器必须按 Module/Panel 实例弱跟踪 `restoring/restored` 状态，禁止重入回调，也不能因为实例被新 snapshot 保留就误判其已经恢复。
- Scene Workspace 恢复时必须区分“源文件确实缺失”和“Asset Source Index 尚未完成首轮对账”。物理源仍存在时应保留 pending scene setup 并重试，不能用暂时为空的运行时 Scene 集合覆盖项目设置。Editor 允许没有任何已加载 Scene，不得为恢复、启动或删除最后一个 Scene 隐式创建 Untitled Scene。

## 15. 禁止 Legacy 兼容与 Schema Version
- InnoEngine 是自用且始终按当前源码、当前 Project 数据共同演进的引擎。新增或修改功能时不支持旧版文件、旧版 schema、旧字段、旧缓存目录、旧 namespace、旧 API 或旧序列化布局，也不得为它们添加 fallback reader、migration、compatibility alias、former ID、deprecated wrapper 或双写逻辑。
- 持久化模型、Attribute、Importer、Build Processor、History、Workspace、Scene、Prefab、Asset metadata、Catalog 和脚本 manifest 不得引入用于 legacy 适配的 `version`、`schemaVersion`、`formatVersion`、`formerVersion` 等字段。格式发生变化时直接更新当前 writer、reader、测试、文档和当前 Project 数据；旧数据可以明确失效并重新生成。
- 不得因为“未来可能兼容”预留迁移分支。只有用户在具体任务中明确要求导入某一种旧格式时，才可以实现一次性、边界清晰的转换工具；转换逻辑不得进入正常运行路径。
- 删除或重构 API 时同步修改所有调用方，不保留旧 overload、旧 namespace facade、转发类型或 `[Obsolete]` 兼容层，除非用户明确要求保留。
- 以上规则不禁止保障当前运行正确性所需的运行时标识，例如 TypeCache/Assembly generation、并发 revision、change counter、content hash、artifact fingerprint、MVID、job handle generation，以及只用于拒绝损坏或错误格式输入的严格 magic/header。它们不得演变成读取多代 legacy schema 的兼容机制。
- 代码审查或清理包含 `version`、`legacy`、`migration`、`compatibility`、`former`、`deprecated` 等名称的实现时，必须先判断其是否只服务于旧数据/API；如果是，应连同测试和文档一起删除，而不是继续扩展。

## 16. 完成提示音
- 在设计阶段等待用户作出会改变方案边界的决策前、最终设计完成后，以及完成用户要求的代码或文件操作并通过必要验证后，默认播放 `/System/Library/Sounds/Glass.aiff` 作为提示音，无需用户在每次任务中重复要求。
- 如果当前环境无法访问音频设备，应在最终结果中明确说明提示音未能播放；用户明确要求静默时不播放。

## 17. Rendering 强制边界
- Rendering 的公开设计必须同时满足：跨平台、API 易用、扩展灵活和低耦合。不得以实现便利为由破坏其中任一项。
- 只有 `Inno.Rendering.Bgfx` 可以引用 `Inno.Native.Bgfx`。BGFX handle、View ID、原生指针和 BGFX 枚举不得出现在其他项目的 public/protected API 中。
- `Inno.Rendering.Core` 必须保持后端中立，且不得引用 Scene、Assets、Editor 或任何具体图形后端。上层模块通过资源描述、能力集合、RenderGraph 和命令编码接口工作。
- 通用 Graph 不得引用 Rendering 或 ImGui；Rendering 也不得反向引用 ShaderGraph 或 Editor Graph。ShaderGraph 只能作为面向 Rendering 契约的上层编译前端。
- 手写 Shader 与节点生成 Shader 必须进入同一个 Shader IR、编译、反射、验证和产物缓存链；不得维护第二套节点专用 shader 编译路径。
- Pipeline、Feature、Pass、Shader Node、GPU 资源与编译产物必须 capability-aware、generation-scoped 且 reload-safe。持久状态只保存 Stable ID 与中立数据，禁止长期保存 collectible ALC 的 `Type`、delegate 或 runtime 对象。
- Project 脚本扩展只允许使用后端中立 Rendering API。扩展失败必须隔离，候选成功后只能在帧安全点原子切换，并保留 last-good Pipeline、Shader 和 GPU 资源。
