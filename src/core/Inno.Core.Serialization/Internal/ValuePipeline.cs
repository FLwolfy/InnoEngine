using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;

namespace Inno.Core.Serialization;

internal static class ValuePipeline
{
    internal static SerializationNode WriteRoot(
        ISerializable value,
        Type declaredType,
        SerializationOperation operation)
    {
        operation.EnterCapture(value, "$");
        try
        {
            ConverterInvoker? converter = ConverterRegistry.Resolve(declaredType);
            if (converter is not null)
                return converter.Write(operation, "$", declaredType, value);
            if (declaredType.IsDefined(typeof(RequiresSerializationConverterAttribute), inherit: true))
                throw new InvalidOperationException(
                    $"Serializable type '{declaredType.FullName}' requires an explicit serialization converter.");

            if (value.GetType() != declaredType)
            {
                throw new InvalidOperationException(
                    $"Default root serialization requires exact type '{declaredType.FullName}', but the runtime value is '{value.GetType().FullName}'. Mark the root type with RequiresSerializationConverter and register a converter for polymorphism.");
            }
            EnsureDefaultSerializableType(declaredType);
            var node = new ObjectSerializationNode();
            WriteProperties(value, node, operation, "$");
            return node;
        }
        finally
        {
            operation.ExitCapture(value);
        }
    }

    internal static object ReadRoot(
        SerializationNode node,
        Type declaredType,
        SerializationOperation operation)
    {
        ConverterInvoker? converter = ConverterRegistry.Resolve(declaredType);
        if (converter is not null)
            return converter.Read(operation, "$", declaredType, RequireObject(node, declaredType, "$"));
        if (declaredType.IsDefined(typeof(RequiresSerializationConverterAttribute), inherit: true))
            throw new InvalidOperationException(
                $"Serializable type '{declaredType.FullName}' requires an explicit serialization converter.");

        EnsureDefaultSerializableType(declaredType);
        ISerializable instance = CreateSerializable(declaredType);
        RestoreProperties(instance, RequireObject(node, declaredType, "$"), operation, "$");
        return instance;
    }

    internal static void RestoreRoot(
        ISerializable target,
        SerializationNode node,
        Type targetType,
        SerializationOperation operation)
    {
        ConverterInvoker? converter = ConverterRegistry.Resolve(targetType);
        if (converter is not null)
        {
            converter.Restore(operation, "$", targetType, RequireObject(node, targetType, "$"), target);
            return;
        }
        if (targetType.IsDefined(typeof(RequiresSerializationConverterAttribute), inherit: true))
            throw new InvalidOperationException(
                $"Serializable type '{targetType.FullName}' requires an explicit serialization converter.");

        EnsureDefaultSerializableType(targetType);
        RestoreProperties(target, RequireObject(node, targetType, "$"), operation, "$" );
    }

    internal static SerializationNode Write(
        object? value,
        Type declaredType,
        SerializationOperation operation,
        string path,
        bool allowDefaultObject)
    {
        operation.EnsureActive();
        Type? nullableType = Nullable.GetUnderlyingType(declaredType);
        Type valueType = nullableType ?? declaredType;
        if (value is null)
        {
            if (valueType.IsValueType && nullableType is null)
                throw new InvalidOperationException($"Non-nullable value '{path}' of type '{valueType.FullName}' cannot be null.");
            return NullSerializationNode.instance;
        }

        bool trackReference = !value.GetType().IsValueType && value is not string && value is not byte[];
        if (trackReference)
            operation.EnterCapture(value, path);
        try
        {
            ConverterInvoker? converter = ConverterRegistry.Resolve(valueType);
            if (converter is not null)
                return converter.Write(operation, path, valueType, value);

            if (IsPrimitive(valueType))
                return new ScalarSerializationNode(NormalizePrimitive(value, valueType, path));
            if (valueType.IsEnum)
                return new ScalarSerializationNode(Convert.ToInt64(value, CultureInfo.InvariantCulture));
            if (valueType == typeof(byte[]))
                return new BinarySerializationNode((byte[])((byte[])value).Clone());
            if (valueType.IsArray)
                return WriteArray((Array)value, valueType, operation, path);
            if (CollectionTypeUtility.TryGetMapTypes(valueType, out Type keyType, out Type mapValueType))
                return WriteMap(value, valueType, keyType, mapValueType, operation, path);
            if (CollectionTypeUtility.TryGetSequenceElementType(valueType, out Type elementType))
                return WriteSequence(value, elementType, operation, path);
            if (valueType.IsValueType)
                return WriteStruct(value, valueType, operation, path);

            if (allowDefaultObject && value is ISerializable serializable)
            {
                if (value.GetType() != valueType)
                {
                    throw new InvalidOperationException(
                        $"Default serialization at '{path}' requires exact type '{valueType.FullName}', but the runtime value is '{value.GetType().FullName}'. Register a converter for polymorphic values.");
                }
                EnsureDefaultSerializableType(valueType);
                var node = new ObjectSerializationNode();
                WriteProperties(serializable, node, operation, path);
                return node;
            }

            throw new InvalidOperationException(
                $"Class value '{path}' of type '{valueType.FullName}' requires an explicit SerializationConverter<{valueType.Name}>.");
        }
        finally
        {
            if (trackReference)
                operation.ExitCapture(value);
        }
    }

