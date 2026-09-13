using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Inno.Extensibility.Types;

namespace Inno.Editor.Shaders;

/// <summary>Shares generation-scoped node presentation between graph and Inspector hosts.</summary>
public sealed class ShaderNodeDrawerRegistry : IDisposable
{
    private readonly TypeCatalog m_types;
    private readonly Registry m_registry;

    /// <summary>Creates a presentation registry owned by the current editor feature lifetime.</summary>
    /// <param name="types">Shared type catalog, which must outlive this registry.</param>
    public ShaderNodeDrawerRegistry(TypeCatalog types)
    { m_types = types ?? throw new ArgumentNullException(nameof(types)); m_registry = new(types); }

    /// <summary>Invokes optional node controls within one generation lease.</summary>
    /// <param name="definitionId">Stable node definition identity.</param>
    /// <param name="context">Invocation-scoped host values, history callback and previews.</param>
    /// <returns>True when a registered drawer rendered the controls; false leaves presentation to the host.</returns>
    public bool TryDraw(string definitionId, ShaderNodeDrawContext context)
    {
        using IDisposable operation = m_types.AcquireOperation("Draw shader node controls");
        if (!m_registry.snapshot.drawers.TryGetValue(definitionId, out ShaderNodeDrawer? drawer)) return false;
        drawer.Draw(context);
        return true;
    }

    /// <summary>Resolves an optional artist-facing name without executing a drawer or retaining its generation.</summary>
    /// <param name="definitionId">Stable compiler-independent node identity.</param>
    /// <returns>The contributed name, or null when the host should use its generic presentation.</returns>
    public string? GetDisplayName(string definitionId)
    {
        using IDisposable operation = m_types.AcquireOperation("Resolve shader node presentation");
        return m_registry.snapshot.names.TryGetValue(definitionId, out string? name) && name.Length != 0 ? name : null;
    }

    /// <summary>Retires the current presentation providers through the shared lifecycle.</summary>
    public void Dispose() => m_registry.Dispose();

    private sealed class Registry(TypeCatalog types) : TypeRegistry<Snapshot>(types)
    {
    internal Snapshot snapshot => current;
    protected override Snapshot Build(TypeCacheSnapshot snapshot)
    {
        var result = new Dictionary<string, ShaderNodeDrawer>(StringComparer.Ordinal);
        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        try
        {
            foreach (Type type in snapshot.GetTypesWithAttribute<ShaderNodeDrawerAttribute>().Select(reference => reference.Resolve(snapshot)))
            {
                ShaderNodeDrawerAttribute attribute = type.GetCustomAttribute<ShaderNodeDrawerAttribute>()!;
                string id = attribute.definitionId;
                if (result.ContainsKey(id)) throw new InvalidOperationException($"Shader node drawer '{id}' is registered twice.");
                result.Add(id, CreateExtension<ShaderNodeDrawer>(type));
                names.Add(id, attribute.displayName);
            }
            return new(result, names);
        }
        catch (Exception failure)
        {
            try { DisposeExtensions(result.Values); }
            catch (Exception retirement) { throw new AggregateException(failure, retirement); }
            throw;
        }
    }
    protected override void DisposeSnapshot(Snapshot snapshot) => DisposeExtensions(snapshot.drawers.Values);
    }
    private sealed record Snapshot(IReadOnlyDictionary<string, ShaderNodeDrawer> drawers, IReadOnlyDictionary<string, string> names);
}
