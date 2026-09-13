# Shader 创作体系、Material Inspector 与 Rendering2D 管线解耦完整计划

## 一、文档状态与审查基线

**目标：让普通 Shader/Material 创作简单，同时保留底层渲染能力与插件扩展能力；不通过给通用引擎增加 2D 特例达成。**

- 原文来自计划阶段；用户随后明确授权完整实施 P1–P6，当前已进入实现阶段。
- 保存位置：`InnoEngine/docs/issues/2026-09-12-shader-authoring-completion-plan.md`。
- 当前进度与实际验证见[实施记录](2026-09-12-shader-authoring-execution.md)。下文的历史审查结论保留为基线，不能替代当前验收。
- 本计划中的“新增”“目标”表示待实现，不能当作现有能力。
- 以本计划中的**显式 Save/Revert**取代早期方案的自动保存到资产行为；草稿恢复仍保留。

### 1.1 总体审查结论

没有发现通用引擎为了 Rendering2D 写死 Sprite、Tilemap、Camera2D、Light2D 等领域逻辑。

通用 Artifact、绑定身份与后端名称分离、资源退休、设备 generation、原子发布和能力检查，属于其他渲染模型也能复用的基础设施，不是 2D 专属妥协。

但当前系统仍不能评价为“创作流程完全清晰、扩展闭环完整”。问题主要集中在：

1. Material 编辑工作流缺失。
2. Shader 图直接暴露了过多底层连接。
3. Target、模板与脚本创作接口尚未收口。
4. 默认物体材质与管线内部程序耦合。
5. Editor 扩展协议、配置依赖和资源生命周期需要补齐。

### 1.2 历史问题与当前状态

| 编号 | 优先级 | 问题 | 当前状态与处理方式 |
|---|---|---|---|
| A01 | P1 | 普通 Material Inspector 没有完整保留 | 待实现。`MaterialAsset` 数据/API 存在，但默认 Drawer 正文为空，隐藏参数没有完整编辑入口 |
| A02 | P1 | 删除、剪切节点与 Pass、参数声明不同步 | 上一轮已修复相关事务与声明同步；作为强制回归项，并扩展覆盖新的共享程序模型 |
| A03 | P1 | 草稿修改直接影响资产与渲染 | Shader 已改为显式 Save；继续验证，Material 使用相同语义 |
| A04 | P2 | 独立 Shader Target 扩展协议缺失 | 待实现，不能把后端编译配置 `ShaderCompileTarget` 当作领域 Target |
| A05 | P2 | `.editor.cs` 创作 API 不完整 | 补齐文档、绑定、模板及新增 Target API 的受支持导出 |
| A06 | P2 | 节点绘制扩展位于具体 Shader Editor Panel | 移入可复用 Editor 功能层，Panel 不再拥有公共创作协议 |
| A07 | P2 | 公开预览签名与项目依赖可见性不一致 | 修正 public 签名涉及的依赖声明；不是重新增加一套预览接口 |
| A08 | P1 | DefaultSprite 多 Pass 重复接线 | 改为共享计算程序与独立 Pass 状态 |
| A09 | P1 | 灯光、阴影、Bloom 等借用默认 Sprite 材质 | 拆分内部 Shader 与资源配置，消除整条链依赖一个默认材质 |
| A10 | P1 | 默认材质缺失可能导致整帧提取为空 | 默认材质只影响需要默认材质的对象，不影响已有自定义材质的对象 |
| A11 | P2 | 已有 Pipeline 资产未成为实际配置权威 | 替换临时创建的无配置 Pipeline，统一 Editor 与运行时配置来源 |
| A12 | P1 | 内部效果失败后可能退回直接绘制 | 移除静默丢失 Mask/PostProcess 语义的降级；明确失败、诊断与 last-good 边界 |
| A13 | P2 | 旧 MaterialGraph 构建缓存残留 | 精确清理旧项目产物；历史问题文档不是可执行 legacy |
| A14 | 回归 | 删除无效、Compute 默认输入错误、Console 标签挤压、画布缩小断言 | 保留上一轮修复，不因重构重新引入 |

