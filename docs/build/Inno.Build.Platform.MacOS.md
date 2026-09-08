# Inno.Build.Platform.MacOS

[Build 索引](README.md) · [Inno.Build](Inno.Build.md) · [macOS Player](../runtime/Inno.Player.md)

该项目实现公开 `MacOSArm64GameBuildTarget : IGameBuildTarget`。`BuildContentAsync` 生成 Metal Shader 与 portable KTX；`PackageAsync` 将验证后的 Support Pack 和 Content 放入 `.app/Contents/MacOS` 与 `Resources/Content`，并写入由 Application ID 驱动的 `Info.plist`。

Target 自己声明稳定 ID `macos-arm64`、显示名称与 macOS ARM64 host preference；`Inno.Build` 不硬编码这些
平台事实。

构造函数只接受 authoring `AssetPipeline` 和 `SerializationRegistry`。所有复制在 staging 内流式执行并保留 Unix executable mode；签名、公证属于将来独立发布阶段，不在当前 target 静默执行。