    internal static object? Read(
        SerializationNode node,
        Type declaredType,
        SerializationOperation operation,
        string path,
        bool allowDefaultObject)
    {
        operation.EnsureActive();
        Type? nullableType = Nullable.GetUnderlyingType(declaredType);
        Type valueType = nullableType ?? declaredType;
        if (node is NullSerializationNode)
        {
            if (!valueType.IsValueType || nullableType is not null)
                return null;
            throw new InvalidOperationException($"Non-nullable value '{path}' of type '{valueType.FullName}' cannot be null.");
        }

        ConverterInvoker? converter = ConverterRegistry.Resolve(valueType);
        if (converter is not null)
            return converter.Read(operation, path, valueType, RequireObject(node, valueType, path));

        if (IsPrimitive(valueType))
            return ReadPrimitive(node, valueType, path);
        if (valueType.IsEnum)
            return Enum.ToObject(valueType, Convert.ToInt64(ReadScalar(node, path), CultureInfo.InvariantCulture));
        if (valueType == typeof(byte[]))
            return node is BinarySerializationNode binary
                ? (byte[])binary.value.Clone()
                : throw TypeMismatch(path, "binary", node);
        if (valueType.IsArray)
            return ReadArray(node, valueType, operation, path);
        if (CollectionTypeUtility.TryGetMapTypes(valueType, out Type keyType, out Type mapValueType))
            return ReadMap(node, valueType, keyType, mapValueType, operation, path);
        if (CollectionTypeUtility.TryGetSequenceElementType(valueType, out Type elementType))
            return ReadSequence(node, valueType, elementType, operation, path);
        if (valueType.IsValueType)
            return ReadStruct(node, valueType, operation, path);

        if (allowDefaultObject && typeof(ISerializable).IsAssignableFrom(valueType))
        {
            EnsureDefaultSerializableType(valueType);
            ISerializable instance = CreateSerializable(valueType);
            RestoreProperties(instance, RequireObject(node, valueType, path), operation, path);
            return instance;
        }

        throw new InvalidOperationException(
            $"Class value '{path}' of type '{valueType.FullName}' requires an explicit SerializationConverter<{valueType.Name}>.");
    }

    internal static void WriteProperties(
        ISerializable value,
        ObjectSerializationNode node,
        SerializationOperation operation,
        string path)
    {
        SerializableMember[] members = ReflectionMetadata.GetSerializableMembers(value.GetType());
        for (int i = 0; i < members.Length; i++)
        {
            SerializableMember member = members[i];
            if ((member.visibility & PropertyVisibility.Serialize) == 0)
                continue;
            string memberPath = AppendPath(path, member.name);
            SerializationNode memberNode = Write(
                member.GetValue(value),
                member.type,
                operation,
                memberPath,
                allowDefaultObject: false);
            if (!node.values.TryAdd(member.name, memberNode))
                throw new InvalidOperationException($"Serialization object '{path}' already contains key '{member.name}'.");
        }
    }

    internal static void RestoreProperties(
        ISerializable target,
        ObjectSerializationNode node,
        SerializationOperation operation,
        string path)
    {
        SerializableMember[] members = ReflectionMetadata.GetSerializableMembers(target.GetType());
        for (int i = 0; i < members.Length; i++)
        {
            SerializableMember member = members[i];
            if ((member.visibility & PropertyVisibility.Deserialize) == 0 ||
                !node.values.TryGetValue(member.name, out SerializationNode? memberNode))
            {
                continue;
            }

            member.SetValue(
                target,
                Read(memberNode, member.type, operation, AppendPath(path, member.name), allowDefaultObject: false));
        }

        operation.ScheduleRestoredObject(target);
    }