历史审查的 **155 项通过**不代表这些缺口不存在。上一轮修复报告记录的 **242 项通过**也不能代替本计划新增功能的验收。

已有证据：

- [Shader 草稿与 Console 修复记录](/Users/aaronliao/Dev/GameEngineDev/InnoEngine/docs/issues/2026-09-12-shader-drafts-and-console.md)
- 早期只读审查报告位于 `/private/tmp/inno-shader-audit.sxGyJ4/AUDIT.md`；正式存档时将其结论和证据摘要纳入计划文档，不能只依赖临时目录。

---

## 二、最终模型、边界与完整数据流程

### 2.1 资产与概念

| 概念 | 职责 | 不应承担的职责 |
|---|---|---|
| `.ishader` | 唯一 Shader 图资产，定义计算、参数、Target、程序与 Pass | 不保存某个 Material 的独立覆盖值 |
| `.ishadersource` | 可供图调用的源码函数模块；端口由前端解析 | 不作为完整 Shader、`main()` 或原生阶段绑定的旁路 |
| `.imaterial` | 引用 Shader，保存参数覆盖、关键词与 Technique 选择 | 不内嵌图、不另建 MaterialGraph |
| Shader Target | 将领域输出、阶段接口、资源语义与能力要求组织成可编译程序 | 不安排整个场景的 Render Graph |
| Shader Contract / Role | 管线与 Shader 程序之间的运行时匹配协议 | 不等于节点，也不负责画布交互 |
| Render Pipeline | 提取结果消费、资源创建、Pass 调度与最终合成 | 不要求所有内部运算塞进用户的 Sprite Shader |
| Render Graph | 描述本帧资源依赖和执行顺序 | 不替代 Shader 内部计算图 |

**Material 是 Shader 的使用配置，不是“很多 Shader 的集合”。** 一个 Shader 可以提供多个 Pass/Technique；内部灯光、Bloom 等程序则由管线选择和调度，不应附带在每个普通 Material 上。

未来 BRDF、PBR Surface 等属于对应渲染领域的节点、函数库和 Target。本次提供它们可接入的机制，不在通用 Core 内置 3D/PBR 语义。

### 2.2 程序集与插件边界

| 所属层 | 计划职责 |
|---|---|
| `Inno.Core.Graphs` | 中立图结构、节点身份、连接与通用结构校验 |
| `Inno.Rendering.Shaders` | Shader 类型系统、公共 IR、源码接口、Target、节点编译、程序与 Pass 组织 |
| `Inno.Rendering.Assets` | Shader/Material/Pipeline 导入导出、依赖、编译缓存和产物发布 |
| Rendering Adapter 工具链 | 对应语言生成、资源布局、反射与平台编译 |
| `Inno.Editor.Graph` | 通用画布导航、选择、连线及图交互基础 |
| 新增 `Inno.Editor.Shaders` | Shader/Material 创作文档、Inspector 扩展、节点呈现与模板接入 |
| Shader Editor Panel | 画布宿主、文件跟随、菜单与状态呈现 |
| Rendering2D 插件 | Sprite Target、高层节点、2D 模板、内部 Shader、管线设置和调度 |

新增公共 API 必须有实际扩展消费者；不得仅为测试扩大可见性。

### 2.3 当前 Rendering2D 类型级流程

以下是现有链路，后续重构保留其职责边界：

