using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Inno.Extensibility.Types;

namespace Inno.Editor.Panel.ShaderEditor;

internal sealed class ShaderNodeDrawerRegistry : TypeRegistry<ShaderNodeDrawerRegistry.Snapshot>
{
    private readonly TypeCatalog m_types;
    internal ShaderNodeDrawerRegistry(TypeCatalog types) : base(types) => m_types = types;
    internal float? ContentHeight(string definitionId)
    {
        using IDisposable operation = m_types.AcquireOperation("Measure shader node controls");
        if (!current.drawers.TryGetValue(definitionId, out ShaderNodeDrawer? drawer)) return null;
        float height = drawer.contentHeight;
        if (!float.IsFinite(height) || height < 0) throw new InvalidOperationException("Shader node content height must be finite and nonnegative.");
        return height;
    }
    internal bool TryDraw(string definitionId, ShaderNodeDrawContext context)
    {
        using IDisposable operation = m_types.AcquireOperation("Draw shader node controls");
        if (!current.drawers.TryGetValue(definitionId, out ShaderNodeDrawer? drawer)) return false;
        drawer.Draw(context);
        return true;
    }

    protected override Snapshot Build(TypeCacheSnapshot snapshot)
    {
        var result = new Dictionary<string, ShaderNodeDrawer>(StringComparer.Ordinal);
        try
        {
            foreach (Type type in snapshot.GetTypesWithAttribute<ShaderNodeDrawerAttribute>().Select(reference => reference.Resolve(snapshot)))
            {
                string id = type.GetCustomAttribute<ShaderNodeDrawerAttribute>()!.definitionId;
                if (result.ContainsKey(id)) throw new InvalidOperationException($"Shader node drawer '{id}' is registered twice.");
                result.Add(id, CreateExtension<ShaderNodeDrawer>(type));
            }
            return new(result);
        }
        catch (Exception failure)
        {
            try { DisposeExtensions(result.Values); }
            catch (Exception retirement) { throw new AggregateException(failure, retirement); }
            throw;
        }
    }
    protected override void DisposeSnapshot(Snapshot snapshot) => DisposeExtensions(snapshot.drawers.Values);
    internal sealed record Snapshot(IReadOnlyDictionary<string, ShaderNodeDrawer> drawers);
}
