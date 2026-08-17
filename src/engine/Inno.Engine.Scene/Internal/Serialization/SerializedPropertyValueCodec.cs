using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

using Inno.Core.Serialization;

namespace Inno.Engine.Scene;

internal static class SerializedPropertyValueCodec
{
    private const BindingFlags C_DECLARED_MEMBERS =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
    private static readonly Dictionary<Type, PropertyMember[]> S_MEMBERS = [];
    private static readonly MethodInfo S_WRITE_VALUE = typeof(SerializedPropertyValueCodec)
        .GetMethod(nameof(WriteValue), BindingFlags.NonPublic | BindingFlags.Static)!;
    private static readonly MethodInfo S_READ_VALUE = typeof(SerializedPropertyValueCodec)
        .GetMethod(nameof(ReadValue), BindingFlags.NonPublic | BindingFlags.Static)!;
    private static readonly object S_SYNC = new();

    internal static IReadOnlyList<PropertyMember> GetMembers(Type componentType)
    {
        ArgumentNullException.ThrowIfNull(componentType);
        lock (S_SYNC)
        {
            if (!S_MEMBERS.TryGetValue(componentType, out PropertyMember[]? members))
            {
                members = BuildMembers(componentType);
                S_MEMBERS.Add(componentType, members);
            }
            return members;
        }
    }

    internal static byte[] Encode(
        PropertyMember member,
        GameComponent component,
        SerializationContext context,
        SceneGraphReferenceMap references)
    {
        ArgumentNullException.ThrowIfNull(member);
        ArgumentNullException.ThrowIfNull(component);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(references);
        object? value = member.GetValue(component);
        using (references.Enter())
        {
            return SerializationManager.Encode(
                writer => S_WRITE_VALUE.MakeGenericMethod(member.type).Invoke(null, [writer, value]),
                context);
        }
    }

    internal static void Decode(
        PropertyMember member,
        GameComponent component,
        ReadOnlySpan<byte> bytes,
        SerializationContext context,
        SceneGraphReferenceMap references)
    {
        ArgumentNullException.ThrowIfNull(member);
        ArgumentNullException.ThrowIfNull(component);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(references);
        byte[] stableBytes = bytes.ToArray();
        using (references.Enter())
        {
            object? value = SerializationManager.Decode(
                stableBytes,
                reader => S_READ_VALUE.MakeGenericMethod(member.type).Invoke(null, [reader]),
                context);
            member.SetValue(component, value);
        }
    }

    private static PropertyMember[] BuildMembers(Type componentType)
    {
        var hierarchy = new List<Type>();
        for (Type? current = componentType; current is not null && current != typeof(object); current = current.BaseType)
            hierarchy.Add(current);
        hierarchy.Reverse();

        var result = new List<PropertyMember>();
        var names = new HashSet<string>(StringComparer.Ordinal);
        for (int typeIndex = 0; typeIndex < hierarchy.Count; typeIndex++)
        {
            foreach (MemberInfo member in hierarchy[typeIndex]
                         .GetMembers(C_DECLARED_MEMBERS)
                         .OrderBy(static item => item.MetadataToken))
            {
                SerializablePropertyAttribute? attribute =
                    member.GetCustomAttribute<SerializablePropertyAttribute>(inherit: true);
                if (attribute is null ||
                    (attribute.propertyVisibility & (PropertyVisibility.Serialize | PropertyVisibility.Deserialize)) !=
                    (PropertyVisibility.Serialize | PropertyVisibility.Deserialize))
                {
                    continue;
                }

                PropertyMember propertyMember = member switch
                {
                    FieldInfo field when !field.IsInitOnly => new PropertyMember(
                        field.Name,
                        field.FieldType,
                        field.GetValue,
                        field.SetValue),
                    PropertyInfo property when
                        property.GetIndexParameters().Length == 0 &&
                        property.GetGetMethod(nonPublic: true) is not null &&
                        property.GetSetMethod(nonPublic: true) is not null => new PropertyMember(
                            property.Name,
                            property.PropertyType,
                            property.GetValue,
                            property.SetValue),
                    _ => throw new InvalidOperationException(
                        $"Serialized member '{componentType.FullName}.{member.Name}' cannot participate in prefab overrides because it is not readable and writable.")
                };
                if (!names.Add(propertyMember.name))
                {
                    throw new InvalidOperationException(
                        $"Serialized component type '{componentType.FullName}' contains duplicate property key '{propertyMember.name}'.");
                }
                result.Add(propertyMember);
            }
        }
        return [.. result];
    }

    private static void WriteValue<TValue>(SerializationWriter writer, object? value)
        => writer.Write("value", (TValue)value!);

    private static object? ReadValue<TValue>(SerializationReader reader)
        => reader.Read<TValue>("value");

    internal sealed class PropertyMember(
        string name,
        Type type,
        Func<object, object?> getter,
        Action<object, object?> setter)
    {
        private readonly Func<object, object?> m_getter = getter;
        private readonly Action<object, object?> m_setter = setter;

        internal string name { get; } = name;
        internal Type type { get; } = type;

        internal object? GetValue(object target) => m_getter(target);
        internal void SetValue(object target, object? value) => m_setter(target, value);
    }
}