| 步骤 | 当前主要类型 | 输入 → 输出 |
|---|---|---|
| 场景数据 | `Camera2D`、`SpriteRenderer2D`、`TilemapRenderer2D`、`ParticleSystem2D`、`Light2D`、`ShadowCaster2D`、`SpriteMask2D` | 用户设置、资产引用、Transform |
| 场景索引 | `Rendering2DSceneSystem` | 参与 2D 的场景对象 → 场景快照 |
| 场景范围 | `Rendering2DSceneScope`、`Rendering2DSceneScopeCache` | 明确的内容范围 → 有序参与场景 |
| 相机与视口 | `Rendering2DRenderer` | 相机或相机栈、视口尺寸、场景范围 → 视口帧 |
| 提取与批处理 | `Rendering2DFrameCollector`、`Rendering2DDrawBatch` | 可见对象 → 排序、材质、实例、灯光及后处理数据 |
| 帧数据传输 | `Rendering2DViewportFrame`、`RenderFrameData` | 中立帧容器承载插件数据 |
| 请求提交 | `Rendering2DRequestProvider` 或 Editor Viewport Contributor | 帧数据、Pipeline、输出目标 → `RenderRequest` |
| 管线构建 | `Rendering2DPipeline.Build`、`RenderPipelineContext` | 请求与资源服务 → Render Graph Pass |
| 材质解析 | `IRenderResourceService`、`RenderMaterialPass` | Shader Contract、Role、Material、布局 → 可绑定 GPU 程序 |
| 执行 | `RenderRuntime`、`CompiledRenderGraph`、`BgfxDevice` | 编译后的 Render Graph → GPU 命令与纹理/窗口输出 |

其中提取器、批次等内部类型用于解释实现，不因此提升为插件公共 API。

输出端必须明确区分：

- **Editor Scene View**：使用编辑器导航相机，输出视口纹理，并叠加网格、选择和工具。
- **Editor Game View**：使用场景内 `Camera2D` 相机栈，输出 Editor 管理的离屏纹理，再由 ImGui 显示。
- **Player**：运行时 Request Provider 提交请求，最终输出窗口或显式离屏目标。

Game View 不是导出的 Player；两者共享渲染机制，但展示目标不同。

### 2.4 修改后的资产与执行关系

```text
SpriteRenderer2D
  → 自定义 Material，或 Pipeline 配置的默认 Sprite Material
  → Material 引用 Sprite Shader
  → Sprite Target 产出符合 Sprite Contract 的程序
  → 管线按 Alpha / Additive 等 Role 选择对应 Pass

Camera2D + Light2D + ShadowCaster2D + PostProcessProfile2DAsset
  → Rendering2DPipeline
  → Pipeline 配置的内部 Shader
  → 灯光/遮挡 → 灯光缓冲 → Sprite 合成
  → Bloom 多级处理 → 最终合成
  → Editor Game 纹理 / Player 输出
```

**拆成多个 `.ishader` 文件后，由 Pipeline 的显式资源引用使用它们；用户不需要逐个给 Sprite 挂载灯光或 Bloom Shader。**

---

## 三、实施计划表与关键设计

### 3.1 实施顺序

| 阶段 | 工作 | 主要产出 | 阶段完成条件 |
|---|---|---|---|
| P0 | 保存计划、捕获基线 | 两仓库差异、资产身份、测试与截图基线 | 不覆盖用户修改，明确所有历史结果 |
| P1 | 补齐创作协议与 Editor 边界 | Target/模板协议、可复用 Editor Shader 层、脚本导出 | 独立扩展工程和 `.editor.cs` 能真实调用 |
| P2 | 共享程序与结构一致性 | 计算图复用、Pass 状态分离、统一领域事务 | 无重复五套接线，删除/复制/Undo 保持一致 |
| P3 | Material 与节点 Inspector | 完整参数编辑、统一选择、草稿 Save/Revert | 从创建到保存、预览、重载的闭环可用 |
| P4 | Pipeline 配置与依赖 | 持久 Pipeline 配置、内部资源引用、完整依赖闭包 | Editor/Player 使用同一明确配置 |
| P5 | Rendering2D 高层创作与内部 Shader 拆分 | Sprite Target、简单模板、独立灯光/阴影/Bloom Shader | 外观与能力不退化，默认材质不再承包内部管线 |
| P6 | 清理、文档与全链验收 | 无 legacy 执行链、扩展样例、验收报告 | 所有约定门槛有证据，失败项明确列出 |

