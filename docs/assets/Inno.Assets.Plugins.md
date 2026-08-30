# Inno.Assets.Plugins

[Assets 索引](README.md) · [Inno.Assets](Inno.Assets.md) · [Project Settings](../core/Inno.Core.Settings.md) · [Rendering](../render/README.md)

`Inno.Assets.Plugins` 实现本地 ZIP/Folder Plugin 容器、导出、安全验证、依赖排序和 Source Mount 激活。它不是 Package Manager：没有版本解析、远程仓库、发布服务或平台二进制安装。ZIP 适合不可变分发，Folder 适合像 Minecraft 资料夹模组一样直接开发；两者进入完全相同的逻辑内容协议。

## 磁盘协议

```text
<Project>/
├─ Assets/                       可写 project mount
├─ Plugins/
│  ├─ sample.rendering.zip       不可变分发 Plugin
│  └─ sample.tools/              可编辑 Folder Plugin
│     ├─ Plugin.inno
│     └─ Assets/...
├─ ProjectSettings.inno
└─ Library/Plugins/<id>/<hash>/  安全解压缓存，可完全重建

sample.rendering.zip
├─ Plugin.inno                   Inno Serialization 清单
└─ Assets/...                    源、.imeta、脚本、.sc 与普通资产
```

`Plugin.inno` 只声明 `pluginId`、显示名、依赖、显式 override、内容根、程序集定义入口与项目设置默认贡献。Component、Pipeline、Feature、Shader Node、Importer 和 Panel 仍由 TypeCache Attribute 自动发现，不写类型清单。

Folder 必须是 `Plugins/` 的直接子目录，ZIP 必须是直接子文件。Folder 不复制到 Library，而是原地校验后建立只读运行时 Mount；“只读”是 Asset API 权限，不妨碍开发者用外部编辑器修改源文件。ZIP 校验后安全解压到内容 hash 缓存。两者的 content hash 都按规范化相对路径与文件内容计算，所以同一逻辑内容导出为 ZIP 或 Folder 时身份一致。

设置贡献不是导出时抄走“当前完整最终设置”。导出器以 `ProjectSettings.inno` 中真正的 project delta 为源，先把它作为待导出 Plugin 的贡献追加到完整 active contributor snapshot，以 Plugin ID、直接依赖与显式 override 权限验证它没有修改未声明 owner；再按 `PluginDefinitionAsset.dependencies` 计算完整依赖闭包，以这些依赖贡献组成 baseline，重新组合并规范化 delta。空 delta 不写入容器；非法替换会在导出时失败，而不是等安装到另一个项目后才失败。可组合协议不会因为另一个无关 Plugin 修改了不同 key 就误报冲突，完整 replacement 协议则自然要求对当前 owner 建立依赖与 override。

## 安装与激活

1. `PluginSourceService` 同时扫描顶层 ZIP 和目录。两者都验证路径逃逸、绝对路径、符号链接/重解析点、重复/大小写/Unicode 规范化冲突、Windows 保留名、entry 数量和大小限制；ZIP 额外验证压缩比并安全解压。
2. 使用 Core Storage 的通用 DependencyGraph 验证 `Plugin.inno` 依赖拓扑、环与反向阻塞链，并验证每个可导入源的 `.imeta`、Persistent ID 冲突和禁止的 DLL/原生库。
3. ZIP 安全解压到内容 hash 目录；Folder 直接使用其 `Assets/`。随后统一建立 `AssetSourceMount(pluginId, ..., isReadOnly: true)`。
4. 所有通过校验的 Plugin 都直接进入候选；放入带 `.cs` 的 ZIP/Folder 即表示允许其以项目脚本相同的本机权限参与编译与执行。
5. 启动时，已验证 Plugin Mount 与 Project Mount 共同构成 AssetManager 的首个 source snapshot，Project 资产不会在 Plugin 引用尚不可见时先经历一次失败导入；后续变化才使用隔离候选事务。
6. `PluginManager` 用隔离 `AssetSourceMountTransaction` 构建候选 Asset Catalog；候选 Catalog 写到独立暂存区，脚本编译通过 `compilationAssets` / `compilationPlugins` 读取候选，但正式 Catalog、普通 Asset API、File Browser 与 Settings 仍保持 last-good。
7. Assembly、TypeCache 与 Registry 候选成功后，在同一 Editor reload 安全事务中临时激活 Mount、Plugin Catalog 与 Settings contributor；后续任一步失败会逆序恢复。
8. 全部迁移成功才 `Complete` mount transaction、原子提升 Catalog、发布 `SourceMountsChanged`、更新 active fingerprint 并释放旧 Loader/ALC；编译失败则删除暂存 Catalog 并丢弃从未公开的候选。

