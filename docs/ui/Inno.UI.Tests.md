# Inno.UI.Tests

[UI 索引](README.md)

验证 RML 导入、Context/文档生命周期、DOM 操作、输入事件、帧几何与纹理释放；还反复销毁带有活动文档和字体纹理的 Context，以覆盖 RmlUi 全局字体缓存回调的生命周期。原生测试要求当前构建配置已有对应的 `inno-ui` 动态库。执行：`dotnet test tests/ui/Inno.UI.Tests/Inno.UI.Tests.csproj`。