执行时不得跳过依赖阶段；阶段未通过不宣称完成。

### 3.2 Target、节点和模板扩展

建立稳定 ID 注册的 Shader Target 协议，负责：

- 声明领域输出、阶段接口、资源语义和 Contract/Role。
- 校验目标所需的设备与编译能力。
- 将高层输出和默认阶段处理降低到既有公共 IR 编译链。
- 返回与节点、端口、源码位置关联的诊断。
- 使用当前 generation 的不可变注册快照。

保留通用 Raster/Compute 创作能力；高层 Target 不是新的资产格式或另一套编译器。

节点编译、Inspector 编辑、画布呈现、预览分别扩展，不要求实现节点编译时依赖 Editor Panel。

模板通过独立注册入口贡献；插件脚本能够使用受支持的文档、绑定和模板 API 创建 `.ishader`，不得通过拼写隐藏 metadata 或引用实现命名空间绕过公共协议。

### 3.3 共享图逻辑与 Pass 分离

将当前“一套节点固定归属一个具体 Pass 的阶段”的限制收敛为：

- 共享程序/阶段计算具有稳定身份。
- Pass 引用程序，并分别保存 blend、depth/stencil、Role 等状态。
- Alpha、Premultiplied、Additive、Multiply、Opaque 复用真正相同的计算。
- 如果某种混合模式确实需要不同输出计算，使用显式程序特化；不能为了共享而改变数学语义。
- 缓存以计算内容、完整依赖、Target、变体和工具链为依据；Pass 名称、节点位置、画布缩放不制造重复编译。
- 首轮只增加同一 Shader 内的共享程序组织；跨文件函数复用继续使用 `.ishadersource`，不额外发明新的 Subgraph 资产格式。

所有 Shader 结构操作统一经过领域编辑 API：

- 删除、剪切、粘贴、复制、断开连接、切换输入种类和类型。
- 同步维护程序、阶段、Pass、参数声明及 Technique/Role 映射。
- 删除一个 Pass 不删除仍被其他 Pass 引用的共享程序。
- 删除程序时明确展示受影响引用，不能留下隐藏悬空声明。
- 参数按稳定身份维护；多处引用不生成多份冲突默认值。
- 保留用户尚未完成的无效图；“允许无效”不等于接受编辑操作制造的隐性残留。
- 每次手势一个完整 History 事务，必须提交；Undo/Redo 同步恢复所有关联数据。

### 3.4 Sprite Contract 与高级节点

现有 `Rendering2DIds.spriteContract` 是运行时契约 ID，不是高级节点。

Rendering2D 插件将提供：

- `SpriteTarget`：拥有 Sprite 的默认顶点处理、实例输入、覆盖率、材质接口和 Role 映射。
- `Sprite Surface Output`：面向作者的颜色、透明度、法线、发光等表面输出。
- 可选高级顶点输入，用于偏移或变形；默认不要求手工处理矩阵和实例布局。
- Sprite 纹理/UV 等便利节点，隐藏常规图不应手工连接的管线绑定。
- 直接可编译的 Sprite 模板。

典型作者图应接近：

```text
Sprite Texture ──→ Multiply ──→ Sprite Surface Output
                      ↑
                 Tint Parameter
```

高级节点不是把所有代码藏进一个无法扩展的黑盒：

- Target 的实现位于插件，可贡献、替换和测试。
- 管线资源绑定在 Inspector 中可检查，但不能伪装为普通材质参数。
- SpriteRenderer 请求的功能必须与 Shader Contract/能力匹配；不兼容时给出对象、材质与缺失能力诊断。
- 保留现有逐实例 Lit/Unlit、primitive、normal、emission、mask 和混合语义。
- 涉及顶点变形、透明裁剪的遮挡/遮罩路径必须复用对应覆盖计算，避免显示与阴影形状不一致。

