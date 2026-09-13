# Inno.Editor.Shaders

[Editor 索引](README.md) · [Wiki 首页](../README.md) · [Shader Editor](Inno.Editor.Panel.ShaderEditor.md) · [Shader 编译](../render/Inno.Rendering.Shaders.md)

## 职责与边界

可复用的 Shader/Material 创作功能。节点编译属于 Rendering.Shaders；呈现协议独立于具体 Panel。节点配置在统一 Inspector 编辑，画布保留摘要与连接。

## 初始化与生命周期

宿主在 TypeCatalog 与预览服务建立后创建 ShaderNodeDrawerRegistry，在 Editor feature 停止时 Dispose。
每次查询与绘制持有共享 generation operation；候选发现重复 ID 时失败，旧 provider 按 TypeRegistry 的事务与退休协议管理。
Drawer 不得保存 DrawContext、PreviewHandle、Asset 或历史回调到下一帧／下一代。

## 公开 API

| API | 语义 |
| --- | --- |
| `ShaderNodeDrawerAttribute(definitionId, displayName)`、`definitionId`、`displayName` | 稳定节点 ID 与可选艺术家可读名称，名称只影响节点标题/菜单，不参与编译 |
| `ShaderNodeDrawer.Draw(context)` | 统一 Inspector 中的帧内绘制入口 |
| `ShaderNodeDrawContext(node, serialization, context, write, previews, inspection, readOnly)` | 独立节点快照、完整 owner context、统一草稿 History 写入、预览与共享 Inspector 上下文 |
| `nodeId`、`previews` | 稳定控件身份和当前 generation 预览服务 |
| `Read<T>(key, defaultValue)` | 解码独立属性，缺失使用声明默认值，损坏明确失败 |
| `Write<T>(key, value, continuous)` | 向宿主提交中立值；进入草稿 History，不保存或发布 Asset |
| `DrawProperty<T>(key, label, defaultValue)` | 复用现有属性 Drawer、资产选择/拖放和数值控件；受草稿 History 与只读检查约束 |
| `ShaderNodeDrawerRegistry(types)`、`TryDraw(id, context)`、`Dispose()` | 按 generation 发现、调用和退休呈现；缺少 Drawer 时返回 false |
| `GetDisplayName(definitionId)` | 获取本代际中立显示名称；未贡献名称时返回 null，由宿主提供通用名称 |
| `MaterialDocuments.Open(path)`、`Read(id)`、`Replace(id, material, finishGesture)`、`Commit(id)` | 原生 Material 草稿；读回的是独立可编辑对象，显式保存前不发布 |
| `MaterialDocuments.ReplaceMany(candidates, finishGesture)`、`CommitMany(ids)` | 一组兼容材质共享一次 History 事务，不提供跨文件原子保存承诺 |
| `ShaderPropertyInspector.Draw(...)`、`Compatible(type, kind)` | Shader 默认值与 Material 覆盖复用属性 Drawer；Float/向量/线性 HDR Color/Matrix/Texture/Sampler，精确类型匹配 |
| `ShaderParameterPresentation`：`group`、`description`、`visible`、`hasRange`、`minimum`、`maximum` | Editor-only 参数展示。按稳定绑定 ID 共享，不改变已有数值，不进入运行时定义或 Player |
| `ShaderParameterPresentation.Read(graph, propertyId, serialization, context)`、`Write(...)` | 读写独立图中的原生展示元数据；调用方负责共享 History。范围必须有限且有序；不是资产保存 API |
| `ShaderPreviewProviderAttribute(contractId)`、`contractId` | 按开放 Shader Contract 注册预览；重复契约阻止候选激活 |
| `ShaderPreviewProvider.CreateLayer(context)` | 插件贡献几何、环境、Pass 输入和 Pipeline，不修改 Scene 或 canonical Material |
| `ShaderPreviewContext(resourceId, material, artifact, definition, diagnostics, pixelWidth, pixelHeight)` 及同名只读属性 | 帧内的独立发布范围、草稿值、不可变编译产物、精确接口、隔离诊断和像素尺寸；禁止跨帧保留 |
| `ShaderPreviews.DrawMaterial(ownerId, material, logicalSize)` | 对独立 Material 覆盖值执行实际 GPU 预览，不保存或应用源资产 |
| `ShaderPreviews.Draw(ownerId, material, compilation, logicalSize)` | Shader 草稿编译结果的实际 GPU 预览；失败时明确标识 last-good |
| `ShaderPreviews.Release(ownerId)` | 关闭预览并释放对应视口和 GPU 程序；未绘制的预览自动退休 |

