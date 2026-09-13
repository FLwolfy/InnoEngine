# Inno.Editor.Panel.ShaderEditor

[Editor 索引](README.md) · [Wiki 首页](../README.md) · [Graph 控制层](Inno.Editor.Graph.md) · [Shader 模型](../render/Inno.Rendering.Shaders.md)

## 职责与边界

内置 `.ishader` 编辑界面，替代已经移除的 Material Graph Panel。`.imaterial` 仍只保存 Shader 引用及参数，不承载图。画布跟随 File Browser 当前 Shader 选择；双击 Shader 打开并聚焦。没有固定侧栏或路径输入框；顶部提供 Save / Revert 和文件名，星号表示尚未应用的草稿。

当前实现与完整验收必须区分：右键菜单、节点值编辑、捕获式平移、鼠标锚点缩放、框选、节点移动、连接、复制粘贴、显式保存已经接线；完整 UI 实操、所有高级节点/资源操作和最终渲染一致性尚待验收。详见[实施状态](../issues/2026-09-11-unified-shader-implementation.md)。

## 初始化与生命周期

宿主提供 AssetPipeline、SerializationRegistry、TypeCatalog、GraphEditorModule、EditorInteractions、EditorShaderCompilation、AssetImportSettingsEdits 和 IEditorPreviewService。ShaderEditorDocuments Module 使用 LifetimeScope 拥有当前 generation 的节点、前端、drawer registry 和文档注册；停止时只保留未保存恢复数据并按依赖顺序退休，不写入源资产。专用 Panel 以 `revealHost: false` 打开共享文档，不额外弹出第二个 Document Host。

文件条目 identity 与资产 identity 是不同身份：从文件条目的 `assetPath` 查询 AssetInfo，再以 `AssetInfo.persistentId` 打开图文档、查询编译、引用源码。不能拿文件条目的 ID 调用资产加载或当成 Shader/source reference。

图修改统一进入 GraphDocumentController / EditorInteractions.history。节点拖动只在释放时提交；数值与文本使用独立 gesture ID。平移、缩放只保存为按资产 persistent ID 索引的视图状态，不恢复 File Browser 选择。

## 公开扩展 API

下列扩展已移入 [Inno.Editor.Shaders](Inno.Editor.Shaders.md)，本 Panel 仅作为使用方，不再拥有公共节点创作协议。

| 类型 / 成员 | 契约 |
| --- | --- |
| `ShaderNodeDrawerAttribute(string definitionId)` / `definitionId` | 注册一个稳定节点 ID 的 Editor-only 呈现；重复 ID 拒绝候选 |
| `ShaderNodeDrawer.Draw(ShaderNodeDrawContext)` | 在统一 Inspector 绘制选中节点的控件；不编译 Shader，不保存当前帧 context |
| `ShaderNodeDrawContext.previews` | 帧内使用共享 generation-scoped 预览；不得缓存过期 handle |
| `ShaderNodeDrawContext.nodeId` | 用于稳定控件身份的节点 ID |
| `Read<T>(key, defaultValue)` | 通过当前 owner 的序列化上下文读取独立值，损坏值不替换为默认 |
| `Write<T>(key, value, continuous)` | 写入中立草稿属性并进入统一 History；不直接保存或发布资产 |

这些类型在 EditorScripts 中使用逻辑命名空间 `InnoEditor.Shaders`。普通 runtime scripts 不引用该项目。编译扩展继续属于 `InnoEditor.Rendering.Shaders`，与 UI drawer 独立。

```csharp
using InnoEditor.Shaders;

[ShaderNodeDrawer("example.surface")]
public sealed class SurfaceDrawer : ShaderNodeDrawer
{
    public override void Draw(ShaderNodeDrawContext context)
    {
        float current = context.Read("gain", 1f);
        // Draw a shared Editor numeric widget here and call Write only after a user edit.
        // context.Write("gain", editedValue, continuous: true);
    }
}
```

## 保存与错误

- 编辑、拖动、连接、Undo/Redo 只改变草稿；Save 按钮、画布右键 Save 或画布聚焦时 Command/Ctrl+S 才写入 `.ishader`。共享 Document Host 的 Save/Save All 也显式保存。保存不以图编译成功为条件。
- 写盘前先保留 Library/Editor/ShaderRecovery 中的中立恢复数据；比较上次读取的源指纹，已发生的外部修改拒绝覆盖。
- 文件切换、关闭 Shader Editor 面板和停止不应用草稿。恢复文件只位于 Library，不参与资产导入或 GPU 发布。失败保留文档、历史与恢复文件，并在画布显示错误。
- 源码外部更新：未编辑文档接受新源；dirty 文档显示冲突，不覆盖磁盘。暂时缺失源保留图和 Undo barrier。
- Save 与编译是不同状态；编译状态明确标记为 Saved asset。保存完成后在下一次 Editor Update 请求导入，不依赖 watcher 延迟；无效已保存图继续显示失败和 last-good，不伪装成成功。
- 安装资产只读，可查看；右键“Copy Shader to Project”创建独立项目资产并选中，创建可撤销。
- Close 由共享文档服务处理 Save/Discard/Cancel；provider 不在 Discard 后偷偷 Save。
- Revert 恢复已保存内容，可通过 Undo 找回草稿；Undo 后仍需 Save 才会应用。
- 源码节点的 `Apply Import Settings` 是对所选 `.ishadersource.imeta` 的独立显式操作，可能影响引用该源码的其他 Shader；它不代替当前 Shader 图的 Save。编辑源码文件本身仍使用 IDE 保存。
- 删除支持 Delete 与 Backspace。删除阶段输出同时删除该阶段的内容；最后一个阶段输出删除后清除对应 Pass 及引用映射；删除最后一个参数输入清理其声明，共享输入保留默认值和剩余阶段可见性。剪切/复制阶段包含其内容，粘贴重建节点身份与 Pass 名称。事务显式 Commit，一次操作对应一次 Undo。

## 当前限制

节点参数和输入默认值现在在 Inspector 编辑，不在画布重复一套字段。未连接数值输入支持精确类型默认值，连接后只显示上游来源；资源/副作用必须接线。
Inspector 的 Compile Draft Preview 使用独立编译缓存，未经 Save 不进入正式资源发布；它当前是编译预览，不是完整材质画面预览。

Pass/Variant、Technique/Role、自定义混合和能力要求在节点临时弹窗中编辑。存储读写、原子加法和 discard 节点通过显式 after/then 连线约束副作用顺序。右键沿用共享菜单与搜索，支持按端口类型创建、分组、连接线转接点和项目副本。转接点保留完整结构体、数组和资源类型。源码导入设置通过 .imeta 与共享 History 编辑；端口快照只保存中立类型/身份，缺失端口以红色保留，不按序号重连。预览按需展开。

源码文件可从节点打开到系统关联的 IDE。右键 `Compilation Diagnostics` 打开临时诊断窗口；带源位置的诊断可通过 `Locate`
定位到源码行并显示行列、复制位置。只读取当前 source mount，源缺失时报告错误；不猜测任意 IDE 的命令行协议。
完整 UI 实操和热重载回归仍在最终验收清单中，不能把接线完成等同为验收通过。
