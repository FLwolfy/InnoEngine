using System;
using System.Collections.Generic;
using System.Linq;

namespace Inno.Rendering;

/// <summary>
/// Describes one reflected material binding expected by compiled programs.
/// </summary>
public sealed class ShaderInterfaceBinding
{
    /// <summary>
    /// Creates a reflected interface binding.
    /// </summary>
    /// <param name="id">
    /// Stable property ID.
    /// </param>
    /// <param name="type">
    /// Expected value or resource type.
    /// </param>
    /// <param name="stages">
    /// Stages that consume the binding.
    /// </param>
    /// <param name="arrayCount">
    /// Required array element count.
    /// </param>
    /// <param name="bindingKind">
    /// Backend-neutral interface binding domain.
    /// </param>
    /// <param name="storageAccess">
    /// Required access for storage resources.
    /// </param>
    /// <param name="nativeName">Adapter-generated reflected symbol, or null when the logical ID is also the symbol.</param>
    /// <param name="location">Explicit resource slot, or null when the caller assigns an ordered layout.</param>
    public ShaderInterfaceBinding(
        ShaderPropertyId id,
        ShaderPropertyType type,
        ShaderStage stages,
        int arrayCount = 1,
        ShaderPropertyBindingKind bindingKind = ShaderPropertyBindingKind.Uniform,
        RenderStorageAccess storageAccess = RenderStorageAccess.Read,
        string? nativeName = null,
        int? location = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(arrayCount);
        if (!Enum.IsDefined(bindingKind))
            throw new ArgumentOutOfRangeException(nameof(bindingKind));
        if (!Enum.IsDefined(storageAccess))
            throw new ArgumentOutOfRangeException(nameof(storageAccess));
        this.id = id;
        this.type = type;
        this.stages = stages;
        this.arrayCount = arrayCount;
        this.bindingKind = bindingKind;
        this.storageAccess = storageAccess;
        this.nativeName = nativeName ?? id.value;
        ArgumentException.ThrowIfNullOrWhiteSpace(this.nativeName);
        if (location < 0) throw new ArgumentOutOfRangeException(nameof(location));
        this.location = location;
    }

    /// <summary>
    /// Gets the stable property ID.
    /// </summary>
    public ShaderPropertyId id { get; }

    /// <summary>
    /// Gets the expected value or resource type.
    /// </summary>
    public ShaderPropertyType type { get; }

    /// <summary>
    /// Gets stages that consume the binding.
    /// </summary>
    public ShaderStage stages { get; }

    /// <summary>
    /// Gets the required array element count.
    /// </summary>
    public int arrayCount { get; }

    /// <summary>
    /// Gets the backend-neutral interface binding domain.
    /// </summary>
    public ShaderPropertyBindingKind bindingKind { get; }

    /// <summary>
    /// Gets required access for storage resources.
    /// </summary>
    public RenderStorageAccess storageAccess { get; }

    /// <summary>Gets the exact reflected symbol emitted by the adapter, distinct from the logical property ID.</summary>
    public string nativeName { get; }
    /// <summary>Gets an explicitly compiled resource slot, or null for a caller-assigned layout.</summary>
    public int? location { get; }
}

/// <summary>
/// Stores the manifest-derived binding contract verified after backend program creation.
/// </summary>
public sealed class ShaderInterface
{
    /// <summary>
    /// Creates a shader interface contract.
    /// </summary>
    /// <param name="bindings">
    /// Stable expected bindings.
    /// </param>
    public ShaderInterface(IReadOnlyList<ShaderInterfaceBinding> bindings)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        this.bindings = Array.AsReadOnly(bindings.ToArray());
    }

    /// <summary>
    /// Gets stable expected bindings.
    /// </summary>
    public IReadOnlyList<ShaderInterfaceBinding> bindings { get; }

}
