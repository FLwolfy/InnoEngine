# InnoEngine Identity、可恢复引用与热重载强制标准

[架构治理](README.md) · [完整项目架构 Overview](ENGINE_ARCHITECTURE_OVERVIEW.md) · [Wiki 首页](../README.md)

> 最初审计快照：2026-09-06。强制不变量持续生效，历史缺口与拟议 API 不是当前实现状态；实际收口以 [2026-09-08 实现交付与集中验收](ENGINE_CLOSURE_IMPLEMENTATION_2026_09_08.md) 为准。
> 文中“必须”和“禁止”是架构约束；“当前实现”与“当前缺口”来自本次源码审计。

## 一、适用范围与最终不变量

本标准同时约束：

- `Inno.Core.Identity` 与所有 live object registry。
- Scripting source、script assembly、Stable Type ID 与 collectible module generation。
- `.iplugin` 安装源、Plugin dependency、extension registry 与 Plugin reload。
- Asset source、`.imeta`、Asset Catalog、Artifact、canonical AssetObject 与 Asset reference。
- Scene、Prefab、GameObject、Component、System、Graph node、Settings contribution 等结构化状态。
- Editor Selection、Focus、DragDrop、Inspector assignment、Clipboard 与 Undo/Redo。
- Rendering、Audio、Animation 以及未来领域中的 reloadable provider、feature、graph 和 resource generation。

最终必须同时成立三个不变量：

1. **所有跨边界 live object 寻址都经过 Identity。** UI、回调、队列、跨帧 operation 和领域 registry 不传裸对象索引；Editor ImGui drag payload 的数据必须是源对象的 Identity `runtimeId`。
2. **Missing 是可恢复状态，不是数据丢失。** 暂时缺少 Asset、Script Type、Plugin 或 extension 时，保留原 identity、类型 identity、位置、属性和中立 payload；同一目标恢复后自动重建，期间 Undo/Redo 不丢记录。
3. **旧 collectible generation 未被 GC 确认回收，reload 不得成功。** 旧 ALC 仍可达时必须继续处于 unload barrier；达到失败阈值后抛出明确异常并进入 Faulted，禁止把 Pending 当作成功或静默遗忘 monitor。

这三个约束属于同一条链，而不是三个独立功能：

```text
Identity 定位逻辑对象
    ↓
Persistent reference 保存意图
    ↓
Resolver 在当前 generation 解析
    ↓ unavailable
Missing state 保留 identity + neutral bytes
    ↓ source/type/plugin returns
Recovery transaction 原子重建
    ↓
Retired generation 清除全部强引用
    ↓
GC unload barrier 确认旧 ALC 消失
```

任何领域自行建立第二套 object token、missing payload 或 reload completion 规则，都违反本标准。

## 二、必须区分的身份种类

“统一走 Identity”不表示把所有 ID 强行改成同一种值。不同身份的生命周期不同，必须按下表使用：

| 身份 | 示例 | 生命周期 | 可以持久化 | 作用 |
| --- | --- | --- | --- | --- |
| Persistent object identity | `Identity.persistentId` | 逻辑对象整个生命期 | 是 | Scene object、Asset、authoring entry 等对象引用 |
| Runtime object identity | `Identity.runtimeId` | 单个 `IdentityAllocator` 注册期 | 否 | 当前进程、当前 identity domain 内快速解析 live object |
| Stable type identity | `TypeRef.stableId` | 跨 assembly generation | 是 | 找回脚本类型、Component/System/extension 类型 |
| Runtime type identity | `TypeRef.runtimeId` | 单个 TypeCache generation | 否 | 当前 TypeCache 中快速解析 `Type` |
| Stable semantic ID | Plugin ID、Feature ID、Bus ID、Importer ID | 协议定义期 | 是 | 标识开放扩展槽或逻辑协议，不代表 live object |
| Generation handle | Audio/GPU/Job handle | 设备或 owner generation | 通常否 | 校验 backend resource 是否仍属于当前 generation |
| Content identity | Artifact key、content hash、MVID | 内容或 build generation | 可按其协议保存 | 校验不可变内容，不代表对象 identity |
| Collection position | list index、dense slot、graph order | 当前容器快照 | 仅作为结构数据 | 表达顺序，不能单独标识对象 |

固定规则：

- `persistentId` 回答“这是不是同一个逻辑对象”。
- `runtimeId` 回答“当前 allocator 中这个注册实例是否仍存活”。
- Stable Type ID 回答“当前 generation 中哪个类型实现同一逻辑类型”。
- Plugin ID、Feature ID 等回答“哪个开放协议或扩展槽”，不能替代 object identity。
- Artifact Key 回答“哪份不可变内容”，不能替代 Asset identity。
- list index 只能表达位置；Undo/Redo payload 中若需要对象，必须同时保存 persistent identity。
- 不得因为术语中都含有 `Id` 就相互转换、复用字段或共用无类型字典。

## 三、Identity domain 与 Registry 标准

### 3.1 Identity domain

每个可隔离生命周期 owner 拥有一个明确的 `IdentityAllocator`：

- 一个 `RuntimeSession` 拥有一个 runtime identity domain。
- Editor authoring workspace 拥有一个 authoring identity domain。
- Edit Session 与 Play Session 不共享 runtime ID。
- Player Session 与 Editor Session 不共享 runtime ID。
- Plugin/Script reload 可以替换实例，但同一逻辑对象恢复原 `persistentId`，并取得新的 `runtimeId`。

`runtimeId` 只有和产生它的 allocator/domain 一起解释才有意义。跨 domain 操作必须拒绝，不能碰巧根据相同整数解析到另一对象。

Editor 的 identity-aware API 必须从当前明确 scope 解析 allocator；不得使用 process-global mutable Identity Manager。scope 继续遵守 strict LIFO，错误 domain、无 scope 和乱序释放都必须明确失败。

### 3.2 哪些对象必须进入 Identity

凡是满足以下任一条件的 live object，都必须成为 identity-bearing object 或拥有一个 identity-bearing record：

- 会被序列化引用。
- 会进入 Selection、Focus、Inspector 或 DragDrop。
- 会被异步 operation、队列或 callback 在稍后重新定位。
- 会参与 Undo/Redo。
- 会在 Script/Plugin/Asset reload 后以新实例恢复。
- 会被多个领域通过 ID 共同引用。

包括但不限于：

- `GameScene`、`GameObject`、`GameComponent`、`GameSystem`。
- canonical `AssetObject`。
- File Browser 中可寻址的文件和目录 authoring entry。
- Script source 与 assembly definition 对应的 Asset entry。
- Plugin 安装项和需要被 Editor 选择的 Plugin content entry。
- Editor 中可选择、拖拽或跨帧定位的 Graph element。

短命值对象、不可变 descriptor、metric snapshot 和纯显示 model 不需要伪装成 `IdentityObject`。如果 UI 需要从它们定位真实对象，应让 UI 保存真实对象的 identity，而不是给每个快照生成第二个随机 token。

