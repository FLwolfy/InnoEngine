using System;
using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;

using YamlDotNet.Serialization;

namespace Inno.Assets.Yaml;

internal class AssetObjectFactory : IObjectFactory
{
    public object Create(Type type)
    {
        if (type.IsAbstract)
            throw new InvalidOperationException($"Cannot instantiate abstract class {type.FullName}");

        var ctor = type.GetConstructor(
            BindingFlags.Instance | 
            BindingFlags.Public | 
            BindingFlags.NonPublic, 
            null, Type.EmptyTypes, null);

        if (ctor != null) return ctor.Invoke(null);
        
        return RuntimeHelpers.GetUninitializedObject(type);
    }

    public object? CreatePrimitive(Type type)
    {
        if (type.IsValueType)
            return Activator.CreateInstance(type);
        return null;
    }

    public bool GetDictionary(IObjectDescriptor descriptor, out IDictionary? dictionary, out Type[]? genericArguments)
    {
        var type = descriptor.Type;

        if (typeof(IDictionary).IsAssignableFrom(type))
        {
            dictionary = Activator.CreateInstance(type) as IDictionary;
            genericArguments = type.IsGenericType ? type.GetGenericArguments() : null;
            return true;
        }

        dictionary = null;
        genericArguments = null;
        return false;
    }

    public Type GetValueType(Type type) => type;

    public void ExecuteOnDeserializing(object value) {}
    public void ExecuteOnDeserialized(object value) {}
    public void ExecuteOnSerializing(object value) {}
    public void ExecuteOnSerialized(object value) {}
}