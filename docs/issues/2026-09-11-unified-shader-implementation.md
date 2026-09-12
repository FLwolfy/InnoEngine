# 统一 Shader 实施记录

本记录区分已经实现的行为与尚未完成的计划，不作为完成验收报告。

[问题索引](README.md) · [Shader 创作 API](../render/Inno.Rendering.Shaders.md)

## 2026-09-12：显式保存取代自动保存

按用户最新要求，Shader 编辑现在是草稿，只有 Save 才写入资产并应用。此前记录中的“自动保存”是历史设计，
不再表示当前行为。删除/断线/类型/源码事务缺少 Commit、文件条目与资产身份混用、阶段声明清理及 Console 标签裁剪
已修复。此次 242 项测试通过、Editor 构建 0 warning / 0 error；范围与证据见
[Shader 草稿保存与 Console 布局验收](2026-09-12-shader-drafts-and-console.md)。整套 Shader 计划的其他验收缺口仍独立保留。

## 2026-09-12：Shader Editor 缩小断言修复

- 根因：画布末尾用 `SetCursorScreenPos` 移到理论底边，没有随后提交布局 item。
  ImGui 的 item 高度计算会取整，非整数 UI spacing 下已登记底边可能略小于理论底边；
  窗口缩小、节点裁剪后，这个差值触发 `ErrorCheckUsingSetCursorPosToExtendParentBoundaries`，导致原生进程中止。
- 修正：完成绝对定位的节点控件绘制后，回到画布原点，用 `Dummy(canvasSize)` 正式提交整个画布占位。
  不关闭断言、不修改 ImGui 原生实现、不限制用户缩小窗口，也不改变 Shader、资产或 Rendering Core。
- 原生回归：同一测试在修改前以用户报告的相同断言中止，修改后通过。
  新增 10 组空图/有节点图用例，覆盖 100%、110%、125%、150%、200% UI spacing / framebuffer scale，
  从 1200×800 连续缩小至 32×32、极窄/极矮再恢复，共 480 个 resize/稳定布局帧；检查图 revision 和磁盘内容未被 resize 修改。
  使用真正 native ImGui，保持断言开启；不将此测试视为 Metal 像素或手工桌面拖拽验收。
- Shader Editor 工作流：18 passed / 0 failed / 0 skipped，包含原鼠标拖动、Undo/Redo、自动保存、冲突和诊断流程。
- 完整 Editor Scripting 测试组：96 passed / 0 failed / 0 skipped，耗时 1 分 18 秒；上述 18 项包含在内，不重复计数。
- 完整 Editor 构建：0 warning / 0 error，12.69 秒。
- 命令（主仓库，使用 `/Users/aaronliao/.dotnet/dotnet`）：
  `test tests/scripting/Inno.Editor.Scripting.Tests/Inno.Editor.Scripting.Tests.csproj --no-restore --filter 'FullyQualifiedName~ShaderEditorWorkflowTests' -v quiet`；
  `test tests/scripting/Inno.Editor.Scripting.Tests/Inno.Editor.Scripting.Tests.csproj --no-build --no-restore -v quiet`；
  `build src/composition/editor/host/Inno.Editor.Application/Inno.Editor.Application.csproj --no-restore -m:1 -p:UseSharedCompilation=false -v quiet`。
- 原始日志与改动前快照：`/private/tmp/inno-shader-resize.xCrTSP/`。

## 最新检查点：2026-09-12 01:20（Asia/Shanghai）

**代码已进入资产替换、纯画布编辑与自动保存闭环；整体验收仍未完成，不宣称全部交付。**
用户已明确授权删除旧完整 Shader API；Windows 验证仍按用户要求暂缓。

### 当前改动边界

- 主引擎：移除旧完整源码 ShaderIR / ShaderSourceAsset 与编译重载；移除 MaterialGraph 类型、独立 importer、旧 Panel、嵌入材质图 metadata、项目/脚本/API 文档引用。
  `.ishader` 是唯一 Shader 图资产，`.ishadersource` 只能作为函数模块，`.imaterial` 仅保存 Shader、参数与变体。
