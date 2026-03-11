using System;

using Inno.Core.ECS;
using Inno.Core.Serialization;
using Inno.Editor.GUI.PropertyGUI;

namespace Inno.Editor.GUI.InspectorGUI.InspectorEditor;

/// <summary>
/// Default Render Components' Serialized Properties
/// </summary>
[InspectorEditorGUI(typeof(GameComponent))]
public class ComponentEditor : IInspectorEditor
{
    private const int MAX_NEST_DEPTH = 8;
    private const float INDENT_WIDTH = 16;

    public virtual void OnInspectorGUI(object target)
    {
        if (target is not GameComponent comp) return;

        string compName = comp.GetType().Name;
        Action? onClose = comp is Transform ? null : () => comp.gameObject.RemoveComponent(comp);

        if (EditorGUILayout.CollapsingHeader(compName, onClose))
        {
            DrawSerializableObject(
                name: compName,
                serializable: comp,
                enabled: true,
                depth: 0
            );
        }
    }

    private static void DrawSerializableObject(
        string name,
        ISerializable serializable,
        bool enabled,
        int depth
    )
    {
        if (depth > MAX_NEST_DEPTH)
        {
            EditorGUILayout.Label($"Max nested depth reached: {name}");
            return;
        }

        var serializedProps = serializable.GetSerializedProperties();
        if (serializedProps.Count == 0)
        {
            EditorGUILayout.Label("No editable properties.");
            return;
        }

        foreach (var prop in serializedProps)
        {
            DrawProperty(prop, enabled, depth);
        }
    }

    private static void DrawProperty(SerializedProperty prop, bool parentEnabled, int depth)
    {
        bool isReadonly = (prop.visibility & PropertyVisibility.RuntimeSet) == 0;
        bool enabled = parentEnabled && !isReadonly;
        
        EditorGUILayout.Indent(INDENT_WIDTH * depth);

        // 1) Draw with Renderer
        if (PropertyRendererRegistry.TryGetRenderer(prop.propertyType, out var renderer))
        {
            renderer!.Bind(
                prop.name,
                prop.GetValue,
                prop.SetValue,
                enabled
            );
            return;
        }

        // 2) Nested ISerializable
        object? value = prop.GetValue();
        if (value is ISerializable nested)
        {
            if (EditorGUILayout.CollapsingLabel(prop.name, true, enabled))
            {
                DrawSerializableObject(
                    name: prop.name,
                    serializable: nested,
                    enabled: enabled,
                    depth: depth + 1
                );
            }

            return;
        }

        // 3) Fallback
        EditorGUILayout.Label($"No renderer for {prop.name} ({prop.propertyType.Name})");
    }
}
