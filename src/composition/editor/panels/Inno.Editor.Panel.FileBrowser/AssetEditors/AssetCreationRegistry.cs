using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Inno.Assets;
using Inno.Editor.Assets;
using Inno.Extensibility.Types;

namespace Inno.Editor.Panel.FileBrowser;

internal sealed class AssetCreationRegistry : TypeRegistry<AssetCreationRegistry.Snapshot>
{
    internal AssetCreationRegistry(TypeCatalog types)
        : base(types)
    {
    }

    internal IReadOnlyList<Registration> templates => current.registrations;

    internal bool TryGet(string id, out Registration? registration)
        => current.byId.TryGetValue(id, out registration);

    protected override Snapshot Build(TypeCacheSnapshot types)
    {
        var registrations = new List<Registration>();
        var byId = new Dictionary<string, Registration>(StringComparer.Ordinal);
        var byMenuPath = new Dictionary<string, Registration>(StringComparer.OrdinalIgnoreCase);

        foreach (Type type in types.GetSubTypesOf<AssetCreationTemplate>()
                     .Select(typeRef => typeRef.Resolve(types))
                     .Where(static type => !type.IsAbstract)
                     .OrderBy(static type => type.FullName, StringComparer.Ordinal))
        {
            AssetCreationMenuAttribute declaration = type.GetCustomAttribute<AssetCreationMenuAttribute>(false)
                ?? throw new InvalidOperationException(
                    $"Asset creation template '{type.FullName}' requires " +
                    $"'{typeof(AssetCreationMenuAttribute).FullName}' source metadata.");
            AssetCreationTemplate template = CreateExtension<AssetCreationTemplate>(type);
            if (!typeof(AssetObject).IsAssignableFrom(template.assetType)
                || template.assetType.IsAbstract)
            {
                throw new InvalidOperationException(
                    $"Asset creation template '{type.FullName}' declares invalid asset type " +
                    $"'{template.assetType.FullName}'.");
            }

            var registration = new Registration(type, template, declaration);
            if (!byId.TryAdd(declaration.id, registration))
            {
                throw new InvalidOperationException(
                    $"Asset creation template id '{declaration.id}' is registered by both " +
                    $"'{byId[declaration.id].implementationType.FullName}' and '{type.FullName}'.");
            }
            if (!byMenuPath.TryAdd(declaration.menuPath, registration))
            {
                throw new InvalidOperationException(
                    $"Asset creation menu path '{declaration.menuPath}' is registered by both " +
                    $"'{byMenuPath[declaration.menuPath].implementationType.FullName}' and '{type.FullName}'.");
            }
            registrations.Add(registration);
        }

        Registration[] ordered = registrations
            .OrderBy(static value => value.declaration.groupOrder)
            .ThenBy(static value => value.declaration.menuPath, StringComparer.Ordinal)
            .ThenBy(static value => value.declaration.itemOrder)
            .ThenBy(static value => value.declaration.id, StringComparer.Ordinal)
            .ToArray();
        return new Snapshot(ordered, byId);
    }

    protected override void DisposeSnapshot(Snapshot snapshot)
        => DisposeExtensions(snapshot.registrations.Select(static value => value.template));

    internal sealed record Registration(
        Type implementationType,
        AssetCreationTemplate template,
        AssetCreationMenuAttribute declaration);

    internal sealed record Snapshot(
        Registration[] registrations,
        IReadOnlyDictionary<string, Registration> byId);
}