- ImGui：同一图链离线构建并嵌入目标产物，运行时缺失产物明确报错，没有启动源码编译旁路。
- Rendering2D：五个 Sprite Raster Pass 已替换为图 + 函数；保留资产 ID、材质参数、光照/阴影/Bloom 语义。
  验证脚本调用统一图编译器；资源稳定性验收现在计数实际完成的渲染帧，不以 CPU Build 次数冒充渲染。
- 通用 Assets：命名输出支持 AuthoringOnly，runtime 导出校验并投影新的内容地址包，不携带图/源码。
- Runtime：定义、绑定布局及已使用 Pass 的 GPU 程序在安全帧边界整批发布；失败保持完整 last-good，Pending retirement 保持所有权。
- Editor：跟随 File Browser 的纯 Shader 画布、共享菜单搜索、Scene/Graph 通用二维导航、类型过滤创建、分组/转接/重连、节点内属性和预览、Pass/Technique/Role/存储设置弹窗。
  源函数 Import Settings 使用通用 sidecar + History。缺失端口保存中立身份和类型，明确重绑定，不按位置误接。
- 自动保存：结构/手势完成保存，连续输入 300 ms 防抖；两次拖动分别 Undo；切换/关闭冲刷；无效图可保存；外部冲突拒绝覆盖并保留恢复数据。
  检查和原子替换之间的外部写入保存为 `.external-conflict-*`，不是操作系统级 CAS。
- Headless Build：先激活 authoring 脚本/importer/图扩展，再生成独立 Player runtime 程序集。编译期 Inspector Attribute 不进入部署程序集。

未将 Sprite、Light2D、Camera2D 领域类型加入通用 Core。原 `extern/cimguizmo` 脏状态未改动，未提交 commit。

### 本轮验证证据与保留项

日志目录：`/private/tmp/inno-shader-assets.3W4LFV/`。

| 验证 | 当前证据 |
| --- | --- |
| Shader 测试 | `shaders-final-tests.log`：110 passed；含真正 native shaderc 编译 |
| Assets Pipeline | `pipeline-final-tests.log`：101 passed |
| Rendering Assets | 25 passed；含 runtime 导出不携带 graph/function 输出、身份和定义不变 |
| Shader Editor 工作流 | `shader-workflow-final.log`：8 passed；实际 ImGui 鼠标事件完成两次拖动、autosave、分别 Undo；诊断菜单与 Import Settings 的有效/无效状态 Undo/Redo、外部冲突保护 |
| Runtime | `runtime-final.log`：55 passed；新增 Pending 程序退休仍保留所有权，由宿主退出 drain 重试，所有已创建程序只完成退休一次 |
| Editor 构建 | `editor-final-build.log`：0 warning / 0 error，24.63 秒 |
| 真实 Metal 回归 | `metal-full-acceptance-rendered.log` 通过 1,800 帧、96 次实际 resize、120 个稳定渲染帧、32+ 灯和五级 Bloom；发生于 AuthoringOnly 输出拆分之前，不能替代拆分后的最终回归 |
| 性能子门槛 | 预热后的提取/相机/提交与十万 Tile 场景 0 managed bytes；不是整个 Editor 帧零分配证明 |
| 独立 Player 导出 | `player-export.log` 成功原子提交；确认无 Editor、Rendering.Shaders、Core.Graphs、Assets.Pipeline 程序集或 Shader 源文件 |
| Player 启停 | `player-smoke.log`：Metal 初始化、600 帧并正常退出，但统计零绘制；仅算启停，不算画面通过 |
| 全 Solution | `solution-acceptance-final.log`：48 项目，1190 passed / 1 failed / 0 skipped；唯一失败为原生 overlay scrollbar。随后新增 Runtime 退休用例并单独重跑 55 passed，不重复累计旧结果 |

