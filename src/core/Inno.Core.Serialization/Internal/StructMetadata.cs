using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Inno.Core.Serialization;

internal static class StructMetadata
{
    private const BindingFlags C_MEMBERS =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private static readonly ConditionalWeakTable<Type, MembersBox> S_CACHE = new();

    internal static StructMember[] GetMembers(Type structType)
    {
        return S_CACHE.GetValue(structType, static type => new MembersBox(BuildMembers(type))).members;
    }

    private static StructMember[] BuildMembers(Type structType)
    {
        var members = new List<StructMember>();
        var names = new HashSet<string>(StringComparer.Ordinal);
        MemberInfo[] candidates = structType
            .GetMembers(C_MEMBERS)
            .OrderBy(static member => member.MetadataToken)
            .ToArray();
        for (int i = 0; i < candidates.Length; i++)
        {
            StructMember? member = candidates[i] switch
            {
                FieldInfo field => TryCreateField(structType, field),
                PropertyInfo property => TryCreateProperty(structType, property),
                _ => null
            };
            if (member is null)
                continue;
            if (!names.Add(member.name))
            {
                throw new InvalidOperationException(
                    $"Struct '{structType.FullName}' declares duplicate serialized key '{member.name}'.");
            }
            members.Add(member);
        }

        if (members.Count == 0)
        {
            throw new InvalidOperationException(
                $"Struct '{structType.FullName}' has no public writable data members. Register a SerializationConverter<{structType.Name}>.");
        }

        return [.. members];
    }

    private static StructMember? TryCreateField(Type structType, FieldInfo field)
    {
        if (field.IsStatic)
            return null;

        SerializablePropertyAttribute? attribute =
            field.GetCustomAttribute<SerializablePropertyAttribute>(inherit: true);
        bool isDefaultMember = field.IsPublic && !field.IsInitOnly;
        if (attribute is null && !isDefaultMember)
            return null;

        PropertyVisibility visibility = attribute?.propertyVisibility ?? PropertyVisibility.Show;
        bool requiresWrite = (visibility & (PropertyVisibility.Deserialize | PropertyVisibility.RuntimeSet)) != 0;
        if (requiresWrite && field.IsInitOnly)
        {
            throw new InvalidOperationException(
                $"Struct field '{structType.FullName}.{field.Name}' must be writable for visibility '{visibility}'.");
        }

        return new StructMember(
            field.Name,
            field.FieldType,
            visibility,
            field.GetValue,
            field.IsInitOnly ? null : field.SetValue);
    }

    private static StructMember? TryCreateProperty(Type structType, PropertyInfo property)
    {
        if (property.GetIndexParameters().Length != 0)
            return null;

        SerializablePropertyAttribute? attribute =
            property.GetCustomAttribute<SerializablePropertyAttribute>(inherit: true);
        MethodInfo? getter = property.GetGetMethod(nonPublic: true);
        MethodInfo? setter = property.GetSetMethod(nonPublic: true);
        bool isDefaultMember = getter?.IsPublic == true && setter?.IsPublic == true;
        if (attribute is null && !isDefaultMember)
            return null;

        PropertyVisibility visibility = attribute?.propertyVisibility ?? PropertyVisibility.Show;
        bool requiresRead = (visibility & (PropertyVisibility.Serialize | PropertyVisibility.RuntimeGet)) != 0;
        bool requiresWrite = (visibility & (PropertyVisibility.Deserialize | PropertyVisibility.RuntimeSet)) != 0;
        if (requiresRead && getter is null)
        {
            throw new InvalidOperationException(
                $"Struct property '{structType.FullName}.{property.Name}' must define a getter for visibility '{visibility}'.");
        }
        if (requiresWrite && setter is null)
        {
            throw new InvalidOperationException(
                $"Struct property '{structType.FullName}.{property.Name}' must define a setter for visibility '{visibility}'.");
        }

        return new StructMember(
            property.Name,
            property.PropertyType,
            visibility,
            getter is null ? null : property.GetValue,
            setter is null ? null : property.SetValue);
    }

    private sealed record MembersBox(StructMember[] members);
}

internal sealed class StructMember
{
    private readonly Func<object, object?>? m_getter;
    private readonly Action<object, object?>? m_setter;

    internal StructMember(
        string name,
        Type type,
        PropertyVisibility visibility,
        Func<object, object?>? getter,
        Action<object, object?>? setter)
    {
        this.name = name;
        this.type = type;
        this.visibility = visibility;
        m_getter = getter;
        m_setter = setter;
    }

    internal string name { get; }

    internal Type type { get; }

    internal PropertyVisibility visibility { get; }

    internal object? GetValue(object target)
        => m_getter is not null
            ? m_getter(target)
            : throw new InvalidOperationException($"Struct member '{name}' does not permit reads.");

    internal void SetValue(object target, object? value)
    {
        if (m_setter is null)
            throw new InvalidOperationException($"Struct member '{name}' does not permit writes.");
        m_setter(target, value);
    }
}
