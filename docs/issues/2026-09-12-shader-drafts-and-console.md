# Shader 草稿保存与 Console 布局修复验收

[问题索引](README.md) · [Shader Editor](../editor/Inno.Editor.Panel.ShaderEditor.md) · [统一 Shader 实施记录](2026-09-11-unified-shader-implementation.md)

## 范围与最新决策

2026-09-12 用户明确要求：Shader 图编辑不直接修改资产，只在显式 Save 时写入并应用。
这一要求取代此前的自动保存设计，不提供两套保存模式或 legacy 开关。本报告仅验收此次编辑/保存与 Console 修复，
不代表整个统一 Shader 计划、所有节点、GPU 像素及性能门槛均已完成。

## 已确认的根因与修复

1. 删除、断开连接、切换常量类型、选择源码函数四处创建了 History transaction 却没有 `Commit()`。
   Dispose 时事务按设计回滚，造成“点了但没变化”。四处均补齐显式提交；删除同时支持 Delete 与 macOS Backspace。
   文本输入控件持有输入时，ImGui 表现层不再把 Backspace、复制和撤销传给底层图；显式保存快捷键仍可用。
2. File Browser 的文件条目 ID 被误作资产 ID。文件条目 ID 与 `AssetInfo.persistentId` 本来就是两个身份。
   Shader 文档、编译查询、源码引用统一使用资产身份；纹理/源码选择通过实际 assetPath 加载。UI 定位仍使用文件条目。
3. 删除节点没有同步 Shader 自己的 Pass/参数声明。新增 `ShaderGraphBindings.RemoveNodes`，以一次中立文档变更删除
   阶段所属节点、失去最后阶段输出的 Pass 与映射，以及失去最后输入节点的参数。共享参数保留默认值及剩余阶段可见性。
   阶段剪切/复制包含内容；粘贴重建身份与唯一 Pass 名称，不静默覆盖不兼容的目标参数。
4. 原自动保存路径会在编辑、切换和退出时写资产。现在编辑只更新共享草稿/History 与 Library 恢复文件；
   Save 按钮、右键 Save、Command/Ctrl+S 和共享文档 Save/Save All 才写盘。Revert 可撤销。
   Save 原子写盘、检查外部指纹；下一次 Editor Update 明确请求导入，不依赖 watcher。编译读取已保存资产而非草稿。
   UI 分开显示“未保存”和“Saved asset 的编译状态”。无效图允许保存，但不能把失败伪装为编译成功。
5. Console 并不是 Shader 独有的一套样式。共用详情表的固定标签列未分配宽度，且与原来的 sizing flags 不匹配，
   导致 Kind/File/Source/Time 被裁成 K/F/S/T。现在按当前字体显式测量标签列，值列填充余宽；极窄时改为上下布局。
   普通 Log 与 Diagnostic 继续共用一套 card、元数据顺序、颜色与右键菜单。

截图中的 `BGFX has no 'position' builtin for stage 'Compute'` 是真实的阶段接口错误，不是排版问题。
没有屏蔽诊断、删除用户 Compute 节点或改写用户 Shader。新建通用输入默认是独立参数；在 Compute 中显式选择 Builtin 时
初始化为 `global-invocation-id` / `uint3`，不再默认套用顶点 `position`。已存在的错误由用户在草稿中修正并 Save 后重新编译。

源码节点的 `Apply Import Settings` 仍是针对 `.ishadersource.imeta` 的独立显式操作；不是图的自动保存。
IDE 保存源码文件也仍属于源文件编辑行为。

## 变更边界

- 主仓库：`Inno.Editor.Panel.ShaderEditor` 的草稿/操作/引用/呈现，`Inno.Editor.ImGui` 的通用文本快捷键保护，
  `Inno.Editor.Panel.Logging` 的共用详情布局，及 `Inno.Rendering.Shaders` 的 Shader 声明一致性操作。
- 配套：公开 API/流程文档、公共工作流测试和原生 ImGui 布局测试；未为测试扩大生产公开面。
- 未修改 Rendering Core、BGFX 编译/设备执行、2D 领域语义或项目 Shader 数据。上述删除 API 属于通用 Shader 创作层，
  不认识 Sprite/Light/Tile，也不进入 Player 的渲染调度。
- `InnoEngine.Rendering2D` 本轮未编辑。两个仓库此前已有的未提交修改均保留，没有提交 commit。

## 验收结果

主机 macOS arm64 / Apple M2；工作目录为 InnoEngine，命令均使用 `/Users/aaronliao/.dotnet/dotnet`。

| 验证 | 命令 | 结果 |
| --- | --- | --- |
| 完整 Editor Scripting | `test tests/scripting/Inno.Editor.Scripting.Tests/Inno.Editor.Scripting.Tests.csproj --no-restore --logger 'console;verbosity=normal'` | 104 passed，0 failed，约 91 秒 |
| Shader 创作/编译 | `test tests/rendering/Inno.Rendering.Shaders.Tests/Inno.Rendering.Shaders.Tests.csproj --no-build --no-restore --logger 'console;verbosity=normal'` | 113 passed，0 failed，约 19 秒；此前本轮已构建并通过同组测试 |
| Console / PlayMode | `test tests/editor/Inno.Editor.PlayMode.Tests/Inno.Editor.PlayMode.Tests.csproj --no-build --no-restore --logger 'console;verbosity=normal'` | 25 passed，0 failed；同轮已构建 |
| Editor 构建 | `build src/composition/editor/host/Inno.Editor.Application/Inno.Editor.Application.csproj --no-restore -m:1 -p:UseSharedCompilation=false -v minimal` | 0 warning / 0 error，28.16 秒 |
| 工作树格式 | `git diff --check` | 通过 |

总计 242 项，不重复计数：Shader Editor 工作流的 26 项已包含在 104 项中；Console 原生布局 4 项已包含在 25 项中。
日志目录：`/private/tmp/inno-shader-save.LjC2nB/`，最终记录分别为 `editor-scripting-final.log`、`shaders-final.log`、
`editor-console.log`、`editor-build.log`。

工作流包含：Delete/Backspace 删除 Compute 阶段及声明、单次 Undo、剪切/粘贴/复制阶段、源码身份与端口、断线事务、
关闭 Save/Discard/Cancel、外部冲突、未保存草稿跨选择/重启恢复、退出前最后一次 Undo、禁用 watcher 后 Save 仍更新
资产 contentVersion、Undo 不直接应用。保留此前 10 组 100%–200% 缩放及窄/矮窗口原生断言回归。

Console 测试实际创建生产 Panel、发布普通日志/诊断、鼠标展开，并检查原生列几何；覆盖 100%/150% UI 缩放及
960/500/260/140 px 宽度变化。保持原生断言启用，不通过关断言或增加测试专用生产入口达成。

本轮没有手工桌面截图、Scene/Game GPU 像素对比或新性能报告；原生 ImGui 与资产版本测试不冒充这些验收。
Windows 按用户此前要求暂缓。
