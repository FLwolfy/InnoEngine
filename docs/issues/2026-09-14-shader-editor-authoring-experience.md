# Shader Editor 创作体验与函数库模型收口

[Issues 索引](README.md) · [Shader Editor](../editor/Inno.Editor.Panel.ShaderEditor.md) · [Shader 模型](../render/Inno.Rendering.Shaders.md)

## 目标与不变量

本轮解决 Shader Editor 默认图方向、自动排版、源码函数创建、菜单、Group、诊断、Output 与统一图标问题。修改必须保持 `.ishader` 是唯一 Shader 图资产、`.ishadersource` 只提供可调用函数、`.imaterial` 只保存使用配置；不恢复完整源码 Shader、MaterialGraph 或 Adapter 旁路。

扩展发现遵守新增的仓库规则：当基类或接口已经完整表达扩展身份时，直接按类型关系发现，不再要求无参数 marker Attribute。Attribute 只在需要携带稳定 ID、顺序、作用域、允许多实例或其他显式语义时使用。Shader Target、Template、节点编译器和源码 Frontend 因此分别按 `ShaderTarget`、`ShaderGraphTemplate`、`IShaderNodeCompiler`、`IShaderSourceFrontend` 发现。

## 完成内容

| 项目 | 实现 |
| --- | --- |
| 阅读方向 | 内置 Rendering2D 与 ImGui 当前图已重排为输入在左、计算居中、Output 在右；新建内部图沿用相同方向 |
| Format | 顶部 `Format` 对当前草稿执行确定性的依赖布局；只修改节点坐标，进入一次 History，可 Undo，不触发语义变化 |
| Check | 顶部 `Check` 隔离编译当前草稿，不保存或发布；移除右键诊断项，使用共享短暂 Modal，完成后自动关闭并把结果发布到 Console |
| 函数库 | 一个 `.ishadersource` 可在 Import Settings 显式列出多个公开函数；每个函数独立分析、冻结和选择，未列出的函数是私有 helper |
| 创建菜单 | 删除含糊的 `Source` 与 `Source Functions` 双入口；统一为 `Create / Functions / 文件 / 函数`，并按 Outputs、Inputs、Textures、Math、Resources、Flow、Utility、Domain 分类 |
| 菜单一致性 | 主菜单分为 Create、View、Edit、Connections、Organize、Assets；共享 Menu Catalog 将分隔要求传播到子菜单，不另造 Shader 专用菜单样式 |
| Group | Group 有稳定身份和选择状态；点击或右键组标题会选择其成员，拖动组标题作为一次手势整体移动成员，Ungroup 作用于选中组 |
| Reroute | `Insert Reroute` 只在选中连接线上插入一个强类型透传点，用于整理长线和转弯；公共 IR 不增加运算，不改变副作用或生成结果 |
| 源码定位 | `Show Source in File Browser` 定位并选中引擎资产，不再调用操作系统打开外部程序 |
| Output | 移除 Create Pass 创作入口；Vertex、Fragment、Compute Output 分别创建，每种 Stage 在一个低级图中最多一个；Paste/Duplicate 不能绕过不变量 |
| Output 删除 | 删除 Output 会清理所有引用 Pass、失去引用的配对 Stage 内容、Technique 映射和失去最后使用者的参数声明，不留下隐性不完整 Pass |
| 统一图标 | Shader、Shader Source、Material、Render Pipeline 的 File Browser、Inspector 与 Header 共用同一资产图标解析；四个图标均可从 `Editor / Appearance / Icons` 修改 |
| 当前资产 | Rendering2D 七个 `.ishader`、十二个源码 sidecar，以及内置 ImGui 图和源码 sidecar已写成当前函数库格式；源码节点显式保存所选函数 |

## 2026-09-14 晚间体验收口

- Format 改为端口感知的分层布局：反向依赖建立列，交替重心扫描减少相邻层交叉，并用目标输入端口顺序排列多个来源；无效草稿中的环不会让排版死循环。
- 动态菜单新增显式 Group placement。分隔线只属于声明它的同级分组，不再从叶子泄漏到 `Create`/`Functions` 等父菜单；Rendering2D 的源码 sidecar 通过 `catalogPath` 贡献 Sprite、Lighting、Shadows、Post Processing/Bloom 分组。
- 节点 Drawer 的呈现元数据可声明 `createPath`、`createOrder` 与 `separatorBefore`；Rendering2D 的 Sprite Surface Output 与 Sprite Texture 因此由插件自主进入 `Rendering 2D` 分组，通用 Shader Editor 不识别 Sprite。
- `.ishader` 默认图标改为 `S`；`.ishadersource` 默认使用函数符号，二者继续通过 Appearance 设置和统一 Asset Icon Provider 驱动 File Browser、Inspector 与 Header。
- Shader/节点 Inspector 全部复用通用 Property Row，label 位于左侧、控件位于右侧。Shader Inspector 移除编辑命令，只保留始终显示的草稿 Preview 与 Shader 数据。
- Shader Editor 使用带 padding 的居中空状态；编辑状态使用两行目标 Header：Shader 下拉选择在上，Save/Revert/Format/Check 在下，不显示大图标。
- Check 移入全局 Editor Modal Host，与脚本/插件重载共享默认固定宽度、居中、遮罩、淡入淡出和阻塞生命周期；不再显示诊断正文或进度条，完成后无论成功失败均自动关闭，诊断进入统一 Console。
- 全引擎扩展 marker 审查删除了 `AssetImporterExtension`、`AssetBuildProcessorExtension`、`SerializationExtension`，以及从未被 Registry 消费且与 `GraphNodeDefinition.id` 重复的 `GraphNodeExtension`。Importer、Build Processor、Converter 现在只按各自基类发现；静态图节点由领域 Registry 按基类或接口发现，数据驱动节点由领域 resolver 创建。保留的 Attribute 仅用于源码生成、AllowMultiple、强制 Converter、脚本 API 排除或需要参数的注册语义。