Plugin 没有交互式 trust 门。collectible ALC 只提供依赖与卸载边界，不是安全沙箱；来源不可信的 Plugin 不应放入项目的 `Plugins/`。

## 公开 API

| API | 作用 |
| --- | --- |
| `PluginManifest` | 原生、渲染无关的容器清单。 |
| `PluginDefinitionAsset` | 在 Project Assets 中定义要导出的根、显式资产、依赖和设置贡献。 |
| `PluginExportService` | `ExportZip` 生成确定性 ZIP；`ExportDirectory` 生成同内容 hash 的可编辑目录。 |
| `PluginSourceService`, `PluginSourceLimits`, `PluginSourceKind` | ZIP/Folder 的有界发现、统一校验、hash 与 ZIP 安全解压。 |
| `PluginScanResult`, `PluginCandidate`, `PluginDiagnostic` | 完整候选快照、物理 source kind 和隔离诊断。 |
| `PluginCatalog` | 当前 discovery 与 active Plugin 快照。 |
| `PluginManager` | 自动轮询、候选构建与原子激活事务。 |

`PluginManager.ActivationCandidateChanged` 只通知宿主脚本编译器有新的隔离候选；它不代表 Plugin 已激活。`activePlugins` 始终表示已提交 generation。`compilationAssets`、`compilationPlugins` 与 `ActivatePending` 都带 `ScriptingApiIgnore`，只用于 Host 的编译/安全点协调，Project/Plugin 代码不能绕过原子提交。

## Source Mount 的含义

Mount 不是第二套 Asset 系统，也不是把容器名当普通目录拼到路径前面。它只是统一 Asset Database 的来源边界：`AssetPath` 由 `AssetSourceId + localPath` 组成，Project Mount 可写，每个 Plugin Mount 对 Asset API 只读。这样 `Assets/Shaders/Main.sc` 与两个 Plugin 中同名文件仍有不同身份，跨 Plugin 引用可检查依赖，File Browser 也能可靠拒绝对已安装 Plugin 内容的保存、移动和删除。Importer、Artifact、Persistent ID、依赖图和 `AssetObject` 缓存仍然只有一套。

资产从 Project 创作根导出或整体移动到 Plugin 后仍保持原 Persistent ID。结构化引用中的 `lastKnownPath` 只是诊断提示，不决定资产所有权；候选 Loader 会先按 Persistent ID 解析当前 Mount 中的真实位置，再规范化 Catalog dependency 并验证 Plugin 边界。因此同一 Plugin 内的 Scene → Texture、Material → Shader 等引用不会因为原创作路径曾属于 Project 而被误判，同时真正的 Plugin → Project 或未声明 Plugin → Plugin 引用仍会被拒绝。

## 创作工作流

在 File Browser 选择 `Create/Plugin Definition` 创建 `.iplugin`，设置 `assetRoots` 或显式资产，然后选择 `Export Plugin ZIP` 或 `Export Plugin Folder`。两种导出器共享同一 Build Plan，并会：

- 包含 Asset import dependency 的传递闭包、`.imeta`、`.iasmdef`、脚本、Shader 和 include；
- 不复制依赖 Plugin 已拥有的内容，只写依赖 ID；
- 对 `settingIds` 只导出相对依赖 baseline 的协议 delta，自动省略无操作记录；
- 在候选 Plugin ownership context 中验证 delta；可组合 key 彼此隔离，完整 replacement 拒绝未声明 contributor；
- 拒绝未包含的项目外引用、缺少 `.imeta`、Library、DLL 和原生库；
- 输出到与 `Assets/` 平级的 `Plugins/`；ZIP 使用稳定顺序和时间戳，Folder 使用暂存目录 + 原子替换，最终逻辑 content hash 一致。

