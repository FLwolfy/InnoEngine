# Inno.Text.Tests

[Text 索引](README.md)

验证无效字体源被拒绝、合法字体导入产生 runtime/font-data artifact、服务经 FreeType/HarfBuzz 完成 shaping 与 rasterization。原生测试要求当前平台已有对应 `inno-text-debug` 动态库。执行：`dotnet test tests/text/Inno.Text.Tests/Inno.Text.Tests.csproj`。
