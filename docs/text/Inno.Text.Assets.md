# Inno.Text.Assets

[Text 索引](README.md) · [Assets](../assets/README.md)

`FontImporter` 处理 `.ttf`、`.otf`、`.ttc`、`.otc`。导入时验证 sfnt/collection 容器头，输出 `runtime` metadata 和不可变 `font-data` artifact。运行时只消费 artifact，不重新读取 Project 源文件；格式错误在导入边界报告。

## 公开契约与工作流

`FontImporter : AssetImporter<FontAsset>` 的公开 `supportedExtensions` 列出四种格式；创作侧通过标准 Asset Pipeline 发现该 importer，无须单独调用。它的 `ImportAsync` 是标准受保护扩展点，读取当前源文件、写入 `FontAsset` 和 `font-data`，并交由统一 Catalog 管理身份、Missing 与依赖。业务脚本仅通过 [Inno.Text](Inno.Text.md) 的 `FontAsset` 使用结果，不引用此创作项目。

将 `Fonts/Interface.ttf` 放入 Project `Assets` 后，脚本可用 `Assets.Load<FontAsset>(Assets.LocalPath("Fonts/Interface.ttf"))` 获得资产；必须在 Asset scope 中调用。字体源缺失或头损坏时导入失败，不生成可被运行时误用的空 face。运行时部署只保留经验证的字体数据和 metadata，不部署 importer。
