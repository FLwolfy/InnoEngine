using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Inno.Core.Serialization;

internal static class ReflectionMetadata
{
    private const BindingFlags C_DECLARED_MEMBERS =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

    private static readonly ConditionalWeakTable<Type, MembersBox> S_MEMBERS = new();
    private static readonly ConditionalWeakTable<Type, RestoreHooksBox> S_RESTORE_HOOKS = new();

    internal static SerializableMember[] GetSerializableMembers(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        return S_MEMBERS.GetValue(type, static value => new MembersBox(BuildSerializableMembers(value))).members;
    }

    internal static IReadOnlyList<SerializedProperty> GetRuntimeProperties(ISerializable value)
    {
        ArgumentNullException.ThrowIfNull(value);
        SerializableMember[] members = GetSerializableMembers(value.GetType());
        var properties = new List<SerializedProperty>(members.Length);
        for (int i = 0; i < members.Length; i++)
        {
            SerializableMember member = members[i];
            bool canRead = (member.visibility & PropertyVisibility.RuntimeGet) != 0;
            if (!canRead)
                continue;

            bool canWrite = (member.visibility & PropertyVisibility.RuntimeSet) != 0;
            properties.Add(new SerializedProperty(
                member.name,
                member.type,
                () => member.GetValue(value),
                propertyValue => member.SetValue(value, propertyValue),
                member.visibility,
                canRead,
                canWrite));
        }

        return properties;
    }

    internal static Action? CreateRestoreCallback(
        ISerializable value,
        SerializationContext context)
    {
        MethodInfo[] hooks = GetRestoreHooks(value.GetType());
        if (hooks.Length == 0)
            return null;

        return () =>
        {
            for (int i = 0; i < hooks.Length; i++)
            {
                object?[]? arguments = hooks[i].GetParameters().Length == 0
                    ? null
                    : [context];
                hooks[i].Invoke(value, arguments);
            }
        };
    }

    private static SerializableMember[] BuildSerializableMembers(Type runtimeType)
    {
        var hierarchy = new List<Type>(8);
        for (Type? current = runtimeType; current is not null && current != typeof(object); current = current.BaseType)
            hierarchy.Add(current);
        hierarchy.Reverse();

        var members = new List<SerializableMember>(32);
        var names = new HashSet<string>(StringComparer.Ordinal);
        for (int depth = 0; depth < hierarchy.Count; depth++)
        {
            Type declaringType = hierarchy[depth];
            MemberInfo[] declaredMembers = declaringType
                .GetMembers(C_DECLARED_MEMBERS)
                .OrderBy(GetSerializableOrder)
                .ThenBy(static member => member.MetadataToken)
                .ToArray();
            for (int i = 0; i < declaredMembers.Length; i++)
            {
                MemberInfo declaredMember = declaredMembers[i];
                SerializablePropertyAttribute? attribute =
                    declaredMember.GetCustomAttribute<SerializablePropertyAttribute>(inherit: true);
                if (attribute is null)
                    continue;

                SerializableMember member = declaredMember switch
                {
                    FieldInfo field => CreateFieldMember(runtimeType, field, attribute.propertyVisibility),
                    PropertyInfo property => CreatePropertyMember(runtimeType, property, attribute.propertyVisibility),
                    _ => throw new InvalidOperationException(
                        $"Serializable member '{runtimeType.FullName}.{declaredMember.Name}' must be a field or property.")
                };
                if (!names.Add(member.name))
                {
                    throw new InvalidOperationException(
                        $"Serializable type '{runtimeType.FullName}' declares duplicate serialized key '{member.name}' in its inheritance hierarchy.");
                }

                members.Add(member);
            }
        }

        return [.. members];
    }

    private static int GetSerializableOrder(MemberInfo member)
    {
        return member.GetCustomAttribute<SerializablePropertyAttribute>(inherit: true)?.order
            ?? int.MaxValue;
    }

    private static SerializableMember CreateFieldMember(
        Type runtimeType,
        FieldInfo field,
        PropertyVisibility visibility)
    {
        bool requiresRead = RequiresRead(visibility);
        bool requiresWrite = RequiresWrite(visibility);
        if (requiresWrite && field.IsInitOnly)
        {
            throw new InvalidOperationException(
                $"Serializable field '{runtimeType.FullName}.{field.Name}' must be writable for visibility '{visibility}'.");
        }

        return new SerializableMember(
            field.Name,
            field.FieldType,
            visibility,
            requiresRead ? field.GetValue : null,
            requiresWrite ? field.SetValue : null);
    }