### 3.3 Registry 的唯一权威性

- live object 的 `persistentId/runtimeId → instance` 解析以 `IdentityAllocator` 为唯一权威。
- 领域可以建立 `path/semantic ID → persistentId` 的二级索引，但不得再维护长期的 `path/semantic ID → collectible object` 权威表。
- 内部 dense/sparse array 可以优化 allocator；slot 必须带 generation，slot 复用后旧 runtime ID 必须失效。
- Registry 不得因索引本身强行延长无 owner 对象的生命周期。需要强所有权时必须由明确 domain owner 持有。
- 注册相同 persistent ID 的两个 live object 必须在 candidate validation 或原子注册阶段失败。
- replacement 必须先捕获 persistent ID、解除旧实例注册，再给新实例注册同一 persistent ID；失败时完整恢复旧注册。

禁止：

- 使用递增整数、数组下标、`GetHashCode()`、原生指针或随机 Guid token 替代 Identity runtime ID。
- 把 `IdentityObject`、`Type`、delegate 或 extension instance 放入跨代静态字典。
- 通过对象引用相等判断跨 reload 的逻辑相等。
- 序列化 `runtimeId`，或在 History、Asset metadata、Scene/Prefab、Plugin manifest、Settings 中保存它。

## 四、Editor Interaction 与 ImGui DragDrop

### 4.1 Native payload

Editor DragDrop 的 native payload 必须满足：

- ImGui payload bytes 只包含一个已注册源对象的 Identity `runtimeId`。
- payload type string 是稳定的 identity kind/domain 协议，例如 Asset、Scene Object 或通用 Editor Identity；它不是一次 drag 的随机 token。
- Preview 和 Delivery 都重新通过对应 `IdentityAllocator` 解析 runtime ID。
- 类型兼容、source ownership、Scene/Asset scope、只读状态和 target policy 在解析后验证。
- stale runtime ID、allocator 已替换、对象已注销或 generation 不匹配时直接 Reject，并取消 drag。
- Drop 成功写入属性或 History 时，转换为目标的 `persistentId`；不得把 runtime ID 留在持久状态中。

目标数据流：

```text
IdentityObject / identity-bearing record
    ↓ require identity.runtimeId
ImGui payload: 4-byte runtimeId
    ↓ preview/delivery
IdentityAllocator.Get<T>(runtimeId)
    ↓ validate current object and target
Apply mutation using persistentId / neutral state
    ↓
Record EditorHistoryChange using persistentId
```

当前实现已经完成该迁移：`EditorDragData` 在构造时只截取 `RuntimeIdentity` 与 presentation label；
`EditorDropRouter` 只保存 active identity，并从注册的 domain allocator 重新解析 source；ImGui payload type 编码
domain，payload bytes 只传该 domain 内的 runtime ID。因此：

- `EditorDragData` 不再强持有任意 source object。
- Drop Router 不再保存 `Guid → object` 会话表。
- `isValid` delegate 不再负责对象有效性；Identity generation 校验负责 stale 检测。
- 预览字符串可以作为短命 presentation value，但不得让它间接持有 collectible object。

### 4.2 多 identity domain

一个 ImGui payload 只有 runtime ID，因此 payload kind 必须能确定解析 domain，或者 draw scope 必须已经唯一绑定 active allocator。两种方式只能选定一个全局协议并统一使用，不能让 Panel 自己猜测。

推荐规则：

- Edit Scene 的对象只在 Edit Session identity scope 中拖拽。
- Play Session 对象只在 Play/Game diagnostics scope 中拖拽，不能投递到 Edit Scene authoring target。
- Asset/Plugin/Script source 使用 Editor authoring identity scope。
- 跨 scope 的“Import/Instantiate/Create Reference”由显式 command 把 persistent identity 转换为目标领域操作，不直接传另一 domain 的 runtime ID。

Reload 进入 quiesce 阶段时，Interaction 统一取消 drag、pending action 和 presentation；新 generation 不继承旧 runtime ID。

## 五、持久引用与 Serialization Context

### 5.1 持久引用格式

持久引用最少保存：

- 目标 `persistentId`。
- 开放的 reference kind，用于选择领域 resolver。
- 需要类型约束时保存 expected Stable Type ID。
- 仅用于诊断的 last-known display name/path 可以保存，但不能作为解析权威。
- 复杂 Missing object 额外保存中立 serialized bytes、依赖、结构位置和必要的 owner identity。

空引用和 Missing 必须严格区分：

| 状态 | 含义 | 是否有 target persistent ID |
| --- | --- | --- |
| Unassigned | 用户明确没有赋值 | 否 |
| Resolved | 当前 generation 找到兼容目标 | 是 |
| Missing | 目标暂时不可用，但引用意图仍存在 | 是 |
| TypeMismatch | ID 存在但当前类型不兼容 | 是 |
| Invalid | 数据损坏、空必填 ID 或身份冲突 | 可能有 |

Resolver 不得把 Missing 自动改写为 null；也不得因当前路径不存在就删除 persistent ID。

### 5.2 统一 reference mechanism

当前架构已经增加后端中立的 `Inno.References` Mechanism，防止 Scene、Assets、Scripting、Plugins 和每个
Editor Panel 分别发明协议。稳定契约为：

```text
Inno.References
├── ReferenceKindId
├── ReferenceKey
├── ReferenceDescriptor
├── ReferenceResolutionState
├── ReferenceResolution
├── IReferenceResolver
├── ReferenceCatalog
├── ReferenceRecoveryTransaction
└── SerializedMissingState
```

语义要求：

- `ReferenceKey` 标识“哪个 owner 的哪个引用槽”，由 owner persistent ID、稳定 property/path key 等中立数据组成。
- `ReferenceDescriptor` 保存目标 persistent ID、reference kind、expected stable type 和诊断 metadata。
- `IReferenceResolver` 只针对当前 immutable generation 解析，不改变 live state。
- `ReferenceCatalog` 发布完整 resolver generation，冲突在 candidate build 阶段失败。
- `SerializedMissingState` 只保存中立值和 bytes，不保存 `Type`、object、delegate 或 ALC-owned实例。
- `ReferenceRecoveryTransaction` 在 owner-thread safe point 原子应用一批 Missing/Recovered 转换。

`Inno.Core.Identity` 仍只拥有 object identity 原语；Missing、resolver 发现和恢复事务属于 Mechanism，不应塞入 MicroKernel。

`RuntimeSession` 拥有一个 immutable `ReferenceCatalog`；Edit/Play 通过 options 注入 authoring resolver，Player
自动加入 `AssetDatabase` resolver，Feature 只从同一 Context 读取。当前 Assets 已通过
`AssetReferenceProtocol` 接入；Scene script-type placeholder 等既有领域表示仍可保留，但恢复发布必须逐步
接到同一个 `ReferenceRecoveryTransaction`，不得新增 `AssetMissingResolver`、`SceneMissingManager`、
`PluginRecoveryHelper` 等平行 owner。

