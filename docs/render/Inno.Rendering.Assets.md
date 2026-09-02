# Inno.Rendering.Assets

[Rendering 索引](README.md) · [公开 API](Inno.Rendering.md) · [ShaderGraph](Inno.Rendering.ShaderGraph.md)

`Inno.Rendering.Assets` 将通用渲染源转换成后端中立候选产物。它不创建 GPU handle，也不包含具体材质模型。

## 当前源格式

| 后缀 | 行为 |
| --- | --- |
| `.ishader` | Inno 原生序列化 `ShaderAsset`；Pass 引用独立 `.sc` 和 varying source。 |
| `.imaterial` | Inno 原生序列化 `MaterialAsset`；保存 Shader 引用、Technique、稳定 Property、Keyword 和开放 Metadata。 |
| `.irenderpipeline` | Inno 原生序列化 `RenderPipelineAsset`；保存 Pipeline Stable Type ID、原生状态和 Feature 配置。 |
| `.sc` / include | 可手写 shaderc 文本，由 Asset 依赖图跟踪。 |
| `.png/.jpg/.jpeg/.tga/.hdr` | 验证源并生成后端可上传纹理候选。 |
| `.obj/.gltf/.glb` | 生成通用 `GeometryData`、section、vertex layout 和 bounds。glTF JSON 仅是外部标准格式解析，不是 Inno 持久化旁路。 |

所有 Inno 自有结构化渲染资产都经过 `SerializationRegistry`、`ISerializable`、`[SerializableProperty]` 和 Asset reference converter；不再使用自定义 JSON reader。

## 统一 Shader 链

```text
ShaderAsset + .sc ─┐
                   ├─ ShaderDefinition / ShaderIRModule
ShaderGraph ───────┘       │
                           ├─ validation / variant planning
                           ├─ backend compiler target
                           ├─ injected target toolchain
                           └─ reflection + last-good artifact
```

`ShaderIRSourceKind` 只区分来源；两条路径没有独立编译器。Assets 层不知道 shaderc、Metal 或 D3D，只调用注入的 `IShaderCompilerToolchain`；具体 profile 与可执行工具由图形后端拥有。同一 `.sc` 因而可以被不同后端编译器消费，不需要平台专用源文件副本。

## 公开 API

| API | 说明 |
| --- | --- |
| `ShaderCompiler`, `ShaderCompileTarget`, `ShaderVariantKey` | capability-aware 的共享 IR、验证、变体与候选编译。 |
| `IShaderCompilerToolchain`, `ShaderToolRequest`, `ShaderToolResult` | 后端编译器注入边界；通用层不选择 API profile。 |
| `ShaderIRArtifactSerialization` | 共享 IR 的 Inno 原生产物序列化。 |
| `ShaderLastGoodStore` | 候选失败时保留当前完整 artifact。 |
| `ShaderAssetRuntime` | 从已提交 ShaderAsset 获得共享 IR。 |
| `GeometryData`, `GeometryArtifactCodec`, `GeometryAssetRuntime` | 通用几何 CPU 产物。 |
| `ITextureTargetCompiler` | 后端拥有的可取消异步纹理目标编译器边界。 |
| `RenderingAssetFormatException` | 源路径可定位的严格格式错误。 |

Importer 声明 include、source、材质、纹理和几何外部 buffer 依赖。Shader 中的 `#include "path.sc"` 从当前 Asset Source Mount 解析并进入依赖图；跨 Plugin 使用 `#include "plugin.id::path.sc"`，且消费方清单必须声明该 Plugin 依赖。`#include <bgfx_shader.sh>` 这类不带 Source ID 的尖括号 include 保留给 shaderc 工具链处理，不会被当作项目资产。Importer 通过候选 Mount 快照读取依赖，禁止直接访问当前全局 Mount，因此尚未发布的 Plugin generation 可以被完整验证后再原子激活。

`.ishader` 的 IR 会嵌入所引用 `.sc` 的当前内容，因此 Shader importer 同时登记 runtime dependency 与 artifact invalidation dependency。任意阶段源码重新导入后，只有受影响的 Shader IR、目标 Program 与其下游资源会重建；不会继续使用包含旧源码的 IR，也不需要清空整个 `Library`。

Build fingerprint 包含 Processor MVID、目标 profile key、输入 artifact 与原生定义 bytes；成功候选才替换 last-good。Editor 注入可执行目标编译器；Player 可以改为注入只读取预编译 artifact 的实现，不需要在运行时携带 shaderc。

Runtime 不会在 render thread 同步启动或等待 shaderc/texturec。源 Shader/Texture 的目标编译通过后台 prewarm job 执行，完成 artifact 只在后续帧安全点发布；同一资产内容更新会取消并退休旧 job，连续编辑不会无限积累编译任务。
