# Inno.Build.Platform.Windows

[Build 索引](README.md) · [Inno.Build](Inno.Build.md) · [Player](../runtime/Inno.Player.md)

该项目实现公开 `WindowsX64GameBuildTarget : IGameBuildTarget`。`BuildContentAsync` 生成 Windows BGFX backend Shader 与 portable KTX；`PackageAsync` 组合 `<Product>-Windows-x64` 目录并将 `Inno.Player.exe` 改为产品名。

Target 自己声明稳定 ID `windows-x64`、显示名称与 Windows x64 host preference；`Inno.Build` 不硬编码这些
平台事实。

目标只处理 staging 与平台布局，不编译脚本、不扫描 Project、不启动 dotnet。Windows 进程执行验证由 Windows x64 CI runner 完成。