2026-09-07 本轮验收补充：Asset ID 缺失不再按 path 认领别的对象；tombstone 保留中立状态/依赖；Recovery 补偿包含失败 participant 自身且释放引用；Registry 候选创建与退休失败已接入统一 Fault gate，并增加真实 ALC 保留根测试。
**这些修复不代表跨域生产 Recovery 已完成**：各 owner 的 Missing slots 尚未全部接到该事务，详见[当前验收阻断表](ENGINE_CLOSURE_ACCEPTANCE_2026_09_07.md)。此标准不因已有测试全绿而降级。

### 5.3 Serialization Context 的唯一组合入口

所有可能包含 Asset/Object/reference 的序列化操作必须使用 owner 组合出的完整 context：

- Runtime Session 提供 runtime serialization context。
- Editor authoring services 提供 authoring serialization context。
- Editor History handler 从 `EditorHistoryContext` 取得同一组 resolver 能力。
- Play snapshot materialization 使用目标 Runtime Session 的 resolver context。
- Missing state capture/restore 使用明确的 capture/resolve context。

领域业务代码不得临时从 `SerializationContext.empty` 开始，然后凭经验只追加一个 resolver。`SerializationContext.empty` 只允许用于已经由类型系统或测试证明不含任何 context-aware converter 的纯值图。

必须集中提供 context factory/set，保证以下能力在相应 scope 中一次组合：

- Identity/reference resolver。
- `IAssetReferenceResolver`。
- Type/serialization generation。
- Scene graph reference map。
- 必要的 Asset dependency collector。

缺少 required resolver 是 composition/startup 错误，不能等到 Play、Undo 或 Missing recovery 时才抛出。此前出现的 `Serialization context 'Inno.Assets.IAssetReferenceResolver' is not registered` 正是这一规则要消除的重复组合问题。

## 六、统一 Missing 模型

### 6.1 Missing 必须保留什么

任何 Missing representation 都必须尽可能保留：

- owner persistent identity。
- target persistent identity。
- Stable Type ID 或开放 extension ID。
- last-known type/display name，仅供诊断。
- 完整中立 property bytes。
- Asset dependencies 与嵌套 reference descriptors。
- Scene/Graph 中的 parent、slot、order 和连接关系。
- enabled、priority 等不应丢失的中立生命周期配置。
- History 所需的 before/after bytes。

不得保留：

- 退休 generation 的 `Type`。
- 旧 Component、Asset、Plugin、extension instance。
- 指向旧 generation 的 delegate、event subscription、Task continuation 或 reflection metadata。
- runtime ID。
- native handle/pointer。

Missing presentation 是状态的视图，不是另一份持久数据。`Missing` 标志通常由“descriptor 当前无法解析”推导；不得为了显示效果创建会覆盖原 identity 的新逻辑对象。

### 6.2 各领域的表现

| 领域 | Missing representation | 恢复键 | 不允许的行为 |
| --- | --- | --- | --- |
| Asset reference | 保留 persistent ID 的 typed missing shell/reference state | Asset persistent ID | 改成 null、只按路径找回 |
| Asset source | 保留 `.imeta` identity 或等价 durable tombstone、last path/type | Asset persistent ID | 因一次 watcher delete 立即遗忘 identity |
| Script Component | Host-owned `MissingGameComponent` + neutral state | persistent ID + Stable Type ID | 保留旧 script instance/Type |
| Script System | Host-owned `MissingGameSystem` + neutral state | persistent ID + Stable Type ID | 静默删除 System |
| Plugin | unavailable installation/generation record；Plugin content identity 仍在引用中 | Plugin ID + content persistent IDs | 继续运行已经移除的旧 Plugin ALC |
| Importer | source identity、import settings、last-good artifact metadata 和 diagnostics | Importer ID + Asset persistent ID | 删除引用或伪造新类型 |
| Graph/Mixer/Animation node | opaque extension state + ports/edges/order | extension ID + node persistent ID | 丢弃未知 node |
| Settings contribution | inactive neutral setting record | Setting ID + Stable Type ID | 因 provider 缺失删除项目覆盖值 |
| Editor History handler | 原 `EditorHistoryChange` 保留在栈顶并成为 barrier | history kind | Handler 缺失时丢弃记录 |

Player build 可以比 Editor 更严格：runtime closure 中 required reference 仍 Missing 时，Build 必须失败并列出完整 reference chain。Editor 则必须保留并显示 Missing，以允许用户修复。

### 6.3 Asset 删除与重现

目标行为：

- Editor 内删除 Asset 时，Undo archive 必须包含 source、`.imeta` 和必要 companion content；Undo 恢复同一 persistent ID。
- 外部暂时删除、同步工具抖动或 Plugin mount 暂不可用时，不得立刻破坏被引用对象的 identity。
- watcher quiet window 内 delete/create 合并为 Modified。
- 确认 Missing 后保留 durable identity record；仅可重建的 Artifact/CAS cache 可以按预算回收。
- 同一 source 携带同一 `.imeta` 返回时自动恢复。
- 新 source 使用不同 persistent ID 时就是新 Asset，不按文件名或路径偷偷接管旧引用。
- 路径只用于定位 source；rename/move 后引用仍由 persistent ID 成立。

当前 Asset 文档所述“确认删除后自动删除 orphan `.imeta`”与全面可恢复目标存在冲突。实现本标准时必须重新定义 durable tombstone 所有权，并同步修改 writer、watcher、tests 和当前格式文档；不能只在 `Library` 留一个重建即消失的偶然缓存。

## 七、恢复事务

2026-09-08 生产追加：`ReferenceRecoveryTransaction` 已实现共享 `IGenerationChange` 五阶段契约。
SceneReloadService 捕获真实 element/Asset dependency slots，Scene owner 应用 provisional 对象后统一解析和 Validate。
失败顺序为结构回滚 → Type/Serializer/外部 Asset publication 回滚 → 旧属性恢复；事务不再在 Apply catch/Dispose
中自行单阶段回滚。Partial Prepare/Apply 的 owner 也必须补偿；Pending 保留 participant 并由共享 gate Fault。

SceneElementSerialization 的 CaptureState/RestoreState 同时服务于 live 和 Missing Component/System；
中立封套由 Core Serialization 编码类型名、属性、依赖和 aliases。History Undo 在类型缺失时创建原 ID 的占位，
Missing 删除/移动保留逻辑类型，恢复后原 Redo 可用。恢复资产必须是同一 persistent ID（原 .imeta），不能按路径抢占。
Missing 属性恢复必须完整；失败保持原占位；自动恢复不改变已保存 Scene 的 dirty baseline。

这些是当前已接入并验证的 Scene + Asset-reference + History 链路，不代表 Assets 全量、Graph、Settings、
Plugin availability 已完成共同批次迁移。下列全域目标仍保持其原有验收强度。

