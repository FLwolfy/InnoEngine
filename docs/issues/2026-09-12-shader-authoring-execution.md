# Shader 创作体系实施记录（进行中）

[计划](2026-09-12-shader-authoring-completion-plan.md) · [最新阶段验收](2026-09-13-shader-authoring-validation.md) · [Issues 索引](README.md)

本页是持续更新的实施证据，不是 P1–P6 完成报告。未自动提交 commit；保留原有用户修改。Windows GPU 验证按用户要求暂缓。

## 阶段状态

| 阶段 | 已实现 | 尚未完成的阶段验收 |
| --- | --- | --- |
| P1 | generation-scoped Target/模板、独立 Editor.Shaders；插件 Target/模板/Inspector；真实 Sprite GPU 预览 provider、隔离资源 scope | 最终扩展与生命周期全链复核 |
| P2 | 共享阶段与 Pass 状态分离、结构事务；DefaultSprite 五种状态共享计算；可选顶点偏移进入同一编译链 | 覆盖/遮挡的像素级回归 |
| P3 | Shader/Material 显式 Save/Revert、恢复/冲突、多选；全部已有 Material 类型、节点默认输入；真实隔离 GPU 预览；Editor-only 参数分组/Tooltip/可见性/范围 | 所有真实 UI 编辑闭环和截图验收仍未穷尽 |
| P4 | Pipeline 权威及嵌套引用；当前插件真实 Player 导出/执行；失败及过期创作输入拒绝导出；Player 设置 scope 覆盖渲染阶段；异步导出 generation 租约覆盖 worker 生命周期 | 安装/移除/恢复的完整组合矩阵 |
| P5 | 四节点 Sprite Target；六个内部 Shader；顶点偏移、Mask 独立覆盖材质及保守 bounds；Metal 35 灯、阴影、五级 Bloom | 十万实例真实 GPU 性能及像素等价尚未验收 |
| P6 | 全量测试、冷启动、真实 Player、96 次 resize；精确隔离 legacy 缓存 | 原生 overlay scrollbar 的既有失败；最终 GPU/UI/全帧性能证据及报告 |

## 两仓库边界

引擎新增或调整的是中立的图/Target/模板协议、图输入类型和编译快照、原生资产源草稿存储、完整 owner 的设置引用恢复，以及 Editor 文档/Inspector/选择与呈现。
未向通用编译器添加 Sprite/Light/Bloom 中央分支。`AssetObject.RestoreProperties` 通过资产原 owner 的弱引用解析，不依赖调用者当前的 ambient session；它不是 2D 设置的反射特例。

Rendering2D 光照/阴影/Bloom/合成现由 Pipeline 的显式 Shader 引用驱动，不再借用 DefaultSprite。
默认图为 Sprite Texture、Tint、Multiply、Surface Output 四节点；Target 展开后五种混合状态复用阶段计算。
所有内部程序仍是图调用源码函数后通过公共 IR/Adapter 编译；曝光、阈值等不再占用 Sprite 实例字段。

公开 API 均有产品调用方：`ShaderPropertyInspector` 同时服务 Shader 默认值与 Material 覆盖值；`AssetInspectionSelection` 由 File Browser 与多材质 Inspector 使用；`ShaderGraphArtifact.Capture` 同时服务正式导入与隔离草稿编译。

## 保存与编译证据

- Shader/Material 草稿及 Undo/Redo 不写源文件，不改变 canonical Material。
- Save 使用内容指纹检测外部修改并原子写盘；下一次 Editor Update 进入正式导入。失败保存/导入在文档显示，不把 Saved 当成 Compiled。
- 草稿编译与正式编译共用 Target 展开、函数冻结、公共 IR、Adapter 编译和反射校验，预览缓存与 canonical 缓存分开。
- 已测试无效草稿保留 preview last-good；正式产物不变；移动节点不生成新的预览编译产物。
- Material 多次采样组成一次组事务，两次手势可以分别 Undo；恢复保存基线后 dirty 标记清除。
- unconnected input default 保存精确 IEEE/整数位模式。连线后默认值失效但不删除；类型变更要求显式重置，不转换或按位置重接。

## 已执行验证

使用 `/Users/aaronliao/.dotnet/dotnet`；项目目录为 InnoEngine。

