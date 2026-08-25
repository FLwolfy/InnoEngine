using System;
using System.Text;

using Inno.Editor.Interactions;

namespace Inno.Editor.Panel.Inspector;

[EditorHistoryHandler(C_KIND)]
internal sealed class GameObjectTagHistoryHandler(SceneInspectionModule inspection) : EditorHistoryHandler
{
    internal const string C_KIND = "inno.editor.inspector.game-object-tag-definition";

    internal static EditorHistoryChange CreateChange(string tag)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tag);
        return new EditorHistoryChange(
            C_KIND,
            EditorHistoryPayload.FromBytes(Encoding.UTF8.GetBytes(tag)));
    }

    protected override EditorHistoryAvailability Query(
        EditorHistoryContext context,
        EditorHistoryChange change,
        EditorHistoryDirection direction)
    {
        try
        {
            _ = Decode(change);
            return EditorHistoryAvailability.Available();
        }
        catch (Exception exception)
        {
            return EditorHistoryAvailability.Unavailable(exception.Message);
        }
    }

    protected override EditorHistoryResult Apply(
        EditorHistoryContext context,
        EditorHistoryChange change,
        EditorHistoryDirection direction)
    {
        string tag;
        try
        {
            tag = Decode(change);
        }
        catch (Exception exception)
        {
            return EditorHistoryResult.Failure(exception.Message);
        }

        bool changed = direction == EditorHistoryDirection.Redo
            ? inspection.tags.Remove(tag)
            : inspection.tags.Add(tag);
        return changed
            ? EditorHistoryResult.Success()
            : EditorHistoryResult.Failure(
                $"Tag definition '{tag}' was not in the expected state for {direction}.");
    }

    private static string Decode(EditorHistoryChange change)
    {
        byte[] bytes = change.payload.ReadBytes();
        string tag = new UTF8Encoding(false, true).GetString(bytes);
        ArgumentException.ThrowIfNullOrWhiteSpace(tag);
        return tag;
    }
}