未来 3D 插件可提供自己的 Surface/BRDF Target，不需要给通用编译器增加 `if Rendering3D` 分支。

### 3.5 内部 Shader 拆分

| 资产职责 | 计划归属 | 使用者 |
|---|---|---|
| Sprite 表面 | 简化后的 `DefaultSprite.ishader` 及用户 Shader | Sprite/Tile/Particle Material |
| 灯光累积 | `LightAccumulation.ishader` | Lighting Pass |
| 阴影与遮挡 | 独立 Shadow/Stencil 程序；需要表面覆盖时复用相应覆盖逻辑 | Shadow/Mask Pass |
| Bloom 阈值提取 | `BloomPrefilter.ishader` | Bloom 首级 |
| Bloom 降采样 | 独立 Bloom Downsample 程序（并可通过 pipeline state 复用） | 各降采样层 |
| Bloom 升采样 | `BloomUpsample.ishader` | 各升采样层 |
| 最终合成 | `FinalComposite.ishader` | 后处理与最终输出 |

固定规则：

- 不为每盏灯、每一级 Bloom 创建一份 Shader。
- 全部通过图、源码函数、公共 IR 和 Adapter 工具链编译。
- 不恢复完整手写 Shader 的启动或运行旁路。
- 删除通过负数 `v_shape` 等魔法值切换灯光/Bloom/合成操作的协议。
- 替换为明确程序选择和具名、类型正确的 Pass 参数。
- 不再借用 Sprite 实例字段存储曝光、阈值等不相关参数。
- 第一轮拆分保持已有光照、混合和滤波语义；算法升级单独记录，不能混入重构造成不可解释的画面变化。

### 3.6 默认配置、资源引用与依赖

采用一处明确的 2D Pipeline 配置来源：

- `Rendering2DProjectSettings` 引用实际 `RenderPipelineAsset`。
- 项目/插件模板配置 `Default2D.irenderpipeline`。
- Scene、Game 和运行时 Request Provider 从同一设置解析 Pipeline。
- 不再通过临时 `new RenderPipelineAsset` 绕过真实配置。
- 本轮不另外增加逐 Camera Pipeline 覆盖，避免新增无必要的配置优先级。

Rendering2D 的强类型 Pipeline Settings 保存：

- 默认 Sprite Material。
- 灯光、阴影、Bloom、最终合成等内部 Shader 引用。
- 必要的管线策略和能力要求。

设置保存在既有 `RenderPipelineAsset` 扩展状态内，不新增必须成对维护的伴随资产。

通用基础只补齐：

- owner-aware 设置捕获与恢复。
- 中立属性 payload、稳定类型身份和自动捕获的资源依赖。
- 嵌套设置依赖并入资产导入、重导入和导出闭包。
- 当前 owner 的完整 Serialization/Reference Context。

不得把资产引用藏在不可追踪的字节或路径字符串中，也不得要求用户维护第二份依赖清单。

错误语义：

- 没有显式 Material 的对象才使用默认 Material。
- 默认 Material 缺失时，对受影响对象诊断，其他有效自定义 Material 继续工作。
- 内部 Shader 缺失不能偷偷改用普通 Sprite Shader。
- 必需效果构建失败时，明确保留可用 last-good 或显示该输出不可用；不静默跳过 Mask、光照或后处理。
- Player 构建不能用旧成功产物掩盖当前必需源错误。

---

## 四、Inspector、保存与创作工作流

### 4.1 统一选择模型

已确认采用 **统一 Inspector**：

| 选择对象 | Inspector 内容 |
|---|---|
| `.imaterial` | Material 的 Shader 引用、参数覆盖、关键词、Technique、预览与保存状态 |
| `.ishader`，未选节点 | Shader Target、公开接口、全局设置与诊断 |
| 单个节点 | 当前节点的输入、默认值、资源、源码、输出或 Pass 设置 |
| 多个节点 | 可共同编辑的兼容属性及 mixed values |
| 缺失节点/源码 | 缺失原因、保留的数据、修复/重绑定入口 |

