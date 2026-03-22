using System;

namespace Inno.Engine.Graphics.Resources.GpuResources.Cache;

/// <summary>
/// Utility builder for producing compact, order-sensitive GPU cache keys ("variants").
///
/// <para>
/// This type is intended for <b>runtime</b> GPU caches (e.g., PSO / resource binding / shader variant lookups)
/// where keys are computed frequently and must be inexpensive.
/// </para>
///
/// <para>
/// <b>Important:</b> This implementation uses <see cref="System.HashCode"/> as the underlying accumulator.
/// The produced key is suitable for in-memory caches within a single process. It is not intended as a
/// persistent or cross-process identifier.
/// </para>
/// </summary>
public sealed class GpuVariant
{
    private const int C_TAG_ID_GUID = unchecked((int)0xA17D_1D01);
    
    private HashCode m_hash;
    private GpuVariant() { }

    /// <summary>
    /// Adds an arbitrary value into the variant hash stream.
    ///
    /// <para>
    /// This method is primarily used for value-like inputs such as enums, integers, booleans, small structs,
    /// and other deterministic state parameters.
    /// </para>
    ///
    /// <para>
    /// <b>Guid note:</b> If you add a <see cref="Guid"/> via this method (i.e., <c>Add(guid)</c>),
    /// it is treated as a generic value. If you want the Guid to be treated as an <i>identifier</i> with
    /// domain separation, prefer <see cref="AddId(Guid)"/>.
    /// </para>
    ///
    /// <para>
    /// <b>Runtime-only note:</b> For reference types, the hash contribution depends on their
    /// <c>GetHashCode()</c> implementation, which may not be stable across processes. For GPU runtime caches
    /// this is often acceptable, but avoid passing arbitrary object instances unless intentional.
    /// </para>
    /// </summary>
    /// <typeparam name="T">Type of the value to hash.</typeparam>
    /// <param name="value">The value to add into the key.</param>
    public void Add<T>(T value) => m_hash.Add(value);

    /// <summary>
    /// Adds a GUID intended to represent an <b>identifier</b> (e.g., asset GUID, shader GUID, texture GUID).
    ///
    /// <para>
    /// This method applies a domain-separation tag before hashing the Guid, ensuring that:
    /// <c>AddId(guid)</c> produces a different hash stream than <c>Add(guid)</c>, even for the same Guid value.
    /// </para>
    ///
    /// <para>
    /// Use this when the Guid represents an identity (resource key) rather than a generic data value.
    /// This reduces accidental collisions and makes the key composition more robust when the same raw Guid
    /// could appear in different semantic positions.
    /// </para>
    /// </summary>
    /// <param name="guid">The identifier GUID to add.</param>
    public void AddId(Guid guid)
    {
        m_hash.Add(C_TAG_ID_GUID);
        m_hash.Add(guid);
    }

    /// <summary>
    /// Adds a type identity into the variant hash stream.
    ///
    /// <para>
    /// This is useful when different code paths or data layouts are keyed by the concrete type, for example:
    /// material/binding implementations, shader interface types, layout descriptors, or decoder/encoder types.
    /// </para>
    ///
    /// <para>
    /// <b>Stability note:</b> This method uses <see cref="RuntimeTypeHandle"/> (via <see cref="Type.TypeHandle"/>),
    /// which is a runtime handle and may vary across processes. This is typically fine for runtime-only GPU caches.
    /// If you later need cross-process stability, switch to hashing a stable type name
    /// (e.g., <c>type.AssemblyQualifiedName</c>) in a separate "stable key" builder.
    /// </para>
    /// </summary>
    /// <param name="type">The type to add.</param>
    public void AddType(Type type) => m_hash.Add(type.TypeHandle);

    /// <summary>
    /// Builds a GPU variant key by allocating a new <see cref="GpuVariant"/>, invoking <paramref name="onDefine"/>
    /// to append all contributing inputs, then returning the final 32-bit hash.
    ///
    /// <para>
    /// The returned integer is intended to be used as a compact lookup key for in-memory GPU caches
    /// (e.g., dictionary keys). The key is order-sensitive and dependent on the exact sequence of <c>Add*</c>
    /// calls performed by <paramref name="onDefine"/>.
    /// </para>
    /// </summary>
    /// <param name="onDefine">Callback that appends all fields that define this GPU variant.</param>
    /// <returns>A 32-bit hash suitable for runtime GPU cache lookup.</returns>
    public static int Build(Action<GpuVariant> onDefine)
    {
        var v = new GpuVariant();
        onDefine(v);
        return v.m_hash.ToHashCode();
    }
}