File Browser 以普通 `Assets`、`Plugins` 两个根显示；`Plugins` 下每个 Plugin ID 是独立可展开目录，不把 `Plugins/<id>` 合并成一个 label。只读 mount 的 Save、Rename、Move、Delete 和 Drop Target 都被拒绝。

脚本依赖方向固定为 `Plugin → Host API`、`Plugin → 已声明依赖 Plugin`、`Project → 已激活 Plugin`。Plugin assembly definition 反向引用 Project assembly 会在候选编译前拒绝；这与 C# 是否恰好使用到某个 Project 类型无关。

## 从零创作 2D 渲染 Plugin

可以先在 Project `Assets/Example2D/` 中创作再导出，也可以直接在 `Plugins/Example2D/Assets/` 中开发 Folder Plugin。一个完整 2D Provider 通常包含：

```text
Assets/Example2D/
├─ Runtime/                 Sprite、Atlas、Tilemap、Canvas、Camera2D 等 Plugin 自有组件与数据
├─ Editor/                  Inspector、Atlas 工具与 EditorViewportProvider
├─ Shaders/                 共用 .sc/include 与 .ishader
├─ Materials/               默认 sprite、mask、UI 等 .imaterial
├─ Pipelines/               .irenderpipeline 与 Provider 自有配置资产
├─ Graphs/                  可选 2D Shader Node/Output
├─ Example2D.iasmdef
└─ Example2D.iplugin
```

实现顺序建议如下：

1. 定义 Plugin 自己的 `SpriteRenderer`、`SpriteAtlas`、`Tilemap`、`Canvas`、`Camera2D` 和序列化设置；内核不会替你预设这些概念。
2. 定义开放协议，例如 Contract `example.2d.sprite`，Role `draw`、`mask`、`picking`，以及 frame data channel、phase 和 resource ID。
3. 实现 `[RenderPipelineExtension("example.2d.pipeline")]`。每帧从 `RenderFrameData` 读取 Plugin 自有快照，进行可见性、layer/order、材质、atlas page 和 blend 分组，随后向 RenderGraph 添加 Raster/Compute/Copy Pass。
4. 在 Pass callback 中通过 `IRenderResourceService` 获取 atlas/geometry/pipeline 的 opaque handle，再用 `RenderCommandEncoder` 录制 indexed/instanced draw；动态 batching、GPU 粒子或 compute culling 都是同一条公开路径。
5. 实现 `[EditorViewportProviderExtension(...)]`，把 Editor Scene/Game viewport 的尺寸和交互转换为 `RenderRequest`；需要 picking 时由 Plugin 自己增加 ID target/pass 并解释结果。
6. 需要节点编辑时注册 Plugin 自有 Shader Node 与 2D Output；节点和手写 Shader 都生成相同 Shader IR，并使用同一 Contract/Role。
7. 在 File Browser 创建 `.iplugin`，把 `assetRoots` 指向 `Example2D`，填写依赖；开发时可执行 `Export Plugin Folder`，分发时执行 `Export Plugin ZIP`。复制任一种容器到另一个项目的 `Plugins/` 后即可进入自动候选与激活流程。

当前 `InnoProject/Plugins/Inno.Rendering.2D` 就是 Folder Plugin 验收实现：它仅使用公开逻辑脚本 API，提供正交/像素完美 Camera、多 Blend Role Sprite、trim/rotation Atlas、九宫格/平铺、稀疏 Tilemap、动画、2D 光、排序/批处理、CPU Picking、Project Settings 和 Scene/Game Viewport Provider。其存在不改变 Rendering Core 的空内核原则；删除该目录后 Editor 仍可无 Pipeline 启动。

精灵图集导入器、透明排序、像素对齐、九宫格、tile chunk、UI clipping、2D light 或 normal-map lighting 都属于该 Plugin，可独立迭代，不要求向 Rendering Core 增加任何 2D API。
