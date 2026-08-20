using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

using Inno.Core.Serialization;
using Inno.Editor.ImGui;
using Inno.Native.ImGui;
using NativeImGui = Inno.Native.ImGui.ImGui;

namespace Inno.Editor.Panel.Inspector;

[PropertyDrawer(typeof(IEnumerable<>), useForChildren: true, priority: -20)]
[PropertyDrawer(typeof(IDictionary<,>), useForChildren: true, priority: 20)]
[PropertyDrawer(typeof(IReadOnlyDictionary<,>), useForChildren: true, priority: 20)]
internal sealed class CollectionPropertyDrawer : IPropertyDrawer
{
    private static readonly Dictionary<string, string> s_mapErrors = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public void Draw(PropertyDrawContext context)
    {
        if (EditorCollectionUtility.TryGetMapTypes(context.propertyType, out Type keyType, out Type valueType))
        {
            DrawMap(context, keyType, valueType);
            return;
        }

        if (EditorCollectionUtility.TryGetSequenceElementType(context.propertyType, out Type elementType))
        {
            DrawSequence(context, elementType);
            return;
        }

        NativeImGui.TextUnformatted($"Unsupported collection: {context.propertyType.Name}");
    }

    private static void DrawSequence(PropertyDrawContext context, Type elementType)
    {
        List<object?> values = EditorCollectionUtility.EnumerateSequence(context.GetValue());
        if (!NativeImGui.TreeNodeEx(
                $"Count: {values.Count}##{context.path}_sequence",
                ImGuiTreeNodeFlags.SpanAvailWidth))
        {
            return;
        }

        if (!context.isReadOnly && NativeImGui.SmallButton($"+##{context.path}_add"))
        {
            values.Add(CreateDefault(elementType));
            context.SetValue(EditorCollectionUtility.BuildSequence(context.propertyType, elementType, values));
        }

        for (int i = 0; i < values.Count; i++)
        {
            int index = i;
            if (!context.isReadOnly && DrawSequenceActions(context, elementType, index, values.Count))
            {
                break;
            }

            context.DrawChild(
                $"Element {index}",
                elementType,
                () => EditorCollectionUtility.EnumerateSequence(context.GetValue())[index],
                value =>
                {
                    List<object?> updated = EditorCollectionUtility.EnumerateSequence(context.GetValue());
                    updated[index] = value;
                    context.SetValue(EditorCollectionUtility.BuildSequence(context.propertyType, elementType, updated));
                });
        }

        NativeImGui.TreePop();
    }

    private static bool DrawSequenceActions(
        PropertyDrawContext context,
        Type elementType,
        int index,
        int count)
    {
        bool changed = false;
        if (NativeImGui.SmallButton($"Up##{context.path}_{index}_up") && index > 0)
        {
            List<object?> updated = EditorCollectionUtility.EnumerateSequence(context.GetValue());
            (updated[index - 1], updated[index]) = (updated[index], updated[index - 1]);
            context.SetValue(EditorCollectionUtility.BuildSequence(context.propertyType, elementType, updated));
            changed = true;
        }

        NativeImGui.SameLine();
        if (NativeImGui.SmallButton($"Down##{context.path}_{index}_down") && index < count - 1)
        {
            List<object?> updated = EditorCollectionUtility.EnumerateSequence(context.GetValue());
            (updated[index + 1], updated[index]) = (updated[index], updated[index + 1]);
            context.SetValue(EditorCollectionUtility.BuildSequence(context.propertyType, elementType, updated));
            changed = true;
        }

        NativeImGui.SameLine();
        if (NativeImGui.SmallButton($"Remove##{context.path}_{index}_remove"))
        {
            List<object?> updated = EditorCollectionUtility.EnumerateSequence(context.GetValue());
            updated.RemoveAt(index);
            context.SetValue(EditorCollectionUtility.BuildSequence(context.propertyType, elementType, updated));
            changed = true;
        }

        return changed;
    }

    private static void DrawMap(PropertyDrawContext context, Type keyType, Type valueType)
    {
        List<KeyValuePair<object?, object?>> entries = EnumerateMap(context);
        if (!NativeImGui.TreeNodeEx(
                $"Count: {entries.Count}##{context.path}_map",
                ImGuiTreeNodeFlags.SpanAvailWidth))
        {
            return;
        }

        if (!context.isReadOnly && NativeImGui.SmallButton($"+##{context.path}_map_add"))
        {
            object? defaultKey = CreateDefault(keyType);
            if (defaultKey is not null && !entries.Any(entry => Equals(entry.Key, defaultKey)))
            {
                entries.Add(new KeyValuePair<object?, object?>(defaultKey, CreateDefault(valueType)));
                context.SetValue(EditorCollectionUtility.BuildMap(
                    context.propertyType,
                    keyType,
                    valueType,
                    entries));
            }
        }

        for (int i = 0; i < entries.Count; i++)
        {
            int index = i;
            string errorPath = $"{context.path}.{index}";
            context.DrawChild(
                $"Key {index}",
                keyType,
                () => EnumerateMap(context)[index].Key,
                key =>
                {
                    List<KeyValuePair<object?, object?>> updated = EnumerateMap(context);
                    bool duplicate = updated.Where((_, candidateIndex) => candidateIndex != index)
                        .Any(entry => Equals(entry.Key, key));
                    if (duplicate)
                    {
                        s_mapErrors[errorPath] = "Duplicate map keys are not allowed.";
                        return;
                    }

                    s_mapErrors.Remove(errorPath);
                    updated[index] = new KeyValuePair<object?, object?>(key, updated[index].Value);
                    context.SetValue(EditorCollectionUtility.BuildMap(
                        context.propertyType,
                        keyType,
                        valueType,
                        updated));
                });
            if (s_mapErrors.TryGetValue(errorPath, out string? error))
            {
                NativeImGui.TextColored(EditorPalette.error, error);
            }

            context.DrawChild(
                $"Value {index}",
                valueType,
                () => EnumerateMap(context)[index].Value,
                value =>
                {
                    List<KeyValuePair<object?, object?>> updated = EnumerateMap(context);
                    updated[index] = new KeyValuePair<object?, object?>(updated[index].Key, value);
                    context.SetValue(EditorCollectionUtility.BuildMap(
                        context.propertyType,
                        keyType,
                        valueType,
                        updated));
                });

            if (!context.isReadOnly && NativeImGui.SmallButton($"Remove##{context.path}_{index}_map_remove"))
            {
                List<KeyValuePair<object?, object?>> updated = EnumerateMap(context);
                updated.RemoveAt(index);
                s_mapErrors.Remove(errorPath);
                context.SetValue(EditorCollectionUtility.BuildMap(
                    context.propertyType,
                    keyType,
                    valueType,
                    updated));
                break;
            }
        }

        NativeImGui.TreePop();
    }

    private static List<KeyValuePair<object?, object?>> EnumerateMap(PropertyDrawContext context)
    {
        if (EditorCollectionUtility.TryEnumerateMap(
                context.GetValue(),
                context.propertyType,
                out List<KeyValuePair<object?, object?>> entries))
        {
            return entries;
        }

        return [];
    }

    private static object? CreateDefault(Type type)
    {
        if (type == typeof(string))
        {
            return string.Empty;
        }

        return type.IsValueType ? Activator.CreateInstance(type) : null;
    }
}
