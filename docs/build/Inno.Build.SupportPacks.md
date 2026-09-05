# Inno.Build.SupportPacks

[Build 索引](README.md) · [Player](../runtime/Inno.Player.md)

该可执行项目生产按 RID 隔离的 source-independent Player Support Pack，不提供稳定 library API。它在引擎构建/发布阶段运行 `dotnet publish`，随后只复制 deployment closure、SDL3/BGFX/miniaudio release Native libraries 和 executable mode。Support Pack 是引擎发行物，不是 Project Asset；正式 Editor distribution 必须随包携带目标目录。

输出主动排除 `.pdb`、`.dbg`、`.map`、`.xml`、源码、Editor、Build、Compiler、Reload、Assets Pipeline、Plugins Authoring 和 toolchain。Game Export 只消费生成结果，本身不运行 dotnet。

源码开发环境可在构建 Editor 后显式生成当前平台 Pack：

```shell
/path/to/dotnet build/support/Inno.Build.SupportPacks/bin/Debug/net9.0/Inno.Build.SupportPacks.dll \
  --engine-root /path/to/InnoEngine \
  --output /path/to/InnoEngine/src/editor/Inno.Editor.Application/bin/Debug/net9.0/SupportPacks \
  --target macos-arm64 \
  --dotnet /path/to/dotnet
```

Editor 默认只从 `AppContext.BaseDirectory/SupportPacks` 读取；构建机也可用 `INNO_SUPPORT_PACK_ROOT` 指向预生成的发行目录。缺失时 Export UI 明确阻止构建，这是部署完整性错误，不会临时 publish Player 或访问引擎源码。Build CLI 必须在自身 composition root 直接部署 Scene importer 等 authoring 实现；`Inno.Build` 的 implementation-only reference 不被当作 CLI 的传递部署闭包。

Support Pack 不再保存或比较由宿主进程加载状态推导出的全局脚本 API 契约。导出时，Compiler 先执行当前逻辑脚本 API 校验，再直接使用所选 Pack 的真实 `Inno.*` 程序集编译部署脚本：未使用的 API 差异不会阻断导出；项目实际使用但 Pack 不具备的 API 会在打包前产生源码诊断。更换 Pack 会改变编译缓存身份，不会沿用旧目标的脚本 DLL。

源码开发阶段只有在 Player 实现程序集、原生库或平台骨架发生变化且需要验证新实现时才重新生成 Pack；正式 Editor 发行包直接携带已生成 Pack，Project 用户不承担“同步”步骤。Support Pack 仍必须独立存在，因为导出产物需要自包含 .NET Runtime、Player composition、发行版原生库与平台目录结构，而这些都不属于 Editor 进程的可部署闭包。
