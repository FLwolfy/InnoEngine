using System;
using Inno.Core.Serialization;

namespace Inno.Assets;

/// <summary>Restores nested neutral asset properties with the owning database's converter generation and references.</summary>
public interface IAssetPropertyStateResolver
{
    /// <summary>Restores a stable typed property payload into a caller-owned current-generation value.</summary>
    /// <typeparam name="TValue">Declared native property contract.</typeparam>
    /// <param name="stableTypeId">Stable identity captured with the properties.</param>
    /// <param name="propertyData">Native property bytes.</param>
    /// <param name="target">Detached destination. Referenced assets resolve in this owner.</param>
    void RestoreProperties<TValue>(Guid stableTypeId, byte[] propertyData, TValue target) where TValue : class, ISerializable;
}
