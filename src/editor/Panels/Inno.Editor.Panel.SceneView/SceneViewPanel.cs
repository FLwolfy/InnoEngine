using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using Inno.Core.Identity;
using Inno.Core.Mathematics;
using Inno.Editor.Core;
using Inno.Editor.Interactions;
using Inno.Editor.Rendering;
using Inno.Editor.Scene;
using Inno.Engine.Scene;
using Inno.Engine.Scene.Components;
using Inno.Engine.Scene.Layers;
using Inno.Native.ImGui;
using Inno.Native.ImGuizmo;
using Inno.Rendering;
using NativeImGui = Inno.Native.ImGui.ImGui;
using NumericVector2 = System.Numerics.Vector2;

namespace Inno.Editor.Panel.SceneView;

/// <summary>Shows a pipeline-rendered editor camera without modifying runtime Camera components.</summary>
[EditorPanel("rendering.scene-view", "Scene", order: 210)]
internal sealed unsafe class SceneViewPanel : EditorPanel
{
    private const string C_VIEWPORT_ID = "scene-view";

    private readonly EditorRenderingModule m_rendering;
    private readonly EditorInteractions m_interactions;
    private readonly SceneEdits m_sceneEdits;
    private RenderPath m_renderPath = RenderPath.Automatic;
    private ImGuizmoOperation m_gizmoOperation = ImGuizmoOperation.Translate;
    private ImGuizmoMode m_gizmoMode = ImGuizmoMode.World;
    private GizmoDragState? m_gizmoDrag;
    private string? m_pipelineAssetPath;
    private bool m_pipelineActivationPending;

    /// <inheritdoc />
    public override bool useWindowPadding => false;

    /// <summary>Creates the Scene View panel.</summary>
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
    protected override void OnDraw(EditorContext context)
    {
        ActivateRestoredPipeline();
        DrawToolbar();
        NumericVector2 available = NativeImGui.GetContentRegionAvail();
        int width = Math.Max(1, (int)MathF.Floor(available.X));
        int height = Math.Max(1, (int)MathF.Floor(available.Y));
        Vector3 position = new(0f, 3f, -6f);
        Matrix view = Matrix.CreateLookAt(position, new Vector3(0f, 1f, 0f), Vector3.UP);
        Matrix projection = Matrix.CreatePerspectiveFieldOfView(
            MathHelper.ToRadians(60f),
            width / (float)height,
            0.05f,
            2000f);
        var renderView = new RenderView(
            view,
            projection,
            position,
            width,
            height,
            GameLayerMask.everything);
        Guid selectedRendererId = GetSelectedRendererId();
        EditorViewportOutput output = m_rendering.Submit(new EditorViewportRequest(
            C_VIEWPORT_ID,
            renderView,
            m_renderPath,
            CameraClearMode.Sky,
            new Color(0.025f, 0.035f, 0.055f, 1f),
            priority: -100,
            enablePicking: true,
            selectedObjectId: selectedRendererId));
        if (output.isReady)
        {
            m_rendering.Draw(output, new NumericVector2(width, height));
            NumericVector2 minimum = NativeImGui.GetItemRectMin();
            NumericVector2 maximum = NativeImGui.GetItemRectMax();
            bool clicked = NativeImGui.IsItemClicked(ImGuiMouseButton.Left);
            bool gizmoOwnsPointer = DrawGizmo(renderView, minimum, maximum);
            if (clicked && !gizmoOwnsPointer)
            {
                Pick(renderView, minimum, maximum);
            }
        }
        else
        {
            NativeImGui.TextUnformatted("Preparing Scene View GPU target...");
        }
    }

    /// <inheritdoc />
    protected override void OnDetach(EditorContext context)
    {
        _ = context;
        CompleteGizmoDrag();
        m_rendering.Release(C_VIEWPORT_ID);
    }

    /// <inheritdoc />
    protected override void Capture(EditorState state)
    {
        state.Set("renderPath", (int)m_renderPath);
        state.Set("pipelineAsset", m_pipelineAssetPath ?? string.Empty);
        state.Set("gizmoOperation", (int)m_gizmoOperation);
        state.Set("gizmoMode", (int)m_gizmoMode);
    }

    /// <inheritdoc />
    protected override void Restore(EditorState state)
    {
        int value = state.Get("renderPath", (int)RenderPath.Automatic);
        m_renderPath = Enum.IsDefined(typeof(RenderPath), value)
            ? (RenderPath)value
            : RenderPath.Automatic;
        string pipelinePath = state.Get("pipelineAsset", string.Empty);
        m_pipelineAssetPath = string.IsNullOrWhiteSpace(pipelinePath) ? null : pipelinePath;
        m_pipelineActivationPending = m_pipelineAssetPath is not null;
        int operation = state.Get("gizmoOperation", (int)ImGuizmoOperation.Translate);
        m_gizmoOperation = Enum.IsDefined(typeof(ImGuizmoOperation), operation)
            ? (ImGuizmoOperation)operation
            : ImGuizmoOperation.Translate;
        int mode = state.Get("gizmoMode", (int)ImGuizmoMode.World);
        m_gizmoMode = Enum.IsDefined(typeof(ImGuizmoMode), mode)
            ? (ImGuizmoMode)mode
            : ImGuizmoMode.World;
    }

