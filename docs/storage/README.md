# Storage API

[Wiki 首页](../README.md) · [Runtime](../runtime/README.md)

Storage 提供游戏应用级持久数据机制，不定义 save slot、schema、autosave 或 cloud sync 策略。

| 项目 | 职责 |
| --- | --- |
| [Inno.Storage](Inno.Storage.md) | 沙箱 key、异步原子 IO 契约与脚本 façade |
| [Inno.Storage.Runtime](Inno.Storage.Runtime.md) | Session Feature 与 execution scope |
| [Inno.Adapter.Storage](Inno.Adapter.Storage.md) | Storage backend 选择与 factory contract |
| [Inno.Adapter.Storage.FileSystem](Inno.Adapter.Storage.FileSystem.md) | 默认本地文件系统 adapter |
