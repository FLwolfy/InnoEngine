using System;
using Inno.Rendering;

namespace Inno.Rendering.Shaders;

public sealed partial class ShaderIrBuilder
{
    /// <summary>Samples a texture using implicit derivatives; the enclosing stage must be Fragment.</summary>
    /// <param name="texture">A sampled 2D, 2D-array, 3D or cube texture.</param>
    /// <param name="coordinate">float2 for 2D, or float3 for an array, volume or cube.</param>
    /// <returns>The sampled float4 value.</returns>
    /// <exception cref="ArgumentException">Operands have incompatible types or owners.</exception>
    public ShaderIrValue Sample(ShaderIrValue texture, ShaderIrValue coordinate)
    {
        ValidateSample(texture, coordinate);
        ShaderIrValue output = NewValue(ShaderSourceType.Atomic("float4"));
        m_instructions.Add(new(ShaderIrOperation.TextureSample, [texture, coordinate], [output]));
        return output;
    }

    /// <summary>Samples a texture at an explicit floating-point mip level without implicit derivatives.</summary>
    /// <param name="texture">A sampled 2D, 2D-array, 3D or cube texture.</param>
    /// <param name="coordinate">float2 for 2D, or float3 for an array, volume or cube.</param>
    /// <param name="level">Scalar floating-point mip level.</param>
    /// <returns>The sampled float4 value.</returns>
    /// <exception cref="ArgumentException">Operands have incompatible types or owners.</exception>
    public ShaderIrValue SampleLevel(ShaderIrValue texture, ShaderIrValue coordinate, ShaderIrValue level)
    {
        ValidateSample(texture, coordinate);
        RequireOwned(level);
        if (!level.type.IsEquivalentTo(ShaderSourceType.Atomic("float"))) throw new ArgumentException("A mip level must be float.", nameof(level));
        ShaderIrValue output = NewValue(ShaderSourceType.Atomic("float4"));
        m_instructions.Add(new(ShaderIrOperation.TextureSampleLevel, [texture, coordinate, level], [output]));
        return output;
    }

    /// <summary>Loads a storage value at this exact point in the block's memory-effect sequence.</summary>
    /// <param name="resource">A readable typed storage input.</param>
    /// <param name="coordinate">uint buffer index; int2 image coordinate, or int3 for an image array/volume.</param>
    /// <returns>The declared buffer element or float4 image value.</returns>
    /// <exception cref="ArgumentException">The resource is write-only or the coordinate is incompatible.</exception>
    public ShaderIrValue LoadStorage(ShaderIrValue resource, ShaderIrValue coordinate)
    {
        ShaderStorageType storage = ValidateStorage(resource, coordinate);
        if (storage.access == RenderStorageAccess.Write) throw new ArgumentException("A write-only resource cannot be loaded.", nameof(resource));
        ShaderIrValue output = NewValue(storage.valueType);
        m_instructions.Add(new(ShaderIrOperation.StorageLoad, [resource, coordinate], [output]));
        return output;
    }

    /// <summary>Stores a storage value without pruning unused writes or reordering surrounding memory operations.</summary>
    /// <param name="resource">A writable typed storage input.</param>
    /// <param name="coordinate">uint buffer index; int2 image coordinate, or int3 for an image array/volume.</param>
    /// <param name="value">Exactly the declared element or image value type.</param>
    /// <exception cref="ArgumentException">The resource is read-only or operands have incompatible types.</exception>
    public void StoreStorage(ShaderIrValue resource, ShaderIrValue coordinate, ShaderIrValue value)
    {
        ShaderStorageType storage = ValidateStorage(resource, coordinate);
        RequireOwned(value);
        if (storage.access == RenderStorageAccess.Read || !storage.valueType.IsEquivalentTo(value.type))
            throw new ArgumentException("A store requires a writable resource and its exact element type.", nameof(resource));
        m_instructions.Add(new(ShaderIrOperation.StorageStore, [resource, coordinate, value], []));
    }

    /// <summary>Atomically adds to a scalar integer buffer element and returns its previous value.</summary>
    /// <param name="resource">A read-write int or uint storage buffer.</param>
    /// <param name="index">Unsigned element index.</param>
    /// <param name="value">Addition operand matching the buffer element type.</param>
    /// <returns>The original scalar value observed by the atomic operation.</returns>
    /// <exception cref="ArgumentException">The resource is not an appropriate read-write integer buffer.</exception>
    public ShaderIrValue AtomicAddStorage(ShaderIrValue resource, ShaderIrValue index, ShaderIrValue value)
    {
        ShaderStorageType storage = ValidateStorage(resource, index);
        RequireOwned(value);
        if (storage.isImage || storage.access != RenderStorageAccess.ReadWrite
            || !(storage.valueType.IsEquivalentTo(ShaderSourceType.Atomic("int")) || storage.valueType.IsEquivalentTo(ShaderSourceType.Atomic("uint")))
            || !storage.valueType.IsEquivalentTo(value.type)) throw new ArgumentException("Atomic add requires a read-write scalar integer buffer.", nameof(resource));
        ShaderIrValue output = NewValue(storage.valueType);
        m_instructions.Add(new(ShaderIrOperation.StorageAtomicAdd, [resource, index, value], [output]));
        return output;
    }

    /// <summary>Discards a fragment when the Boolean condition is true; other stages reject this instruction.</summary>
    /// <param name="condition">Scalar Boolean from this block.</param>
    /// <exception cref="ArgumentException">The condition is not a Boolean or belongs to another builder.</exception>
    public void Discard(ShaderIrValue condition)
    {
        RequireOwned(condition);
        if (!condition.type.IsEquivalentTo(ShaderSourceType.Atomic("bool"))) throw new ArgumentException("Discard requires bool.", nameof(condition));
        m_instructions.Add(new(ShaderIrOperation.Discard, [condition], []));
    }

    private void ValidateSample(ShaderIrValue texture, ShaderIrValue coordinate)
    {
        RequireOwned(texture);
        RequireOwned(coordinate);
        if (!texture.type.IsEquivalentTo(ShaderSourceType.Atomic(texture.type.id)))
            throw new ArgumentException("A sampled binding cannot be disguised as an aggregate.", nameof(texture));
        string type = texture.type.id switch
        {
            "sampled-texture2d" => "float2",
            "sampled-texture2d-array" or "sampled-texture3d" or "sampled-texture-cube" => "float3",
            _ => throw new ArgumentException("Sampling requires a supported sampled texture type.", nameof(texture))
        };
        if (!coordinate.type.IsEquivalentTo(ShaderSourceType.Atomic(type))) throw new ArgumentException($"Sampling requires a {type} coordinate.", nameof(coordinate));
    }

    private ShaderStorageType ValidateStorage(ShaderIrValue resource, ShaderIrValue coordinate)
    {
        RequireOwned(resource);
        RequireOwned(coordinate);
        ShaderStorageType storage = resource.type.storage ?? throw new ArgumentException("A typed storage binding is required.", nameof(resource));
        string coordinateType = !storage.isImage ? "uint" : storage.array || storage.dimension == RenderTextureDimension.Texture3D ? "int3" : "int2";
        if (!coordinate.type.IsEquivalentTo(ShaderSourceType.Atomic(coordinateType)))
            throw new ArgumentException($"This resource requires a {coordinateType} coordinate.", nameof(coordinate));
        return storage;
    }
}
