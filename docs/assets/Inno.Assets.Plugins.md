# Inno.Assets.Plugins

[Assets 索引](README.md) · [Inno.Assets](Inno.Assets.md) · [Project Settings](../core/Inno.Core.Settings.md) · [Rendering](../render/README.md)

`Inno.Assets.Plugins` 实现本地 ZIP Plugin 容器、导出、安全验证、信任、依赖排序和 Source Mount 激活。它不是 Package Manager：没有版本解析、远程仓库、发布服务或平台二进制安装。

## 磁盘协议

```text
<Project>/
├─ Assets/                       可写 project mount
├─ Plugins/
│  └─ sample.rendering.zip       安装的只读 Plugin
├─ ProjectSettings.inno
└─ Library/Plugins/<id>/<hash>/  安全解压缓存，可完全重建

sample.rendering.zip
├─ Plugin.inno                   Inno Serialization 清单
└─ Assets/...                    源、.imeta、脚本、.sc 与普通资产
```

`Plugin.inno` 只声明 `pluginId`、显示名、依赖、显式 override、内容根、程序集定义入口与项目设置默认贡献。Component、Pipeline、Feature、Shader Node、Importer 和 Panel 仍由 TypeCache Attribute 自动发现，不写类型清单。

设置贡献不是导出时抄走“当前完整最终设置”。导出器以 `ProjectSettings.inno` 中真正的 project delta 为源，先把它作为待导出 Plugin 的贡献追加到完整 active contributor snapshot，以 Plugin ID、直接依赖与显式 override 权限验证它没有修改未声明 owner；再按 `PluginDefinitionAsset.dependencies` 计算完整依赖闭包，以这些依赖贡献组成 baseline，重新组合并规范化 delta。空 delta 不写入 ZIP；非法替换会在导出时失败，而不是等安装到另一个项目后才失败。可组合协议不会因为另一个无关 Plugin 修改了不同 key 就误报冲突，完整 replacement 协议则自然要求对当前 owner 建立依赖与 override。

## 安装与激活

1. `PluginArchiveService` 扫描 ZIP，并验证路径逃逸、绝对路径、符号链接、重复/大小写/Unicode 规范化冲突、Windows 保留名、entry 数量、压缩比和大小限制。
2. 使用 Core Storage 的通用 DependencyGraph 验证 `Plugin.inno` 依赖拓扑、环与反向阻塞链，并验证每个可导入源的 `.imeta`、Persistent ID 冲突和禁止的 DLL/原生库。
3. 安全解压到内容 hash 目录，建立 `AssetSourceMount(pluginId, ..., isReadOnly: true)`。
4. 纯资产 Plugin 可进入候选；带 `.cs` 的 Plugin 必须先按稳定 Plugin ID 信任。
5. `PluginManager` 用隔离 `AssetSourceMountTransaction` 构建候选 Asset Catalog；脚本编译通过 `compilationAssets` / `compilationPlugins` 读取候选，但 active Catalog、普通 Asset API、File Browser 与 Settings 仍保持 last-good。
6. Assembly、TypeCache 与 Registry 候选成功后，在同一 Editor reload 安全事务中临时激活 Mount、Plugin Catalog 与 Settings contributor；后续任一步失败会逆序恢复。
7. 全部迁移成功才 `Complete` mount transaction、发布 `SourceMountsChanged`、更新 active fingerprint 并释放旧 Loader/ALC；编译失败则直接丢弃从未公开的候选。

信任不是沙箱。获信任 Plugin 代码拥有与普通项目脚本相同的本机进程权限；collectible ALC 只提供依赖与卸载边界。

## 公开 API

| API | 作用 |
| --- | --- |
| `PluginManifest` | 原生、渲染无关的容器清单。 |
| `PluginDefinitionAsset` | 在 Project Assets 中定义要导出的根、显式资产、依赖和设置贡献。 |
| `PluginExportService` | 计算依赖闭包并生成稳定顺序、稳定时间戳的确定性 ZIP。 |
| `PluginArchiveService`, `PluginArchiveLimits` | 有界扫描、校验和安全解压。 |
| `PluginScanResult`, `PluginArchiveCandidate`, `PluginArchiveDiagnostic` | 完整候选快照和隔离诊断。 |
| `PluginCatalog` | 当前 discovery 与 active Plugin 快照。 |
| `PluginManager` | 自动轮询、信任与激活事务。 |

`PluginManager.ActivationCandidateChanged` 只通知宿主脚本编译器有新的隔离候选；它不代表 Plugin 已激活。`activePlugins` 始终表示已提交 generation。`compilationAssets`、`compilationPlugins` 与 `ActivatePending` 都带 `ScriptingApiIgnore`，只用于 Host 的编译/安全点协调，Project/Plugin 代码不能绕过信任或原子提交。

## Source Mount 的含义

Mount 不是第二套 Asset 系统，也不是把 ZIP 当普通目录拼到路径前面。它只是统一 Asset Database 的来源边界：`AssetPath` 由 `AssetSourceId + localPath` 组成，Project Mount 可写，每个 Plugin Mount 只读。这样 `Assets/Shaders/Main.sc` 与两个 Plugin 中同名文件仍有不同身份，跨 Plugin 引用可检查依赖，File Browser 也能可靠拒绝对只读 ZIP 内容的保存、移动和删除。Importer、Artifact、Persistent ID、依赖图和 `AssetObject` 缓存仍然只有一套。

## 创作工作流

在 File Browser 选择 `Create/Plugin Definition` 创建 `.iplugin`，设置 `assetRoots` 或显式资产，然后选择 `Export Plugin ZIP`。导出器会：

- 包含 Asset import dependency 的传递闭包、`.imeta`、`.iasmdef`、脚本、Shader 和 include；
- 不复制依赖 Plugin 已拥有的内容，只写依赖 ID；
- 对 `settingIds` 只导出相对依赖 baseline 的协议 delta，自动省略无操作记录；
- 在候选 Plugin ownership context 中验证 delta；可组合 key 彼此隔离，完整 replacement 拒绝未声明 contributor；
- 拒绝未包含的项目外引用、缺少 `.imeta`、Library、DLL 和原生库；
- 输出到与 `Assets/` 平级的 `Plugins/`，安装后条目只读。

File Browser 以 `Assets` 与 `Plugins/<id>` 多根显示。只读 mount 的 Save、Rename、Move、Delete 和 Drop Target 都被拒绝。

脚本依赖方向固定为 `Plugin → Host API`、`Plugin → 已声明依赖 Plugin`、`Project → 已激活 Plugin`。Plugin assembly definition 反向引用 Project assembly 会在候选编译前拒绝；这与 C# 是否恰好使用到某个 Project 类型无关。

## 从零创作 2D 渲染 Plugin

先在 Project `Assets/Example2D/` 中正常开发，不直接编辑 ZIP。一个完整 2D Provider 通常包含：

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
7. 在 File Browser 创建 `.iplugin`，把 `assetRoots` 指向 `Example2D`，填写依赖；执行 `Export Plugin ZIP`。复制 ZIP 到另一个项目的 `Plugins/`，信任代码后即可激活。

精灵图集导入器、透明排序、像素对齐、九宫格、tile chunk、UI clipping、2D light 或 normal-map lighting 都属于该 Plugin，可独立迭代，不要求向 Rendering Core 增加任何 2D API。
