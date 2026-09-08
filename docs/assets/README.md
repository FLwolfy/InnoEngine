# Assets API

[Wiki 首页](../README.md) · [Plugins](../plugins/README.md) · [Build](../build/README.md) · [Identity 与可恢复引用标准](../architecture/IDENTITY_REFERENCE_RELOAD_STANDARD.md)

Assets 分成真实部署边界：Player-safe runtime contract 与 authoring pipeline。

| 项目 | 职责 |
| --- | --- |
| [Inno.Assets](Inno.Assets.md) | identity、reference、runtime catalog、Artifact metadata、AssetDatabase、runtime asset types |
| [Inno.Assets.Pipeline](Inno.Assets.Pipeline.md) | Source Mount、watcher、Importer、dependency graph、Artifact writer、incremental import/export |

```text
Assets/ (writable authoring source)
Plugins/ (read-only .iplugin mounts)
       ↓ shared AssetPipeline
Library/AssetDatabase + Library/Artifacts
       ↓ Build runtime closure
Content/catalog.inno + content-<hash>.pack
       ↓
AssetDatabase (Player)
```

Asset mutation 只能在 pipeline owner thread 发生，并以单次 `AssetChangeSet` 推进 revision。后台导出使用预先捕获的不可变 Serialization generation，不能从 worker 触发 TypeCatalog 切换。

Source Mount 候选已接入共享引用 Recovery 与 Core 退休 barrier；候选 canonical 在发布前不注册 runtime ID。
Assembly Catalog/Importer 重建复用同一候选；已有 Plugin source candidate 保持原发布 owner。`.imeta` 与诊断
在候选阶段暂存，失败恢复旧 loader，不再依赖下一次访问重扫补偿。
Authoring Loader 和 Runtime Database 的 Pending unload 均保留对象与 payload，详见对应项目页。

Asset/Artifact 租约统一保留 Pending value 与 callback；`RetentionScope` 复用 Core lifetime。
Runtime Database 的预算 eviction 重试不重复递减租约计数，详见 [Residency 约束](Inno.Assets.md)。
