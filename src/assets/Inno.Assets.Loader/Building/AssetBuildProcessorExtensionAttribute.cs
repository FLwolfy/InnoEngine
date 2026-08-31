using System;

namespace Inno.Assets.Loader;

/// <summary>Marks an aggregate asset build processor for TypeCache discovery.</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class AssetBuildProcessorExtensionAttribute : Attribute;