AuthoringOnly 拆分后的 `metal-final-isolated.log` 完成 1,800 帧与干净退出，实际创建了图 Shader、HDR / D24S8 和 Bloom 目标；
CPU 子门槛 P95：提取 0.112 ms、runtime camera 0.104 ms、camera stack 0.107 ms、提交 0.107 ms、十万 Tile 0.095 ms，均 0 managed bytes。
但当前锁屏环境下视口只取得 8×5 / 30×5 等极小尺寸，未达到 96 次 resize 和 120 个稳定帧的验收条件，**该次不算最终 GPU 回归通过**。
`player-readback.log` 使用独立 runtime session 读回实际发布内容：43 个对象、1 个 Rendering2D request provider、Base Camera active/enabled/renderToBackbuffer 均为 true；
这排除了场景/相机丢失，但仍不能代替 Player 实际绘制验收。

`OverlayScrollbarsDoNotReserveWindowContentWidth` 期望 0、实际 14。相关测试、native flag 与 `CimguiSourceOverlay` 均未改动，
本轮开始时已缺少相应 native overlay 实现；未跳过或削弱断言，不能报告全 Solution 通过。

当前项目 `DefaultSprite.ishader` 在 00:25:33 另有新增 Compute 节点，其输入设为 `Builtin / position`，本轮最终验证因此正确拒绝编译。
已向用户询问其归属，**未删除或覆盖这些新增内容**。Player 导出使用隔离临时项目及此前保留的五 Raster Pass 图副本；不以此宣称当前工作区新增图有效。

Mac 锁屏使界面工具无法操作，已请求用户手动解锁；未绕过锁屏。纯画布视觉、DPI/浮动/停靠/边缘 tooltip、
真实 Shader 修改和 Undo 后 Scene/Game 像素一致性、拆分后的最终 Metal resize 仍待完成。
源码诊断弹窗与共享 Import Settings 历史测试已通过；精确 IDE 跳转协议未作跨应用保证。

原记录下方全部为历史检查点，涉及“未获删除授权”“旧 API 仍在”等描述不再代表当前状态。

## 历史检查点：2026-09-11 21:50（Asia/Shanghai）

**未完成；不能作为整个统一 Shader 计划的最终验收，也不建议作为完成态直接提交。**
本节覆盖下方此前基础批次的进度描述。用户本轮要求继续资产替换、Shader Editor、自动保存及 legacy 清理；
Windows 编译和实机验证已由用户明确暂缓。

### 本轮实际改动

| 范围 | 已实现与已观察到的行为 | 尚未完成的边界 |
| --- | --- | --- |
| Shader 图资产 | `.ishader` importer 接到原生 GraphDocument、阶段/Pass 校验、typed program 编译；`.ishadersource` 通过函数 importer 和 `.imeta` 设置进入依赖快照，未完成图可独立保存 | 旧完整源码 ShaderIR API 仍在；不能宣称只有一套实现 |
| Rendering2D 资产 | DefaultSprite 改为 170 节点、195 连线、5 Pass 的图；Vertex/Fragment 改为函数源码模块，保留原 Shader 和源码资产身份 | 插件验证脚本中旧 `.sc` 路径以及现有 Material 图 metadata 的清理仍需完成 |
| ImGui 内置 Shader | 图和函数源码由离线工具生成目标产物并嵌入分发；启动只加载匹配平台/API 的产物，旧启动源码拼装类已删除 | 增量构建必须继续核对工具链 DLL、原生 include 的完整失效输入；Windows 未验收 |
| Shader Editor | 替换旧 Panel；跟随 File Browser 选择，纯画布，无 Save/New/Open 工具栏和固定侧栏；节点内数值/资源输入与阶段/Pass 弹窗；菜单接入 EditorInteractions | 搜索与端口类型过滤创建、完整分组/连线操作、源码定位、预览、复杂目标配置与真实交互验收未闭环 |
| 共享交互 | Scene View 与图画布复用 EditorPlanarNavigation；中键/Alt 左键捕获、鼠标锚点缩放；图选中/框选/移动/连线、Esc/F；每次移动单独 History；粘贴身份重映射进入同一事务 | 大图逐帧分配与性能、不同 DPI、停靠/浮动/resize 仍需专项验收 |
| 自动保存 | 结构修改和完成手势保存，连续数值/文本修改 300 ms 防抖；共享文档和 History；源文件 hash 冲突检查、原子替换、只读拒绝、恢复数据；切换/关闭/退出冲刷 | 写入前 hash 检查与替换之间不是操作系统级 CAS；缺失源文件跨重启恢复、路径重命名后的 Document Host 状态仍需收口 |
| 节点扩展 | Editor-only ShaderNodeDrawer、声明 Attribute 与 Read/Write context；TypeRegistry 代际内调用，和节点编译扩展分开 | 独立绘制扩展的真实脚本热重载/卸载、任意控件高度和完整 UI 回归未验收 |
| Legacy | MaterialGraph 库、Panel、独立 importer、求值器、脚本导出与相关测试项目源码已删除并去除主项目/Solution 引用 | 旧 ShaderIR 模块/validator、ShaderSourceAsset/importer、旧编译 overload、运行时辅助入口及导出引用仍在，删除被安全审查拦截 |

