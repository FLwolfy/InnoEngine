using System;
using System.Collections.Generic;

using Inno.Core.Reflection;
using Inno.Core.Serialization;
using Inno.Native.ImGui;
using NativeImGui = Inno.Native.ImGui.ImGui;

namespace Inno.Editor.Inspection.Drawers;

[PropertyDrawer(typeof(ISerializable), useForChildren: true, priority: 20)]
internal sealed class SerializableObjectPropertyDrawer : IPropertyDrawer
{
    /// <inheritdoc />
    public void Draw(PropertyDrawContext context)
    {
        object? value = context.GetValue();
        Type runtimeType = value?.GetType() ?? context.propertyType;
        DrawTypeSelector(context, runtimeType, value);
        value = context.GetValue();
        if (value is not ISerializable serializable)
        {
            NativeImGui.TextUnformatted("Null");
            return;
        }

        string header = $"{value.GetType().Name}##{context.path}_foldout";
        if (!NativeImGui.TreeNodeEx(header, ImGuiTreeNodeFlags.SpanAvailWidth))
        {
            return;
        }

        IReadOnlyList<SerializedProperty> properties = SerializationManager.GetProperties(serializable);
        for (int i = 0; i < properties.Count; i++)
        {
            context.DrawChild(properties[i]);
        }

        NativeImGui.TreePop();
    }

    private static void DrawTypeSelector(PropertyDrawContext context, Type runtimeType, object? value)
    {
        if (!NativeImGui.BeginCombo($"##{context.path}_runtime_type", value is null ? "Null" : runtimeType.Name))
        {
            return;
        }

        if (NativeImGui.Selectable("Null", value is null))
        {
            context.SetValue(null);
        }

        IReadOnlyList<Type> candidates = TypeCache.GetTypesImplementing<ISerializable>();
        for (int i = 0; i < candidates.Count; i++)
        {
            Type candidate = candidates[i];
            if (!context.propertyType.IsAssignableFrom(candidate))
            {
                continue;
            }

            if (NativeImGui.Selectable(candidate.Name, candidate == runtimeType && value is not null))
            {
                context.SetValue(Activator.CreateInstance(candidate, nonPublic: true));
            }
        }

        NativeImGui.EndCombo();
    }
}
