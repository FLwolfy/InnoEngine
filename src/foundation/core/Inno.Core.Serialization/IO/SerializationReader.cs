using System;
using System.Collections.Generic;

namespace Inno.Core.Serialization;

/// <summary>
/// Reads a structured object during the active deserialization operation.
/// </summary>
public sealed class SerializationReader
{
    private readonly SerializationOperation m_operation;
    private readonly ObjectSerializationNode m_node;

    internal SerializationReader(
        SerializationOperation operation,
        ObjectSerializationNode node,
        string path,
        Type valueType)
    {
        m_operation = operation;
        m_node = node;
        this.path = path;
        this.valueType = valueType;
    }

    /// <summary>
    /// Gets the immutable context supplied to the current operation.
    /// </summary>
    public SerializationContext context => m_operation.context;

    /// <summary>
    /// Gets the current diagnostic path.
    /// </summary>
    public string path { get; }

    /// <summary>
    /// Gets the declared value type represented by this reader.
    /// </summary>
    public Type valueType { get; }

    /// <summary>
    /// Determines whether the current object contains a named member.
    /// </summary>
    /// <param name="name">
    /// The member name.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the member exists; otherwise, <see langword="false"/>.
    /// </returns>
    public bool Contains(string name)
    {
        m_operation.EnsureActive();
        return m_node.values.ContainsKey(name);
    }

    /// <summary>
    /// Reads a required named value through the unified value pipeline.
    /// </summary>
    /// <typeparam name="TValue">
    /// The declared value type.
    /// </typeparam>
    /// <param name="name">
    /// The required member name.
    /// </param>
    /// <returns>
    /// The restored value.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the member is missing or invalid.
    /// </exception>
    public TValue Read<TValue>(string name)
    {
        SerializationNode node = GetRequiredNode(name);
        return (TValue)ValuePipeline.Read(
            node,
            typeof(TValue),
            m_operation,
            AppendPath(name),
            allowDefaultObject: false)!;
    }

    /// <summary>
    /// Attempts to read a named value through the unified value pipeline.
    /// </summary>
    /// <typeparam name="TValue">
    /// The declared value type.
    /// </typeparam>
    /// <param name="name">
    /// The optional member name.
    /// </param>
    /// <param name="value">
    /// The restored value when the member exists.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the member exists; otherwise, <see langword="false"/>.
    /// </returns>
    public bool TryRead<TValue>(string name, out TValue value)
    {
        m_operation.EnsureActive();
        if (!m_node.values.TryGetValue(name, out SerializationNode? node))
        {
            value = default!;
            return false;
        }

        value = (TValue)ValuePipeline.Read(
            node,
            typeof(TValue),
            m_operation,
            AppendPath(name),
            allowDefaultObject: false)!;
        return true;
    }

    /// <summary>
    /// Reads a required named structured object.
    /// </summary>
    /// <param name="name">
    /// The required member name.
    /// </param>
    /// <returns>
    /// A reader for the child object.
    /// </returns>
    public SerializationReader ReadObject(string name)
    {
        SerializationNode node = GetRequiredNode(name);
        if (node is not ObjectSerializationNode child)
            throw new InvalidOperationException($"Serialization value '{AppendPath(name)}' must be an object.");
        return new SerializationReader(m_operation, child, AppendPath(name), typeof(object));
    }

    /// <summary>
    /// Reads a required ordered array of structured objects.
    /// </summary>
    /// <param name="name">
    /// The required member name.
    /// </param>
    /// <returns>
    /// Operation-scoped readers for the array elements.
    /// </returns>
    public IReadOnlyList<SerializationReader> ReadObjectArray(string name)
    {
        SerializationNode node = GetRequiredNode(name);
        if (node is not ArraySerializationNode array)
            throw new InvalidOperationException($"Serialization value '{AppendPath(name)}' must be an array.");

        var readers = new SerializationReader[array.values.Count];
        for (int i = 0; i < array.values.Count; i++)
        {
            if (array.values[i] is not ObjectSerializationNode child)
                throw new InvalidOperationException($"Serialization value '{AppendPath(name)}[{i}]' must be an object.");
            readers[i] = new SerializationReader(m_operation, child, $"{AppendPath(name)}[{i}]", typeof(object));
        }

        return readers;
    }

    /// <summary>
    /// Restores annotated properties from the current structured object into an existing object.
    /// </summary>
    /// <param name="target">
    /// The target whose annotated properties should be restored.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="target"/> is null.
    /// </exception>
    public void RestoreProperties(ISerializable target)
    {
        ArgumentNullException.ThrowIfNull(target);
        m_operation.EnsureActive();
        ValuePipeline.RestoreProperties(target, m_node, m_operation, path);
    }

    /// <summary>
    /// Schedules a callback to run after the complete decode operation succeeds.
    /// </summary>
    /// <param name="callback">
    /// The callback to schedule.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="callback"/> is null.
    /// </exception>
    public void OnCompleted(Action callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        m_operation.AddCompletionCallback(callback);
    }

    private SerializationNode GetRequiredNode(string name)
    {
        m_operation.EnsureActive();
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("A non-empty serialization member name is required.", nameof(name));
        if (!m_node.values.TryGetValue(name, out SerializationNode? node))
            throw new InvalidOperationException($"Required serialization value '{AppendPath(name)}' is missing.");
        return node;
    }

    private string AppendPath(string name) => path == "$" ? $"$.{name}" : $"{path}.{name}";
}