本体增加的图、资源绑定、导入设置、文档/历史/导航能力均属于对应通用层；未把 Sprite、Camera2D、Light2D
等领域类型加入通用 Rendering Core。当前代码仍有下列未闭环项，不能以“可复用”替代正确性验收。

### 当前验证命令与结果

工作目录为主仓库；以下使用 `/Users/aaronliao/.dotnet/dotnet`，常用参数
`--no-restore -m:1 -p:UseSharedCompilation=false -v quiet`。新项目引用先 restore。

| 命令入口 | 本检查点结果 |
| --- | --- |
| `test tests/rendering/Inno.Rendering.Shaders.Tests/Inno.Rendering.Shaders.Tests.csproj` | 107 passed / 0 failed / 0 skipped；含原生 shaderc 编译，不等同于 GPU 执行 |
| `test tests/rendering/Inno.Rendering.Assets.Tests/Inno.Rendering.Assets.Tests.csproj` | 22 passed / 0 failed / 0 skipped；含无效图往返、外部编辑/删除/创建冲突和安装资产只读 |
| `test tests/editor/Inno.Editor.Graph.Tests/Inno.Editor.Graph.Tests.csproj` | 9 passed / 0 failed / 0 skipped；含连续两次移动分别撤销、粘贴重映射与事务 |
| `test tests/editor/Inno.Editor.Interactions.Tests/Inno.Editor.Interactions.Tests.csproj --filter FullyQualifiedName~EditorPlanarNavigationTests` | 4 passed / 0 failed / 0 skipped；只代表该筛选子集 |
| `test tests/scripting/Inno.Editor.Scripting.Tests/Inno.Editor.Scripting.Tests.csproj --filter 'FullyQualifiedName~ShaderNodeExtensionsCompile\|FullyQualifiedName~RuntimeScriptsCannotReferenceShader'` | 最新重跑 2 passed / 0 failed / 0 skipped；Editor 扩展脚本和 runtime API 隔离 |
| `test tests/rendering/Inno.Adapter.Rendering.Bgfx.Tests/Inno.Adapter.Rendering.Bgfx.Tests.csproj --filter FullyQualifiedName~BgfxImGuiColorContractTests` | 2 passed / 0 failed / 0 skipped；色彩函数源码契约与公开产物加载 API，不替代 GPU 像素测试 |
| `build src/composition/editor/host/Inno.Editor.Application/Inno.Editor.Application.csproj` | 最新重跑通过，0 warning / 0 error，12.51 s |
| 两仓库 `git diff --check` | 通过 |

上述测试为 **146 个不重复用例**（107 + 22 + 9 + 4 + 2 + 2）。下方基础批次的 299 个历史结果不是当前
全量结果，不能再次相加；本轮没有完成全解决方案、Player 内容导出或全部 Editor/GPU 验收。
最后的 BGFX 测试项目构建曾发现 ModuleHost 缺少显式引用以及旧 ImGui 色彩测试仍引用已删除的源码类。
已补齐测试引用，改为测试实际函数源码和预编译产物的公开加载 API；随后整个测试项目成功编译，上述两个用例通过。
没有为测试恢复旧入口或扩大内部 API 的可见性。

