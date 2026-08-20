using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

using Inno.Core.Reflection;

namespace Inno.Editor.Panel.FileBrowser.AssetEditors;

internal sealed class AssetEditorRegistry : TypeRegistry<AssetEditorRegistry.Snapshot>
{
    private static readonly AssetEditor S_DEFAULT_EDITOR = new DefaultAssetEditor();

    internal AssetEditor Resolve(Type? assetType)
    {
        if (assetType is null)
            return S_DEFAULT_EDITOR;
        Registration? best = null;
        int bestDistance = int.MaxValue;
        foreach (Registration registration in current.registrations)
        {
            if (!AssetTypeDistance.TryGet(assetType, registration.assetType, out int distance))
                continue;
            if (distance > 0 && !registration.useForChildren)
                continue;
            if (best is null || distance < bestDistance ||
                distance == bestDistance && registration.priority > best.priority)
            {
                best = registration;
                bestDistance = distance;
            }
        }
        return best?.editor ?? S_DEFAULT_EDITOR;
    }

    protected override Snapshot Build(TypeCacheSnapshot types)
    {
        var instances = new Dictionary<Type, AssetEditor>();
        var registrations = new List<Registration>();
        foreach (Type type in types.GetTypesWithAttribute<AssetEditorAttribute>()
                     .OrderBy(static value => value.FullName, StringComparer.Ordinal))
        {
            AssetEditor editor = instances.TryGetValue(type, out AssetEditor? existing)
                ? existing
                : CreateExtension<AssetEditor>(type);
            instances[type] = editor;
            foreach (AssetEditorAttribute attribute in type.GetCustomAttributes<AssetEditorAttribute>(false))
            {
                if (registrations.Any(value =>
                        value.assetType == attribute.assetType &&
                        value.useForChildren == attribute.useForChildren &&
                        value.priority == attribute.priority))
                {
                    throw new InvalidOperationException(
                        $"Asset editor registration for '{attribute.assetType.FullName}' conflicts at " +
                        $"priority {attribute.priority}.");
                }
                registrations.Add(new Registration(
                    attribute.assetType,
                    attribute.useForChildren,
                    attribute.priority,
                    type,
                    editor));
            }
        }
        return new Snapshot(registrations.ToArray());
    }

    protected override void DisposeSnapshot(Snapshot snapshot)
    {
        foreach (AssetEditor editor in snapshot.registrations
                     .Select(static value => value.editor)
                     .Distinct<AssetEditor>(ReferenceEqualityComparer.Instance))
        {
            if (editor is IDisposable disposable)
                disposable.Dispose();
        }
    }

    internal sealed record Snapshot(Registration[] registrations);

    internal sealed record Registration(
        Type assetType,
        bool useForChildren,
        int priority,
        Type implementationType,
        AssetEditor editor);

    private sealed class DefaultAssetEditor : AssetEditor;
}
