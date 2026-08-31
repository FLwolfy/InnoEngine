using System;

namespace Inno.Assets.Loader;

/// <summary>
/// Marks a stateless asset importer for automatic TypeCacheManager discovery.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class AssetImporterExtensionAttribute : Attribute;