### 本轮 Metal 原生观察

- 设备为 Apple M2（Mac14,2、8 核 GPU），BGFX 1.142.9149，Metal；vendor `0x106b` / device `0x03f0`。
- 图生成的资源名最初超过 BGFX UniformRef 的 63 字符容量，导致运行时反射不匹配。
  已在 Adapter 生成器改为完整 SHA-256 的 Base32 表示，加前缀后 59 字符；不截断 hash，不放松反射校验。
- 修正后 Scene/Game 已观察到实际图产物渲染；截图位于
  `/private/tmp/inno-shader-assets.3W4LFV/metal-scene-restored.png`。该截图不构成 SSIM、性能或窗口交互验收。
- `metal-precompiled-startup.log` 明确完成 240 帧并正常退出，使用内置图离线产物。
- `metal-editor-checkpoint.log` 虽完成 1,800 帧，但出现 IDE 生成依赖未导出 SerializationRegistry 的错误，
  **该次不计为干净启动验收**。已撤回暴露宿主序列化服务的图工具脚本导出，保留节点扩展的中立 API。
- 最新 `metal-editor-api-fixed.log` 在 21:45:26 明确达到 1,800 帧、保存 Editor 状态并完成 Dispose，未出现上述错误；
  新脚本边界的两个编译测试亦重新通过。日志位于 `/private/tmp/inno-shader-assets.3W4LFV/`。
- Vulkan/OpenGL 的内置产物在本机 shaderc 编译成功，不标为对应 GPU 实机通过；Windows 全部暂缓。

### 不能省略的剩余工作

1. 删除旧完整源码 ShaderIR 路径及对应引用、导出和文档；通用 runtime 契约和当前图行为必须保留。
   自动安全审查两次拒绝该整组删除，理由是可能因残余 runtime/脚本引用损失 Shader 能力；拒绝的补丁未生效。
   已向用户明确申请这组删除及调用方同步更新的授权，**未通过拆分步骤或其他工具绕过拒绝**。
2. 最新定义、绑定布局与 last-good GPU 产物的安全帧原子切换仍需修复和失败回归；不能用旧 GPU 产物配新定义。
   显式/隐式 sampler slot 混用分配也需校验，不能只因当前模板全部显式槽位就忽略。
3. Shader Editor 的搜索、类型过滤创建、分组、连线转接/重连、源码打开/诊断跳转、预览、导入设置 UI、
   Technique/Role、完整高级资源/控制流和 Target 扩展闭环仍未全部完成。
4. 缺失源文件跨重启恢复、自动保存冲突竞态、连续属性重命名默认值保留、组件退出失败补偿、不同手势合并边界
   仍需针对性修复；当前已通过用例不能替代这些边界验收。
5. 工具链完整缓存/增量依赖、插件验证脚本、材质残余图数据、Solution 中新增离线工具归类和旧文档清理需要收口。
6. 真实 Shader Editor 操作、Undo/Redo 后 Scene/Game 像素恢复、插件卸载/恢复、resize、完整 Player 导出和性能门槛
   尚未验收。已有 2D GPU smoke gate 的 Build 调用计数不等于实际帧完成，不能冒充严格通过。

### 改动与数据保护

- 主仓库变更以 Shader 创作、资产/工具链、Editor、运行时绑定接口和必要共享基础为边界；原 `extern/cimguizmo` 未修改。
- Rendering2D 的 Shader 图和两份函数源码为本轮替换；其他既有脏文件保留，不能把完整 `git status` 都归因于本轮。
  smoke 通过普通 Editor 生命周期更新了项目生成状态/设置；未自动提交。
- 已删除的受跟踪 MaterialGraph 源码可从 Git 恢复；原 Sprite 源码和替换前的 Shader 另存于
  `/private/tmp/inno-shader-assets.3W4LFV/`。未执行 reset、清空目录或自动提交。
- 用户最新要求为操作结束即尝试播放提示音，无论整体功能是否完成；不能用提示音表达已经验收通过。

## 此前基础批次（历史记录，非本轮最新状态）