## 边界说明

- `ShaderTarget` 的继承与原无参数 `[ShaderTarget]` 确实表达了同一身份，双重要求没有增加语义，只制造漏标和热重载时序风险，因此删除 Attribute 是结构简化，不是放宽校验。
- `[EditorAction(...)]`、`[AssetIcon(...)]`、`[InspectionDrawer(...)]` 等仍合理，因为它们携带 action ID、区域、扩展名、优先级或目标类型等注册参数。
- Format 属于 Editor 视图状态；Save 才会把排版写入资产，未保存排版不影响 Scene/Game 或 canonical artifact。
- Output 节点定义 GPU 阶段的最终接口；Pass 仍作为 Shader 定义中的渲染状态和 Role 绑定存在，但不再作为一键复制阶段节点的创作对象。
- 多函数源码库仍由对应 Adapter Frontend 解析函数声明。不存在隐式 `main`；用户通过 Import Settings 明确决定哪些声明进入图 API。

## 验收

2026-09-14 实施结果：

| 验收项 | 结果 |
| --- | --- |
| `Inno.Rendering.Shaders.Tests` | 125/125 通过；覆盖基类/接口发现、源码解析、Target、共享程序与 IR lowering |
| `Inno.Rendering.Assets.Tests` | 28/28 通过；新增同一源码库显式导出多个图函数的导入测试 |
| `ShaderEditorWorkflowTests` | 40/40 通过；新增端口次序排版、源码函数目录分隔、插件节点自主分组、稳定 Check Modal、Header/空状态相关生命周期覆盖 |
| `Inno.Editor.Scripting.Tests` | 120/120 通过；包含裁剪 Script API、插件 Importer 基类发现与完整 Shader Editor 工作流 |
| `Inno.Core.Graphs.Tests` | 6/6 通过；移除无消费者 `GraphNodeExtension` 后，动态端口解析与 resolver 校验保持有效 |
| `Inno.Core.Serialization.Tests` | 33/33 通过；Converter 仅按基类发现，生成 Converter 不再附加 marker |
| `Inno.Assets.Pipeline.Tests` | 112/112 通过；Importer 与 Build Processor 仅按基类发现 |
| `Inno.Scene.Tests` | 49/49 通过；Scene/Prefab Importer 与 Scene Converter 自动发现无回归 |
| `Inno.Editor.Interactions.Tests` | 96/96 通过；共享菜单分隔与交互基础无回归 |
| `Inno.Editor.Inspection.Tests` | 1/1 通过；Drawer 激活边界保持有效 |
| `Inno.Tooling.Architecture.Tests` | 47/47 通过；项目边界、公开面和 legacy 约束无回归 |
| Editor 全量构建 | 本轮再次以 `-warnaserror` 构建，0 warning / 0 error；内置 ImGui 图成功生成 Metal、Vulkan、OpenGL 产物 |
| Plugin EditorScripts | Editor 生成当前 Script API 后 `-warnaserror`，0 warning / 0 error |
| Metal 实机 | 当前源码充分预热 6000 帧 Editor smoke 通过；Rendering2D/Shader Editor 扩展成功加载，达到 frame limit、无活动 Error，BGFX `Shutdown complete`。严格 300 帧冷启动检查会在异步 Post-process 尚未发布时正确报告 unavailable，不以静默降级掩盖未就绪输出 |

实机首次验证还发现并修正了 Inspector Drawer 同时请求两个注入依赖的问题：图标解析现在经已有文档模块取得 AssetPipeline，Drawer 构造只依赖公共图标提供者，脚本 generation 可以原子激活。该修复没有扩大 Inspector composition root，也没有引入服务定位 fallback。

Windows D3D11、D3D12、Vulkan 实机验证沿用用户此前决定，当前未执行，不能由本机 Metal 结果替代。