恢复不是“下一帧发现了就直接 new 一个对象”。所有领域使用同一种 transaction 语义：

1. 收集当前 Missing descriptors，不修改 live state。
2. 对 candidate generation 批量解析 persistent ID、Stable Type ID、Plugin/extension ID。
3. 验证类型兼容、identity 冲突、结构约束、依赖闭包和 capability。
4. 构造候选对象；尚未发布到 active registry。
5. 使用完整 Serialization Context 恢复 property bytes 和嵌套引用。
6. 捕获应用所需的最小 rollback state。
7. 在 owner-thread safe point 原子替换结构、注册相同 persistent ID 并重绑引用。
8. 全部成功后发布 candidate reference snapshot；失败则逆序恢复原 Missing 状态。
9. 恢复诊断只在 commit 后解除；失败诊断保留并说明具体 slot/target/type。

恢复必须具有以下语义：

- 同一个 persistent ID 恢复为同一个逻辑对象，但获得新的 runtime ID。
- Missing → Resolved 的环境变化本身不把干净 Scene 标记为用户修改。
- 用户在 Missing 期间进行的删除、移动、清空引用等显式修改仍然正常 dirty，并进入 History。
- 一个对象内部部分引用仍 Missing，不妨碍对象本身恢复；嵌套 Missing descriptor 继续保留。
- 类型不兼容、构造失败或 property restore 失败时保留原 Missing 对象，不产生半恢复状态。
- 恢复不得依赖旧 generation instance 执行业务回调。

## 八、Undo/Redo 与 Missing

Undo/Redo 是统一可恢复引用协议的使用者，不建立第二套 resolver。

### 8.1 History payload

所有 reload-safe `EditorHistoryChange` 只能保存：

- stable history kind。
- owner/target persistent ID。
- Stable Type ID、Plugin/extension ID 等稳定协议身份。
- property path、结构 index/order 和标量。
- before/after 中立 serialization bytes。
- 由 History blob store 持有的大 payload reference。

禁止保存 runtime ID、runtime object、`Type`、delegate、extension instance、Service、ALC、native pointer 或任意 closure。

### 8.2 暂时不可用不是记录失效

- 目标、类型、Plugin 或 Handler 暂时 Missing 时，`Query` 返回带原因的 `Unavailable` barrier。
- barrier 不移动 Undo/Redo 指针，不删除记录，也不跳过顶部记录执行后面的操作。
- 相同 identity/handler 恢复后，下一次 Query 自动变为 Available。
- Undo 需要重建对象而类型仍缺失时，必须恢复对应 Missing placeholder，而不是失败后丢数据。
- Redo 删除/替换 Missing placeholder 时，继续按 persistent ID 和结构位置操作。
- 自动 Missing/Recovery 不制造伪造的用户 History entry；它只改变现有引用的 resolution state。
- 用户在 Missing 状态下明确清空引用、删除 placeholder 或更换目标时，照常生成可逆记录。

每个 History handler 必须从共享 `EditorHistoryContext` 取得完整 reference/asset/serialization context。AssetSource、Scene Element、Scene Property、Graph 和 Settings handler 不得各自拼接不同 context。

直接验收场景：

```text
Create ScriptComponent referencing Asset A
    → Delete ScriptComponent
    → remove its Plugin or Asset A
    → Undo
    → restore a Missing Component / Missing asset reference with original IDs
    → reinstall Plugin / restore Asset A
    → automatically recover the concrete Component and asset
    → Redo still removes the same logical object
```

该流程中任何一步都不得因为缺少 `IAssetReferenceResolver`、旧 `Type` 或 handler generation 而丢弃 History。

## 九、统一 Hot Reload 状态机

Scripting、Plugin、Asset type、Serializer、Editor extension、Rendering/Audio/Animation provider 必须进入同一个 generation transaction，不允许各领域独立“刷新一下”。

```text
Idle
  ↓
Discover / Compile / Import
  ↓
Prepare Candidate
  ↓
Validate Complete Closure
  ↓
Capture Neutral State
  ↓
Quiesce Retiring Generation
  ↓
Atomic Activate + Recover References
  ├── pre-commit failure → Rollback → Idle(last-good)
  ↓
Retire Old Generation
  ↓
AwaitingCollection
  ├── every ALC collected → Completed → Idle
  └── retention threshold reached → throw → Faulted
```

### 9.1 Candidate 与原子发布

- source、Plugin mount、Assembly catalog、TypeCache、Serialization converters、Asset catalog、Settings、Runtime Subsystem 和 Editor Registry 组成一个完整 candidate closure。
- candidate build 只读取 immutable 输入，不修改 active generation。
- Stable ID 冲突、依赖缺失、cycle、类型不兼容和恢复失败在 commit 前拒绝，并保留 last-good。
- publication 只能发生在 owner-thread/frame safe point。
- participant 按依赖顺序 prepare/apply，失败按反序 rollback。
- publication 后旧对象必须停止产生事件、任务、callback 和 native invocation。

### 9.2 GC unload barrier

所有退休异常入口必须按完整异常树分类，而不是只匹配直接异常类型。使用 Core
`RetirementPendingException.Find` 识别 Aggregate/InnerException 中的 Pending 与 timeout，并原样保留外层异常。
可重试 owner 使用 `CollectCompletedFailures` 保存普通错误分支：Pending 不能被记录为已完成错误后释放依赖，
普通错误也不能在下一次成功重试后消失。嵌套 timeout 保持终态，不重新开 deadline。
已结束 Task 或取消 callback 若报告未退休依赖，不能以 Task.IsCompleted 或取消请求已发出作为释放证明。
工作必须在调用其同步前缀之前完成所有权登记；取消回调执行期间以及并发/重入 Dispose 不能重复释放资源。
包装 Pending 的 OperationCanceledException 仍表示有依赖未退休，不能通过转换为 canceled Task 消除该信号。

同步领域操作通过共享 `GenerationCoordinator.AcquireOperation` 保护当前 owner，防止查询触发自动 Catalog
刷新后，发布事务在同一调用栈中等待仍执行的旧 owner 退休。发布线程可以借用 scope，其他线程不能进入候选。
AwaitingCollection 期间只允许访问已经发布的数据；Play/Build/Export 和新代际继续受严格 admission 限制。
候选失败完整回滚后，不因异常本身再次标记 dirty；新的输入变化或显式重建才重试，last-good 必须仍可查询。

`AssemblyLoadContext.Unload()` 只发起 cooperative unload，不代表完成。reload 对调用者可见的成功条件必须是：

1. 每个 retiring collectible ALC 只剩弱引用 monitor。
2. 已执行 Full GC。
3. 已执行 `GC.WaitForPendingFinalizers()`。
4. 已再次执行 Full GC。
5. 所有 monitor 都确认 load context 不再可达。
6. 对应 shadow generation storage 已成功清理，或由明确独立 cleanup failure 报告。

