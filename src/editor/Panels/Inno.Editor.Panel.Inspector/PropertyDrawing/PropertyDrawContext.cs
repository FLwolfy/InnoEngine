using System;
using System.Linq;

using Inno.Core.Serialization;
using Inno.Engine.Scene;
using Inno.Editor.Core;
using Inno.Editor.Interactions;

namespace Inno.Editor.Panel.Inspector;

/// <summary>
/// Encapsulates one editable property path and its drawing services.
/// </summary>
public sealed class PropertyDrawContext
{
    private readonly Func<object?> m_getter;
    private readonly Action<object?> m_setter;
    private readonly SerializedPropertyRenderer m_renderer;

    /// <summary>
    /// Gets the shared editor context.
    /// </summary>
    public EditorContext editorContext { get; }

    /// <summary>Gets the active editor interaction entry point.</summary>
    public EditorInteractions interactions { get; }

    /// <summary>
    /// Gets the stable control path.
    /// </summary>
    public string path { get; }

    /// <summary>
    /// Gets the display label.
    /// </summary>
    public string label { get; }

    /// <summary>
    /// Gets the declared property type.
    /// </summary>
    public Type propertyType { get; }

    /// <summary>
    /// Gets the serialization visibility flags.
    /// </summary>
    public PropertyVisibility visibility { get; }

    /// <summary>
    /// Gets whether assignments are disabled.
    /// </summary>
    public bool isReadOnly => (visibility & PropertyVisibility.RuntimeSet) == 0;

    internal PropertyDrawContext(
        EditorContext editorContext,
        EditorInteractions interactions,
        string path,
        string label,
        Type propertyType,
        PropertyVisibility visibility,
        Func<object?> getter,
        Action<object?> setter,
        SerializedPropertyRenderer renderer)
    {
        this.editorContext = editorContext;
        this.interactions = interactions ?? throw new ArgumentNullException(nameof(interactions));
        this.path = path;
        this.label = label;
        this.propertyType = propertyType;
        this.visibility = visibility;
        m_getter = getter;
        m_setter = setter;
        m_renderer = renderer;
    }

    /// <summary>
    /// Gets the latest value.
    /// </summary>
    /// <returns>The latest value returned by the serialized property's getter.</returns>
    public object? GetValue() => m_getter();

    /// <summary>
    /// Assigns a value when the property is writable.
    /// </summary>
    /// <param name="value">New value.</param>
    public void SetValue(object? value)
    {
        if (isReadOnly)
            return;
        object? before = m_getter();
        if (Equals(before, value))
            return;
        object? selected = interactions.selection.selectedTarget;
        GameScene? scene = ResolveScene(selected);
        if (scene is not null)
        {
            SceneSnapshotOperation.Execute(
                interactions,
                $"Change {label}",
                scene,
                () => m_setter(value),
                new ScenePropertyHistoryKey(scene.identity.persistentId, path));
            return;
        }

        m_setter(value);
        interactions.history.RecordValue<object?>(
            $"Change {label}",
            before,
            value,
            m_setter,
            new PropertyHistoryKey(interactions.selection.selectedTarget, path));
    }

    /// <summary>
    /// Draws a nested value whose setter writes through this property path.
    /// </summary>
    /// <param name="childName">The stable child member name appended to this property path.</param>
    /// <param name="childType">The declared type used to resolve a property drawer.</param>
    /// <param name="getter">The callback that reads the latest child value.</param>
    /// <param name="setter">The callback that writes an edited child value.</param>
    /// <param name="readOnly">Whether the child should be presented without assignment support.</param>
    public void DrawChild(
        string childName,
        Type childType,
        Func<object?> getter,
        Action<object?> setter,
        bool readOnly = false)
    {
        PropertyVisibility childVisibility = readOnly || isReadOnly
            ? PropertyVisibility.Readonly
            : PropertyVisibility.Show;
        m_renderer.Draw(
            editorContext,
            $"{path}.{childName}",
            childName,
            childType,
            childVisibility,
            getter,
            setter);
    }

    /// <summary>
    /// Draws a nested serialized property.
    /// </summary>
    /// <param name="property">Nested property.</param>
    public void DrawChild(SerializedProperty property)
    {
        ArgumentNullException.ThrowIfNull(property);
        m_renderer.Draw(
            editorContext,
            $"{path}.{property.name}",
            property.name,
            property.propertyType,
            isReadOnly || !property.canWrite ? PropertyVisibility.Readonly : property.visibility,
            property.GetValue,
            property.SetValue);
    }

    /// <summary>
    /// Draws a nested value inline without adding another label row.
    /// </summary>
    /// <param name="childName">The stable child member name appended to this property path.</param>
    /// <param name="childType">The declared type used to resolve a property drawer.</param>
    /// <param name="getter">The callback that reads the latest child value.</param>
    /// <param name="setter">The callback that writes an edited child value.</param>
    /// <param name="readOnly">Whether the child should be presented without assignment support.</param>
    public void DrawInlineChild(
        string childName,
        Type childType,
        Func<object?> getter,
        Action<object?> setter,
        bool readOnly = false)
    {
        PropertyVisibility childVisibility = readOnly || isReadOnly
            ? PropertyVisibility.Readonly
            : PropertyVisibility.Show;
        var childContext = new PropertyDrawContext(
            editorContext,
            interactions,
            $"{path}.{childName}",
            childName,
            childType,
            childVisibility,
            getter,
            setter,
            m_renderer);
        IPropertyDrawer drawer = m_renderer.Resolve(childType);
        drawer.Draw(childContext);
    }

    private readonly record struct PropertyHistoryKey(object? target, string path);

    private readonly record struct ScenePropertyHistoryKey(Guid sceneId, string path);

    private static GameScene? ResolveScene(object? target)
        => target switch
        {
            GameScene scene => scene,
            GameObject gameObject => gameObject.scene,
            GameComponent component when !component.isDestroyed => component.gameObject.scene,
            GameSystem system when !system.isDestroyed => ResolveSystemScene(system),
            _ => null
        };

    private static GameScene? ResolveSystemScene(GameSystem system)
    {
        foreach (GameScene scene in SceneManager.loadedScenes)
        {
            if (scene.GetSystems().Contains(system))
                return scene;
        }
        return null;
    }
}