    private static SerializableMember CreatePropertyMember(
        Type runtimeType,
        PropertyInfo property,
        PropertyVisibility visibility)
    {
        if (property.GetIndexParameters().Length != 0)
            throw new InvalidOperationException($"Serializable property '{runtimeType.FullName}.{property.Name}' cannot be an indexer.");

        bool requiresRead = RequiresRead(visibility);
        bool requiresWrite = RequiresWrite(visibility);
        MethodInfo? getter = property.GetGetMethod(nonPublic: true);
        MethodInfo? setter = property.GetSetMethod(nonPublic: true);
        if (requiresRead && getter is null)
        {
            throw new InvalidOperationException(
                $"Serializable property '{runtimeType.FullName}.{property.Name}' must define a getter for visibility '{visibility}'.");
        }
        if (requiresWrite && setter is null)
        {
            throw new InvalidOperationException(
                $"Serializable property '{runtimeType.FullName}.{property.Name}' must define a setter for visibility '{visibility}'.");
        }

        return new SerializableMember(
            property.Name,
            property.PropertyType,
            visibility,
            getter is null ? null : property.GetValue,
            setter is null ? null : property.SetValue);
    }

    private static MethodInfo[] GetRestoreHooks(Type runtimeType)
    {
        return S_RESTORE_HOOKS.GetValue(
            runtimeType,
            static value => new RestoreHooksBox(BuildRestoreHooks(value))).hooks;
    }

    private static MethodInfo[] BuildRestoreHooks(Type runtimeType)
    {
        var hierarchy = new List<Type>(8);
        for (Type? current = runtimeType; current is not null && current != typeof(object); current = current.BaseType)
            hierarchy.Add(current);
        hierarchy.Reverse();

        var hooks = new List<MethodInfo>(hierarchy.Count);
        for (int i = 0; i < hierarchy.Count; i++)
        {
            MethodInfo[] declaredHooks = hierarchy[i]
                .GetMethods(C_DECLARED_MEMBERS)
                .Where(static method => method.IsDefined(typeof(OnSerializableRestored), inherit: false))
                .ToArray();
            if (declaredHooks.Length > 1)
            {
                throw new InvalidOperationException(
                    $"Serializable type '{hierarchy[i].FullName}' declares more than one OnSerializableRestored method.");
            }
            if (declaredHooks.Length == 0)
                continue;

            MethodInfo hook = declaredHooks[0];
            ParameterInfo[] parameters = hook.GetParameters();
            bool validParameters = parameters.Length == 0 ||
                parameters.Length == 1 && parameters[0].ParameterType == typeof(SerializationContext);
            if (hook.IsStatic || hook.IsVirtual || hook.ReturnType != typeof(void) || !validParameters)
            {
                throw new InvalidOperationException(
                    $"Restore callback '{hierarchy[i].FullName}.{hook.Name}' must be a non-static, non-virtual void method " +
                    "with either no parameters or one SerializationContext parameter.");
            }

            hooks.Add(hook);
        }

        return [.. hooks];
    }

    private static bool RequiresRead(PropertyVisibility visibility)
        => (visibility & (PropertyVisibility.Serialize | PropertyVisibility.RuntimeGet)) != 0;

    private static bool RequiresWrite(PropertyVisibility visibility)
        => (visibility & (PropertyVisibility.Deserialize | PropertyVisibility.RuntimeSet)) != 0;

    private sealed record MembersBox(SerializableMember[] members);
    private sealed record RestoreHooksBox(MethodInfo[] hooks);
}

internal sealed class SerializableMember
{
    private readonly Func<object, object?>? m_getter;
    private readonly Action<object, object?>? m_setter;

    internal SerializableMember(
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
            : throw new InvalidOperationException($"Serializable member '{name}' does not permit reads.");

    internal void SetValue(object target, object? value)
    {
        if (m_setter is null)
            throw new InvalidOperationException($"Serializable member '{name}' does not permit writes.");
        m_setter(target, value);
    }
}