| 命令范围 | 最近已完成结果 |
| --- | --- |
| `build src/composition/editor/host/Inno.Editor.Application/Inno.Editor.Application.csproj -v quiet --disable-build-servers` | 0 warning / 0 error（隔离草稿编译接入后） |
| `test tests/rendering/Inno.Rendering.Shaders.Tests/...csproj` | 125 通过 |
| `test tests/rendering/Inno.Rendering.Assets.Tests/...csproj` | 27 通过（包含双 owner 捕获/恢复） |
| `test tests/rendering/Inno.Rendering.Runtime.Tests/...csproj` | 55 通过（owner-aware 设置修改后） |
| `test tests/scripting/Inno.Editor.Scripting.Tests/...csproj --filter 'FullyQualifiedName~ShaderEditorWorkflowTests\|FullyQualifiedName~ShaderNodeExtensionsCompileThroughTheEditorOnlyLogicalApi'` | 32 通过 |
| `test tests/assets/Inno.Assets.Tests/...csproj` | 29 通过 |
| `test tests/assets/Inno.Assets.Pipeline.Tests/...csproj` | 101 通过 |
| `test tests/editor/Inno.Editor.Interactions.Tests/...csproj` | 96 通过 |
| `test tests/editor/Inno.Editor.Graph.Tests/...csproj` | 9 通过 |
| `test tests/tooling/Inno.Tooling.Architecture.Tests/...csproj` | 47 通过 |

这些结果是分批验证，不是最新代码的全解决方案测试声明。

后续验证：

- 完整 Scripting 113 项通过，包含新增 Pipeline 设置脚本 API；ShaderEditorWorkflowTests 34 项通过。
- 独立 Inspector 契约测试通过：条件 Drawer 同级分派和实际歧义诊断。
- 修复 using static 用户函数在原生 API 重写后被命名空间遮蔽的问题，使用原始语义绑定而非 2D 特例。
- 六个内部 Shader 和四节点 Target 经统一链路完成离线 Metal 编译。
- 新资产 Editor 1800 帧正常退出；GPU acceptance：35 灯、38 灯光 draw、4 阴影 draw、HDR/MRT/D24S8、五级 Bloom 通过。
- 烟雾退出检查活动 DiagnosticHub Error，避免忽略脚本/Shader 失败。
- 新 Pipeline Inspector 的真实鼠标选择、清空 Bloom 引用、Command+Z、Revert 已验证；Scene/Game 继续渲染，源 SHA256 始终为 aacbb91d588347ad6bab26f9f591264b15c852fadefbd10201b171f0613fb25d。
- 实际 Shader Editor 能打开并用 F 聚焦四节点图。此检查不等于全部编辑交互或性能门槛通过。
- 实际启动曾查出 Material/Pipeline 条件 Drawer 注册冲突；已增加显式 conditional 契约，不用不同魔法优先级绕过。

Metal Editor 执行过 900、12,000、60,000 帧的限定帧数启动/退出。60,000 帧测试启动器正常结束；可视截图能看到 Scene/Game 内容与灯光/Bloom。截图还发现旧的“输出未就绪”诊断没有随恢复消失，已补按请求/相机区分的 Resolve，仍需下一轮验证。
UI 自动化可以取得截图，但点击当前测试启动器返回 `noWindowsAvailable`；因此不能声称人工交互、拖放和多选的实际窗口验收已通过。
未把这些启动结果计作零分配/十万实例/32 灯的性能门槛通过。

## 2026-09-13 新增验证与修复

