# Inno.Rendering.Assets

[Rendering 索引](README.md) · [公开运行时 API](Inno.Rendering.md) · [Shader 图](Inno.Rendering.Shaders.md)

## 唯一创作链

`.ishader` 是原生 `GraphDocument`。手写代码只能作为 `.ishadersource` 函数模块参加图编译，不存在完整 vertex/fragment 源码资产入口。

```text
.ishader + .ishadersource / .imeta / include 依赖
    → 冻结图与函数依赖 → 类型、阶段和能力检查
    → ShaderIrProgram / ShaderIrStage → Adapter 生成与编译
    → 反射校验 → RenderShaderArtifact（定义 + binary + bindings）
```

图不是 Render Graph：资源生命周期、灯光、Bloom 和绘制顺序继续由 Pipeline 调度。

| 后缀 | 所有权与内容 |
| --- | --- |
| `.ishader` | 图、节点、端口、参数、Pass、Technique 和目标配置；运行时不携带画布 |
| `.ishadersource` | 普通函数源码；`.imeta` 保存语言、公开函数和各 Adapter 实现引用 |
| `.imaterial` | Shader 引用、参数覆盖、Technique、变体与开放 metadata；不保存图 |
| `.irenderpipeline` | Pipeline Stable Type ID、原生配置和 Feature |
| 图片、OBJ / glTF | 通用纹理和几何导入；不认识 2D / 3D 材质模型 |

Inno 自有结构化内容只经过 owner 的 SerializationRegistry 和完整 AssetSerializationContext。GLTF 等外部文件格式由对应 importer 解析。

## 编译与源模块 API

| API | 契约 |
| --- | --- |
| `ShaderCompiler.CompileGraphAsync` | 显式接收 `IAssetArtifactLookup`，读取已导入图和冻结函数依赖，使用当前 generation 的节点与语言 registry |
| `CompileAsync(definition, program, target, variant, serialization, context, cancellationToken)` | 发布前捕获定义 bytes；声明、绑定和 binary 来自同一候选 |
| `CompileAsync(ShaderIrStage, target, cancellationToken)` | 单个 typed stage 的后端生成与编译，不发布 GPU 对象 |
| `IShaderCompilerToolchain` | 声明支持语言与目标，消费 typed IR，返回结构化诊断 |
| `ShaderStageToolRequest / ShaderStageToolResult` | 冻结输入阶段、目标、结果 bytes、反射 bindings 和 diagnostics |
| `ShaderGraphArtifact.GetSemanticHash` | 内容、依赖与语义配置哈希；忽略节点位置及保留的 `inno.editor.*` 视图 metadata |
| `ShaderSourceBundle` | 冻结源文件、include、实现身份；不持有运行时对象或解析器 |
| `ShaderSourceImportSettings` | 语言 ID、公开函数、实现 ID 和其他实现的资产引用 |
| `ShaderGraphSourceStore.Read / Save` | 直接读写可序列化图，不以编译成功为保存前提 |
| `ShaderLastGoodStore` | 坏候选不覆盖成功产物；构建不得以其掩盖当前源错误 |
| `ITextureTargetCompiler` | Adapter 拥有的可取消纹理目标编译 |
| `GeometryData / GeometryArtifactCodec` | 通用几何 CPU 产物 |

ShaderSource importer 在候选 mount 中解析依赖，端口由前端解析函数签名，不用正则猜测。不同 Adapter 的实现必须显式提供且接口一致，不自动翻译任意原生代码。语言、节点与工具链通过稳定 ID 和 TypeCatalog generation 接入；公共编译器不判断具体渲染插件。

## 保存、失效与发布

- 图的源快照保存与编译状态独立。无效图可保存并恢复，Editor 显式标记编译失败及 last-good。
- 自动保存通过同目录原子替换。预先发现的外部修改拒绝覆盖；在检查与原子替换间发生的竞争写入会被原子捕获并保留为 `.external-conflict-*`，报告冲突并保留恢复文档，不静默丢弃任一版本。
- 通用 Import Settings 使用现有 `.imeta`；引用进入 source / artifact 依赖追踪。设置保存后导入失败仍保留设置和旧成功产物，异常事务则尝试恢复原 sidecar。
- 后台编译只发布当前请求。节点移动不启动编译；语义、依赖、目标、变体或扩展 generation 变化会失效。
- Runtime 按帧采纳完整 publication：定义、各已使用 Pass 的程序及绑定布局一致切换。候选任一程序创建失败保留全部旧 publication；Undo 发布旧语义产物时也重新切换。
- 源码解析器、图、Editor 及 native 工具链不进入 Player。部署包只携带所需 runtime 数据和目标产物。
- 图及函数快照分别写入 `shader-graph`、`shader-function` 命名产物，标记 `AuthoringOnly`。Shader 的 `runtime` 输出为空，源无关的定义保存在资产状态及目标产物中；函数资产整体只用于创作。导出由通用 Artifact output scope 裁剪，不在 AssetLoader 判断 Shader 类型。
- ImGui 也在构建时经过这条图链，启动只读匹配目标的嵌入产物；缺失即安装/构建错误，不进行启动源码编译。

实际验收与尚未完成的项目见[统一 Shader 实施记录](../issues/2026-09-11-unified-shader-implementation.md)。
