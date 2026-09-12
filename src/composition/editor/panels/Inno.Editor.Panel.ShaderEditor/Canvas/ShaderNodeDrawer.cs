using System;
using Inno.Core.Graphs;
using Inno.Core.Serialization;
using Inno.Editor.Rendering;

namespace Inno.Editor.Panel.ShaderEditor;

/// <summary>Registers Editor-only node controls independently from a node's shader compiler.</summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class ShaderNodeDrawerAttribute : Attribute
{
    /// <summary>Associates the drawer with one stable node definition.</summary>
    /// <param name="definitionId">The compiler-independent graph node identity.</param>
    public ShaderNodeDrawerAttribute(string definitionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(definitionId);
        this.definitionId = definitionId;
    }
    /// <summary>Gets the stable node identity handled by this drawer.</summary>
    public string definitionId { get; }
}

/// <summary>Provides reloadable inline controls; instances must not retain frame contexts or asset objects.</summary>
public abstract class ShaderNodeDrawer
{
    /// <summary>Gets the inline content height in unscaled canvas units; the host applies the current canvas zoom.</summary>
    public virtual float contentHeight => 110f;

    /// <summary>Draws controls within the current node's width using the shared Editor styling.</summary>
    /// <param name="context">Frame-scoped value access and undoable writes.</param>
    public abstract void Draw(ShaderNodeDrawContext context);
}

/// <summary>Exposes detached node values and one-gesture writes without exposing the live graph document.</summary>
public sealed class ShaderNodeDrawContext
{
    private readonly GraphNodeRecord m_node;
    private readonly SerializationRegistry m_serialization;
    private readonly SerializationContext m_context;
    private readonly Action<string, GraphSerializedValue, bool> m_write;
    internal ShaderNodeDrawContext(GraphNodeRecord node, SerializationRegistry serialization, SerializationContext context,
        Action<string, GraphSerializedValue, bool> write, IEditorPreviewService previews)
    { m_node = node; m_serialization = serialization; m_context = context; m_write = write; this.previews = previews; }

    /// <summary>Gets the shared generation-scoped preview service for optional inline texture previews; valid only during this draw.</summary>
    public IEditorPreviewService previews { get; }

    /// <summary>Gets the stable node identity for Editor widget IDs.</summary>
    public GraphNodeId nodeId => m_node.id;

    /// <summary>Reads a detached property value; missing values use the node's declared default.</summary>
    /// <typeparam name="T">Native serializable property type.</typeparam>
    /// <param name="key">Stable node-local property key.</param>
    /// <param name="defaultValue">Default used only when the property is absent.</param>
    /// <returns>The detached value. Malformed stored data is not silently replaced.</returns>
    public T Read<T>(string key, T defaultValue)
        => Inno.Rendering.Shaders.ShaderGraphDocument.Read(m_node, key, defaultValue, m_serialization, m_context);

    /// <summary>Records an unsaved draft edit through shared history; only an explicit document save applies it to the asset.</summary>
    /// <typeparam name="T">Native serializable property type.</typeparam>
    /// <param name="key">Stable node-local property key.</param>
    /// <param name="value">New detached value.</param>
    /// <param name="continuous">Whether this sample belongs to the active text or numeric gesture.</param>
    public void Write<T>(string key, T value, bool continuous = false)
        => m_write(key, Inno.Rendering.Shaders.ShaderGraphDocument.Encode(value, m_serialization, m_context), continuous);
}