- 画布只保留节点、端口、连线、摘要与按需预览。
- 不恢复固定 Blackboard 或侧栏。
- 不在节点和 Inspector 同时维护两套可编辑字段。
- 节点选择不能清除当前 Shader 文档或伪造 File Browser 选择。
- Inspector 锁定使用既有机制，不建立独立选择系统。

### 4.2 Material Inspector

内置通用 Material Drawer，至少实现：

- Shader 选择、定位、打开 Shader Editor。
- 根据 Shader 声明生成参数控件，而不是展示原始数组。
- 明确区分“继承 Shader 默认值”和“Material 已覆盖”。
- 单字段重置覆盖、全部重置、恢复默认和可撤销编辑。
- 合法关键词组合与 Technique 选择；只有一种有效选择时避免冗余控件。
- 当前实际支持的 `Float`、`Vector`、`Color`、`Matrix`、`Texture` 及 Texture Sampler。
- 基于声明形状显示向量组件；不把不支持的类型伪装成 Float。
- 纹理拖放、选择、清空、缩略图和 Missing 引用修复。
- HDR/线性色彩语义明确。
- 更换 Shader 后，兼容稳定参数保留；不兼容或已移除的覆盖进入可见的待处理区，不静默丢失。
- 只展示 `Material` 所有的绑定为可编辑参数；`RenderPass` 所有的缓冲、纹理等显示来源与只读诊断。

Material 的变化不修改 Shader 默认值。Shader 默认值变化也不覆盖已有 Material override。

### 4.3 节点与各种 Input 编辑

统一 Inspector 覆盖：

- 常量：受支持的标量、布尔、整数、向量和矩阵等类型。
- 暴露参数：稳定 ID、名称、默认值、范围、分组、说明及材质可见性。
- 阶段输入：受 Target 约束的合法语义和类型，不给 Compute 提供无效的顶点 `position` 默认值。
- 纹理/资源：资产引用、采样设置及绑定所有权。
- 源码函数：文件选择、公开接口、自动端口、实现匹配和源码定位。
- 输出与 Pass：阶段、MRT 输出、工作组尺寸、Role、混合、深度和模板状态。
- Target：能力要求和插件贡献的可编辑设置。

连接状态与默认值必须明确：

- 未连接输入可以编辑其默认值。
- 已连接输入展示上游来源，不让默认值看起来仍会生效。
- 参数默认值、Material 覆盖值、Pass 注入值明确区分。
- 端口改名、移除或类型变化保留待修复连接，不按位置误接。
- 插件缺失时保留节点、端口、参数和连线，不清空文档。

UI 复用已有 Inspector 体系：

- Header 描述通过 Tooltip 展示。
- 常驻说明使用 Text。
- HelpBox 表达信息、警告或错误，不代替普通段落。
- Tooltip 在窄窗口及屏幕边缘保持可读。
- Float 平时显示一位小数，进入文本编辑显示真实精度；不修改底层数值精度。
- 不为 Shader 再建第二套数值、菜单、拖放或 Undo 控件。

### 4.4 显式 Save/Revert 与预览

已确认 Shader、Material 统一采用以下语义：

| 操作 | 草稿 | 源资产 | Scene/Game |
|---|---|---|---|
| 编辑参数、节点、连接 | 修改 | 不修改 | 不应用 |
| Undo/Redo | 修改 | 不修改 | 不应用 |
| 专用预览 | 读取草稿 | 不修改 | 不应用 |
| Save | 建立新保存基线 | 原子写入 | 成功导入/编译后安全发布 |
| Revert | 恢复最新保存基线 | 不修改 | 不修改 |
| 切换选择 | 保留文档和恢复数据 | 不自动保存 | 不修改 |