    private void DrawToolbar()
    {
        DrawPipelinePicker();
        NativeImGui.SameLine();
        NativeImGui.TextUnformatted("Path:");
        NativeImGui.SameLine();
        if (NativeImGui.Button(m_renderPath == RenderPath.ForwardPlus ? "Forward+ *" : "Forward+"))
        {
            m_renderPath = RenderPath.ForwardPlus;
        }

        NativeImGui.SameLine();
        if (NativeImGui.Button(m_renderPath == RenderPath.Deferred ? "Deferred *" : "Deferred"))
        {
            m_renderPath = RenderPath.Deferred;
        }

        NativeImGui.SameLine();
        if (NativeImGui.Button(m_renderPath == RenderPath.Automatic ? "Auto *" : "Auto"))
        {
            m_renderPath = RenderPath.Automatic;
        }

        NativeImGui.SameLine();
        NativeImGui.TextUnformatted("Gizmo:");
        NativeImGui.SameLine();
        if (NativeImGui.Button(m_gizmoOperation == ImGuizmoOperation.Translate ? "Move *" : "Move"))
        {
            m_gizmoOperation = ImGuizmoOperation.Translate;
        }

        NativeImGui.SameLine();
        if (NativeImGui.Button(m_gizmoOperation == ImGuizmoOperation.Rotate ? "Rotate *" : "Rotate"))
        {
            m_gizmoOperation = ImGuizmoOperation.Rotate;
        }

        NativeImGui.SameLine();
        if (NativeImGui.Button(m_gizmoOperation == ImGuizmoOperation.Scale ? "Scale *" : "Scale"))
        {
            m_gizmoOperation = ImGuizmoOperation.Scale;
        }

        NativeImGui.SameLine();
        if (NativeImGui.Button(m_gizmoMode == ImGuizmoMode.World ? "World" : "Local"))
        {
            m_gizmoMode = m_gizmoMode == ImGuizmoMode.World
                ? ImGuizmoMode.Local
                : ImGuizmoMode.World;
        }
    }

    private void DrawPipelinePicker()
    {
        string preview = m_rendering.activePipelineAssetPath is string active
            ? Path.GetFileNameWithoutExtension(active)
            : "Built-in Default";
        NativeImGui.SetNextItemWidth(155f);
        if (!NativeImGui.BeginCombo("##ScenePipeline", preview))
        {
            return;
        }

        try
        {
            foreach (EditorPipelineAssetInfo pipeline in m_rendering.GetPipelineAssets())
            {
                bool selected = string.Equals(
                    pipeline.assetPath,
                    m_rendering.activePipelineAssetPath,
                    StringComparison.Ordinal);
                if (NativeImGui.Selectable(
                        $"{pipeline.displayName} ({pipeline.defaultRenderPath})",
                        selected))
                {
                    if (m_rendering.TryActivatePipelineAsset(pipeline.assetPath))
                    {
                        m_pipelineAssetPath = pipeline.assetPath;
                    }
                }

                if (selected)
                {
                    NativeImGui.SetItemDefaultFocus();
                }
            }
        }
        finally
        {
            NativeImGui.EndCombo();
        }
    }

    private bool DrawGizmo(
        RenderView renderView,
        NumericVector2 minimum,
        NumericVector2 maximum)
    {
        if (!TryGetSelectedGameObject(out GameObject? gameObject) || !gameObject.isRuntimeValid)
        {
            CompleteGizmoDrag();
            return false;
        }

        Transform transform = gameObject.transform;
        Matrix world = Matrix.CreateTranslation(transform.worldPosition)
            * Matrix.CreateFromQuaternion(transform.worldRotation)
            * Matrix.CreateScale(transform.worldScale);
        float[] view = renderView.viewMatrix.ToColumnMajorArray();
        float[] projection = renderView.projectionMatrix.ToColumnMajorArray();
        float[] model = world.ToColumnMajorArray();
        ImGuizmo.BeginFrame();
        ImGuizmo.SetDrawlist();
        ImGuizmo.SetOrthographic(false);
        ImGuizmo.SetRect(
            minimum.X,
            minimum.Y,
            Math.Max(1f, maximum.X - minimum.X),
            Math.Max(1f, maximum.Y - minimum.Y));

        bool changed;
        fixed (float* viewPointer = view)
        fixed (float* projectionPointer = projection)
        fixed (float* modelPointer = model)
        {
            changed = ImGuizmo.Manipulate(
                viewPointer,
                projectionPointer,
                m_gizmoOperation,
                m_gizmoMode,
                modelPointer);
        }

        if (changed && Matrix.Decompose(FromColumnMajor(model), out Vector3 scale, out Quaternion rotation, out Vector3 translation))
        {
            m_gizmoDrag ??= new GizmoDragState(
                gameObject.identity.persistentId,
                transform.localPosition,
                transform.localRotation,
                transform.localScale);
            transform.worldPosition = translation;
            transform.worldRotation = rotation;
            transform.worldScale = scale;
        }

        bool ownsPointer = ImGuizmo.IsOver() || ImGuizmo.IsUsing();
        if (!ImGuizmo.IsUsing())
        {
            CompleteGizmoDrag();
        }

        return ownsPointer;
    }

