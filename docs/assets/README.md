# Assets API

[Wiki 首页](../README.md) · [Plugins](../plugins/README.md) · [Build](../build/README.md)

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
