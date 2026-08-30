using System;
using System.Numerics;

using Inno.Editor.Core;
using Inno.Editor.Interactions;
using Inno.Editor.Rendering;
using Inno.Editor.Scene;
using Inno.Engine.Scene;
using Inno.Engine.Scene.Components;
using Inno.Native.ImGui;
using Inno.Native.ImGuizmo;
using NativeImGui = Inno.Native.ImGui.ImGui;
using NativeImGuizmo = Inno.Native.ImGuizmo.ImGuizmo;
using EngineMatrix = Inno.Core.Mathematics.Matrix;
using EngineQuaternion = Inno.Core.Mathematics.Quaternion;

namespace Inno.Editor.Panel.SceneView;

/// <summary>Presents the active Plugin provider and host-owned transform manipulation for the Scene viewport.</summary>
[EditorPanel("rendering.scene-view", "Scene", order: 210)]
internal sealed class SceneViewPanel : EditorPanel
{
    private const string C_VIEWPORT_ID = "scene-view";
    private static readonly EditorViewportKindId S_KIND = new("inno.editor.viewport.scene");

    private readonly EditorRenderingModule m_rendering;
    private readonly EditorInteractions m_interactions;
    private readonly SceneEdits m_sceneEdits;
    private ImGuizmoOperation m_operation = ImGuizmoOperation.Translate;
    private ImGuizmoMode m_mode = ImGuizmoMode.World;
    private Transform? m_gestureTarget;
    private TransformSnapshot m_gestureBefore;

    internal SceneViewPanel(
        EditorRenderingModule rendering,
        EditorInteractions interactions,
        SceneEdits sceneEdits)
    {
        m_rendering = rendering ?? throw new ArgumentNullException(nameof(rendering));
        m_interactions = interactions ?? throw new ArgumentNullException(nameof(interactions));
        m_sceneEdits = sceneEdits ?? throw new ArgumentNullException(nameof(sceneEdits));
    }

    /// <inheritdoc />
    public override bool useWindowPadding => false;

    /// <inheritdoc />
    protected override void OnDraw(EditorContext context)
    {
        _ = context;
        if (m_gestureTarget is not null && !NativeImGui.IsMouseDown(ImGuiMouseButton.Left))
            CommitGesture();

        DrawManipulationToolbar();
        NativeImGui.SameLine();
        Vector2 toolbarAvailable = NativeImGui.GetContentRegionAvail();
        m_rendering.DrawProviderToolbar(
            S_KIND,
            C_VIEWPORT_ID,
            Math.Max(1, (int)MathF.Floor(toolbarAvailable.X)),
            Math.Max(1, (int)MathF.Floor(toolbarAvailable.Y)));

        Vector2 available = NativeImGui.GetContentRegionAvail();
        int width = Math.Max(1, (int)MathF.Floor(available.X));
        int height = Math.Max(1, (int)MathF.Floor(available.Y));
        if (!m_rendering.TrySubmit(S_KIND, C_VIEWPORT_ID, width, height, out EditorViewportOutput output))
        {
            NativeImGui.TextUnformatted(
                m_rendering.GetProviderError(S_KIND) ?? "No active rendering provider for Scene View.");
            return;
        }
        if (!output.isReady)
        {
            NativeImGui.TextUnformatted("Preparing Scene View GPU target...");
            return;
        }

        m_rendering.Draw(output, new Vector2(width, height));
        Vector2 minimum = NativeImGui.GetItemRectMin();
        Vector2 maximum = NativeImGui.GetItemRectMax();
        bool gizmoOwnsPointer = DrawTransformGizmo(minimum, maximum);
        if (!NativeImGui.IsItemClicked(ImGuiMouseButton.Left) || gizmoOwnsPointer)
            return;
        Vector2 mouse = NativeImGui.GetMousePos();
        float x = (mouse.X - minimum.X) / Math.Max(1f, maximum.X - minimum.X);
        float y = (mouse.Y - minimum.Y) / Math.Max(1f, maximum.Y - minimum.Y);
        m_rendering.HandlePointer(S_KIND, C_VIEWPORT_ID, width, height, x, y, button: 0);
    }

    /// <inheritdoc />
    protected override void OnDetach(EditorContext context)
    {
        _ = context;
        CommitGesture();
        m_rendering.Release(C_VIEWPORT_ID);
    }

    private void DrawManipulationToolbar()
    {
        if (NativeImGui.Button(m_operation == ImGuizmoOperation.Translate ? "Move *" : "Move"))
            m_operation = ImGuizmoOperation.Translate;
        NativeImGui.SameLine();
        if (NativeImGui.Button(m_operation == ImGuizmoOperation.Rotate ? "Rotate *" : "Rotate"))
            m_operation = ImGuizmoOperation.Rotate;
        NativeImGui.SameLine();
        if (NativeImGui.Button(m_operation == ImGuizmoOperation.Scale ? "Scale *" : "Scale"))
            m_operation = ImGuizmoOperation.Scale;
        NativeImGui.SameLine();
        if (NativeImGui.Button(m_mode == ImGuizmoMode.Local ? "Local" : "World"))
            m_mode = m_mode == ImGuizmoMode.Local ? ImGuizmoMode.World : ImGuizmoMode.Local;
    }

