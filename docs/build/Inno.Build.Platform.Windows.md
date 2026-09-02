# Inno.Build.Platform.Windows

[Build 索引](README.md) · [Inno.Build](Inno.Build.md) · [Player](../runtime/Inno.Player.md)

该项目实现公开 `WindowsX64GameBuildTarget : IGameBuildTarget`。`BuildContentAsync` 生成 Windows BGFX backend Shader 与 portable KTX；`PackageAsync` 组合 `<Product>-Windows-x64` 目录并将 `Inno.Player.exe` 改为产品名。

目标只处理 staging 与平台布局，不编译脚本、不扫描 Project、不启动 dotnet。Windows 进程执行验证由 Windows x64 CI runner 完成。
