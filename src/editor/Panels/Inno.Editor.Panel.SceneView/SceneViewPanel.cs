using System;
using System.Collections.Generic;
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
using Inno.Rendering;
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
    private readonly IEditorSceneWorkspace m_workspace;
    private readonly EditorSettings m_settings;
    private Vector4 m_backgroundColor;
    private NavigationDrag m_navigationDrag;
    private ImGuiMouseButton m_navigationDragButton;
    private ImGuizmoOperation m_operation = ImGuizmoOperation.Translate;
    private ImGuizmoMode m_mode = ImGuizmoMode.World;
    private Transform? m_gestureTarget;
    private TransformSnapshot m_gestureBefore;

    internal SceneViewPanel(
        EditorRenderingModule rendering,
        EditorInteractions interactions,
        SceneEdits sceneEdits,
        IEditorSceneWorkspace workspace,
        EditorSettings settings)
    {
        m_rendering = rendering ?? throw new ArgumentNullException(nameof(rendering));
        m_interactions = interactions ?? throw new ArgumentNullException(nameof(interactions));
        m_sceneEdits = sceneEdits ?? throw new ArgumentNullException(nameof(sceneEdits));
        m_workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
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
        m_rendering.SetContentScope(C_VIEWPORT_ID, CreateContentScope());
        Vector2 minimum = NativeImGui.GetCursorScreenPos();
        Vector2 maximum = minimum + new Vector2(width, height);
        bool hovered = NativeImGui.IsMouseHoveringRect(minimum, maximum);
        _ = m_rendering.TryConfigureNavigation(
            S_KIND,
            C_VIEWPORT_ID,
            width,
            height,
            out EditorViewportNavigationProfile navigationProfile);
        bool navigationOwnsPointer = HandleNavigation(
            navigationProfile,
            hovered,
            minimum,
            maximum);
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
        minimum = NativeImGui.GetItemRectMin();
        maximum = NativeImGui.GetItemRectMax();
        bool gizmoOwnsPointer = !navigationOwnsPointer && DrawTransformGizmo(minimum, maximum);
        if (!NativeImGui.IsItemClicked(ImGuiMouseButton.Left)
            || gizmoOwnsPointer
            || navigationOwnsPointer
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
        m_navigationDrag = NavigationDrag.None;
        CommitGesture();
        m_rendering.Release(C_VIEWPORT_ID);
    }

    /// <inheritdoc />
    protected override void Capture(EditorState state)
    {
        EditorViewportNavigationState navigation = m_rendering.GetNavigationState(C_VIEWPORT_ID);
        state.Set("navigation.initialized", navigation.isInitialized);
        if (!navigation.isInitialized)
            return;
        state.Set("navigation.position.x", navigation.position.x);
        state.Set("navigation.position.y", navigation.position.y);
        state.Set("navigation.position.z", navigation.position.z);
        state.Set("navigation.rotation.x", navigation.rotation.x);
        state.Set("navigation.rotation.y", navigation.rotation.y);
        state.Set("navigation.rotation.z", navigation.rotation.z);
        state.Set("navigation.rotation.w", navigation.rotation.w);
        state.Set("navigation.pivot.x", navigation.pivot.x);
        state.Set("navigation.pivot.y", navigation.pivot.y);
        state.Set("navigation.pivot.z", navigation.pivot.z);
        state.Set("navigation.projection", (int)navigation.projection);
        state.Set("navigation.mode", (int)navigation.mode);
        state.Set("navigation.orthographicSize", navigation.orthographicSize);
        state.Set("navigation.fieldOfView", navigation.fieldOfView);
        state.Set("navigation.nearClip", navigation.nearClip);
        state.Set("navigation.farClip", navigation.farClip);
        state.Set("navigation.focusDistance", navigation.focusDistance);
        state.Set("navigation.movementSpeed", navigation.movementSpeed);
    }

    /// <inheritdoc />
    protected override void Restore(EditorState state)
    {
        if (!state.Get("navigation.initialized", false))
            return;
        var position = new EngineVector3(
            state.Get("navigation.position.x", 0f),
            state.Get("navigation.position.y", 0f),
            state.Get("navigation.position.z", 0f));
        var rotation = new EngineQuaternion(
            state.Get("navigation.rotation.x", 0f),
            state.Get("navigation.rotation.y", 0f),
            state.Get("navigation.rotation.z", 0f),
            state.Get("navigation.rotation.w", 1f));
        var pivot = new EngineVector3(
            state.Get("navigation.pivot.x", 0f),
            state.Get("navigation.pivot.y", 0f),
            state.Get("navigation.pivot.z", 0f));
        EditorViewportProjection projection = (EditorViewportProjection)state.Get(
            "navigation.projection",
            (int)EditorViewportProjection.Orthographic);
        try
        {
            EditorViewportNavigationState navigation = m_rendering.GetNavigationState(C_VIEWPORT_ID);
            if (projection == EditorViewportProjection.Perspective)
            {
                navigation.ConfigurePerspective(
                    position,
                    rotation,
                    state.Get("navigation.fieldOfView", 60f),
                    state.Get("navigation.nearClip", 0.01f),
                    state.Get("navigation.farClip", 1000f));
            }
            else
            {
                navigation.ConfigureOrthographic(
                    position,
                    rotation,
                    state.Get("navigation.orthographicSize", 5f));
            }
            navigation.pivot = pivot;
            navigation.focusDistance = state.Get("navigation.focusDistance", 10f);
            navigation.movementSpeed = state.Get("navigation.movementSpeed", 5f);
            navigation.mode = (EditorViewportNavigationMode)state.Get(
                "navigation.mode",
                (int)EditorViewportNavigationMode.Planar);
        }
        catch (ArgumentException)
        {
            // Malformed workspace camera state is ignored and the active provider supplies a fresh default.
        }
    }

    private void ApplySettings(EditorSettings settings)
        => m_backgroundColor = SceneViewBackgroundSetting.Read(settings);

    private RenderContentScope CreateContentScope()
    {
        var contents = new List<RenderContentReference>(m_workspace.scenes.Count);
        RenderContentId? activeContent = null;
        foreach (GameScene scene in m_workspace.scenes)
        {
            if (scene.isDestroyed)
                continue;
            var contentId = new RenderContentId(scene.identity.persistentId);
            contents.Add(new RenderContentReference(contentId, scene));
            if (ReferenceEquals(scene, m_workspace.activeScene))
                activeContent = contentId;
        }
        return new RenderContentScope(contents, activeContent);
    }

    private bool HandleNavigation(
        EditorViewportNavigationProfile profile,
        bool hovered,
        Vector2 minimum,
        Vector2 maximum)
    {
        EditorViewportNavigationState navigation = m_rendering.GetNavigationState(C_VIEWPORT_ID);
        if (!navigation.isInitialized
            || profile.capabilities == EditorViewportNavigationCapabilities.None)
        {
            m_navigationDrag = NavigationDrag.None;
            return false;
        }

        if (!SupportsMode(profile, navigation.mode))
            navigation.mode = profile.defaultMode;
        ImGuiIOPtr io = NativeImGui.GetIO();
        if (m_navigationDrag != NavigationDrag.None
            && !NativeImGui.IsMouseDown(m_navigationDragButton))
        {
            m_navigationDrag = NavigationDrag.None;
        }
        if (hovered && m_navigationDrag == NavigationDrag.None)
        {
            if (profile.capabilities.HasFlag(EditorViewportNavigationCapabilities.Pan)
                && NativeImGui.IsMouseClicked(ImGuiMouseButton.Middle))
            {
                m_navigationDrag = NavigationDrag.Pan;
                m_navigationDragButton = ImGuiMouseButton.Middle;
            }
            else if (io.KeyAlt && NativeImGui.IsMouseClicked(ImGuiMouseButton.Left))
            {
                if (profile.capabilities.HasFlag(EditorViewportNavigationCapabilities.Orbit))
                {
                    m_navigationDrag = NavigationDrag.Orbit;
                    m_navigationDragButton = ImGuiMouseButton.Left;
                }
                else if (profile.capabilities.HasFlag(EditorViewportNavigationCapabilities.Pan))
                {
                    m_navigationDrag = NavigationDrag.Pan;
                    m_navigationDragButton = ImGuiMouseButton.Left;
                }
            }
            else if (NativeImGui.IsMouseClicked(ImGuiMouseButton.Right)
                     && profile.capabilities.HasFlag(EditorViewportNavigationCapabilities.Fly))
            {
                m_navigationDrag = NavigationDrag.Fly;
                m_navigationDragButton = ImGuiMouseButton.Right;
            }
        }

        bool ownsPointer = m_navigationDrag != NavigationDrag.None;
        if (!hovered && !ownsPointer)
            return false;
        float width = MathF.Max(1f, maximum.X - minimum.X);
        float height = MathF.Max(1f, maximum.Y - minimum.Y);
        float aspect = width / height;
        switch (m_navigationDrag)
        {
            case NavigationDrag.Pan:
                Pan(navigation, io.MouseDelta, height);
                NativeImGui.SetMouseCursor(ImGuiMouseCursor.ResizeAll);
                break;
            case NavigationDrag.Orbit:
                Orbit(navigation, profile, io.MouseDelta);
                break;
            case NavigationDrag.Fly:
                Fly(navigation, profile, io);
                break;
        }

        if (hovered
            && profile.capabilities.HasFlag(EditorViewportNavigationCapabilities.Zoom)
            && io.MouseWheel != 0f)
            Zoom(navigation, profile, io.MouseWheel, minimum, width, height, aspect);
        if (hovered
            && profile.capabilities.HasFlag(EditorViewportNavigationCapabilities.FrameSelection)
            && NativeImGui.IsKeyPressed(ImGuiKey.F, repeat: false))
        {
            EditorViewportFocusBounds? bounds = profile.focusBounds;
            if (bounds is null && TryGetSelectedTransform(out Transform? selected))
                bounds = new EditorViewportFocusBounds(selected!.worldPosition, 0.5f);
            if (bounds is EditorViewportFocusBounds focus)
                Frame(navigation, profile, focus);
        }
        return ownsPointer;
    }

    private static void Pan(EditorViewportNavigationState navigation, Vector2 mouseDelta, float height)
    {
        float verticalSpan = navigation.projection == EditorViewportProjection.Orthographic
            ? navigation.orthographicSize * 2f
            : 2f * navigation.focusDistance
                * MathF.Tan(navigation.fieldOfView * MathF.PI / 360f);
        float unitsPerPixel = verticalSpan / height;
        EngineVector3 right = EngineVector3.Transform(EngineVector3.RIGHT, navigation.rotation);
        EngineVector3 up = EngineVector3.Transform(EngineVector3.UP, navigation.rotation);
        EngineVector3 delta = right * (-mouseDelta.X * unitsPerPixel)
            + up * (mouseDelta.Y * unitsPerPixel);
        navigation.position += delta;
        navigation.pivot += delta;
    }

    private static void Orbit(
        EditorViewportNavigationState navigation,
        EditorViewportNavigationProfile profile,
        Vector2 mouseDelta)
    {
        EngineVector3 worldUp = GetWorldUp(profile);
        float sensitivity = GetPositive(profile.rotationSensitivity, 0.005f);
        EngineQuaternion yaw = EngineQuaternion.CreateFromAxisAngle(
            worldUp,
            -mouseDelta.X * sensitivity);
        EngineQuaternion yawed = (yaw * navigation.rotation).normalized;
        EngineVector3 right = EngineVector3.Transform(EngineVector3.RIGHT, yawed).normalized;
        navigation.rotation = ApplyConstrainedPitch(
            yawed,
            right,
            worldUp,
            -mouseDelta.Y * sensitivity);
        navigation.mode = EditorViewportNavigationMode.Orbit;
        EngineVector3 forward = EngineVector3.Transform(EngineVector3.FORWARD, navigation.rotation);
        navigation.position = navigation.pivot - forward * navigation.focusDistance;
    }

    private static void Fly(
        EditorViewportNavigationState navigation,
        EditorViewportNavigationProfile profile,
        ImGuiIOPtr io)
    {
        float sensitivity = GetPositive(profile.rotationSensitivity, 0.005f);
        EngineVector3 worldUp = GetWorldUp(profile);
        EngineQuaternion yaw = EngineQuaternion.CreateFromAxisAngle(
            worldUp,
            -io.MouseDelta.X * sensitivity);
        EngineQuaternion yawed = (yaw * navigation.rotation).normalized;
        EngineVector3 right = EngineVector3.Transform(EngineVector3.RIGHT, yawed).normalized;
        navigation.rotation = ApplyConstrainedPitch(
            yawed,
            right,
            worldUp,
            -io.MouseDelta.Y * sensitivity);
        navigation.mode = EditorViewportNavigationMode.Fly;

        EngineVector3 movement = EngineVector3.ZERO;
        EngineVector3 forward = EngineVector3.Transform(EngineVector3.FORWARD, navigation.rotation);
        right = EngineVector3.Transform(EngineVector3.RIGHT, navigation.rotation);
        if (NativeImGui.IsKeyDown(ImGuiKey.W)) movement += forward;
        if (NativeImGui.IsKeyDown(ImGuiKey.S)) movement -= forward;
        if (NativeImGui.IsKeyDown(ImGuiKey.D)) movement += right;
        if (NativeImGui.IsKeyDown(ImGuiKey.A)) movement -= right;
        if (NativeImGui.IsKeyDown(ImGuiKey.E)) movement += worldUp;
        if (NativeImGui.IsKeyDown(ImGuiKey.Q)) movement -= worldUp;
        if (movement.LengthSquared() > 0.000001f)
        {
            float multiplier = io.KeyShift
                ? GetPositive(profile.fastMovementMultiplier, 4f)
                : 1f;
            navigation.position += movement.normalized
                * navigation.movementSpeed
                * multiplier
                * MathF.Max(0f, io.DeltaTime);
            navigation.pivot = navigation.position + forward * navigation.focusDistance;
        }
    }

    private static void Zoom(
        EditorViewportNavigationState navigation,
        EditorViewportNavigationProfile profile,
        float wheel,
        Vector2 minimum,
        float width,
        float height,
        float aspect)
    {
        float zoomSensitivity = GetPositive(profile.zoomSensitivity, 0.16f);
        if (navigation.projection == EditorViewportProjection.Orthographic)
        {
            Vector2 mouse = NativeImGui.GetMousePos();
            float normalizedX = Math.Clamp((mouse.X - minimum.X) / width, 0f, 1f);
            float normalizedY = Math.Clamp((mouse.Y - minimum.Y) / height, 0f, 1f);
            float previousSize = navigation.orthographicSize;
            EngineVector3 previousOffset = GetViewportOffset(
                normalizedX,
                normalizedY,
                previousSize,
                aspect,
                navigation.rotation);
            float minimumSize = GetPositive(profile.minimumOrthographicSize, 0.001f);
            float maximumSize = MathF.Max(
                minimumSize,
                GetPositive(profile.maximumOrthographicSize, 100000f));
            float nextSize = Math.Clamp(
                previousSize * MathF.Exp(-wheel * zoomSensitivity),
                minimumSize,
                maximumSize);
            EngineVector3 nextOffset = GetViewportOffset(
                normalizedX,
                normalizedY,
                nextSize,
                aspect,
                navigation.rotation);
            EngineVector3 delta = previousOffset - nextOffset;
            navigation.position += delta;
            navigation.pivot += delta;
            navigation.orthographicSize = nextSize;
            return;
        }

        float minimumDistance = GetPositive(profile.minimumFocusDistance, 0.01f);
        float maximumDistance = MathF.Max(
            minimumDistance,
            GetPositive(profile.maximumFocusDistance, 1000000f));
        navigation.focusDistance = Math.Clamp(
            navigation.focusDistance * MathF.Exp(-wheel * zoomSensitivity),
            minimumDistance,
            maximumDistance);
        EngineVector3 direction = EngineVector3.Transform(EngineVector3.FORWARD, navigation.rotation);
        navigation.position = navigation.pivot - direction * navigation.focusDistance;
    }

    private static void Frame(
        EditorViewportNavigationState navigation,
        EditorViewportNavigationProfile profile,
        EditorViewportFocusBounds focus)
    {
        float padding = GetPositive(profile.framePadding, 1.25f);
        float radius = MathF.Max(focus.radius, 0.01f);
        navigation.pivot = focus.center;
        EngineVector3 forward = EngineVector3.Transform(EngineVector3.FORWARD, navigation.rotation);
        if (navigation.projection == EditorViewportProjection.Orthographic)
        {
            float minimumSize = GetPositive(profile.minimumOrthographicSize, 0.001f);
            float maximumSize = MathF.Max(
                minimumSize,
                GetPositive(profile.maximumOrthographicSize, 100000f));
            navigation.orthographicSize = Math.Clamp(radius * padding, minimumSize, maximumSize);
            navigation.focusDistance = MathF.Max(navigation.focusDistance, radius * 2f);
        }
        else
        {
            float halfFov = navigation.fieldOfView * MathF.PI / 360f;
            float distance = radius * padding / MathF.Max(0.001f, MathF.Tan(halfFov));
            navigation.focusDistance = Math.Clamp(
                distance,
                GetPositive(profile.minimumFocusDistance, 0.01f),
                GetPositive(profile.maximumFocusDistance, 1000000f));
        }
        navigation.position = focus.center - forward * navigation.focusDistance;
    }

    private static bool SupportsMode(
        EditorViewportNavigationProfile profile,
        EditorViewportNavigationMode mode)
        => mode switch
        {
            EditorViewportNavigationMode.Orbit =>
                profile.capabilities.HasFlag(EditorViewportNavigationCapabilities.Orbit),
            EditorViewportNavigationMode.Fly =>
                profile.capabilities.HasFlag(EditorViewportNavigationCapabilities.Fly),
            _ => (profile.capabilities & EditorViewportNavigationCapabilities.Planar) != 0
        };

    private static EngineQuaternion ApplyConstrainedPitch(
        EngineQuaternion yawed,
        EngineVector3 right,
        EngineVector3 worldUp,
        float angle)
    {
        EngineQuaternion pitch = EngineQuaternion.CreateFromAxisAngle(right, angle);
        EngineQuaternion candidate = (pitch * yawed).normalized;
        EngineVector3 forward = EngineVector3.Transform(EngineVector3.FORWARD, candidate).normalized;
        return MathF.Abs(EngineVector3.Dot(forward, worldUp)) < 0.999f
            ? candidate
            : yawed;
    }

    private static EngineVector3 GetWorldUp(EditorViewportNavigationProfile profile)
        => profile.worldUp.LengthSquared() > 0.000001f
            ? profile.worldUp.normalized
            : EngineVector3.UP;

    private static float GetPositive(float value, float fallback)
        => float.IsFinite(value) && value > 0f ? value : fallback;

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
        EngineMatrix world = target.localToWorldMatrix;
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
            target.SetWorldTransform(position, rotation, scale);
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

    private enum NavigationDrag
    {
        None,
        Pan,
        Orbit,
        Fly
    }
}
