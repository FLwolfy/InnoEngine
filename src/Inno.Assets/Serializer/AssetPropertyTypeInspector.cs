using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

using Inno.Assets.AssetType;

using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.TypeInspectors;

namespace Inno.Assets.Serializer;

public sealed class AssetPropertyTypeInspector : TypeInspectorSkeleton
{
    public override string GetEnumName(Type enumType, string name) => name;
    public override string GetEnumValue(object enumValue) => enumValue.ToString()!;

    public override IEnumerable<IPropertyDescriptor> GetProperties(Type type, object? container)
    {
        if (!type.IsAssignableTo(typeof(InnoAsset)))
            throw new ArgumentException($"{nameof(type)} must be assignable to {nameof(InnoAsset)}");

        foreach (var member in GetAllMembers(type))
        {
            var attr = member.GetCustomAttribute<AssetPropertyAttribute>();
            if (attr == null) continue;

            if (member is PropertyInfo p)
            {
                yield return attr.readOnly
                    ? new ReadonlyPropertyDescriptor(p)
                    : new ForceSetPropertyDescriptor(p);
            }
            else if (member is FieldInfo f)
            {
                yield return attr.readOnly
                    ? new ReadonlyFieldDescriptor(f)
                    : new ForceSetFieldDescriptor(f);
            }
        }
    }

    private static IEnumerable<MemberInfo> GetAllMembers(Type type)
    {
        var stack = new Stack<Type>();
        var t = type;
        while (t != null && t != typeof(object))
        {
            stack.Push(t);
            t = t.BaseType;
        }

        while (stack.Count > 0)
        {
            var current = stack.Pop();

            foreach (var p in current.GetProperties(
                         BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                     .OrderBy(p => p.MetadataToken))
            {
                // skip indexers
                if (p.GetIndexParameters().Length != 0) continue;
                yield return p;
            }

            foreach (var f in current.GetFields(
                         BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                     .OrderBy(f => f.MetadataToken))
            {
                yield return f;
            }
        }
    }

    private sealed class ReadonlyPropertyDescriptor(PropertyInfo property) : IPropertyDescriptor
    {
        public string Name => property.Name;
        public bool AllowNulls => true;
        public bool CanWrite => false;
        public Type Type => property.PropertyType;
        public Type? TypeOverride { get; set; }
        public int Order { get; set; }
        public ScalarStyle ScalarStyle { get; set; }
        public bool Required => false;
        public Type? ConverterType => null;

        public T? GetCustomAttribute<T>() where T : Attribute => property.GetCustomAttribute<T>();

        public IObjectDescriptor Read(object target)
        {
            var value = property.GetValue(target);
            return new ObjectDescriptor(value, property.PropertyType, property.PropertyType);
        }

        public void Write(object target, object? value)
        {
            // DO NOTHING
        }
    }

    private sealed class ForceSetPropertyDescriptor(PropertyInfo property) : IPropertyDescriptor
    {
        public string Name => property.Name;
        public bool AllowNulls => true;
        public bool CanWrite => true;
        public Type Type => property.PropertyType;
        public Type? TypeOverride { get; set; }
        public int Order { get; set; }
        public ScalarStyle ScalarStyle { get; set; }
        public bool Required => false;
        public Type? ConverterType => null;

        public T? GetCustomAttribute<T>() where T : Attribute => property.GetCustomAttribute<T>();

        public IObjectDescriptor Read(object target)
        {
            var value = property.GetValue(target);
            return new ObjectDescriptor(value, property.PropertyType, property.PropertyType);
        }

        public void Write(object target, object? value)
        {
            property.SetValue(target, value);
        }
    }

    private sealed class ReadonlyFieldDescriptor(FieldInfo field) : IPropertyDescriptor
    {
        public string Name => field.Name;
        public bool AllowNulls => true;
        public bool CanWrite => false;
        public Type Type => field.FieldType;
        public Type? TypeOverride { get; set; }
        public int Order { get; set; }
        public ScalarStyle ScalarStyle { get; set; }
        public bool Required => false;
        public Type? ConverterType => null;

        public T? GetCustomAttribute<T>() where T : Attribute => field.GetCustomAttribute<T>();

        public IObjectDescriptor Read(object target)
        {
            var value = field.GetValue(target);
            return new ObjectDescriptor(value, field.FieldType, field.FieldType);
        }

        public void Write(object target, object? value)
        {
            // DO NOTHING
        }
    }

    private sealed class ForceSetFieldDescriptor(FieldInfo field) : IPropertyDescriptor
    {
        public string Name => field.Name;
        public bool AllowNulls => true;
        public bool CanWrite => true;
        public Type Type => field.FieldType;
        public Type? TypeOverride { get; set; }
        public int Order { get; set; }
        public ScalarStyle ScalarStyle { get; set; }
        public bool Required => false;
        public Type? ConverterType => null;

        public T? GetCustomAttribute<T>() where T : Attribute => field.GetCustomAttribute<T>();

        public IObjectDescriptor Read(object target)
        {
            var value = field.GetValue(target);
            return new ObjectDescriptor(value, field.FieldType, field.FieldType);
        }

        public void Write(object target, object? value)
        {
            field.SetValue(target, value);
        }
    }
}