    private static SerializationNode WriteArray(
        Array value,
        Type arrayType,
        SerializationOperation operation,
        string path)
    {
        if (arrayType.GetArrayRank() != 1)
            throw new InvalidOperationException($"Only one-dimensional arrays are supported at '{path}'.");
        Type elementType = arrayType.GetElementType()!;
        var node = new ArraySerializationNode();
        for (int i = 0; i < value.Length; i++)
            node.values.Add(Write(value.GetValue(i), elementType, operation, $"{path}[{i}]", false));
        return node;
    }

    private static object ReadArray(
        SerializationNode node,
        Type arrayType,
        SerializationOperation operation,
        string path)
    {
        if (arrayType.GetArrayRank() != 1)
            throw new InvalidOperationException($"Only one-dimensional arrays are supported at '{path}'.");
        if (node is not ArraySerializationNode array)
            throw TypeMismatch(path, "array", node);
        Type elementType = arrayType.GetElementType()!;
        Array result = Array.CreateInstance(elementType, array.values.Count);
        for (int i = 0; i < array.values.Count; i++)
            result.SetValue(Read(array.values[i], elementType, operation, $"{path}[{i}]", false), i);
        return result;
    }

    private static SerializationNode WriteSequence(
        object value,
        Type elementType,
        SerializationOperation operation,
        string path)
    {
        if (value is not IEnumerable enumerable)
            throw new InvalidOperationException($"Sequence value '{path}' is not enumerable.");
        var node = new ArraySerializationNode();
        int index = 0;
        foreach (object? element in enumerable)
        {
            node.values.Add(Write(element, elementType, operation, $"{path}[{index}]", false));
            index++;
        }
        return node;
    }

    private static object ReadSequence(
        SerializationNode node,
        Type sequenceType,
        Type elementType,
        SerializationOperation operation,
        string path)
    {
        if (node is not ArraySerializationNode array)
            throw TypeMismatch(path, "array", node);
        var values = new object?[array.values.Count];
        for (int i = 0; i < values.Length; i++)
            values[i] = Read(array.values[i], elementType, operation, $"{path}[{i}]", false);
        return CollectionTypeUtility.BuildSequence(sequenceType, elementType, values);
    }

    private static SerializationNode WriteMap(
        object value,
        Type mapType,
        Type keyType,
        Type mapValueType,
        SerializationOperation operation,
        string path)
    {
        if (!CollectionTypeUtility.TryEnumerateMap(value, mapType, out List<KeyValuePair<object?, object?>> entries))
            throw new InvalidOperationException($"Map value '{path}' cannot be enumerated.");
        var node = new MapSerializationNode();
        for (int i = 0; i < entries.Count; i++)
        {
            KeyValuePair<object?, object?> entry = entries[i];
            if (entry.Key is null)
                throw new InvalidOperationException($"Map key '{path}[{i}].key' cannot be null.");
            node.values.Add(new KeyValuePair<SerializationNode, SerializationNode>(
                Write(entry.Key, keyType, operation, $"{path}[{i}].key", false),
                Write(entry.Value, mapValueType, operation, $"{path}[{i}].value", false)));
        }
        return node;
    }

    private static object ReadMap(
        SerializationNode node,
        Type mapType,
        Type keyType,
        Type mapValueType,
        SerializationOperation operation,
        string path)
    {
        if (node is not MapSerializationNode map)
            throw TypeMismatch(path, "map", node);
        var entries = new KeyValuePair<object?, object?>[map.values.Count];
        for (int i = 0; i < entries.Length; i++)
        {
            entries[i] = new KeyValuePair<object?, object?>(
                Read(map.values[i].Key, keyType, operation, $"{path}[{i}].key", false),
                Read(map.values[i].Value, mapValueType, operation, $"{path}[{i}].value", false));
        }
        return CollectionTypeUtility.BuildMap(mapType, keyType, mapValueType, entries);
    }

    private static SerializationNode WriteStruct(
        object value,
        Type structType,
        SerializationOperation operation,
        string path)
    {
        StructMember[] members = StructMetadata.GetMembers(structType);
        var node = new ObjectSerializationNode();
        for (int i = 0; i < members.Length; i++)
        {
            StructMember member = members[i];
            if ((member.visibility & PropertyVisibility.Serialize) == 0)
                continue;
            node.values.Add(
                member.name,
                Write(member.GetValue(value), member.type, operation, AppendPath(path, member.name), false));
        }
        return node;
    }