    private unsafe bool DrawTransformGizmo(Vector2 minimum, Vector2 maximum)
    {
        if (!m_rendering.TryGetManipulationSpace(
                C_VIEWPORT_ID,
                out EditorViewportManipulationSpace manipulationSpace)
            || !TryGetSelectedTransform(out Transform? selected))
        {
            return false;
        }

        Transform target = m_gestureTarget ?? selected!;
        EngineMatrix world = EngineMatrix.CreateTranslation(target.worldPosition)
            * EngineMatrix.CreateFromQuaternion(target.worldRotation)
            * EngineMatrix.CreateScale(target.worldScale);
        float* view = stackalloc float[16];
        float* projection = stackalloc float[16];
        float* model = stackalloc float[16];
        WriteColumnMajor(manipulationSpace.viewMatrix, view);
        WriteColumnMajor(manipulationSpace.projectionMatrix, projection);
        WriteColumnMajor(world, model);

        NativeImGuizmo.BeginFrame();
        NativeImGuizmo.SetDrawlist(NativeImGui.GetWindowDrawList());
        NativeImGuizmo.SetRect(
            minimum.X,
            minimum.Y,
            MathF.Max(1f, maximum.X - minimum.X),
            MathF.Max(1f, maximum.Y - minimum.Y));
        NativeImGuizmo.SetOrthographic(manipulationSpace.isOrthographic);
        ImGuizmoMode effectiveMode = m_operation == ImGuizmoOperation.Scale
            ? ImGuizmoMode.Local
            : m_mode;
        bool changed = NativeImGuizmo.Manipulate(
            view,
            projection,
            m_operation,
            effectiveMode,
            model);
        bool isUsing = NativeImGuizmo.IsUsing();
        if (isUsing && m_gestureTarget is null)
        {
            m_gestureTarget = target;
            m_gestureBefore = TransformSnapshot.Capture(target);
        }
        if (changed && EngineMatrix.Decompose(
                ReadColumnMajor(model),
                out Inno.Core.Mathematics.Vector3 scale,
                out EngineQuaternion rotation,
                out Inno.Core.Mathematics.Vector3 position)
            && IsUsable(position, rotation, scale))
        {
            target.worldPosition = position;
            target.worldRotation = rotation;
            target.worldScale = scale;
        }
        if (!isUsing && m_gestureTarget is not null)
            CommitGesture();
        return isUsing || NativeImGuizmo.IsOver(m_operation);
    }

    private bool TryGetSelectedTransform(out Transform? transform)
    {
        transform = m_interactions.selection.selectedTarget switch
        {
            GameObject gameObject when !gameObject.isDestroyed => gameObject.transform,
            Transform selectedTransform when !selectedTransform.isDestroyed => selectedTransform,
            GameComponent component when !component.isDestroyed => component.transform,
            _ => null
        };
        return transform is not null;
    }

    private void CommitGesture()
    {
        Transform? target = m_gestureTarget;
        if (target is null)
            return;
        TransformSnapshot before = m_gestureBefore;
        m_gestureTarget = null;
        if (target.isDestroyed)
            return;
        TransformSnapshot after = TransformSnapshot.Capture(target);
        before.ApplyLocal(target);
        string name = m_operation switch
        {
            ImGuizmoOperation.Rotate => "Rotate GameObject",
            ImGuizmoOperation.Scale => "Scale GameObject",
            _ => "Move GameObject"
        };
        using EditorHistoryTransaction transaction = m_interactions.history.BeginTransaction(name);
        _ = m_sceneEdits.ChangeProperty(
            target,
            nameof(Transform.localPosition),
            () => target.localPosition = after.position,
            name);
        _ = m_sceneEdits.ChangeProperty(
            target,
            nameof(Transform.localRotation),
            () => target.localRotation = after.rotation,
            name);
        _ = m_sceneEdits.ChangeProperty(
            target,
            nameof(Transform.localScale),
            () => target.localScale = after.scale,
            name);
        transaction.Commit();
    }

    private static bool IsUsable(
        Inno.Core.Mathematics.Vector3 position,
        EngineQuaternion rotation,
        Inno.Core.Mathematics.Vector3 scale)
        => float.IsFinite(position.x)
           && float.IsFinite(position.y)
           && float.IsFinite(position.z)
           && float.IsFinite(rotation.x)
           && float.IsFinite(rotation.y)
           && float.IsFinite(rotation.z)
           && float.IsFinite(rotation.w)
           && float.IsFinite(scale.x)
           && float.IsFinite(scale.y)
           && float.IsFinite(scale.z)
           && MathF.Abs(scale.x) > 0.00001f
           && MathF.Abs(scale.y) > 0.00001f
           && MathF.Abs(scale.z) > 0.00001f;

    private static unsafe void WriteColumnMajor(EngineMatrix matrix, float* destination)
    {
        destination[0] = matrix.m11;
        destination[1] = matrix.m21;
        destination[2] = matrix.m31;
        destination[3] = matrix.m41;
        destination[4] = matrix.m12;
        destination[5] = matrix.m22;
        destination[6] = matrix.m32;
        destination[7] = matrix.m42;
        destination[8] = matrix.m13;
        destination[9] = matrix.m23;
        destination[10] = matrix.m33;
        destination[11] = matrix.m43;
        destination[12] = matrix.m14;
        destination[13] = matrix.m24;
        destination[14] = matrix.m34;
        destination[15] = matrix.m44;
    }

    private static unsafe EngineMatrix ReadColumnMajor(float* source)
        => new(
            source[0], source[4], source[8], source[12],
            source[1], source[5], source[9], source[13],
            source[2], source[6], source[10], source[14],
            source[3], source[7], source[11], source[15]);

    private readonly record struct TransformSnapshot(
        Inno.Core.Mathematics.Vector3 position,
        EngineQuaternion rotation,
        Inno.Core.Mathematics.Vector3 scale)
    {
        internal static TransformSnapshot Capture(Transform transform)
            => new(transform.localPosition, transform.localRotation, transform.localScale);

        internal void ApplyLocal(Transform transform)
        {
            transform.localPosition = position;
            transform.localRotation = rotation;
            transform.localScale = scale;
        }
    }
}