公共签名涉及 Graph、Serialization、Rendering Preview 与 TypeCatalog，其项目引用向下游公开。
节点编译与脚本导出依赖仅作为实现依赖。

```csharp
using InnoEditor.Shaders;

[ShaderNodeDrawer("example.surface")]
public sealed class SurfaceDrawer : ShaderNodeDrawer
{
    public override void Draw(ShaderNodeDrawContext context)
    {
        context.DrawProperty("gain", "Gain", 1f);
    }
}
```

Editor 脚本导出 Drawer、预览 Provider、标记、帧内 Context、MaterialDocuments、ShaderPreviews 和 ShaderPropertyInspector，命名空间为 `InnoEditor.Shaders`。Registry 由宿主管理。

## 实际渲染预览

Material Inspector 的 Material Preview 与 Shader Inspector 的 Shader Output Preview 共用此模块。输出预览表示整个 Shader 的结果，不冒充任意中间节点的数值可视化。
Rendering2D 的 `SpriteShaderPreview`/`SpritePreviewPipeline` 是独立的 Editor-only 消费者：使用中性的未受光 Sprite 平面、白色实例纹理和独立材质覆盖，通过普通 Render Graph 绘制。
不同领域通过自己的 Contract 提供网格、环境与 Pass 参数，通用引擎没有 Sprite 分支。若多种可预览 Contract 同时匹配，要求选择 Technique，不按发现顺序猜测。

预览 Shader 使用隔离编译产物；Material 使用当前 Shader 产物和独立覆盖。GPU 创建、反射/能力校验、绑定以及 last-good 复用正式资源链，但缓存键包含独立 owner scope，错误只进入预览 reporter。
预览帧只保留本帧的 Provider 结果；后续状态仅保存稳定 ID、计数和中立诊断。宿主 generation 切换时统一清理视口及注册，Provider 通过 TypeRegistry 候选事务退休。

## Material 工作流

File Browser 的 Create 菜单支持创建 Material 和从选中 Shader 创建 Material。单击材质进入 Inspector；Command/Ctrl+单击可选择多个材质，兼容参数在同一 Inspector 中共同编辑。
继承值与独立 override 分开显示，支持单项/全部重置、关键词、Technique，以及更换 Shader 后保留的未解析参数。
颜色值按线性 RGBA、允许 HDR 输入，不对底层浮点数作显示精度截断。纹理选择/拖放与采样器使用既有属性 Drawer。

参数节点 Inspector 可编辑分组、hover 说明、材质可见性与 Float 编辑范围。多选材质只显示共同可见且类型兼容的参数，数值范围使用交集；不把改动前已超界的值静默钳制。隐藏是创作 UI 策略，不禁止运行时通过稳定 ID 设置参数。
展示记录保存在 `inno.editor.parameter.*` 图 metadata，由同一参数的所有节点共享。语义指纹排除此保留前缀；仅改展示不重编译 GPU 程序。Material 使用已导入的 Shader 创作快照，按资产 contentVersion 缓存，不每帧重读整个图；未保存 Shader 草稿不会改变 Material Inspector。

保存使用共享 Document Host，Save/Revert/关闭确认/全部保存不另建系统。MaterialDocuments 组合 [AssetDraftDocuments](Inno.Editor.Assets.md)，恢复数据在 Library/Editor/AssetDrafts/inno.material；外部修改冲突保留两份内容，不覆盖外部源。
资产/节点多选只保存稳定 ID；Drawer 重新查询当前 generation，不把节点选择当作 File Browser 选择。
完整渲染预览及 UI 验收仍见[实施记录](../issues/2026-09-12-shader-authoring-execution.md)，不能把编译预览等同为画面预览。