该 barrier 覆盖所有 collectible retirement，而不只覆盖成功 commit 后的 previous generation：

- 成功 reload 退休的 previous generation。
- pre-commit/activation rollback 后废弃的 candidate generation。
- Plugin uninstall 或 unavailable transaction 退休的 dependency closure。
- Host/Runtime/Editor shutdown 主动卸载的 generation。
- 单独调用 module unload API 产生的 generation。

因此 rollback/unload/dispose API 也必须返回或加入同一个 unload barrier。没有 collectible ALC 的纯数据 Asset
candidate 可以立即通过这一阶段，但不能为了统一表面 API 无条件触发 Full GC。

项目脚本的 GameScripts 与 EditorScripts 属于同一个创作代际：任一侧发生变化时，两侧的加载上下文
共同进入候选切换和退休验证。编译器仍可复用未变化的编译产物，但不能保留旧 GameScripts 上下文并
单独卸载 EditorScripts；Editor 泛型扩展闭合于 Game 类型时，CLR 的加载器依赖可能反向保留 Editor
上下文。Scene 状态通过既有候选事务恢复；不得以延长超时、跳过 GC 或忽略存活 monitor 代替完整退休。

允许把多次 GC verification 分散到后续 Editor frame，以避免一个同步无限循环冻结 UI；但在 barrier 完成前：

- reload 状态必须保持 `AwaitingCollection`/`Compiling`，不能显示 100%。
- public reload Task/operation 不能返回 Success。
- 不得消费下一个 reload candidate。
- 不得开始 Play、Build/Export 或另一个 Plugin/Script generation transaction。
- 不能清空 monitor、重置计数后假装完成。

诊断阈值不是成功超时。达到阈值仍有 ALC 存活时：

- 构造并抛出专用 unload failure（目标名称 `AssemblyUnloadException`）。
- 列出 module、domain、scope、generation、等待时长和 GC 次数。
- reload subsystem 进入 `Faulted`。
- 当前进程不得继续接受新的 generation transaction。
- Editor 可以保留最小诊断/保存能力，但 Play、Build 和继续 reload 必须被阻止；完整重启 Host 才能恢复。
- 不允许 catch 后只写日志并继续把当前状态当作正常完成。

如果 candidate 已经发布后才发现 retention，不能伪称已 rollback；这是明确的 post-commit retirement failure。Faulted 状态必须如实表达“新 generation 已发布，但进程的代际隔离已破坏”。

当前 `Inno.Extensibility.Reload.AssemblyUnloadBarrier` 已实现 Success-or-Exception：每次 collection cycle 执行
Full GC → Finalizers → Full GC；全部 probe 完成才进入 `Completed`，超过 retention threshold 则抛
`AssemblyUnloadException` 并保持 `Faulted`。`ScriptReloadHost` 在 barrier 完成前不消费下一 generation；测试
同时验证正常退休对象被回收和强引用泄漏时的失败语义。Host shutdown 只发起最外层 owner 的逆序退休，
不能在仍拥有下层对象时同步等待自己制造的引用环；最终回收由 composition teardown 完成。

### 9.3 必须释放的强引用根

每个 reload participant 在退休前必须审计并释放：

- static field、static event 和 static generic cache。
- Host/Editor/Runtime service field。
- TypeCache、converter registry、reflection/member accessor cache。
- Scene object、Asset canonical instance、dependency retention 和 provider cache。
- Editor Selection、Focus、Inspector lock、DragDrop、Action argument、Menu/Toolbar model 和 Modal/Presentation state。
- History 中的 delegate operation、runtime merge key 或 captured object。
- event subscription、observable、timer、watcher callback。
- 未完成 Task、continuation、CancellationToken registration、async state machine。
- Thread、ThreadLocal、AsyncLocal 和 thread-static value。
- native function pointer、GCHandle、reverse P/Invoke callback 和 unmanaged user data。
- Rendering/Audio/Animation graph 中保存的 managed provider、node 或 callback。
- Logger scope、diagnostic payload、metric observer 和 exception object 中意外保留的 collectible type。
- `ModuleLoadContext` 自身的共享 Assembly 解析表：在 `Unloading` 时释放，避免与跨 ALC 泛型依赖形成退休保留环；仅调用 `Unload()` 而保留该表仍可能使 GC barrier 无法完成。

长期 owner 只允许持有 Stable ID、persistent ID、中立 bytes、immutable host-owned descriptor 或弱引用。任何需要执行 extension 代码的 `Type`、delegate 和实例都必须收口在当前 generation snapshot 内。

### 9.4 Asset reload 与 assembly reload 的关系

2026-09-08 领域追加：Audio Provider 的部分构造由 TypeRegistry 的候选 ownership 登记，Provider/Clip/Bus
的 Pending 不能提前标 disposed 或释放下层设备；设备候选与旧设备退休复用 Core deadline，并保留终止性 Fault。
Editor viewport presentation 改为直接返回共享 ContentReadScope，不再用持有 live GameScene 的平行快照。
2026-09-08 下一批已接通 Rendering Provider/Pipeline/Feature、reload Complete/Rollback、Runtime 和 GPU
退出链：使用共享退休屏障和持久退出步骤，Pending 不清 owner、不 Finish、不提交设备帧；timeout 关闭
新提交与代际操作，重复 Dispose 仍保留下层资源。Pipeline 候选与活动状态共享单一 generation owner。
这不代表正常帧内所有 GPU 替换/淘汰路径、Rendering 全部资源职责拆分或全域 Recovery 已完成；继续以累积收口报告为准。

纯数据 Asset reload 不一定创建 ALC，但仍走 candidate/validate/safe-point/rollback。只要 Asset 的 concrete type、Importer、Converter 或 Provider 来自 collectible module，该 Asset reload 就必须加入同一 assembly generation transaction，并受 GC barrier 约束。

不得出现以下时序：

- Asset Catalog 已发布新 Plugin mount，但 TypeCache 仍是旧 generation。
- Scene 已恢复新 Component，但 Serialization Registry rollback 到旧 converter。
- Plugin 已从安装集合移除，但旧 Plugin ALC 继续作为 last-good 运行。
- ALC unload 仍 Pending，却开始下一次 Asset/Script rescan 并覆盖诊断。

## 十、错误、诊断与用户界面

统一状态至少区分：

| 状态 | 用户含义 | 系统行为 |
| --- | --- | --- |
| Resolved | 引用正常 | 正常编辑和运行 |
| Missing | 暂时不可用 | 显示 Missing，保留数据，允许修复 |
| Unavailable | 当前 operation 被 Missing/barrier 阻止 | 不改变状态，可重试 |
| CandidateFailed | 新 generation 构建/验证失败 | 保留 last-good |
| AwaitingCollection | 已退休旧代，等待 GC 证明卸载 | 阻止重叠 generation operation |
| Faulted | ALC retention 或 rollback 完整性失败 | 明确异常，阻止 Play/Build/reload |