    private void Pick(
        RenderView renderView,
        NumericVector2 minimum,
        NumericVector2 maximum)
    {
        NumericVector2 mouse = NativeImGui.GetMousePos();
        float width = Math.Max(1f, maximum.X - minimum.X);
        float height = Math.Max(1f, maximum.Y - minimum.Y);
        float normalizedX = Math.Clamp((mouse.X - minimum.X) / width, 0f, 1f);
        float normalizedY = Math.Clamp((mouse.Y - minimum.Y) / height, 0f, 1f);
        RenderWorldSnapshot world = RenderWorldSnapshot.CaptureLoadedScenes();
        if (RenderPicking.TryPickBounds(
                world.objects,
                renderView,
                normalizedX,
                normalizedY,
                out Guid rendererId)
            && IdentityManager.Get<MeshRenderer>(rendererId) is { isDestroyed: false } renderer)
        {
            m_interactions.SetSelection(renderer.gameObject);
            return;
        }

        m_interactions.SetSelection(null);
    }

    private void CompleteGizmoDrag()
    {
        if (m_gizmoDrag is not GizmoDragState drag
            || IdentityManager.Get<GameObject>(drag.gameObjectId) is not { isRuntimeValid: true } gameObject)
        {
            m_gizmoDrag = null;
            return;
        }

        Transform transform = gameObject.transform;
        Vector3 finalPosition = transform.localPosition;
        Quaternion finalRotation = transform.localRotation;
        Vector3 finalScale = transform.localScale;
        transform.localPosition = drag.position;
        transform.localRotation = drag.rotation;
        transform.localScale = drag.scale;
        m_gizmoDrag = null;

        if (finalPosition == drag.position
            && finalRotation == drag.rotation
            && finalScale == drag.scale)
        {
            return;
        }

        using EditorHistoryTransaction transaction = m_interactions.history.BeginTransaction("Transform GameObject");
        _ = m_sceneEdits.ChangeProperty(
            transform,
            nameof(Transform.localPosition),
            () => transform.localPosition = finalPosition,
            "Move GameObject");
        _ = m_sceneEdits.ChangeProperty(
            transform,
            nameof(Transform.localRotation),
            () => transform.localRotation = finalRotation,
            "Rotate GameObject");
        _ = m_sceneEdits.ChangeProperty(
            transform,
            nameof(Transform.localScale),
            () => transform.localScale = finalScale,
            "Scale GameObject");
        transaction.Commit();
    }

    private Guid GetSelectedRendererId()
        => TryGetSelectedGameObject(out GameObject? gameObject)
            && gameObject.TryGetComponent(out MeshRenderer? renderer)
            && renderer is not null
                ? renderer.identity.persistentId
                : Guid.Empty;

    private bool TryGetSelectedGameObject([NotNullWhen(true)] out GameObject? gameObject)
    {
        if (m_interactions.selection.selectedTarget is GameObject selectedObject)
        {
            gameObject = selectedObject;
            return true;
        }

        if (m_interactions.selection.selectedTarget is GameComponent component && !component.isDestroyed)
        {
            gameObject = component.gameObject;
            return true;
        }

        gameObject = null;
        return false;
    }

    private void ActivateRestoredPipeline()
    {
        if (!m_pipelineActivationPending || m_pipelineAssetPath is null)
        {
            return;
        }

        m_pipelineActivationPending = false;
        _ = m_rendering.TryActivatePipelineAsset(m_pipelineAssetPath);
    }

    private static Matrix FromColumnMajor(IReadOnlyList<float> values)
        => new(
            values[0], values[4], values[8], values[12],
            values[1], values[5], values[9], values[13],
            values[2], values[6], values[10], values[14],
            values[3], values[7], values[11], values[15]);

    private sealed record GizmoDragState(
        Guid gameObjectId,
        Vector3 position,
        Quaternion rotation,
        Vector3 scale);
}
