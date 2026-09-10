using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Inno.Editor.Inspection;

internal static class InspectorMemberMetadata
{
    private const BindingFlags C_DECLARED_MEMBERS = BindingFlags.Instance | BindingFlags.Public |
                                                    BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
    private static readonly ConditionalWeakTable<Type, TypeMembers> S_MEMBERS = new();
    private static readonly ConditionalWeakTable<MemberInfo, AttributesBox> S_ATTRIBUTES = new();

    internal static MemberInfo? Resolve(Type ownerType, string memberName)
    {
        ArgumentNullException.ThrowIfNull(ownerType);
        ArgumentException.ThrowIfNullOrWhiteSpace(memberName);
        return S_MEMBERS.GetValue(ownerType, static type => new TypeMembers(type)).members
            .GetValueOrDefault(memberName);
    }

    internal static Attribute[] GetAttributes(MemberInfo member)
    {
        ArgumentNullException.ThrowIfNull(member);
        return S_ATTRIBUTES.GetValue(member, static value => new AttributesBox(value)).attributes;
    }

    private static Attribute[] BuildAttributes(MemberInfo member)
        => member.GetCustomAttributes(inherit: true)
            .OfType<Attribute>()
            .Select(static (attribute, index) => new OrderedAttribute(
                attribute,
                attribute is Inno.Editor.Annotations.InspectorPresentationAttribute presentation
                    ? presentation.order
                    : 0,
                index))
            .OrderBy(static value => value.order)
            .ThenBy(static value => value.index)
            .Select(static value => value.attribute)
            .ToArray();

    private sealed class AttributesBox
    {
        internal AttributesBox(MemberInfo member)
            => attributes = BuildAttributes(member);

        internal Attribute[] attributes { get; }
    }

    private sealed class TypeMembers
    {
        internal TypeMembers(Type runtimeType)
        {
            var hierarchy = new Stack<Type>();
            for (Type? type = runtimeType; type is not null && type != typeof(object); type = type.BaseType)
                hierarchy.Push(type);
            var result = new Dictionary<string, MemberInfo>(StringComparer.Ordinal);
            while (hierarchy.Count > 0)
            {
                foreach (MemberInfo member in hierarchy.Pop().GetMembers(C_DECLARED_MEMBERS))
                {
                    if (member is FieldInfo or PropertyInfo)
                        result[member.Name] = member;
                }
            }
            members = result;
        }

        internal IReadOnlyDictionary<string, MemberInfo> members { get; }
    }

    private readonly record struct OrderedAttribute(Attribute attribute, int order, int index);
}
