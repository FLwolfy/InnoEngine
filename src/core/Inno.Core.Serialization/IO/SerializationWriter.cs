using System;
using System.Collections.Generic;

namespace Inno.Core.Serialization;

/// <summary>
/// Writes a structured object during the active serialization operation.
/// </summary>
public sealed class SerializationWriter
{
    private readonly SerializationOperation m_operation;
    private readonly ObjectSerializationNode m_node;

    internal SerializationWriter(
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
    /// Gets the declared value type represented by this writer.
    /// </summary>
    public Type valueType { get; }

    /// <summary>
    /// Writes a named value through the unified value pipeline.
    /// </summary>
    /// <typeparam name="TValue">
    /// The declared value type.
    /// </typeparam>
    /// <param name="name">
    /// The unique non-empty member name.
    /// </param>
    /// <param name="value">
    /// The value to write.
    /// </param>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="name"/> is empty.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the name is duplicated or the value is unsupported.
    /// </exception>
    public void Write<TValue>(string name, TValue value)
    {
        ValidateName(name);
        AddNode(name, ValuePipeline.Write(value, typeof(TValue), m_operation, AppendPath(name), allowDefaultObject: false));
    }

    /// <summary>
    /// Writes a named structured object.
    /// </summary>
    /// <param name="name">
    /// The unique non-empty member name.
    /// </param>
    /// <param name="write">
    /// The callback that fills the child object.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="write"/> is null.
    /// </exception>
    public void WriteObject(string name, Action<SerializationWriter> write)
    {
        ArgumentNullException.ThrowIfNull(write);
        ValidateName(name);
        var child = new ObjectSerializationNode();
        AddNode(name, child);
        write(new SerializationWriter(m_operation, child, AppendPath(name), typeof(object)));
    }

    /// <summary>
    /// Writes an ordered array of structured objects.
    /// </summary>
    /// <typeparam name="TValue">
    /// The source element type.
    /// </typeparam>
    /// <param name="name">
    /// The unique non-empty member name.
    /// </param>
    /// <param name="values">
    /// The elements to write.
    /// </param>
    /// <param name="writeElement">
    /// The callback that fills each element object.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when an argument is null.
    /// </exception>
    public void WriteObjectArray<TValue>(
        string name,
        IEnumerable<TValue> values,
        Action<SerializationWriter, TValue> writeElement)
    {
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(writeElement);
        ValidateName(name);

        var array = new ArraySerializationNode();
        AddNode(name, array);
        int index = 0;
        foreach (TValue value in values)
        {
            var child = new ObjectSerializationNode();
            array.values.Add(child);
            writeElement(
                new SerializationWriter(m_operation, child, $"{AppendPath(name)}[{index}]", typeof(TValue)),
                value);
            index++;
        }
    }

    /// <summary>
    /// Writes annotated properties from a serializable object into the current structured object.
    /// </summary>
    /// <param name="value">
    /// The object whose annotated properties should be written.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="value"/> is null.
    /// </exception>
    public void WriteProperties(ISerializable value)
    {
        ArgumentNullException.ThrowIfNull(value);
        m_operation.EnsureActive();
        m_operation.EnterCapture(value, path);
        try
        {
            ValuePipeline.WriteProperties(value, m_node, m_operation, path);
        }
        finally
        {
            m_operation.ExitCapture(value);
        }
    }

    private void AddNode(string name, SerializationNode node)
    {
        m_operation.EnsureActive();
        if (!m_node.values.TryAdd(name, node))
            throw new InvalidOperationException($"Serialization object '{path}' already contains key '{name}'.");
    }

    private string AppendPath(string name) => path == "$" ? $"$.{name}" : $"{path}.{name}";

    private static void ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("A non-empty serialization member name is required.", nameof(name));
    }
}