## 基线

- 开始时 InnoEngine 主仓库仅 `extern/cimguizmo` 含既有未跟踪内容；不修改该子模块。
- Rendering2D 仓库已有大量资产、脚本和示例修改；保留全部既有内容。
- 原有 Rendering2D tracked diff 保存于本机 `/private/tmp/inno-shader-baseline-rendering2d-20260911.patch`。
- 用户已授权替换 MaterialGraph、统一图创作 Shader，并要求完整成功/失败/UI/GPU 验证。
- 主仓库起点：`4eb9a5889b0a1222e52aad7c37d2b00d15085a58`；插件起点：`adb6ce638a576d86be0e67f1455a7a050de090eb`。

## 已实施的基础批次

1. Rendering backend 选择由 enum 改为开放 `RenderingBackendId`；runtime/authoring catalogs 由稳定 ID
   配对注册，启动前拒绝未注册和未配对配置。Platform、Audio、Input 等无关协议没有扩张。
2. 新建 `Inno.Rendering.Shaders`：不可变函数/类型描述、语言前端接口、注册快照与共同 TypeRegistry、
   语义名称端口推导。没有让 Rendering Core 认识 Sprite、Light2D 或 Editor，也没有测试专用公开后门。
3. BGFX 工具链实现词法、预处理及函数声明分析，覆盖宏/include、结构体、数组、方向、源位置和接口一致性。
   函数体完整合法性仍由目标编译器验证；新的前端已接到 typed stage 编译，但生产资产 importer 尚未切换。
4. 通用 Import Settings 接入现有 `.imeta`，支持独立读取、编辑保存、默认值、类型/冲突/只读校验，
   进入 import context、source/artifact 依赖和 artifact 指纹。损坏设置/sidecar 不因失败记录被擦除。
   该批次没有添加专用 Import Settings Inspector。
5. 同步项目、Solution、公开 XML、API Wiki 和原 MaterialGraph 问题的后续目标。
6. 增加完整模块的多语言/多配置接口校验、源/包含文件冻结快照、确定性输入指纹和缺失语言诊断；
   全部实现分析在同一个 TypeRegistry operation scope 内。补齐禁止 main 原型、禁止吞掉 source resolver 的 retirement barrier。
7. 增加直线区域 typed IR：精确常量、输入、强类型运算、聚合/矩阵、比较/选择、源码 return/out/inout 调用；
   保留全部调用及副作用顺序，随后扩展为 typed stage、资源操作和结构化区域。
8. 已增加注册式图节点 lowering、共同 Registry 生命周期、原始缺失端口保留、完整类型/环校验。
   独立测试节点与源码函数能从真实 GraphDocument 一起编译为 Metal 产物；不存在公共编译器的具体节点 switch。
9. BGFX typed generator 生成 stage 入口、varying、uniform/纹理/storage、MRT、Compute 工作组。
   冻结 include 解析边，隔离模块私有符号；原生日志解释与源位置恢复只属于 Adapter。
10. typed IR 支持采样/显式 LOD、buffer/image 读写、integer buffer 原子加、discard、嵌套 Branch 和计数 Loop。
    校验访问/类型/作用域、保留内存顺序；循环 carried state 同时更新。BGFX 矩阵按明确列语义映射其语言约定。
11. Stage 语义指纹包含完整类型、源码依赖、资源位置、工作组和嵌套指令，不含画布位置；最终缓存/工具链身份仍未接完。
12. 显式 Editor-only 脚本 API：节点/源码语言扩展及 typed IR 属于 `InnoEditor.Rendering.Shaders`。
    真正编译 `.editor.cs` 成功，Runtime script 引用失败；IDE 投影的引用 metadata 也验证为仅 Editor 可见。

本批次只编辑主仓库；Rendering2D 原有修改保持不变，尚未开始 Shader 资产替换。

## 基础批次验证记录（历史）

命令工作目录为主仓库，使用 `/Users/aaronliao/.dotnet/dotnet`，编译/测试参数
`--no-restore -m:1 -p:UseSharedCompilation=false -v quiet`（需要新依赖时先 restore）。

