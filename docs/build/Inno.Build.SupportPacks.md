# Inno.Build.SupportPacks

[Build 索引](README.md) · [Player](../runtime/Inno.Player.md)

该可执行项目生产按 RID 隔离的 source-independent Player Support Pack，不提供稳定 library API。它在引擎构建/发布阶段运行 dotnet publish，随后只复制 deployment closure、release Native libraries 和 executable mode。

输出主动排除 `.pdb`、`.dbg`、`.map`、`.xml`、源码、Editor、Build、Compiler、Reload、Assets Pipeline、Plugins Authoring 和 toolchain。Game Export 只消费生成结果，本身不运行 dotnet。
