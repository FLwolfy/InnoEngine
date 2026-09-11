using System;
using Inno.Adapter.Presentation.ImGui;
using Inno.Core.Mathematics;
using Inno.Editor.ImGui;
using Inno.Editor.Inspection;
using Inno.Editor.Scene;
using Inno.Scene.Components;
using EditorWidget = Inno.Editor.ImGui.ImGuiWidget.ImGuiWidget;
using NativeImGui = Inno.Native.ImGui.ImGui;

namespace Inno.Editor.Panel.Inspector;

[InspectionDrawer(typeof(Transform))]
internal sealed class TransformInspectionDrawer(SceneEdits edits) : InspectionDrawer<Transform>
{
    private bool m_world;

    /// <inheritdoc />
    public override string icon => ImGuiIcon.ArrowsUpDownLeftRight;

    /// <inheritdoc />
    protected override (string name, Action<string>? setter) BindName(InspectionDrawContext context, Transform target)
        => ("Transform", null);

    /// <inheritdoc />
    protected override void Draw(InspectionDrawContext context, Transform target)
    {
        EditorWidget.SectionHeader(m_world ? "World Space" : "Local Space", m_world
            ? "Position, rotation, and scale include the parent hierarchy. Edits are stored as local values and support Undo/Redo."
            : "Position, rotation, and scale are relative to the parent. Rotation is expressed in degrees.");
        EditorWidget.PropertyRow("transform.world", "World", () =>
            EditorWidget.CompactCheckbox("##world", ref m_world),
            tooltip: "Display and edit world coordinates. Disable to edit values relative to the parent. This is an editor view preference.");

        bool invertible = true;
        if (m_world && target.parent is not null)
        {
            try { _ = target.parent.worldToLocalMatrix; }
            catch (InvalidOperationException) { invertible = false; }
        }
        if (!invertible)
            EditorWidget.HelpBox("World editing requires an invertible parent transform. Edit in Local Space or restore the parent's zero scale.",
                ImGuiIcon.TriangleExclamation, EditorPalette.warning);
        NativeImGui.BeginDisabled(!invertible);
        try
        {
            Vector3 position = m_world ? target.worldPosition : target.localPosition;
            Quaternion rotation = m_world ? target.worldRotation : target.localRotation;
            Vector3 euler = rotation.ToEulerAnglesZYXDegrees();
            Vector3 scale = m_world ? target.worldScale : target.localScale;
            DrawVector("Position", "Translation in world units.", position, value => Change(target, "localPosition", () =>
            {
                if (m_world) target.worldPosition = value;
                else target.localPosition = value;
            }));
            DrawVector("Rotation", "Euler rotation in degrees.", euler, value => Change(target, "localRotation", () =>
            {
                Quaternion next = Quaternion.FromEulerAnglesZYXDegrees(value);
                if (m_world) target.worldRotation = next;
                else target.localRotation = next;
            }));
            DrawVector("Scale", "Scale along each axis. One preserves the original size.", scale, value => Change(target, "localScale", () =>
            {
                if (m_world) target.worldScale = value;
                else target.localScale = value;
            }));
        }
        finally { NativeImGui.EndDisabled(); }
    }

    private void Change(Transform target, string property, Action mutation)
        => edits.ChangeProperty(target, property, mutation, "Change Transform " + property,
            $"transform:{target.identity.persistentId:N}:{property}:{m_world}");

    private static void DrawVector(string label, string tooltip, Vector3 value, Action<Vector3> apply)
    {
        EditorWidget.PropertyRow("transform." + label, label, () =>
        {
            float width = MathF.Max(1f, (NativeImGui.GetContentRegionAvail().X - NativeImGui.GetStyle().ItemSpacing.X * 2f) / 3f);
            bool changed = EditorWidget.AxisDragFloat(label, "X", ref value.x, width);
            NativeImGui.SameLine();
            changed |= EditorWidget.AxisDragFloat(label, "Y", ref value.y, width);
            NativeImGui.SameLine();
            changed |= EditorWidget.AxisDragFloat(label, "Z", ref value.z, width);
            if (changed) apply(value);
        }, tooltip: tooltip);
    }
}
