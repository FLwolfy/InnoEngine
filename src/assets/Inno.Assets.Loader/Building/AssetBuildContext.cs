using System;
using System.Collections.Generic;

using Inno.Assets.Core;

namespace Inno.Assets.Loader;

/// <summary>Provides a stable input snapshot to an aggregate asset build.</summary>
/// <typeparam name="TDefinition">The build definition asset type.</typeparam>
public sealed class AssetBuildContext<TDefinition> where TDefinition : AssetObject
{
    /// <summary>Creates a build context.</summary>
    /// <param name="definition">The build definition.</param>
    /// <param name="inputs">The immutable input catalog snapshots.</param>
    public AssetBuildContext(TDefinition definition, IReadOnlyList<AssetInfo> inputs)
    {
        this.definition = definition ?? throw new ArgumentNullException(nameof(definition));
        this.inputs = inputs ?? throw new ArgumentNullException(nameof(inputs));
    }

    /// <summary>Gets the build definition.</summary>
    public TDefinition definition { get; }

    /// <summary>Gets the input catalog snapshots.</summary>
    public IReadOnlyList<AssetInfo> inputs { get; }
}
