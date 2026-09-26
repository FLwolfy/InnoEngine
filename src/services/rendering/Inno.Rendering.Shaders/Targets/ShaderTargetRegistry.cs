using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using Inno.Core.Graphs;
using Inno.Core.Serialization;
using Inno.Extensibility.Types;

namespace Inno.Rendering.Shaders;

/// <summary>
/// Resolves domain targets under the same generation lease as their graph expansion.
/// </summary>
public sealed class ShaderTargetRegistry : IDisposable
{
    private readonly TypeCatalog m_types;
    private readonly Registry m_registry;

    /// <summary>
    /// Creates a target owner participating in shared candidate publication and retirement.
    /// </summary>
    /// <param name="types">
    /// The authoring catalog, which must outlive this registry.
    /// </param>
    public ShaderTargetRegistry(TypeCatalog types)
    { m_types = types ?? throw new ArgumentNullException(nameof(types)); m_registry = new(types); }

    /// <summary>
    /// Gets detached stable target identities available in the current generation.
    /// </summary>
    public IReadOnlyList<string> ids
    {
        get
        {
            using IDisposable operation = m_types.AcquireOperation("Query shader targets");
            return Array.AsReadOnly(m_registry.snapshot.Keys.Order(StringComparer.Ordinal).ToArray());
        }
    }

    /// <summary>
    /// Expands an assigned target, or copies an explicitly authored low-level graph with no domain target.
    /// </summary>
    /// <param name="document">
    /// Authored source, never modified by this operation.
    /// </param>
    /// <param name="serialization">
    /// Current owner converters.
    /// </param>
    /// <param name="context">
    /// Complete owner reference context.
    /// </param>
    /// <param name="cancellationToken">
    /// Cancellation before and during extension invocation.
    /// </param>
    /// <returns>
    /// Detached explicit stage graph consumed by the existing compiler.
    /// </returns>
    /// <exception cref="ShaderTargetUnavailableException">
    /// The assigned target is absent from this generation.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The target returns an invalid result.
    /// </exception>
    public GraphDocument Expand(GraphDocument document, SerializationRegistry serialization, SerializationContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(serialization);
        ArgumentNullException.ThrowIfNull(context);
        using IDisposable operation = m_types.AcquireOperation("Expand shader target");
        cancellationToken.ThrowIfCancellationRequested();
        string id = ShaderGraphDocument.ReadTarget(document, serialization, context);
        if (id.Length == 0) return document.Clone();
        if (!m_registry.snapshot.TryGetValue(id, out ShaderTarget? target))
            throw new ShaderTargetUnavailableException(id);
        GraphDocument expanded = target.Expand(new(document.Clone(), serialization, context), cancellationToken)
            ?? throw new InvalidOperationException($"Shader target '{id}' returned no program.");
        cancellationToken.ThrowIfCancellationRequested();
        return expanded.Clone();
    }

    /// <summary>
    /// Retires the target snapshot through the shared registry lifecycle.
    /// </summary>
    public void Dispose() => m_registry.Dispose();

    private sealed class Registry(TypeCatalog types) : TypeRegistry<IReadOnlyDictionary<string, ShaderTarget>>(types)
    {
        internal IReadOnlyDictionary<string, ShaderTarget> snapshot => current;

        /// <summary>
        /// Builds a validated result from the current immutable input snapshot.
        /// </summary>
        /// <param name="types">
        /// The active type catalog generation used for extension resolution.
        /// </param>
        /// <returns>
        /// An immutable snapshot of the values selected by the operation.
        /// </returns>
protected override IReadOnlyDictionary<string, ShaderTarget> Build(TypeCacheSnapshot types)
        {
            (Type type, string id)[] registrations = types.GetTypesWithAttribute<ShaderTargetAttribute>()
                .Select(value => value.Resolve(types))
                .OrderBy(static type => type.FullName, StringComparer.Ordinal)
                .Select(static type =>
                {
                    if (type.IsAbstract || !typeof(ShaderTarget).IsAssignableFrom(type))
                    {
                        throw new InvalidOperationException(
                            $"Shader target '{type.FullName}' must be a concrete {nameof(ShaderTarget)}.");
                    }
                    return (type, type.GetCustomAttribute<ShaderTargetAttribute>(inherit: false)!.id);
                })
                .ToArray();
            string? duplicateId = registrations
                .GroupBy(static registration => registration.id, StringComparer.Ordinal)
                .FirstOrDefault(static group => group.Count() > 1)?.Key;
            if (duplicateId is not null)
                throw new InvalidOperationException($"Duplicate shader target '{duplicateId}'.");

            var targets = new Dictionary<string, ShaderTarget>(StringComparer.Ordinal);
            foreach ((Type type, string id) in registrations)
            {
                ShaderTarget target = CreateExtension<ShaderTarget>(type);
                targets.Add(id, target);
            }
            return targets;
        }

        /// <summary>
        /// Releases the generation lease retained by an immutable registry snapshot.
        /// </summary>
        /// <param name="snapshot">
        /// The immutable state snapshot consumed by this operation.
        /// </param>
protected override void DisposeSnapshot(IReadOnlyDictionary<string, ShaderTarget> snapshot)
            => DisposeExtensions(snapshot.Values);
    }
}
