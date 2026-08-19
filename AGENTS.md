# InnoEngine 开发规范（AGENTS）

## 1. 项目风格目标
- 保持当前仓库一致的可读性与可扩展性。
- 优先保持层次清晰、边界明确、低耦合。
- 所有新增注释默认使用英文（尤其是公开 API）。

## 2. 命名规范
- 文件名与主类型名保持一致。
- 命名空间与目录层级保持一致。
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
- 对外公开成员（`public`）优先补齐 XML 注释：
  - `/// <summary>`（必需）
  - 必要时补 `param` / `returns` / `exception`
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
- `src/core`, `src/engine`, `src/assets`, `src/editor`, `src/platform`, `build`, `tests`
- 新文件尽量放置在匹配现有分层与职责目录。

## 11. Wiki 文档维护
- API Wiki 统一位于根目录 `docs/`，入口为 `docs/README.md`。
- Wiki 目录优先映射源码分层：`docs/core`、`docs/assets`、`docs/engine`、`docs/editor`、`docs/platform`；每个分类必须有 `README.md` 索引。
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
