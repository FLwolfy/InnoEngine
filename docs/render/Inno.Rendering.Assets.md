# Inno.Rendering.Assets

[返回 Rendering 索引](README.md) · [Wiki 首页](../README.md) · [艺术家 API](Inno.Rendering.md) · [BGFX 后端](Inno.Rendering.Bgfx.md)

## 职责与边界

`Inno.Rendering.Assets` 负责把项目源文件转换为后端中立、内容寻址的候选产物。它注册 Shader、Material、Pipeline、Texture 与 Mesh Importer，维护统一 Shader IR、renderer profile 选择、shaderc 调用、结构化诊断和 CPU last-good artifact。

该程序集不创建 GPU handle。BGFX tools 与 `Inno.Assets.Loader` 是私有实现依赖；公开结果只泄漏 `Inno.Rendering`、`Inno.Rendering.Core` 及中立数据。Player 只读取已经构建的 artifact，不应运行 shaderc。

## 已支持源格式

| 后缀 | 当前导入行为 |
| --- | --- |
| `.ishader` | 严格 JSON manifest；允许注释和尾逗号；读取 `.sc`/varying、递归扫描 include，生成共享 `ShaderIRModule` |
| `.imaterial` | 按 Shader 稳定 Property ID 校验 Float/Vector/Color/Matrix/Texture，验证 Keyword 选项并声明 Shader/Texture 依赖 |
| `.irenderpipeline` | 导入 Pipeline Stable Type ID、Render Path、质量和有序 Feature 中立配置 |
| `.png/.jpg/.jpeg/.tga/.hdr` | 验证容器头和尺寸；HDR 标记 Linear，其余默认 sRGB；Editor 以 texturec 生成带 mip 的 RGBA8 KTX 候选并在帧安全点上传 |
| `.obj` | 顶点去重、面扇三角化、缺失法线/切线生成、submesh 规范化，并提交对象空间 bounds |
| `.gltf/.glb` | glTF 2 accessor/buffer/GLB 解析、外部 buffer 依赖、attribute/index 校验、对象空间 bounds，并从右手坐标转换到引擎左手坐标 |

所有自有 artifact 使用严格 magic 或结构验证，不包含 legacy schema、迁移器或多代 reader。

## 统一 Shader 链

```text
.ishader source ─┐
                 ├─ ShaderDefinition + ShaderIRModule ─ validation ─ shaderc profile ─ target bytes
.ishadergraph ───┘                                           │
                                                            └─ ShaderInterface
```

手写与节点生成 stage 都以 `ShaderIRStageModule` 进入同一个 `ShaderCompiler`。区别仅由 `ShaderIRSourceKind` 和源码行到 Node ID 的映射表达；不存在第二套节点专用编译器。

`ShaderIRValidator` 在调用 shaderc 前验证 Property/Keyword/Pass 唯一性、Pass-local binding、Raster stage 配对、Compute 隔离、Pass tag 与 capability。`ShaderIRPass.bindingIds` 让同一 Shader 的 clustered 与 fallback Pass 保持各自最小资源接口；创建 Program 时以 `CompiledShaderPass.shaderInterface` 验证真实反射。能力不满足的替代 Pass 会产生 Warning 并跳过，只要同一 Shader 仍有可执行 Pass，候选仍然有效。`RendererProfileCatalog` 是平台/profile 的唯一选择点：Windows x64 支持 D3D/Vulkan/OpenGL 目录，macOS arm64 支持 Metal/Vulkan/OpenGL 目录。

## 公开 API

| API | 语义 |
| --- | --- |
| `ShaderTargetPlatform`, `ShaderCompilerProfile`, `RendererProfileCatalog` | capability-aware 目标 profile 选择 |
| `ShaderCompileTarget`, `ShaderVariantKey` | 编译策略和确定性静态变体键 |
| `ShaderCompiler`, `ShaderCompilationResult` | 候选编译，不直接修改 active state |
| `ShaderStageArtifact`, `CompiledShaderPass`, `CompiledShaderArtifact` | 不可变目标二进制、Pass-local 与模块级 manifest-derived interface |
| `ShaderLastGoodStore`, `ShaderArtifactSelection` | 成功时原子替换，失败时保留当前可用 artifact |
| `ShaderAssetRuntime` | 从已提交 Shader 资产解码共享 IR |
| `TextureTargetCompiler` | 将受支持源图转换成已验证、跨后端可消费的 KTX 目标产物 |
| `MeshVertex`, `MeshSubMesh`, `MeshData`, `MeshAssetRuntime` | 后端上传使用的规范化 CPU geometry |
| `RenderingAssetFormatException` | 带 JSON/source path 的严格格式错误 |

Importer、artifact codec 与具体 shaderc toolchain 是内部实现，不属于脚本契约。

## 依赖、热重载与失败隔离

- `.sc`、varying、include 与 glTF 外部 buffer 都调用 `DependsOnSource`；Material 使用强类型 `ResolveDependency<TAsset>` 声明并解析 Shader/Texture。
- Importer 候选只有在完整状态和全部命名 artifact 成功后才提交；已有 `lastSuccessfulArtifactKey` 不会因候选失败被覆盖。
- Shader candidate 编译失败时，`ShaderLastGoodStore` 按 Persistent ID、target key 和 variant 保留上一份完整 artifact。
- Build fingerprint 包含 Processor MVID、Definition 类型/MVID、源 hash、序列化定义内容、runtime payload 和输入 artifact，修改定义不会误命中旧构建。
- Asset 本身不持有 GPU handle；GPU program/texture/buffer 的原子切换与延迟释放由渲染设备安全点完成。
- Texture candidate 构建或上传失败时不替换 Registry 中的旧 handle；成功候选安装后才延迟释放旧 GPU 纹理。

## 常见工作流

```csharp
using Inno.Rendering.Assets;

ShaderIRModule module = ShaderAssetRuntime.GetModule(shader);
ShaderCompilerProfile profile = RendererProfileCatalog.Resolve(targetPlatform, capabilities);
var target = new ShaderCompileTarget(profile, capabilities);
ShaderCompilationResult candidate = await new ShaderCompiler().CompileAsync(
    module,
    target,
    ShaderVariantKey.empty,
    assetsDirectory);
ShaderArtifactSelection selected = lastGood.Select(
    shader.identity.persistentId,
    target.key,
    ShaderVariantKey.empty,
    candidate);
```

Editor 应展示 `candidate.diagnostics`；只在 `candidateSucceeded` 时安排 BGFX Program 在帧边界替换。

## 相邻页面

- [Inno.Rendering](Inno.Rendering.md)：Shader/Material/Pipeline 公开资产契约。
- [Inno.Rendering.Core](Inno.Rendering.Core.md)：capability 与命令边界。
- [Inno.Rendering.Bgfx](Inno.Rendering.Bgfx.md)：GPU artifact 的最终设备实现。