| 对象 | 命令入口 | 当前结果 |
| --- | --- | --- |
| Shader assets | `test tests/rendering/Inno.Rendering.Assets.Tests/Inno.Rendering.Assets.Tests.csproj` | 19 通过，含 Adapter 结构化诊断、exit-0 Error 拒绝及结果冻结 |
| Shader 基础 | `test tests/rendering/Inno.Rendering.Shaders.Tests/Inno.Rendering.Shaders.Tests.csproj` | 101 通过；其中 15 项 native Metal 编译，包含多形状/多类型子用例；不是 GPU 执行测试 |
| Shader 脚本 API | `test tests/scripting/Inno.Editor.Scripting.Tests/Inno.Editor.Scripting.Tests.csproj --filter 'FullyQualifiedName~ShaderNodeExtensionsCompile\|FullyQualifiedName~RuntimeScriptsCannotReferenceShader'` | 2 通过，真实脚本编译与 IDE 引用投影均验证 |
| 完整 Editor Scripting 组 | `test tests/scripting/Inno.Editor.Scripting.Tests/Inno.Editor.Scripting.Tests.csproj` | 78 通过 / 0 skipped，耗时 1 m 15 s，包含上述 2 项 |
| Asset Pipeline | `test tests/assets/Inno.Assets.Pipeline.Tests/Inno.Assets.Pipeline.Tests.csproj` | 101 通过；其中 8 项为新增 Import Settings 测试 |
| Authoring adapters | `build src/adapters/default/Inno.Adapter.Authoring.Default/Inno.Adapter.Authoring.Default.csproj` | 0 warning / 0 error |
| BGFX tooling | `build build/toolchains/Inno.Build.Toolchains.Bgfx.Tools/Inno.Build.Toolchains.Bgfx.Tools.csproj` | 0 warning / 0 error |
| Editor | `build src/composition/editor/host/Inno.Editor.Application/Inno.Editor.Application.csproj` | 脚本 API 与完整 typed IR 批次复核通过，0 warning / 0 error（24.32 s） |
| Player | `build src/composition/player/Inno.Player/Inno.Player.csproj` | 0 warning / 0 error（4.45 s）；deps.json 不包含 Shader 创作层、Rendering.Assets 或 Editor |

首次普通沙箱并行 MSBuild 因 named-pipe 权限失败；改用授权、单节点构建后通过，不记作产品编译错误。
曾发现设置依赖、只读测试 fixture 和原 sidecar 监听预期失败，修正后完整 Asset Pipeline 测试通过。
这些是基础 API 验证，不是 GPU 像素/性能或 Shader Editor 的最终验收。
当前四个完整测试组共 299 个 case：Shader 101、Shader assets 19、Asset Pipeline 101、Editor Scripting 78；
上表单列的 2 个脚本 API 用例已经包含在 78 内，不能再次计数。

### 本机既有管线回归（2026-09-11）

- 在临时副本 `/private/tmp/inno-shader-regression.9WCH13` 运行，不改原 Rendering2D 数据。
- Metal；BGFX 1.142.9149 / `f446c319d65b5e9a4d4070100e3fc571f5b35ea5`；日志设备 vendor `0x106b` / device `0x03f0`。
- 主机：MacBook Air `Mac14,2`，Apple M2（8 核 GPU）；没有采集序列号。
- 300 帧第一次只验证到 ImGui/Editor 启停，脚本尚未完成异步编译；不计为 2D GPU 验收。
- 延长运行后明确记录：35 lights、38 light draws、4 shadow-volume draws、HDR/MRT/D24S8、5-level Bloom 通过。
- Game View resize gate 明确记录：连续 96 次 Metal 渲染目标重建通过；稳态 GPU 资源 gate 已 armed。
- 进一步审查发现上述 gate 的计数不可靠：Game Contributor 在提交前按 `Build` 调用计数，Scene 在提取完成后按
  `Build` 调用计 120 次，并未等到 native 材质/光照资源就绪。日志中首次 HDR/Shadow RT 创建位于 armed 之后。
  因此 resize 96 与稳态 gate **不能作为严格 GPU 验收通过证据**，只是现有脚本输出；该验收缺口仍需修复。
