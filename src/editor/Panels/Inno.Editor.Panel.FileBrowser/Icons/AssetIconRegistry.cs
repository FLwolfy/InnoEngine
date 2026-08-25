using System;
using System.Collections.Generic;
using System.Linq;

using Inno.Assets.Core;
using Inno.Core.Reflection;
using Inno.Editor.Settings;

namespace Inno.Editor.Panel.FileBrowser;

internal sealed class AssetIconRegistry : TypeRegistry<AssetIconRegistry.Snapshot>
{
    private readonly EditorSettings m_settings;

    internal AssetIconRegistry(EditorSettings settings)
    {
        m_settings = settings ?? throw new ArgumentNullException(nameof(settings));
    }

    internal bool TryResolve(Type? assetType, string relativePath, out string icon)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        if (TryResolveType(assetType, out icon) || TryResolveExtension(relativePath, out icon))
        {
            try
            {
                icon = m_settings.Get(icon).GetAsString("value", icon) ?? icon;
            }
            catch (ArgumentException)
            {
                // Extension-defined attributes may still contain a literal glyph.
            }
            return true;
        }
        return false;
    }

    protected override Snapshot Build(TypeCacheSnapshot types)
    {
        var typeRegistrations = new List<TypeRegistration>();
        var extensionRegistrations = new List<ExtensionRegistration>();
        foreach (Type declarationType in types.types
                     .OrderBy(static type => type.Assembly.GetName().Name, StringComparer.Ordinal)
                     .ThenBy(static type => type.FullName, StringComparer.Ordinal))
        {
            foreach (AssetIconAttribute attribute in declarationType
                         .GetCustomAttributes(typeof(AssetIconAttribute), inherit: false)
                         .Cast<AssetIconAttribute>()
                         .OrderBy(static value => value.assetType?.FullName ?? value.extension, StringComparer.Ordinal)
                         .ThenBy(static value => value.priority))
            {
                if (attribute.assetType is Type assetType)
                {
                    ValidateType(attribute, declarationType, assetType, typeRegistrations);
                    typeRegistrations.Add(new TypeRegistration(
                        assetType,
                        attribute.useForChildren,
                        attribute.priority,
                        declarationType,
                        attribute.icon));
                }
                else if (attribute.extension is string extension)
                {
                    ValidateExtension(attribute, declarationType, extension, extensionRegistrations);
                    extensionRegistrations.Add(new ExtensionRegistration(
                        extension,
                        attribute.priority,
                        declarationType,
                        attribute.icon));
                }
            }
        }

        return new Snapshot(typeRegistrations.ToArray(), extensionRegistrations.ToArray());
    }

    private bool TryResolveType(Type? assetType, out string icon)
    {
        TypeRegistration? best = null;
        int bestDistance = int.MaxValue;
        if (assetType is not null)
        {
            foreach (TypeRegistration registration in current.typeRegistrations)
            {
                if (!AssetTypeDistance.TryGet(assetType, registration.assetType, out int distance) ||
                    distance > 0 && !registration.useForChildren)
                {
                    continue;
                }

                if (best is null ||
                    distance < bestDistance ||
                    distance == bestDistance && registration.priority > best.priority)
                {
                    best = registration;
                    bestDistance = distance;
                }
            }
        }

        icon = best?.icon ?? string.Empty;
        return best is not null;
    }

    private bool TryResolveExtension(string relativePath, out string icon)
    {
        ExtensionRegistration? best = null;
        foreach (ExtensionRegistration registration in current.extensionRegistrations)
        {
            if (!relativePath.EndsWith(registration.extension, StringComparison.OrdinalIgnoreCase))
                continue;
            if (best is null ||
                registration.extension.Length > best.extension.Length ||
                registration.extension.Length == best.extension.Length && registration.priority > best.priority)
            {
                best = registration;
            }
        }

        icon = best?.icon ?? string.Empty;
        return best is not null;
    }

    private static void ValidateType(
        AssetIconAttribute attribute,
        Type declarationType,
        Type assetType,
        IReadOnlyList<TypeRegistration> registrations)
    {
        if (!typeof(AssetObject).IsAssignableFrom(assetType))
        {
            throw new InvalidOperationException(
                $"Asset icon declaration on '{declarationType.FullName}' targets " +
                $"'{assetType.FullName}', " +
                $"which does not derive from '{typeof(AssetObject).FullName}'.");
        }

        if (registrations.Any(value =>
                value.assetType == assetType &&
                value.useForChildren == attribute.useForChildren &&
                value.priority == attribute.priority))
        {
            throw new InvalidOperationException(
                $"Asset icon registration for '{assetType.FullName}' conflicts at priority " +
                $"{attribute.priority}.");
        }
    }

    private static void ValidateExtension(
        AssetIconAttribute attribute,
        Type declarationType,
        string extension,
        IReadOnlyList<ExtensionRegistration> registrations)
    {
        if (registrations.Any(value =>
                string.Equals(value.extension, extension, StringComparison.OrdinalIgnoreCase) &&
                value.priority == attribute.priority))
        {
            throw new InvalidOperationException(
                $"Asset icon declaration on '{declarationType.FullName}' for extension '{extension}' " +
                $"conflicts at priority {attribute.priority}.");
        }
    }

    internal sealed record Snapshot(
        TypeRegistration[] typeRegistrations,
        ExtensionRegistration[] extensionRegistrations);

    internal sealed record TypeRegistration(
        Type assetType,
        bool useForChildren,
        int priority,
        Type declarationType,
        string icon);

    internal sealed record ExtensionRegistration(
        string extension,
        int priority,
        Type declarationType,
        string icon);
}
