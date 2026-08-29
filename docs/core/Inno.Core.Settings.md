# Inno.Core.Settings

[Core 索引](README.md) · [Wiki 首页](../README.md) · [ZIP Plugin](../assets/Inno.Assets.Plugins.md) · [Editor Settings](../editor/Inno.Editor.Settings.md)

`Inno.Core.Settings` 提供宿主中立、强类型、可热重载的 runtime/Plugin 项目设置。它不依赖 Editor、Rendering、Scene 或 Plugin Loader；Game Layers、Tags、渲染质量、输入映射及任意 Plugin 协议都只是普通设置定义。

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
    ProjectSettingsManager.Get<SampleRenderingSettings>(SampleRenderingSettings.settingId);
```

设置类型必须是带无参构造函数的非抽象 `ISerializable` class，并拥有 `StableTypeId`。`Get`/`TryGet` 每次返回隔离快照，调用方不能通过修改返回对象绕过 Apply，也不会把 Plugin generation 实例固定在 Host cache 中。

`ProjectSettingsManager.revision` 是单调递增的有效设置快照编号。需要长期观察变化的 Host/Plugin runtime 可以比较 revision 后重新 `Get`；核心不提供会把 collectible ALC subscriber 固定住的静态 change event。

## 持久化、增量与合成

```text
设置类型的构造默认值
  < 依赖拓扑顺序中的 Plugin 默认贡献
  < ProjectSettings.inno 中的项目 override
```

`ProjectSettings.inno` 只保存项目 contribution。持久真相是 `ProjectSettingId`、Stable Type ID 和 Inno Serialization bytes；Editor UI 路径不是 runtime identity。

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
| `ProjectSettingId` | 跨路径、ZIP 和 generation 稳定的设置协议 ID。 |
| `ProjectSettingDefinitionAttribute` | 向 TypeCache 声明设置类型。 |
| `ProjectSettingComposerAttribute` | 为一个 Setting ID 声明唯一的协议 Composer。 |
| `ProjectSettingComposer<TSetting, TContribution>` | 定义语义 delta capture、empty 判断与确定性组合。 |
| `ProjectSettingContribution<TContribution>` | 一个已解码 delta 及其所有权上下文。 |
| `ProjectSettingContributionContext` | Contributor ID、来源与替换权限查询。 |
| `ProjectSettingRecord` | Stable Type ID 与 Composer delta/full replacement bytes 的中立记录。 |
| `ProjectSettingsContributor` | 依赖有序的 Plugin 默认贡献。 |
| `ProjectSettingsDocument` | 项目最高优先级 contribution 的原生序列化文档。 |
| `ProjectSettings` | Host 使用的实例服务：clone、合成、批量 Apply、恢复与原子写入。 |
| `ProjectSettingsManager` | 项目级唯一读取入口与 Host transaction 边界。 |

面向项目脚本的常用 API 是 `ProjectSettingId`、`ProjectSettingDefinitionAttribute` 和 `ProjectSettingsManager.Get/TryGet/revision`。记录、Contributor、文档替换与初始化由 Plugin/Host 基础设施拥有。

## Layer 与 Tag 的位置

`GameLayerStack` 与 `GameTagCatalog` 是 `Inno.Engine.Scene` 提供的两个普通 Project Setting：

- Project Settings 保存可用 Layer/Tag 的定义。
- Scene/Prefab 保存每个 GameObject 的 `layer` slot 与 `tag` value；Layer definition 另外拥有跨 Plugin 稳定的 `GameLayerId`。
- 删除定义不会静默重写 Scene；旧 assignment 保留并产生 undefined diagnostic，用户可以恢复定义或显式修改对象。
- Layer 保持固定 32-bit slot，因为 mask、碰撞过滤和批量查询需要稳定 bit identity；Tag 保持名称集合，因为它不承担位掩码语义。

`GameLayerStack` 的 Composer 使用 stable layer ID、slot 与 interaction pair 作为 operation key；`GameTagCatalog` 使用 tag 字符串作为 key。不同 Plugin 添加不同 Layer/Tag 时会自然合并；完全相同的声明去重；同一 ID、slot 或 interaction 的不兼容声明才冲突。这不是 Scene 专用持久化旁路，也不是 Settings Core 的 Plugin 特判；其他 Plugin 可以用同一个 Composer API 定义自己的 map、set、ordered list 或 graph 合并协议。

[上一页：Inno.Core.Serialization](Inno.Core.Serialization.md) · [下一页：Core 索引](README.md)
