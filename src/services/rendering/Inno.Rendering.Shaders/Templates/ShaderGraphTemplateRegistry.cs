using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
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
            return Array.AsReadOnly(m_registry.snapshot.Values.Select(static value => value.info)
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
        if (!m_registry.snapshot.TryGetValue(id, out Entry? entry))
            throw new InvalidOperationException($"Shader template '{id}' is unavailable.");
        return entry.template.Create(serialization, context).Clone();
    }

    /// <summary>Retires providers through the shared generation lifecycle.</summary>
    public void Dispose() => m_registry.Dispose();

    private sealed record Entry(ShaderGraphTemplateInfo info, ShaderGraphTemplate template);

    private sealed class Registry(TypeCatalog types) : TypeRegistry<IReadOnlyDictionary<string, Entry>>(types)
    {
        internal IReadOnlyDictionary<string, Entry> snapshot => current;
        protected override IReadOnlyDictionary<string, Entry> Build(TypeCacheSnapshot types)
        {
            (Type type, ShaderGraphTemplateAttribute metadata)[] registrations = types
                .GetTypesWithAttribute<ShaderGraphTemplateAttribute>()
                .Select(value => value.Resolve(types))
                .OrderBy(static value => value.FullName, StringComparer.Ordinal)
                .Select(static type =>
                {
                    if (type.IsAbstract || !typeof(ShaderGraphTemplate).IsAssignableFrom(type))
                    {
                        throw new InvalidOperationException(
                            $"Shader graph template '{type.FullName}' must be a concrete {nameof(ShaderGraphTemplate)}.");
                    }
                    return (type, type.GetCustomAttribute<ShaderGraphTemplateAttribute>(inherit: false)!);
                })
                .ToArray();
            string? duplicateId = registrations
                .GroupBy(static registration => registration.metadata.id, StringComparer.Ordinal)
                .FirstOrDefault(static group => group.Count() > 1)?.Key;
            if (duplicateId is not null)
                throw new InvalidOperationException($"Duplicate shader template '{duplicateId}'.");

            var templates = new Dictionary<string, Entry>(StringComparer.Ordinal);
            foreach ((Type type, ShaderGraphTemplateAttribute metadata) in registrations)
            {
                ShaderGraphTemplate template = CreateExtension<ShaderGraphTemplate>(type);
                var entry = new Entry(new ShaderGraphTemplateInfo(metadata.id, metadata.displayName), template);
                templates.Add(metadata.id, entry);
            }
            return templates;
        }
        protected override void DisposeSnapshot(IReadOnlyDictionary<string, Entry> snapshot)
            => DisposeExtensions(snapshot.Values.Select(static value => value.template));
    }
}