补充要求：

- 预览编译隔离于正式产物发布；不能污染 canonical Asset 或运行时缓存。
- 可序列化的无效 Shader 图允许 Save，明确显示“已保存、编译失败、使用 last-good”。
- 保存状态和编译状态分别呈现。
- 连续输入可防抖更新专用预览与恢复数据，但不是自动应用资产。
- 关闭脏文档、退出时提供 Save/Discard/Cancel。
- 外部修改发生冲突时不静默覆盖；保留草稿并提供重新加载或另存。
- 只读引擎/插件资产可查看、复制到项目，不能直接写安装内容。
- 保存后定义、绑定布局和 GPU 程序在安全帧边界一致切换。
- 不同后台请求竞争时仅允许最新有效请求发布。
- 不为多个文档的 Save All 虚构跨文件原子性；逐文档报告成功或失败。

### 4.5 用户最终使用流程

**创建 Material：**

1. File Browser 创建 Material，或从选中 Shader 创建 Material。
2. Inspector 选择 Shader。
3. 修改颜色、纹理、数值等覆盖。
4. 专用预览查看效果。
5. Save 后引用该 Material 的 Scene/Game 对象更新。

**创建自定义 Sprite Shader：**

1. File Browser 创建 Rendering2D 提供的 Sprite 模板。
2. Shader Editor 打开少量有效高层节点。
3. 右键增加纹理、运算、参数或源码函数节点。
4. Inspector 编辑节点设置。
5. 预览、Save、编译。
6. Material 选择该 Shader；SpriteRenderer 使用 Material。

**调整灯光与 Bloom：**

- 普通用户修改 `Light2D`、Camera 和 PostProcess Profile。
- 管线开发者修改 Pipeline 配置或对应内部 Shader。
- 普通 Sprite 图不出现整套灯光生成和 Bloom 运算。

---

## 五、测试、清理与完成标准

### 5.1 验收矩阵

| 类别 | 必须覆盖 |
|---|---|
| 图一致性 | 删除共享程序、最后一个参数引用、Pass、剪切粘贴、类型变更；Undo/Redo 恢复全部关联声明 |
| 共享编译 | 五种混合状态不复制五套节点；相同程序缓存复用；特化不同语义不误合并 |
| Target 扩展 | 独立插件贡献节点、Target、模板、Inspector 与预览，不修改公共中央分支 |
| 脚本 API | 实际 `.editor.cs` 创作与编译；IDE 投影一致；运行时脚本不能引用 Editor-only API |
| Material | 全部现有值类型、override/reset、关键词、Technique、Missing、换 Shader、多选和保存 |
| 节点 Input | 类型对应控件、连接默认值、源码接口变更、Compute 输入合法性 |
| 草稿可靠性 | 不保存不改源文件/Scene/Game；切换、关闭、重启恢复；冲突与写盘失败不丢数据 |
| 发布 | Shader Save、Material Save、再次编辑、Undo 后 Save，渲染对应正确 generation |
| Pipeline 依赖 | 嵌套设置依赖收集、安装路径变化、插件只读、删除/恢复、Player 导出完整闭包 |
| 解耦 | 缺少默认材质不屏蔽自定义材质；内部效果不再借用默认 Sprite Material |
| GPU 能力 | Vertex/Fragment/Compute、多 Pass、实例化、MRT、storage、变体与能力不足诊断不退化 |
| 真实渲染 | 本机 Metal 的 ImGui、Scene/Game、32 灯、阴影、Mask、Bloom、材质更新和相机栈 |
| UI 稳定性 | 小窗口、DPI、Tooltip 边缘、浮动/停靠、持续 resize、画布手势与文本快捷键 |
| 生命周期 | 插件移除/恢复、设备 generation、预览退休、旧 ALC 回收；History 不保留旧对象 |
| Legacy | 源码、项目引用、脚本导出、模板、打包、Editor/Player 输出均无旧执行链 |