    private static object ReadStruct(
        SerializationNode node,
        Type structType,
        SerializationOperation operation,
        string path)
    {
        ObjectSerializationNode objectNode = RequireObject(node, structType, path);
        object result = Activator.CreateInstance(structType)!;
        StructMember[] members = StructMetadata.GetMembers(structType);
        for (int i = 0; i < members.Length; i++)
        {
            StructMember member = members[i];
            if ((member.visibility & PropertyVisibility.Deserialize) == 0 ||
                !objectNode.values.TryGetValue(member.name, out SerializationNode? memberNode))
            {
                continue;
            }
            member.SetValue(result, Read(memberNode, member.type, operation, AppendPath(path, member.name), false));
        }
        return result;
    }

    private static ISerializable CreateSerializable(Type valueType)
    {
        ConstructorInfo? constructor = valueType.GetConstructor(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            Type.EmptyTypes,
            modifiers: null);
        if (constructor is null)
        {
            throw new InvalidOperationException(
                $"Serializable type '{valueType.FullName}' requires a parameterless constructor or an explicit converter.");
        }

        try
        {
            return (ISerializable)constructor.Invoke(null);
        }
        catch (TargetInvocationException exception)
        {
            throw new InvalidOperationException(
                $"The parameterless constructor for serializable type '{valueType.FullName}' failed.",
                exception.InnerException ?? exception);
        }
    }

    private static void EnsureDefaultSerializableType(Type valueType)
    {
        if (!typeof(ISerializable).IsAssignableFrom(valueType))
            throw new InvalidOperationException($"Type '{valueType.FullName}' does not implement ISerializable.");
        if (valueType.IsAbstract || valueType.IsInterface)
            throw new InvalidOperationException($"Serializable type '{valueType.FullName}' requires an explicit converter because it is not concrete.");
        if (valueType.IsDefined(typeof(RequiresSerializationConverterAttribute), inherit: true))
            throw new InvalidOperationException($"Serializable type '{valueType.FullName}' requires an explicit serialization converter.");
    }

    private static ObjectSerializationNode RequireObject(SerializationNode node, Type valueType, string path)
        => node as ObjectSerializationNode
           ?? throw new InvalidOperationException(
               $"Serialization value '{path}' for '{valueType.FullName}' must be an object, but was '{GetNodeKind(node)}'.");

    private static object ReadPrimitive(SerializationNode node, Type valueType, string path)
    {
        object scalar = ReadScalar(node, path);
        if (valueType == typeof(string))
            return scalar is string text ? text : throw TypeMismatch(path, "string", node);
        if (valueType == typeof(Guid))
            return scalar is Guid guid ? guid : throw TypeMismatch(path, "Guid", node);
        if (scalar.GetType() == valueType)
            return scalar;
        try
        {
            return Convert.ChangeType(scalar, valueType, CultureInfo.InvariantCulture)!;
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException(
                $"Serialization scalar '{path}' cannot be converted from '{scalar.GetType().FullName}' to '{valueType.FullName}'.",
                exception);
        }
    }

    private static object ReadScalar(SerializationNode node, string path)
        => node is ScalarSerializationNode scalar
            ? scalar.value
            : throw TypeMismatch(path, "scalar", node);

    private static object NormalizePrimitive(object value, Type valueType, string path)
    {
        if (value.GetType() == valueType)
            return value;
        try
        {
            return Convert.ChangeType(value, valueType, CultureInfo.InvariantCulture)!;
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException(
                $"Primitive value '{path}' cannot be converted to '{valueType.FullName}'.",
                exception);
        }
    }

    private static bool IsPrimitive(Type type)
        => type == typeof(bool) ||
           type == typeof(byte) ||
           type == typeof(sbyte) ||
           type == typeof(short) ||
           type == typeof(ushort) ||
           type == typeof(int) ||
           type == typeof(uint) ||
           type == typeof(long) ||
           type == typeof(ulong) ||
           type == typeof(float) ||
           type == typeof(double) ||
           type == typeof(decimal) ||
           type == typeof(string) ||
           type == typeof(Guid);

    private static InvalidOperationException TypeMismatch(string path, string expected, SerializationNode actual)
        => new($"Serialization value '{path}' must be {expected}, but was '{GetNodeKind(actual)}'.");

    private static string GetNodeKind(SerializationNode node)
        => node switch
        {
            NullSerializationNode => "null",
            ScalarSerializationNode => "scalar",
            BinarySerializationNode => "binary",
            ObjectSerializationNode => "object",
            ArraySerializationNode => "array",
            MapSerializationNode => "map",
            _ => node.GetType().Name
        };

    private static string AppendPath(string path, string name) => path == "$" ? $"$.{name}" : $"{path}.{name}";
}
