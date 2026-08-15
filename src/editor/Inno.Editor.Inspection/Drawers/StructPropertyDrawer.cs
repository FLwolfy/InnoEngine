using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

using Inno.Core.Serialization;
using Inno.Native.ImGui;
using NativeImGui = Inno.Native.ImGui.ImGui;

namespace Inno.Editor.Inspection.Drawers;

[PropertyDrawer(typeof(ValueType), useForChildren: true, priority: -100)]
internal sealed class StructPropertyDrawer : IPropertyDrawer
{
    private static readonly Dictionary<Type, MemberInfo[]> s_memberCache = [];
    private static readonly object C_SYNC = new();

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
        lock (C_SYNC)
        {
            if (s_memberCache.TryGetValue(type, out MemberInfo[]? cached))
            {
                return cached;
            }

            IEnumerable<MemberInfo> fields = StructSerializer.GetStructSerializableFields(type);
            IEnumerable<MemberInfo> properties = StructSerializer.GetStructSerializableProperties(type);
            MemberInfo[] members = fields.Concat(properties)
                .OrderBy(static member => member.MetadataToken)
                .ToArray();
            s_memberCache[type] = members;
            return members;
        }
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
}