诊断必须包含可定位信息：

- owner persistent ID。
- reference kind 和 property/path。
- target persistent ID。
- Stable Type ID / Plugin ID / extension ID。
- 当前 generation。
- Missing/Faulted 原因。

Editor 的显示规范：

- Inspector 字段显示 `Missing (<last-known name>)`，不显示成普通 `None`。
- Hierarchy/Graph 保留原位置和连接，使用统一 Missing visual state。
- Console diagnostics 可清除视图，但下一次完整 report 会重新发布仍成立的问题。
- 恢复后 Missing visual 和对应诊断自动解除。
- 错误信息不能建议用户删除数据作为默认修复方式。

## 十一、目标代码与项目结构

目标结构用于收口协议；具体实现前仍需逐项确认 project dependency，不能直接复制空项目：

```text
src/foundation/core/Inno.Core.Identity/
├── Identity.cs
├── IdentityObject.cs
├── IdentityAllocator.cs
├── IdentityRegistry.cs
└── RuntimeIdCodec.cs

src/content/references/Inno.References/                 # 目标 Mechanism
├── ReferenceKindId.cs
├── ReferenceKey.cs
├── ReferenceDescriptor.cs
├── ReferenceResolutionState.cs
├── ReferenceResolution.cs
├── IReferenceResolver.cs
├── ReferenceCatalog.cs
├── ReferenceRecoveryTransaction.cs
└── SerializedMissingState.cs

src/composition/editor/framework/Inno.Editor.Interactions/
├── DragDrop/
│   ├── EditorIdentityDragData.cs
│   ├── EditorIdentityDropRouter.cs
│   └── EditorDropContext.cs
└── History/
    ├── EditorHistoryContext.cs
    ├── EditorHistoryChange.cs
    └── ...

src/foundation/extensibility/Inno.Extensibility.Modules/
├── Reloading/
│   ├── AssemblyReloadSession.cs
│   ├── AssemblyUnloadBarrier.cs
│   └── AssemblyUnloadException.cs
└── Modules/
    └── AssemblyUnloadMonitor.cs
```

领域只实现 adapter：

```text
Inno.Assets              → Asset reference resolver / typed missing shell
Inno.Scene               → Missing Component/System state adapter
Inno.Plugins.Authoring   → Plugin availability and mount candidate
Inno.Scripting.Reload    → compile request and generation orchestration
Inno.Editor.Interactions → identity drag + shared history consumer
Rendering/Audio/...      → extension graph state adapters
```

命名统一：

- `Identity`：一个对象的 persistent/runtime identity snapshot。
- `Reference`：指向另一个逻辑对象的持久意图。
- `Resolver`：只解析，不拥有目标、不提交 transaction。
- `Catalog`：完整 immutable generation 的权威索引。
- `Recovery`：Missing → Resolved 的事务。
- `Placeholder`/`Missing*`：领域可展示、可序列化的 host-owned 缺失对象。
- `Monitor`：只观察，不驱动状态迁移。
- `Barrier`：阻止 operation 在条件满足前完成。
- `Handle`：generation-scoped backend/runtime resource，不是 persistent object reference。

禁止用 `Manager`、`Helper`、`Token`、`TemporaryId` 模糊这些不同职责。

## 十二、Architecture Tool 强制检查

在可以稳定、低误报地实现后，Architecture Tool 至少应检查：

1. Scene、Asset、Plugin authoring 和 Editor addressable model 必须使用 `IdentityObject` 或声明的 identity-bearing protocol。
2. ImGui Editor identity drag payload 的 unmanaged 数据只能是 Identity runtime ID；禁止随机 Guid token 路由到 managed source object。
3. `runtimeId` 不得出现在 Serialization DTO、History payload model、Settings、Scene/Prefab 或 Asset metadata public schema。
4. reload-safe History payload 不得含 `object`、`Type`、delegate、IdentityObject、Assembly 或 ALC。
5. collectible extension registry 的 active snapshot 外不得保存 extension `Type`、instance 或 delegate。
6. reference-aware serializer 调用不得直接使用 `SerializationContext.empty`；必须从声明的 owner context 进入。
7. Missing placeholder/state 不得公开或内部保存 collectible `Type`/object/delegate。
8. assembly reload Success path 必须经过 completed unload barrier。
9. timeout/failure path不得清空仍 Pending 的 monitor 后回到 Idle。
10. GC barrier Pending 时禁止开始新 compile/reload/Play/Build/Export。
11. Asset/Scene/Plugin/Settings/Graph resolver 必须注册到共享 reference catalog，禁止领域中央 switch。
12. 二级索引只能把领域 key 映射到 identity/immutable record，不能成为跨 generation live object 权威表。

需要理解 C# 类型与字段的规则使用 Roslyn/metadata analysis；不要用会误伤 `contentVersion`、list index 或 backend handle 的关键词扫描代替语义检查。

## 十三、测试矩阵

### 13.1 Identity

- 注册、注销和相同 persistent ID replacement。
- slot 复用后 stale runtime ID 不能解析新对象。
- 不同 allocator 中相同 runtime ID 不能跨 domain 解析。
- duplicate persistent ID candidate 原子失败。
- Registry 索引不意外阻止无 owner object GC。

### 13.2 DragDrop

- native payload 精确包含 runtime ID，不包含 object token。
- Preview 和 Drop 都二次解析；对象在两者之间注销时 Drop Reject。
- reload/session 切换自动取消 active drag。
- Asset、Scene、Plugin/Script entry 使用同一 identity routing contract。
- 跨 Edit/Play/authoring domain 投递被拒绝。

### 13.3 Missing 与恢复

每个支持领域都覆盖：

```text
resolved
  → remove source/type/plugin/provider
  → missing state remains saveable
  → close/reopen or reload
  → reinstall/restore same identity
  → recovered concrete state
```

另外验证：

- nested references 部分恢复。
- expected type mismatch 保持 Missing。
- identity 冲突不产生半恢复状态。
- Graph edge、Scene order、Component/System identity 和 property bytes 不丢失。
- clean document 的自动 Missing/Recovery 不改变 dirty state。
- Player build 对 required Missing closure 明确失败。

### 13.4 Undo/Redo

- 在 target Missing、type Missing、Plugin removed、handler unavailable 时栈指针不移动。
- 恢复后原操作自动重新可用。
- Undo 创建/删除 Missing Component/System/Asset reference。
- redo branch、transaction compensation 和 disk-spilled payload 保持中立。
- Scene/Asset history 始终获得完整 Asset/reference Serialization Context。
- `Remove ScriptComponent → remove Plugin/Asset → Undo → recover → Redo` 全链通过。

### 13.5 GC barrier

