namespace Inno.References;

/// <summary>
/// Resolves one open reference kind against a complete immutable domain generation.
/// </summary>
public interface IReferenceResolver
{
    /// <summary>
    /// Gets the unique reference kind implemented by this resolver.
    /// </summary>
    ReferenceKindId kindId { get; }

    /// <summary>
    /// Resolves a descriptor without mutating its owner or the authoritative catalog.
    /// </summary>
    /// <param name="descriptor">
    /// The descriptor whose <see cref="ReferenceDescriptor.kindId"/> matches this resolver.
    /// </param>
    /// <returns>
    /// A complete resolution result that preserves the supplied descriptor.
    /// </returns>
    ReferenceResolution Resolve(ReferenceDescriptor descriptor);
}
