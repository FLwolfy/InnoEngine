using System;

namespace Inno.Assets.Pipeline;

/// <summary>
/// Marks a stateless asset importer for automatic TypeCatalog discovery.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class AssetImporterExtensionAttribute : Attribute;