测试只证明实际运行过的范围。第二语言/Target 的测试扩展不冒充第二个真实 GPU Adapter。

### 5.2 性能约束

- Shader 图属于创作期，运行时执行编译产物，不解释图。
- 平移、缩放、移动节点不触发语义编译。
- 大图静止显示不得每帧重建文档。
- 保持场景提取、排序、实例上传和资源缓存策略，不因拆分 Shader 回到逐物体分配或提交。
- 在相同硬件、场景、预热条件下记录修改前后 P50/P95、GC、显存、draw/batch/instance 和编译缓存命中。
- 继续检验既定十万可见实例、稀疏 Tilemap、32 灯性能场景，以及 CPU P95 5 ms、总帧 P95 16.6 ms、稳态运行时零托管分配目标。
- 已有性能基线不得超过 10% 回归；尚未达到的原始目标明确标为未达标，不伪称已通过。
- Editor 创作 UI 与运行时提取/提交的分配分别统计，不混淆测量范围。

### 5.3 Legacy 与当前资产更新

- 不恢复 MaterialGraph、`.imaterialgraph`、Material 内嵌图或完整手写 Shader 旁路。
- 更新当前 `.ishader`、模板、生成工具、测试资产和文档；不保留双格式读取器、别名或兼容开关。
- 当前资产重写保留其逻辑身份、Material 覆盖和已有渲染意图。
- DefaultSprite 中用户添加的节点不能因为“恢复默认模板”被直接删除；基线记录后保留为可恢复数据或单独用户资产。
- 精确清理已确认废弃项目的 `bin/obj`，不删除整个工作区缓存或用户内容。
- 历史审查报告保留，增加“已修复/被替代”说明，不把历史文字命中当作 legacy 代码。
- 更新架构、脚本 API、公开 XML、中文使用文档和插件扩展示例。

### 5.4 两个仓库的变更边界

| 仓库 | 允许的主要变化 | 禁止的变化 |
|---|---|---|
| InnoEngine | 通用 Target/共享程序协议、资产依赖与发布、可复用 Shader/Material Inspector、脚本 API | Sprite/Light/Bloom 特例进入通用编译器、测试专用 public API、第二套 History/引用系统 |
| InnoEngine.Rendering2D | Sprite Target/节点/模板、内部 Shader、Pipeline 设置、资源选择、管线接入及示例 | 修改通用 Core 才能识别某个 2D 节点；硬编码安装路径；借默认 Sprite Material 执行全部内部运算 |

### 5.5 最终扩展能力与边界

完成后，应有实际样例证明：

- 插件可以自主增加普通节点、源码函数、Shader 和 Material。
- 插件可以自主增加高层 Target、模板、Inspector 和预览呈现。
- 新节点能降低到现有公共 IR 时，不需要修改引擎中央编译器。
- 新领域渲染管线可以组合既有设备能力与 Render Graph。
- 新语言或 Adapter 通过对应前端、工具链及设备契约接入。
- 全新 GPU 阶段、原生值类型或执行模型仍可能需要扩展底层契约与 Adapter；不承诺“任何东西都无需修改引擎”。

### 5.6 完成条件与固定限制

最终报告必须提供阶段完成表、两仓库变更边界、测试命令与结果、Metal 硬件信息、截图、性能比较、legacy 扫描结果和剩余问题。

固定限制：

- Windows GPU 验证按用户要求暂缓，最终报告明确列为未执行。
- `Inno.Text`、3D/PBR 实现、骨骼动画等不纳入本次实施。
- 不自动提交 commit。
- 不覆盖无关用户修改。
- 有任何未通过的当前范围验收项，就不能报告“全部完成”。

**最终交付标准：普通用户不必面对 DefaultSprite 当前的大量底层节点；插件开发者仍能表达高级渲染能力；引擎只提供通用协议；Shader、Material、Pipeline 的职责、保存和资源依赖均清楚且可验证。**
