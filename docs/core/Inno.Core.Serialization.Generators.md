# Inno.Core.Serialization.Generators

[Core 索引](README.md) · [Serialization](Inno.Core.Serialization.md)

该 analyzer project 提供公开 `SerializationConverterGenerator : IIncrementalGenerator`，在标注 `GenerateSerializationConverterAttribute` 的 compilation 中生成普通封闭 DTO converter。

生成器验证可用构造器、property key、支持的 scalar/collection/map 类型和重复声明。生成代码与目标 DTO 同 compilation，因此可以访问 internal 类型；它带明确 generated marker，由手写 XML 检查豁免。

多态、对象身份、引用图、自定义恢复不变量和外部类型不使用自动生成路径，继续实现显式 `SerializationConverter<T>`。
