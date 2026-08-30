using System;
using System.Numerics;

using Inno.Editor.Core;
using Inno.Editor.Interactions;
using Inno.Editor.Rendering;
using Inno.Editor.Scene;
using Inno.Editor.Settings;
using Inno.Engine.Scene;
using Inno.Engine.Scene.Components;
using Inno.Native.ImGui;
using Inno.Native.ImGuizmo;
using NativeImGui = Inno.Native.ImGui.ImGui;
using NativeImGuizmo = Inno.Native.ImGuizmo.ImGuizmo;
using EngineMatrix = Inno.Core.Mathematics.Matrix;
using EngineQuaternion = Inno.Core.Mathematics.Quaternion;
using EngineVector3 = Inno.Core.Mathematics.Vector3;

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
    private readonly EditorSettings m_settings;
    private Vector4 m_backgroundColor;
    private int m_cameraPanButton = -1;
    private ImGuizmoOperation m_operation = ImGuizmoOperation.Translate;
    private ImGuizmoMode m_mode = ImGuizmoMode.World;
    private Transform? m_gestureTarget;
    private TransformSnapshot m_gestureBefore;

    internal SceneViewPanel(
        EditorRenderingModule rendering,
        EditorInteractions interactions,
        SceneEdits sceneEdits,
        EditorSettings settings)
    {
        m_rendering = rendering ?? throw new ArgumentNullException(nameof(rendering));
        m_interactions = interactions ?? throw new ArgumentNullException(nameof(interactions));
        m_sceneEdits = sceneEdits ?? throw new ArgumentNullException(nameof(sceneEdits));
        m_settings = settings ?? throw new ArgumentNullException(nameof(settings));
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

        Vector2 available = NativeImGui.GetContentRegionAvail();
        int width = Math.Max(1, (int)MathF.Floor(available.X));
        int height = Math.Max(1, (int)MathF.Floor(available.Y));
        m_rendering.SetPresentation(
            C_VIEWPORT_ID,
            new EditorViewportPresentation(ToEngineColor(m_backgroundColor)));
        if (!m_rendering.TrySubmit(S_KIND, C_VIEWPORT_ID, width, height, out EditorViewportOutput output))
        {
            DrawUnavailable(
                available,
                m_rendering.GetProviderError(S_KIND) ?? "No active rendering provider for Scene View.");
            return;
        }
        if (!output.isReady)
        {
            DrawUnavailable(available, "Preparing Scene View GPU target...");
            return;
        }

        m_rendering.Draw(output, new Vector2(width, height));
        Vector2 minimum = NativeImGui.GetItemRectMin();
        Vector2 maximum = NativeImGui.GetItemRectMax();
        bool hovered = NativeImGui.IsItemHovered();
        bool cameraOwnsPointer = HandleCameraNavigation(hovered, minimum, maximum);
        bool gizmoOwnsPointer = !cameraOwnsPointer && DrawTransformGizmo(minimum, maximum);
        if (!NativeImGui.IsItemClicked(ImGuiMouseButton.Left)
            || gizmoOwnsPointer
            || cameraOwnsPointer
            || NativeImGui.GetIO().KeyAlt)
            return;
        Vector2 mouse = NativeImGui.GetMousePos();
        float x = (mouse.X - minimum.X) / Math.Max(1f, maximum.X - minimum.X);
        float y = (mouse.Y - minimum.Y) / Math.Max(1f, maximum.Y - minimum.Y);
        m_rendering.HandlePointer(S_KIND, C_VIEWPORT_ID, width, height, x, y, button: 0);
    }

    /// <inheritdoc />
    protected override void OnAttach(EditorContext context)
    {
        _ = context;
        ApplySettings(m_settings);
        m_settings.changed += ApplySettings;
    }

    /// <inheritdoc />
    protected override void OnDetach(EditorContext context)
    {
        _ = context;
        m_settings.changed -= ApplySettings;
        m_cameraPanButton = -1;
        CommitGesture();
        m_rendering.Release(C_VIEWPORT_ID);
    }

    /// <inheritdoc />
    protected override void Capture(EditorState state)
    {
        EditorViewportCamera camera = m_rendering.GetCamera(C_VIEWPORT_ID);
        state.Set("camera.initialized", camera.isInitialized);
        if (!camera.isInitialized)
            return;
        state.Set("camera.position.x", camera.position.x);
        state.Set("camera.position.y", camera.position.y);
        state.Set("camera.position.z", camera.position.z);
        state.Set("camera.rotation.x", camera.rotation.x);
        state.Set("camera.rotation.y", camera.rotation.y);
        state.Set("camera.rotation.z", camera.rotation.z);
        state.Set("camera.rotation.w", camera.rotation.w);
        state.Set("camera.orthographicSize", camera.orthographicSize);
    }

    /// <inheritdoc />
    protected override void Restore(EditorState state)
    {
        if (!state.Get("camera.initialized", false))
            return;
        var position = new EngineVector3(
            state.Get("camera.position.x", 0f),
            state.Get("camera.position.y", 0f),
            state.Get("camera.position.z", 0f));
        var rotation = new EngineQuaternion(
            state.Get("camera.rotation.x", 0f),
            state.Get("camera.rotation.y", 0f),
            state.Get("camera.rotation.z", 0f),
            state.Get("camera.rotation.w", 1f));
        float size = state.Get("camera.orthographicSize", 5f);
        try
        {
            m_rendering.GetCamera(C_VIEWPORT_ID).ConfigureOrthographic(position, rotation, size);
        }
        catch (ArgumentException)
        {
            // Malformed workspace camera state is ignored and the active provider supplies a fresh default.
        }
    }

    private void ApplySettings(EditorSettings settings)
        => m_backgroundColor = SceneViewBackgroundSetting.Read(settings);

    private bool HandleCameraNavigation(bool hovered, Vector2 minimum, Vector2 maximum)
    {
        EditorViewportCamera camera = m_rendering.GetCamera(C_VIEWPORT_ID);
        if (!camera.isInitialized || !camera.isOrthographic)
            return false;

        ImGuiIOPtr io = NativeImGui.GetIO();
        if (m_cameraPanButton >= 0
            && !NativeImGui.IsMouseDown((ImGuiMouseButton)m_cameraPanButton))
        {
            m_cameraPanButton = -1;
        }
        if (hovered && m_cameraPanButton < 0)
        {
            if (NativeImGui.IsMouseClicked(ImGuiMouseButton.Middle))
                m_cameraPanButton = (int)ImGuiMouseButton.Middle;
            else if (io.KeyAlt && NativeImGui.IsMouseClicked(ImGuiMouseButton.Left))
                m_cameraPanButton = (int)ImGuiMouseButton.Left;
        }

        bool panning = m_cameraPanButton >= 0;
        if (!hovered && !panning)
            return false;
        float width = MathF.Max(1f, maximum.X - minimum.X);
        float height = MathF.Max(1f, maximum.Y - minimum.Y);
        if (panning)
        {
            float worldPerPixel = camera.orthographicSize * 2f / height;
            EngineVector3 localDelta = new(
                -io.MouseDelta.X * worldPerPixel,
                io.MouseDelta.Y * worldPerPixel,
                0f);
            camera.position += EngineVector3.Transform(localDelta, camera.rotation);
            NativeImGui.SetMouseCursor(ImGuiMouseCursor.ResizeAll);
        }

        if (hovered && io.MouseWheel != 0f)
        {
            Vector2 mouse = NativeImGui.GetMousePos();
            float normalizedX = Math.Clamp((mouse.X - minimum.X) / width, 0f, 1f);
            float normalizedY = Math.Clamp((mouse.Y - minimum.Y) / height, 0f, 1f);
            float aspect = width / height;
            float previousSize = camera.orthographicSize;
            EngineVector3 previousOffset = GetViewportOffset(
                normalizedX,
                normalizedY,
                previousSize,
                aspect,
                camera.rotation);
            float nextSize = Math.Clamp(
                previousSize * MathF.Exp(-io.MouseWheel * 0.16f),
                0.01f,
                100000f);
            EngineVector3 nextOffset = GetViewportOffset(
                normalizedX,
                normalizedY,
                nextSize,
                aspect,
                camera.rotation);
            camera.position += previousOffset - nextOffset;
            camera.orthographicSize = nextSize;
        }
        return panning;
    }

    private static EngineVector3 GetViewportOffset(
        float normalizedX,
        float normalizedY,
        float halfHeight,
        float aspect,
        EngineQuaternion rotation)
    {
        var local = new EngineVector3(
            (normalizedX * 2f - 1f) * halfHeight * aspect,
            (1f - normalizedY * 2f) * halfHeight,
            0f);
        return EngineVector3.Transform(local, rotation);
    }

    private void DrawUnavailable(Vector2 size, string message)
    {
        Vector2 minimum = NativeImGui.GetCursorScreenPos();
        Vector2 maximum = minimum + size;
        NativeImGui.GetWindowDrawList().AddRectFilled(
            minimum,
            maximum,
            NativeImGui.ColorConvertFloat4ToU32(m_backgroundColor));
        NativeImGui.TextUnformatted(message);
    }

    private static Inno.Core.Mathematics.Color ToEngineColor(Vector4 value)
        => new(value.X, value.Y, value.Z, value.W);

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
