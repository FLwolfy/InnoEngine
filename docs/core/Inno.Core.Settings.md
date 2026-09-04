# Inno.Core.Settings

[Core 索引](README.md) · [Wiki 首页](../README.md) · [Plugin](../plugins/Inno.Plugins.Authoring.md) · [Editor Settings](../editor/Inno.Editor.Settings.md)

`Inno.Core.Settings` 提供宿主中立、强类型、可热重载的 runtime/Plugin 项目设置。它不依赖 Editor、Rendering、Scene 或 Plugin Loader；Game Layers、Tags、渲染质量、输入映射及任意 Plugin 协议都只是普通设置定义。文档 IO 复用 [Inno.Core.IO](Inno.Core.IO.md)，领域层不再各自复制 staging/replace/rollback。

## Project Identity 与两类 ID

`ProjectIdentitySettings` 在 `Project/Identity/Project ID` 编辑当前项目命名空间。项目内容的逻辑身份保存 `ProjectLocalId`，需要对外表达时通过 `ProjectId.Qualify` 或 `Settings.QualifyId` 得到严格的 `projectId.name`。因此修改 Project ID 只改变解析结果，不改写 Scene、Prefab、Asset 或 Plugin contribution。
Authoring Host 从项目目录名生成合法的初始 Project ID；空白或纯非 ASCII 名称使用 `inno.project`。该值是当前项目的 host default，用户在 Settings 中填写后才形成项目 override。Player 则以已验证 runtime manifest 的 Application ID 作为同一默认值，因此没有第二份可漂移身份。


Game/Plugin 导出的根身份直接使用当前 `ProjectId`；Layer、Tag、Sorting Layer 等项目内子身份才组合为 `projectId.name`。`ProjectSettingId`、依赖 Plugin ID、Asset source ID 等跨项目协议身份仍由各自 owner 定义，不能误加当前 Project ID。`ProjectIdentitySettings` 标记为 `allowPluginContributions: false`，导出 `.iplugin` 时不会把宿主项目身份装进包，也不允许 Plugin 覆盖消费项目身份。

## 定义与读取

```csharp
using InnoEngine.Reflection;
using InnoEngine.Serialization;
using InnoEngine.Settings;

[StableTypeId("11111111-2222-3333-4444-555555555555")]
[ProjectSettingDefinition("sample.rendering")]
public sealed class SampleRenderingSettings : ISerializable
{
    [SerializableProperty]
    public bool enableCompute { get; set; } = true;

    public static ProjectSettingId settingId => new("sample.rendering");
}

SampleRenderingSettings settings =
    ProjectSettingsStore.Get<SampleRenderingSettings>(SampleRenderingSettings.settingId);
```

设置类型必须是带无参构造函数的非抽象 `ISerializable` class，并拥有 `StableTypeId`。`Get`/`TryGet` 每次返回隔离快照，调用方不能通过修改返回对象绕过 Apply，也不会把 Plugin generation 实例固定在 Host cache 中。

`ProjectSettingsStore.revision` 是单调递增的有效设置快照编号。需要长期观察变化的 Host/Plugin runtime 可以比较 revision 后重新 `Get`；核心不提供会把 collectible ALC subscriber 固定住的静态 change event。

## 持久化、增量与合成

```text
设置类型的构造默认值
  < 依赖拓扑顺序中的 Plugin 默认贡献
  < Settings.Project.inno 中的项目 override
```

`Settings.Project.inno` 只保存项目 contribution。持久真相是 `ProjectSettingId`、Stable Type ID 和 Inno Serialization bytes；Editor UI 路径不是 runtime identity。

Contributor 的依赖闭包、顺序和环检测复用 [Core Storage](Inno.Core.Storage.md) 的 `DependencyGraph<string>`。Settings 只在此之上增加 owner/override 与协议合成规则，不维护另一套拓扑实现。

设置协议有两种组合方式：

- 未声明 Composer：贡献是完整值，保持明确的 replacement 语义。两个无依赖关系的 Plugin 同时贡献同一 Setting ID 会冲突；后一个 Plugin 只有同时声明依赖与显式 override 才能替换。
- 声明 Composer：贡献是协议自己定义的语义增量。不同 Plugin 可以修改同一设置中的不同 key；相同 operation 可去重；同一 key 的不兼容修改才冲突。冲突是否允许替换仍由依赖与显式 override 决定。

这让 Settings Core 不需要知道 Layer、Tag、Input Map 或 Render Feature 的字段。每个可组合协议通过 `ProjectSettingComposer<TSetting, TContribution>` 定义三件事：从完整编辑值捕获 delta、判断 delta 是否为空、按依赖顺序组合 delta。`TContribution` 仍是普通 `ISerializable` 中立数据，不能保存 CLR `Type`、delegate 或 runtime 对象。

