using System;
using System.Collections.Generic;
using System.Linq;
using Inno.Core.Graphs;
using Inno.Core.Serialization;
using Inno.Extensibility.Types;

namespace Inno.Rendering.Shaders;

/// <summary>Describes a creation menu item without retaining a plugin instance.</summary>
/// <param name="id">Stable template identity.</param>
/// <param name="displayName">User-facing label.</param>
public sealed record ShaderGraphTemplateInfo(string id, string displayName);

/// <summary>Owns generation-safe template discovery and invocation for editor asset creation.</summary>
public sealed class ShaderGraphTemplateRegistry : IDisposable
{
    private readonly TypeCatalog m_types;
    private readonly Registry m_registry;

    /// <summary>Registers a template owner with the shared type-generation catalog.</summary>
    /// <param name="types">Catalog which must outlive this owner.</param>
    public ShaderGraphTemplateRegistry(TypeCatalog types)
    { m_types = types ?? throw new ArgumentNullException(nameof(types)); m_registry = new(types); }

    /// <summary>Gets detached menu descriptions for the current generation.</summary>
    public IReadOnlyList<ShaderGraphTemplateInfo> templates
    {
        get
        {
            using IDisposable operation = m_types.AcquireOperation("Describe shader templates");
            return Array.AsReadOnly(m_registry.snapshot.Values.Select(static value => new ShaderGraphTemplateInfo(value.id, value.displayName))
                .OrderBy(static value => value.displayName, StringComparer.Ordinal).ThenBy(static value => value.id, StringComparer.Ordinal).ToArray());
        }
    }

    /// <summary>Invokes the selected template under one generation lease.</summary>
    /// <param name="id">Stable template identity from the creation menu.</param>
    /// <param name="serialization">Current owner converters.</param>
    /// <param name="context">Complete owner reference context.</param>
    /// <returns>A detached graph; no provider instance crosses the invocation boundary.</returns>
    /// <exception cref="InvalidOperationException">The selected template is unavailable.</exception>
    public GraphDocument Create(string id, SerializationRegistry serialization, SerializationContext context)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        using IDisposable operation = m_types.AcquireOperation("Create shader from template");
        if (!m_registry.snapshot.TryGetValue(id, out ShaderGraphTemplate? template))
            throw new InvalidOperationException($"Shader template '{id}' is unavailable.");
        return template.Create(serialization, context).Clone();
    }

    /// <summary>Retires providers through the shared generation lifecycle.</summary>
    public void Dispose() => m_registry.Dispose();

    private sealed class Registry(TypeCatalog types) : TypeRegistry<IReadOnlyDictionary<string, ShaderGraphTemplate>>(types)
    {
        internal IReadOnlyDictionary<string, ShaderGraphTemplate> snapshot => current;
        protected override IReadOnlyDictionary<string, ShaderGraphTemplate> Build(TypeCacheSnapshot types)
        {
            var templates = new Dictionary<string, ShaderGraphTemplate>(StringComparer.Ordinal);
            foreach (Type type in types.GetTypesWithAttribute<ShaderGraphTemplateAttribute>().Select(value => value.Resolve(types))
                         .OrderBy(static value => value.FullName, StringComparer.Ordinal))
            {
                ShaderGraphTemplate template = CreateExtension<ShaderGraphTemplate>(type);
                ArgumentException.ThrowIfNullOrWhiteSpace(template.id);
                ArgumentException.ThrowIfNullOrWhiteSpace(template.displayName);
                if (!templates.TryAdd(template.id, template)) throw new InvalidOperationException($"Duplicate shader template '{template.id}'.");
            }
            return templates;
        }
        protected override void DisposeSnapshot(IReadOnlyDictionary<string, ShaderGraphTemplate> snapshot)
            => DisposeExtensions(snapshot.Values);
    }
}
