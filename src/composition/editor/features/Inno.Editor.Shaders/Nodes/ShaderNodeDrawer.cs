using System;
using Inno.Core.Graphs;
using Inno.Core.Serialization;
using Inno.Editor.Rendering;
using Inno.Editor.Inspection;
using UI = Inno.Native.ImGui.ImGui;

namespace Inno.Editor.Shaders;

/// <summary>Registers Editor-only node controls independently from a node's shader compiler.</summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class ShaderNodeDrawerAttribute : Attribute
{
    /// <summary>Associates the drawer with one stable node definition.</summary>
    /// <param name="definitionId">The compiler-independent graph node identity.</param>
    /// <param name="displayName">Optional artist-facing name used in node headers and creation menus.</param>
    public ShaderNodeDrawerAttribute(string definitionId, string displayName = "")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(definitionId);
        this.definitionId = definitionId;
        this.displayName = displayName ?? throw new ArgumentNullException(nameof(displayName));
    }
    /// <summary>Gets the stable node identity handled by this drawer.</summary>
    public string definitionId { get; }
    /// <summary>Gets the optional presentation-only name; it does not participate in Shader compilation or identity.</summary>
    public string displayName { get; }
}

/// <summary>Provides reloadable Inspector controls; instances must not retain frame contexts or asset objects.</summary>
public abstract class ShaderNodeDrawer
{
    /// <summary>Draws the selected node's controls using the shared Inspector styling.</summary>
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
    private readonly InspectionDrawContext m_inspection;
    private readonly bool m_readOnly;
    /// <summary>Creates invocation-scoped access for a graph or Inspector host.</summary>
    /// <param name="node">Detached node values for this invocation.</param>
    /// <param name="serialization">Current owner converter registry.</param>
    /// <param name="context">Complete owner reference context.</param>
    /// <param name="write">Host callback recording changes in the shared draft history.</param>
    /// <param name="previews">Current generation preview service.</param>
    /// <param name="inspection">Shared Inspector context, including native property controls and drag/drop.</param>
    /// <param name="readOnly">Whether the source belongs to an immutable installation.</param>
    public ShaderNodeDrawContext(GraphNodeRecord node, SerializationRegistry serialization, SerializationContext context,
        Action<string, GraphSerializedValue, bool> write, IEditorPreviewService previews, InspectionDrawContext inspection, bool readOnly)
    { m_node = node; m_serialization = serialization; m_context = context; m_write = write; this.previews = previews; m_inspection = inspection; m_readOnly = readOnly; }

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
    {
        if (m_readOnly) throw new InvalidOperationException("Copy this installed Shader into the project before editing.");
        m_write(key, Inno.Rendering.Shaders.ShaderGraphDocument.Encode(value, m_serialization, m_context), continuous);
    }

    /// <summary>Draws a node property through the existing Inspector controls, including precise numbers and asset picking.</summary>
    /// <typeparam name="T">Supported native property type.</typeparam>
    /// <param name="key">Stable node property identity.</param>
    /// <param name="label">Artist-facing property label.</param>
    /// <param name="defaultValue">Value used only when no property is stored.</param>
    public void DrawProperty<T>(string key, string label, T defaultValue)
    {
        T value = Read(key, defaultValue);
        m_inspection.properties.DrawValue(m_inspection.editorContext, m_node, "shader.node." + key, label, typeof(T),
            () => value, edited => value = (T)edited!, new Edits(mutation =>
            { mutation(); Write(key, value, UI.IsAnyItemActive()); }), m_readOnly);
    }

    private sealed class Edits(Action<Action> apply) : IInspectionPropertyEditService
    {
        public bool ChangeProperty(object owner, string propertyName, Action mutation, string historyName)
        { apply(mutation); return true; }
    }
}