```csharp
using System.Collections.Generic;

using InnoEngine.Serialization;
using InnoEngine.Settings;

internal sealed class SampleDelta : ISerializable
{
    [SerializableProperty]
    internal string[] additions { get; set; } = [];
}

[ProjectSettingComposer("sample.rendering")]
internal sealed class SampleComposer
    : ProjectSettingComposer<SampleRenderingSettings, SampleDelta>
{
    protected override SampleDelta CaptureContribution(
        SampleRenderingSettings baseline,
        SampleRenderingSettings value)
    {
        // Return only operations authored above the supplied baseline.
        return new SampleDelta();
    }

    protected override bool IsEmpty(SampleDelta contribution)
        => contribution.additions.Length == 0;

    protected override void Compose(
        SampleRenderingSettings target,
        IReadOnlyList<ProjectSettingContribution<SampleDelta>> contributions)
    {
        // Apply dependency-ordered operations. Use contribution.context.CanOverride(ownerId)
        // before replacing data owned by another Plugin.
    }
}
```

`ProjectSettingContributionContext.contributorId` 是当前贡献所有者；`source` 区分 Plugin 与 Project；`CanOverride(ownerId)` 统一执行“项目最高优先级”以及“Plugin 必须依赖并显式 override 原 owner”的规则。Composer 只负责协议内部的 key/operation 所有权，不读取 Plugin Loader，也不建立中央类型分支。

候选导入、脚本、Composer 构造、delta 解码、组合或设置恢复任一步失败，当前有效 snapshot 保持不变。Registry 随 TypeCache generation 原子切换，不长期保存旧 ALC 的类型或委托。

## 公开 API

| API | 说明 |
| --- | --- |
| `ProjectId` / `ProjectLocalId` / `ProjectScopedId` | 项目命名空间、本地稳定键与运行时 `projectId.name` 组合结果。 |
| `ProjectIdentitySettings` | `Project/Identity` 中可编辑、不可由 Plugin 贡献的项目身份。 |
| `SettingsFileNames` | 三个正式设置文档名的唯一来源。 |
| `SettingsDocumentStore<T>` | 基于 Inno Serialization 与 Core.IO 的验证、capture、restore、原子 save。 |
| `ProjectSettingId` | 跨路径、`.iplugin` 和 generation 稳定的设置协议 ID。 |
| `ProjectSettingDefinitionAttribute` | 向 TypeCache 声明设置类型。 |
| `ProjectSettingComposerAttribute` | 为一个 Setting ID 声明唯一的协议 Composer。 |
| `ProjectSettingComposer<TSetting, TContribution>` | 定义语义 delta capture、empty 判断与确定性组合。 |
| `ProjectSettingContribution<TContribution>` | 一个已解码 delta 及其所有权上下文。 |
| `ProjectSettingContributionContext` | Contributor ID、来源与替换权限查询。 |
| `ProjectSettingRecord` | Stable Type ID 与 Composer delta/full replacement bytes 的中立记录。 |
| `ProjectSettingsContributor` | 依赖有序的 Plugin 默认贡献。 |
| `ProjectSettingsDocument` | 项目最高优先级 contribution 的原生序列化文档。 |
| `ProjectSettings` | Host 使用的实例服务：clone、合成、批量 Apply、恢复与原子写入。 |
| `ProjectSettingsStore` | 项目级唯一读取入口与 Host transaction 边界。 |
| `IProjectSettingsLookup` | Editor 与 Player 共用的最小只读有效设置边界。 |
| `Settings` | 项目脚本使用的无状态读取门面，只解析当前异步执行作用域。 |
| `ProjectSettingsExecutionContext` | Composition Root 使用的严格 LIFO Session 绑定边界。 |

面向项目脚本的常用 API 是 `ProjectSettingId`、`ProjectSettingDefinitionAttribute` 和
`Settings.Get/TryGet/revision`。`Settings` 不保存静态可变数据；Editor 与 Player Host 使用
`ProjectSettingsExecutionContext.EnterScope(IProjectSettingsLookup)` 绑定当前 Session，并通过
`AsyncLocal` 隔离并行异步流。记录、Contributor、文档替换、初始化和 Store 生命周期仍由
Plugin/Host 基础设施拥有。

## Layer 与 Tag 的位置

`GameLayerStack` 与 `GameTagCatalog` 是 `Inno.Scene` 提供的两个普通 Project Setting：

- Project Settings 保存可用 Layer/Tag 的定义。
- Scene/Prefab 保存每个 GameObject 的 `layer` slot 与 `tag` value；定义只保存 project-independent local key，`GameLayerId` 与 Tag ID 在读取边界按当前 Project ID 解析。
- 删除定义不会静默重写 Scene；旧 assignment 保留并产生 undefined diagnostic，用户可以恢复定义或显式修改对象。
- Layer 保持固定 32-bit slot，因为 mask、碰撞过滤和批量查询需要稳定 bit identity；Tag 保持名称集合，因为它不承担位掩码语义。

`GameLayerStack` 的 Composer 使用自动生成的 local layer ID、slot 与 interaction pair 作为 operation key；`GameTagCatalog` 使用 tag 名称及其确定性 local ID 作为 key。不同 Plugin 添加不同 Layer/Tag 时会自然合并；完全相同的声明去重；同一 ID、slot 或 interaction 的不兼容声明才冲突。这不是 Scene 专用持久化旁路，也不是 Settings Core 的 Plugin 特判；其他 Plugin 可以用同一个 Composer API 定义自己的 map、set、ordered list 或 graph 合并协议。

[上一页：Inno.Core.Serialization](Inno.Core.Serialization.md) · [下一页：Core 索引](README.md)