- 连续多轮 Script 与 Plugin reload 后每个旧 ALC 弱引用都失效。
- rollback candidate、Plugin uninstall、显式 unload 和 shutdown 的 ALC 也必须失效。
- Asset、Scene、Editor registry 和 History 不保留旧 ALC。
- 故意泄露 static delegate/event、Task、Thread、GCHandle、native callback 时，reload 抛 unload exception 并进入 Faulted。
- Pending 时第二次 reload、Play、Build/Export 被拒绝。
- 成功状态只在 monitor 全部 Completed 后发布。
- shadow generation storage 在成功后清理。
- shutdown 对仍 Pending/Faulted generation 聚合报告，不静默退出。

## 十四、当前实现审计

| 区域 | 当前可复用基础 | 与目标标准的差距 |
| --- | --- | --- |
| Core Identity | persistent ID、generation runtime ID、显式 domain、`RuntimeIdentity`、per-session allocator 和 stale 检查 | 继续审计尚未跨边界的领域二级索引，禁止产生第二权威 owner |
| Scene | `EngineObject : IdentityObject`；共享五阶段 Recovery 已接真实 element/Asset slots；replacement 保留 persistent ID；Missing 保留结构和 bytes | 继续扩展全域批次、复杂嵌套引用与失败组合 |
| Assets | `AssetObject` 与 `AssetFileEntry` 均进入 Identity；candidate source identity dormant/activate/rollback；统一 Asset resolver/context | durable tombstone 与跨领域 Missing 的批量 recovery participant 仍需继续统一 |
| Type identity | `TypeRef` 已区分 stable/runtime type ID | 它是类型身份，不应被误当作 object Identity；跨域恢复仍需共享 transaction |
| Missing Scene type | `MissingGameComponent`、`MissingGameSystem` 保存 Stable Type ID、bytes、dependencies 并可在 reload 恢复 | Graph/Settings/Plugin unavailable 状态仍需完成共享 recovery participant 接入 |
| Editor DragDrop | domain-qualified runtime ID payload、allocator re-resolution、stale rejection 和 reload cancellation 已完成 | 新增 drag source 必须先成为当前 authoring/scene identity domain 的成员 |
| Editor History | stable kind、中立 bytes、persistent ID、candidate handler map、统一 Asset context；Missing Component/System Undo/Redo 与恢复已接通 | Graph/Settings/Plugin availability 等跨领域 Missing 自动恢复仍需补齐 coverage |
| Reload | 公共 `AssemblyUnloadBarrier`、弱 probe、双 GC、专用异常和 Faulted gate 已实现 | 继续扩展 Plugin uninstall/rollback/shutdown 的 soak 与 intentional-leak coverage |
| Plugins/Scripting | Plugin ID、source identity、Stable Type ID、collectible ALC、closure reload 和严格 GC gate 已存在 | Plugin unavailable 与通用 Missing recovery 仍需补齐领域 participant |

因此后续不是推翻现有系统：runtime token、Asset context 与 reload completion 已收口；剩余工作是把既有
Scene/Graph/Settings/Plugin Missing 表示接入共享 recovery transaction，而不是删除它们保存的领域结构。

## 十五、实施顺序

### 阶段 A：锁定语义与机器规则（已完成）

- 采用本文身份分类，禁止 runtime ID 持久化。
- 为 Identity domain、persistent reference、Missing 和 unload barrier 增加 Architecture 规则。
- 修正文档/API 中把 object identity、type identity、semantic ID 和 handle 混称的地方。

### 阶段 B：统一 identity-aware Editor interaction（已完成）

- 给所有可拖拽 authoring/scene model 提供 identity-bearing live record。
- 用 runtime ID payload 替换 `Guid` token + managed source table。
- 统一 resolver routing、stale rejection 和 reload cancellation。

### 阶段 C：建立 `Inno.References`（基础协议与 Asset/Runtime owner 已完成）

- 建立 reference descriptor、resolver catalog、resolution state、missing state 和 recovery transaction。
- Assets、Scene、Scripting、Plugins 只贡献领域 resolver/adapter。
- 建立 Runtime/Editor/History 的完整 Serialization Context owner。

### 阶段 D：迁移 Missing 与 History（Asset context 已完成，跨领域 recovery 继续）

- 保留现有 Scene Missing 能力，但改为共享 recovery transaction。
- 补齐 Asset tombstone、Plugin/Importer unavailable、Graph/Settings opaque extension state。
- 让所有 History handler 使用统一 context，并覆盖 Missing barrier/recovery。

### 阶段 E：升级 reload completion（Scripting 主路径已完成）

- 把 `AssemblyUnloadMonitor` 提升为不可绕过的 unload barrier。
- 让 Complete、Rollback、Unload 和 shutdown 产生的 monitor 全部加入 barrier。
- 移除 timeout 后清空 observation 的成功路径。
- 增加 `AssemblyUnloadException` 和 Runtime/Editor Faulted policy。
- 审计并清除全部 host-owned ALC roots。

### 阶段 F：全链验收（macOS 持续执行，Windows 由 CI 执行）

- Scripting、Plugin、Asset、Scene、Editor、Rendering、Audio 分别运行 remove/recover/Undo/Redo 测试。
- 运行 intentional leak tests，证明未回收一定抛异常。
- 运行连续 reload soak，证明成功一定意味着旧 ALC 已消失。
- macOS ARM64 与 Windows x64 都运行相同规则。

## 十六、验收标准与早期执行快照

下方复选框保留早期执行快照，不是当前未完成清单。强制标准不变；后续 Settings/Graph/Plugin 生产事务、强引用矩阵、关闭时 GC 屏障及最终运行结果统一见[当前交付与验收](ENGINE_CLOSURE_IMPLEMENTATION_2026_09_08.md)。不能把旧复选框或旧测试计数当作当前状态。

2026-09-07 续轮补充：[Runtime/Play 退休与共享 IO 的新证据](ENGINE_CLOSURE_CONTINUATION_2026_09_07.md)。
启动失败和 Play Stop 不再丢失 Pending owner；退休 deadline 失败关闭共享 generation gate。
本次追加已把通用 Layer/Module/Panel、Type/Assembly Registry 接到 Core 退休机制；未退休信号不再被普通错误聚合掩盖。
共享 `EnsureRetirementSafe()` 同时保护 Runtime/metadata 的退出；deadline 失败后重复 Dispose 仍不得提前销毁依赖。
这些变化仍不等价于以下跨领域 Missing-slot Recovery 已完成，相关未完成项继续保持未勾选。
2026-09-08 追加已完成 Scene + Asset-reference + History 的实际五阶段 Recovery 生产链；全域复选项继续按完整范围判断。