- `ShaderParameterPresentation` 只进入 Shader 图的 Editor metadata，不进入 ShaderDefinition/Player；分组、Tooltip、可见性、Float 范围已接通参数节点与单/多 Material Inspector。序列化和语义 hash 不变测试通过。
- `ShaderPreviews` 使用插件真实 `SpriteShaderPreview` 消费者；草稿 GPU 发布与 canonical 材质、Shader 发布键隔离。实际红色预览已出现，源文件 SHA 未改变。资源在帧边界退休，面板关闭和未提交的预览 scope 均会释放。
- 独立 Metal 验证：Sprite 默认顶点偏移、连接计算的顶点偏移各编译五个 Pass；Fragment-only 纹理输出用于顶点偏移会被拒绝。原作者图未被展开过程修改。日志：`/private/tmp/inno-sprite-vertex-acceptance.log`。
- 实际 Player 曾暴露 `Settings.revision` 在渲染提取时没有执行 scope；已在 Player host 的整个运行循环绑定现有 Settings owner。未增加 Rendering2D 分支或新 Shell 公共 API。
- 最新源码重新生成 macOS 支持包，再完整 BuildCLI 导出，未手工替换 DLL：1800 帧 Metal 正常退出，`views=15 draws=92 dispatches=0`，无活动 Error。日志：`/private/tmp/inno-authoring-player-final-metal.log`。包内不含 Editor、Shader 图编译器/解析器或源码资产。
- 临时项目的必需 Bloom source 注入 `#error` 后，旧导出错误地通过；现由通用 AssetLoader 递归校验 Artifact 创作输入和 Source 指纹，在写出内容前拒绝，明确报告 `BloomPrefilter.ishader -> BloomPrefilter.ishadersource:2`。Editor last-good 仍能读取；资产用例 **103 项通过**。注入已撤回，仅发生在临时副本。日志：`/private/tmp/inno-authoring-invalid-export-verified.log`、`/private/tmp/inno-authoring-export-tests-final.log`。
- 冷项目未限帧运行 1800 帧只持续约 5 秒，无法等待首次脚本编译；60000 帧的冷启动已恢复 Target，无活动导入错误退出。验证脚本因此使用独立项目副本和更充分的默认帧预算，不重写用户 SampleScene。
- 96 次真实 framebuffer 重建通过。验收不再要求旧管线固定至少 20 个 view；仍要求真实绘制、已完成 GPU 效果条件、每次 resize 的分配增长及最终稳定资源门槛。稳态采样须在 resize 压力阶段结束后开始。
- 最新冷启动提取测试：240 个稳定样本，全部 0 托管 bytes；Scene P95 0.109 ms、runtime camera 0.107 ms、camera stack 0.108 ms、request submission 0.109 ms、十万可见 Tile / 百万格稀疏域 / 32 灯提取 0.090 ms。这些是 **CPU 缓存命中的提取/请求**，不是十万实例 GPU 或全帧零分配证据。
- 本机 MacBook Air / Apple M2（8 GPU cores）/ 16 GB / Metal。尚无可声明全帧 P95、GPU 显存、同场景像素容差或完整 10% 回归门槛通过的证据。
- 全解决方案第一轮 49 个测试项目：1245 通过、1 失败。唯一失败为 `OverlayScrollbarsDoNotReserveWindowContentWidth`；2026-09-11 报告已记录同一失败，相关 native overlay 源码非本次 Shader 修改。没有删除或放宽这个测试；最新全量复测另行记录，不能报全绿。
- 随后的完整解决方案复测：49 个项目，**1247 通过 / 1 个相同失败**。进一步的实际 IDE `-warnaserror` 构建发现 reference 投影丢失 `MaterialPropertyBlock?` 注解；已在通用 ScriptApiStubSourceBuilder 保留 nullable 类型，不修改插件传入的合法 `null`。包含新增真实 ShaderPreview 逻辑 API 用例的 Scripting suite **116 项通过**；该修复后仍需单独记录 IDE 严格构建结果。

### 可恢复的 legacy 缓存清理

已确认两个旧 MaterialGraph 项目目录只剩 bin/obj 和空 Properties，连同 13 个旧 ScriptApi generation 缓存移至 `/private/tmp/inno-materialgraph-cache-quarantine.LYILxV`。未删除整个 Library/ScriptApi，未动当前 IDE references。原目录名和 generation hash 在隔离目录中保留，可以恢复；历史报告及用户原 Shader 快照不属于可执行 legacy，继续保留。

### 原始资产快照

Rendering2D 的 `Documentation/ShaderAuthoringBaseline` 保留原始原生资产快照，不参与资产导入或运行时打包：

- `original-sources.bin`：原始 Shader 来源及共享程序转换所需记录。
- `DefaultSprite.before-shared-programs.bin`：共享阶段整理前的图。
- `DefaultSprite.before-surface-target.bin`：四节点替换前的用户图。
- `SpriteFragment.before-internal-split.txt`、`SpriteVertex.before-internal-split.txt`：分离内部效果前函数。
- `Default2D.before-owned-settings.bin`、`Settings.Project.before-pipeline-reference.bin`：配置替换前快照。

DefaultSprite 原有 Additive Vertex 源节点错误引用了 Fragment 函数；已显式校正到 SpriteVertex，才合并等价阶段。不得把该修正隐藏成“机械无语义改动”。其原始数据仍在快照中。

## 剩余交付

本轮最后一批验证：

- 完整 Metal 脚本 **exit 0**：60000 帧、35 灯、五级 Bloom、96 次目标重建、120 帧稳定资源门槛、CPU 提取/请求门槛及真实 EditorScripts `-warnaserror` 全部通过。日志 `/private/tmp/inno-authoring-final-metal-script.log`。
- 解决方案复测 **1248 通过 / 1 个相同 native overlay 失败 / 0 skipped**；49 个测试项目。日志 `/private/tmp/inno-authoring-final-solution-tests.log`。
- 随后异步导出补全严格 generation read lease 的跨 await 生命周期；成功、取消、失败后均可释放 admission，Assets.Pipeline 单独复测 **106 通过**。不能把它重复累计到上一行的全量测试结果。
- 最后完整 `build InnoEngine.sln --no-restore --disable-build-servers -m:1 -v quiet`：**0 warning / 0 error**。日志 `/private/tmp/inno-authoring-final-solution-build.log`。
- 窗口工具明确报告 Mac 锁屏且自动解锁失败；剩余实际鼠标操作及截图需要用户解锁 Mac。后台 Metal 运行结果不代替这些 UI 项目。

以上未完成项继续保持开放，详细阶段表、命令、性能范围与缺口见[最新阶段验收报告](2026-09-13-shader-authoring-validation.md)。不称 P1–P6 全部完成。