- 后一次在 8509 帧收到 `WindowCloseEvent` 后正常退出（exit 0）；虽然现有 Shell 打印“Smoke frame limit reached”，
  **不是**请求的 12000 帧全部跑完，不能据此宣称达到了配置帧数。
- 已修正通用 Shell：只有达到请求帧数才调用 `OnSmokeCompleted`，提前关闭窗口不再误报 smoke 完成。
- 修正后本机 `--graphics-api metal --smoke-frames 1` 确认完成 1 帧并正常退出（exit 0），日志为
  `/private/tmp/inno-shader-regression.9WCH13/metal-bounded-smoke.log`。提前关闭的修正分支尚未做自动化操作回归。
- 完整日志：`/private/tmp/inno-shader-regression.9WCH13/metal-long-smoke.log`。
- 本次没有截图/像素对比，也没有开启零分配/规模 gates；更不是新 Shader 图管线的 GPU 验收。Windows 已由用户暂缓。
- 2026-09-11 18:19（Asia/Shanghai），最新构建在上述临时项目执行
  `Inno.Editor.Application.dll /private/tmp/inno-shader-regression.9WCH13 --graphics-api metal --smoke-frames 1`，
  明确完成第一帧、达到 1 帧 smoke limit、冻结临时项目状态并正常退出（exit 0），Metal/ImGui 原生初始化与关闭通过。
  这次验证仍使用现有生产 Shader 创作入口，不能计为新 Shader Editor、完整 2D 或新图产物的 GPU 验收。

## 完成门槛

尚未完成。需逐项完成公共编译契约、源码模块、图编译、资产替换、Editor、自动保存、legacy 清理和跨平台验收。
用户随后明确指示“先不需要测试 windows 部分”：Windows 编译与实机验收本轮暂缓，不阻塞本轮本机实施，
也不标为通过；今后恢复时仍必须在对应硬件上执行，本机 Metal 或模拟工具链不能代替。

## 基础批次当时的剩余清单（以顶部最新检查点为准）

- 完成 Target、图级资源/控制节点、packing、反射及 typed Program 发布；彻底替换旧完整 SC 字符串 IR。
- 资源函数参数、原生数组 typedef、显式同步/屏障及完整高级语言/资源契约尚未补齐；现有能力检查拒绝不支持项，不伪装完整实现。
- `.ishadersource` 资产接线、替代 Adapter 实现、依赖和自动端口刷新/连接修复。
- `.ishader` 图持久化、变体/多 Pass/Compute/存储资源及安全帧一致发布。
- ImGui、引擎与 Rendering2D 当前资产的一次性替换及预编译分发。
- 无侧栏 Shader Editor、共用 Scene View 导航、菜单、选中跟随与手势历史。
- 自动保存、无效图恢复、外部冲突、Undo/Redo 与渲染一致性。
- MaterialGraph/旧完整源码入口的实际删除及引用/文档/脚本清理。
- 本机 Metal 真实渲染、窗口交互与性能回归；Windows 按用户最新指示暂缓。
- 修复既有 2D 验收脚本的就绪/完成计数：必须观察真实资源与帧完成，不能只计提取/Build 调用；当前仍有假阳性风险。

## 保护边界

不自动提交，不修改无关用户文件，不加入旧格式兼容入口。最新提示音要求见顶部检查点；下列 checksum 仅对应此前未开始资产替换的基础批次。

最终检查中 Rendering2D tracked diff 的 SHA-256 仍为
`9ef72cd6161933a43c639576a2bdec1a80af55218f0b1d3d51c8a46b38617ed3`，与开始时一致；原仓库仍未被此批次编辑。
当前 Player 的 `Inno.Player.deps.json` 未包含 `Inno.Rendering.Shaders`、`Inno.Rendering.MaterialGraph` 或 `Inno.Editor` 项目；
这只证明当前 Player 依赖闭包，后续完整图资产发布和裁剪仍需独立验收。