同日后续追加：Asset Source Mount 候选也已使用 `ReferenceRecoveryTransaction`，候选 canonical object
在激活前只持有 persistent ID，不能抢占活动 runtime 注册；Missing 与原 `.imeta` 返回经过同一引用槽解析。
Source Complete/Rollback 与 Pipeline shutdown 使用 Core lifetime/barrier；Authoring Loader 与 Runtime Database
保留 Pending unload 的对象、payload 和下层所有权。Assembly Catalog/Importer 现也复用隔离 Source candidate，
已有 Plugin compilation candidate 只被验证、不重复取得发布所有权。候选 `.imeta` 与中立诊断在提交/激活前不写入
活动状态；失败直接恢复原 loader，不再延迟到 GetLoader 时 Rescan。Settings、Graph 与 Plugin availability
的共同生产事务仍未完成，C07 保持部分完成。

2026-09-07 本轮收口补充：Module/Script/Editor 共用 GenerationCoordinator；
Build/Export 持有跨 await 的 read lease，自动 Catalog refresh 使用互斥 change reservation 并在读租约期间延后。
Registry/Module 的退休和回滚清理错误会聚合抛出并 Fault，不再仅写日志后继续。
这些实现不等于下列 Missing/Recovery 项已全部通过；跨域生产路径统一仍以
[实施记录 C07](ENGINE_CONSOLIDATION_IMPLEMENTATION.md)的未关闭项为准。

Settings 的 effective cache 已改为 Stable Type ID + property bytes，查询只创建当前 definition 的临时快照；
Store 必须从 Editor/Build 的 AssetPipeline 或 Player 的 RuntimeSession.assets 取得完整 owner context。
Compose、replacement、contribution 与 clone 共用此 context，真实脚本卸载测试覆盖第二个未主动 Rebuild 的 Store。
这关闭了实例缓存/丢失 resolver 缺口，但 Settings/Graph/Plugin availability 的共同 Recovery participant 仍未完成。

Audio Provider 的内容收集使用 invocation-scoped、有界 context，返回后清空 Clip 引用并撤销跨帧提交；
只有整批验证成功的 snapshot 能进入当前 Update。不得用长期保存此 collector 代替 Identity 解析。

共享 `AssetLease<T>`/`ArtifactLease` 保留直接或包装 Pending 的 value 与回调，并串行化并发/重入释放；
`RetentionScope` 与 Audio Clip cache 复用 Core `LifetimeScope`，不在 native Clip 退出前释放 Artifact，
也不在 Artifact Pending 重试时重复销毁 native Clip。Runtime Database 的 lease 计数与预算 eviction
分阶段推进，每个租约只递减一次。取消 preload 在清理 Pending 期间保留等待者。后续 C06 批次已将
Aggregate/InnerException 分类传播到 Core、Registry、Catalog、Reference recovery 与 Host 退出链；
相邻普通错误以及激活失败原因也必须随 Pending 保留。具体执行结果以累积报告的最新验收段为准；
这不等于失败 startup 与真实设备 callback/Task 的完整强引用 soak 矩阵已经关闭。

### Identity 与索引

- [ ] 所有跨 UI/回调/队列/帧边界的 live object 寻址经过 Identity。
- [x] ImGui DragDrop payload bytes 是 Identity runtime ID，payload type 同时限定 identity domain。
- [x] runtime ID 从不进入持久状态或 History。
- [ ] domain 二级索引不成为 live object 的第二权威来源。
- [ ] persistent ID、Stable Type ID、semantic ID、handle 和 content key 没有混用。

### Missing 与恢复

- [ ] Asset、Script Type、Plugin、Importer、Graph/Settings extension 缺失时数据仍完整。
- [x] `Inno.References` 在 API 中严格区分 Missing、Unassigned、TypeMismatch 和 Invalid。
- [ ] 同一 identity 恢复后自动重建，取得新 runtime ID。
- [ ] 自动 Missing/Recovery 不制造 dirty/history noise。
- [ ] 恢复失败保持原 Missing 状态并发布精确诊断。

### Undo/Redo

- [x] History 只保存 stable identity 和 neutral bytes。
- [ ] 暂时 Missing 只形成可恢复 barrier，不丢弃或跳过记录。
- [ ] 恢复后原 Undo/Redo 自动可用。
- [x] 所有 Asset-aware Scene/History/Play handler 使用 owner 创建的完整 Serialization Context。
- [ ] Missing placeholder 的创建、删除、移动和属性修改可逆。

### Reload 与 GC

- [x] Scripting reload Success 前所有退休 ALC monitor 均 Completed。
- [x] Pending monitor 不会因 timeout 被遗忘。
- [ ] GC barrier 期间禁止重叠 reload、Play、Build 和 Export。
- [x] ALC retention 抛 `AssemblyUnloadException` 并进入 Faulted。
- [ ] static/event/task/thread/native callback 等强引用有自动化泄漏测试。
- [ ] 成功 reload 后旧 generation shadow storage 已清理。

最终原则：

> 瞬时访问使用 Identity runtime ID；持久意图使用 persistent ID 和 Stable ID；暂时不可用保留为 Missing；
> 当前 generation 负责解析和恢复；旧 generation 只有在 GC 证明完全不可达后才算真正结束。

## 2026-09-08 生产映射

GraphEditorModule 以独立 IdentityAllocator 解析 live document，资源集合承担 session 的强引用所有权，History 保存 Guid persistent ID 和中立 Graph bytes，Panel 不保存跨代 AssetObject。Graph 文档源的 Missing 保留脏数据，定义缺席的 node record 不删除。

SettingsStore.CreateReloadChange 捕获 neutral effective values/contributors/revision，在旧类型与 owner resolver 恢复后恢复旧值，不写盘。PluginEnvironment.CreateReloadChange 通过 ReferenceRecoveryTransaction 把 Source Mount、Catalog、Settings 与 Editor assembly transaction 合并；无代码变化也进入同一个 GenerationCoordinator。语义 ID 的结构恢复 participant 不伪装 object slots。

Editor participant Capture 在共享 Prepare 内执行，失败只补偿已尝试阶段；代码使用 IGenerationChange，禁止两段无所有权 activate/restore Action 替代事务。Plugin 后台 scan、Lifetime completion、Jobs callback 与 Rendering owners 统一 Pending/timeout 所有权。公开 XML/Wiki 的“只读”是正常托管契约，不能作为对抗不受信任 unsafe 代码的沙箱保证。

ScriptReloadHost 关闭前先等待共享 GenerationCoordinator 完成上一批 AwaitingCollection，再发布模块卸载；退休失败继续抛异常并封锁 Host，不清除 monitor 后伪装关闭成功。此顺序由真实脚本双代卸载回归和非空原生 Editor 600 帧退出验证覆盖。

自动编译必须区分源输入变化和当前候选的发布通知回声，不能由 Assembly/Asset Catalog 自身重新发布而无限排队。Editor 编译票据与进度 UI 也参与完整 GC 完成语义：先发布、后回收、最后成功；外部边界已经推进完成屏障时，Editor 仍须完成 deferred ticket。退休失败显式进入 Failed 并解除忙碌弹窗，不能被遗留 compilation request 覆盖，也不能因关闭弹窗就放开 Faulted Host 门禁。
