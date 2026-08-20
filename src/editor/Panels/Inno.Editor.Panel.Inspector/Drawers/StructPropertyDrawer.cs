using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;

using Inno.Core.Serialization;
using Inno.Native.ImGui;
using NativeImGui = Inno.Native.ImGui.ImGui;

namespace Inno.Editor.Panel.Inspector.Drawers;

[PropertyDrawer(typeof(ValueType), useForChildren: true, priority: -100)]
internal sealed class StructPropertyDrawer : IPropertyDrawer
{
    private static readonly ConditionalWeakTable<Type, MembersBox> S_MEMBER_CACHE = new();

    /// <inheritdoc />
    public void Draw(PropertyDrawContext context)
    {
        object boxedValue = context.GetValue() ?? Activator.CreateInstance(context.propertyType)!;
        MemberInfo[] members = GetMembers(context.propertyType);
        if (members.Length == 0)
        {
            NativeImGui.TextUnformatted(boxedValue.ToString() ?? context.propertyType.Name);
            return;
        }

        if (!NativeImGui.TreeNodeEx($"{context.propertyType.Name}##{context.path}", ImGuiTreeNodeFlags.SpanAvailWidth))
        {
            return;
        }

        for (int i = 0; i < members.Length; i++)
        {
            MemberInfo member = members[i];
            Type memberType = GetMemberType(member);
            bool memberReadOnly = !CanWrite(member) ||
                (GetVisibility(member) & PropertyVisibility.RuntimeSet) == 0;
            context.DrawChild(
                member.Name,
                memberType,
                () => GetMemberValue(member, context.GetValue() ?? boxedValue),
                value =>
                {
                    object updated = context.GetValue() ?? Activator.CreateInstance(context.propertyType)!;
                    SetMemberValue(member, updated, value);
                    context.SetValue(updated);
                },
                memberReadOnly);
        }

        NativeImGui.TreePop();
    }

    private static MemberInfo[] GetMembers(Type type)
    {
        return S_MEMBER_CACHE.GetValue(type, static value => new MembersBox(
            value
                .GetMembers(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Where(IsRuntimeVisibleMember)
                .OrderBy(static member => member.MetadataToken)
                .ToArray())).members;
    }

    private static bool IsRuntimeVisibleMember(MemberInfo member)
    {
        SerializablePropertyAttribute? attribute =
            member.GetCustomAttribute<SerializablePropertyAttribute>(inherit: true);
        PropertyVisibility visibility = attribute?.propertyVisibility ?? PropertyVisibility.Show;
        if ((visibility & PropertyVisibility.RuntimeGet) == 0)
            return false;

        return member switch
        {
            FieldInfo field =>
                !field.IsStatic &&
                (attribute is not null || field.IsPublic && !field.IsInitOnly),
            PropertyInfo property =>
                property.GetIndexParameters().Length == 0 &&
                (attribute is not null ||
                 property.GetGetMethod(nonPublic: true)?.IsPublic == true &&
                 property.GetSetMethod(nonPublic: true)?.IsPublic == true),
            _ => false
        };
    }

    private static PropertyVisibility GetVisibility(MemberInfo member)
    {
        return member.GetCustomAttribute<SerializablePropertyAttribute>(true)?.propertyVisibility
            ?? PropertyVisibility.Show;
    }

    private static Type GetMemberType(MemberInfo member)
    {
        return member switch
        {
            FieldInfo field => field.FieldType,
            PropertyInfo property => property.PropertyType,
            _ => throw new InvalidOperationException($"Unsupported struct member '{member.MemberType}'.")
        };
    }

    private static object? GetMemberValue(MemberInfo member, object target)
    {
        return member switch
        {
            FieldInfo field => field.GetValue(target),
            PropertyInfo property => property.GetValue(target),
            _ => null
        };
    }

    private static bool CanWrite(MemberInfo member)
    {
        return member switch
        {
            FieldInfo field => !field.IsInitOnly,
            PropertyInfo property => property.GetSetMethod(true) is not null,
            _ => false
        };
    }

    private static void SetMemberValue(MemberInfo member, object target, object? value)
    {
        switch (member)
        {
            case FieldInfo field:
                field.SetValue(target, value);
                break;
            case PropertyInfo property:
                property.SetValue(target, value);
                break;
        }
    }

    private sealed record MembersBox(MemberInfo[] members);
}
