using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

using Inno.Scripting.Api;
using Inno.Extensibility.Types;
using Inno.Editor.Core;
using Inno.Editor.Interactions;
using Inno.Rendering;
using EngineColor = Inno.Core.Mathematics.Color;

namespace Inno.Editor.Rendering;

/// <summary>
/// Composes reloadable rendering-model contributors while retaining only opaque presentation outputs.
/// </summary>
[EditorModule("rendering.viewports", order: 175)]
public sealed class EditorRenderingModule : EditorModule
{
    private const int C_RENDERING_STATISTICS_ORDER = 100;
    private const int C_VIEWPORT_STATISTICS_ORDER = 200;

    private readonly Dictionary<string, EditorViewportNavigationState> m_navigationStates = new(StringComparer.Ordinal);
    private readonly Dictionary<string, RenderContentScope> m_contentScopes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> m_compositionErrors = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> m_controllerIds = new(StringComparer.Ordinal);
    private readonly Dictionary<string, EditorViewportManipulationSpace> m_manipulationSpaces =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, EditorViewportPresentation> m_presentations =
        new(StringComparer.Ordinal);
    private readonly IEditorRenderingHost m_host;
    private readonly EditorInteractions m_interactions;
    private readonly EditorViewportContributorRegistry m_contributors;
    private EditorContext? m_context;

    /// <summary>
    /// Creates the composition host around stable rendering and interaction services.
    /// </summary>
    /// <param name="host">
    /// Host-owned target and opaque texture bridge.
    /// </param>
    /// <param name="interactions">
    /// Shared Editor interaction and selection service.
    /// </param>
    /// <param name="types">
    /// The type catalog that owns viewport contributor generations.
    /// </param>
    [ScriptingApiIgnore]
    public EditorRenderingModule(
        IEditorRenderingHost host,
        EditorInteractions interactions,
        TypeCatalog types)
    {
        m_host = host ?? throw new ArgumentNullException(nameof(host));
        m_interactions = interactions ?? throw new ArgumentNullException(nameof(interactions));
        m_contributors = new EditorViewportContributorRegistry(
            types ?? throw new ArgumentNullException(nameof(types)));
    }

    /// <summary>
    /// Gets whether the current extension generation contributes to one viewport purpose.
    /// </summary>
    /// <param name="kind">
    /// Open viewport purpose.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when at least one contributor is registered.
    /// </returns>
    public bool HasContributors(EditorViewportKindId kind)
        => kind.isValid && m_contributors.contributors.byKind.ContainsKey(kind);

    /// <summary>
    /// Gets the most recent isolated contribution or composition failure for one stable viewport.
    /// </summary>
    /// <param name="viewportId">
    /// Stable panel viewport identity.
    /// </param>
    /// <returns>
    /// The failure message, or null when no current failure exists.
    /// </returns>
    public string? GetCompositionError(string viewportId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(viewportId);
        return m_compositionErrors.GetValueOrDefault(viewportId);
    }

    /// <summary>
    /// Gets the host-owned neutral navigation state for one stable Editor viewport.
    /// </summary>
    /// <param name="viewportId">
    /// Stable panel viewport identity.
    /// </param>
    /// <returns>
    /// The reusable navigation state owned by the Editor host.
    /// </returns>
    [ScriptingApiIgnore]
    public EditorViewportNavigationState GetNavigationState(string viewportId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(viewportId);
        if (!m_navigationStates.TryGetValue(viewportId, out EditorViewportNavigationState? state))
        {
            state = new EditorViewportNavigationState();
            m_navigationStates.Add(viewportId, state);
        }
        return state;
    }

    /// <summary>
    /// Sets the explicit ordered host content visible to one Editor viewport.
    /// </summary>
    /// <param name="viewportId">
    /// Stable panel viewport identity.
    /// </param>
    /// <param name="content">
    /// Current frame-safe content scope.
    /// </param>
    [ScriptingApiIgnore]
    public void SetContentScope(string viewportId, RenderContentScope content)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(viewportId);
        m_contentScopes[viewportId] = content ?? throw new ArgumentNullException(nameof(content));
    }

    /// <summary>
    /// Queries the selected controller's navigation contract before viewport input is processed.
    /// </summary>
    /// <param name="kind">
    /// Open viewport purpose.
    /// </param>
    /// <param name="viewportId">
    /// Stable panel viewport identity.
    /// </param>
    /// <param name="pixelWidth">
    /// Positive target width.
    /// </param>
    /// <param name="pixelHeight">
    /// Positive target height.
    /// </param>
    /// <param name="profile">
    /// Receives the selected controller profile.
    /// </param>
    /// <returns>
    /// True when a contributor returned a usable navigation profile and became the controller.
    /// </returns>
    public bool TryConfigureNavigation(
        EditorViewportKindId kind,
        string viewportId,
        int pixelWidth,
        int pixelHeight,
        out EditorViewportNavigationProfile profile)
    {
        profile = EditorViewportNavigationProfile.disabled;
        if (!TryCreateContext(kind, viewportId, pixelWidth, pixelHeight, out EditorViewportContext? context))
            return false;
        var failures = new List<string>();
        EditorViewportContributorRegistry.Registration[] contributors = GetApplicableContributors(context!, failures);
        foreach (EditorViewportContributorRegistry.Registration registration in contributors
                     .OrderByDescending(static value => value.attribute.controllerPriority)
                     .ThenBy(static value => value.attribute.id, StringComparer.Ordinal))
        {
            try
            {
                EditorViewportNavigationProfile candidate = registration.contributor.ConfigureNavigation(context!)
                    ?? throw new InvalidOperationException("Viewport contributor returned a null navigation profile.");
                if (!candidate.id.isValid)
                    continue;
                profile = candidate;
                m_controllerIds[viewportId] = registration.attribute.id;
                SetCompositionFailures(viewportId, failures);
                return true;
            }
            catch (Exception exception)
            {
                failures.Add(
                    $"Viewport contributor '{registration.attribute.id}' navigation failed: {exception.Message}");
            }
        }
        m_controllerIds.Remove(viewportId);
        SetCompositionFailures(viewportId, failures);
        return false;
    }

    /// <summary>
    /// Sets presentation preferences supplied to every contributor for one Editor viewport.
    /// </summary>
    /// <param name="viewportId">
    /// Stable panel viewport identity.
    /// </param>
    /// <param name="presentation">
    /// Current host-owned presentation preferences.
    /// </param>
    [ScriptingApiIgnore]
    public void SetPresentation(string viewportId, EditorViewportPresentation presentation)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(viewportId);
        m_presentations[viewportId] = presentation;
    }

    /// <summary>
    /// Tries to get the manipulation space from the selected controller's latest contribution.
    /// </summary>
    /// <param name="viewportId">
    /// Stable panel viewport identity.
    /// </param>
    /// <param name="space">
    /// Receives the latest exact view/projection contract.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the selected controller supplied a manipulation space.
    /// </returns>
    [ScriptingApiIgnore]
    public bool TryGetManipulationSpace(
        string viewportId,
        out EditorViewportManipulationSpace space)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(viewportId);
        return m_manipulationSpaces.TryGetValue(viewportId, out space);
    }

    /// <summary>
    /// Draws toolbar controls owned by the selected model controller.
    /// </summary>
    /// <param name="kind">
    /// Open viewport purpose.
    /// </param>
    /// <param name="viewportId">
    /// Stable panel viewport identity.
    /// </param>
    /// <param name="pixelWidth">
    /// Current target width.
    /// </param>
    /// <param name="pixelHeight">
    /// Current target height.
    /// </param>
    public void DrawControllerToolbar(
        EditorViewportKindId kind,
        string viewportId,
        int pixelWidth,
        int pixelHeight)
    {
        if (!TryCreateContext(kind, viewportId, pixelWidth, pixelHeight, out EditorViewportContext? context))
            return;
        var failures = new List<string>();
        EditorViewportContributorRegistry.Registration[] contributors = GetApplicableContributors(context!, failures);
        EditorViewportContributorRegistry.Registration? registration = SelectController(viewportId, contributors);
        if (registration is null)
        {
            SetCompositionFailures(viewportId, failures);
            return;
        }
        try
        {
            registration.contributor.DrawToolbar(context!);
        }
        catch (Exception exception)
        {
            failures.Add(
                $"Viewport contributor '{registration.attribute.id}' toolbar failed: {exception.Message}");
        }
        SetCompositionFailures(viewportId, failures);
    }

    /// <summary>
    /// Builds, composes, submits, and returns one host-owned offscreen viewport.
    /// </summary>
    /// <param name="kind">
    /// Open viewport purpose.
    /// </param>
    /// <param name="viewportId">
    /// Stable panel viewport identity.
    /// </param>
    /// <param name="pixelWidth">
    /// Positive target width.
    /// </param>
    /// <param name="pixelHeight">
    /// Positive target height.
    /// </param>
    /// <param name="output">
    /// Receives the opaque viewport output.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when at least one model contribution was accepted.
    /// </returns>
    public bool TrySubmit(
        EditorViewportKindId kind,
        string viewportId,
        int pixelWidth,
        int pixelHeight,
        out EditorViewportOutput output)
    {
        output = default;
        if (!TryCreateContext(kind, viewportId, pixelWidth, pixelHeight, out EditorViewportContext? context))
            return false;
        var failures = new List<string>();
        EditorViewportContributorRegistry.Registration[] contributors = GetApplicableContributors(context!, failures);
        if (contributors.Length == 0)
        {
            PublishViewportStatistics(
                kind,
                viewportId,
                pixelWidth,
                pixelHeight,
                "Unavailable",
                contributorIds: "None");
            m_host.Release(viewportId);
            m_controllerIds.Remove(viewportId);
            m_manipulationSpaces.Remove(viewportId);
            failures.Add("No rendering-model contributor accepts the current viewport content.");
            SetCompositionFailures(viewportId, failures);
            return false;
        }

        var accepted = new List<AcceptedContribution>(contributors.Length);
        RenderTextureFormat? targetFormat = null;
        foreach (EditorViewportContributorRegistry.Registration registration in contributors)
        {
            try
            {
                EditorViewportContribution contribution = registration.contributor.Build(context!)
                    ?? throw new InvalidOperationException("Viewport contributor returned a null contribution.");
                if (targetFormat is RenderTextureFormat selectedFormat
                    && contribution.targetFormat != selectedFormat)
                {
                    failures.Add(
                        $"Viewport contributor '{registration.attribute.id}' requested target format " +
                        $"'{contribution.targetFormat}' while the composition uses '{selectedFormat}'.");
                    continue;
                }
                targetFormat ??= contribution.targetFormat;
                accepted.Add(new AcceptedContribution(registration, contribution));
            }
            catch (Exception exception)
            {
                failures.Add($"Viewport contributor '{registration.attribute.id}' failed: {exception.Message}");
            }
        }
        if (accepted.Count == 0)
        {
            HandleViewportFailure(kind, viewportId, pixelWidth, pixelHeight, contributors, failures);
            return false;
        }

        try
        {
            output = m_host.Submit(new EditorViewportComposition(
                viewportId,
                pixelWidth,
                pixelHeight,
                targetFormat!.Value,
                accepted.Select(static value => new EditorViewportLayer(
                    value.registration.attribute.id,
                    value.contribution.pipeline,
                    value.contribution.data,
                    value.registration.attribute.order))));
            EditorViewportContributorRegistry.Registration? controller = SelectController(viewportId, contributors);
            EditorViewportContribution? controllerContribution = controller is null
                ? null
                : accepted.FirstOrDefault(value => ReferenceEquals(value.registration, controller))?.contribution;
            if (controllerContribution?.manipulationSpace is EditorViewportManipulationSpace manipulationSpace)
                m_manipulationSpaces[viewportId] = manipulationSpace;
            else
                m_manipulationSpaces.Remove(viewportId);
            PublishViewportStatistics(
                kind,
                viewportId,
                pixelWidth,
                pixelHeight,
                output.isReady ? "Ready" : "Preparing",
                string.Join(", ", accepted.Select(static value => value.registration.attribute.id)),
                accepted);
            SetCompositionFailures(viewportId, failures);
            return true;
        }
        catch (Exception exception)
        {
            failures.Add($"Viewport composition failed: {exception.Message}");
            HandleViewportFailure(kind, viewportId, pixelWidth, pixelHeight, contributors, failures);
            return false;
        }
    }

    /// <summary>
    /// Forwards a normalized click to the selected model controller.
    /// </summary>
    /// <param name="kind">
    /// Open viewport purpose.
    /// </param>
    /// <param name="viewportId">
    /// Stable panel viewport identity.
    /// </param>
    /// <param name="pixelWidth">
    /// Current target width.
    /// </param>
    /// <param name="pixelHeight">
    /// Current target height.
    /// </param>
    /// <param name="x">
    /// Normalized horizontal position.
    /// </param>
    /// <param name="y">
    /// Normalized vertical position.
    /// </param>
    /// <param name="button">
    /// Platform-independent pointer button index.
    /// </param>
    public void HandlePointer(
        EditorViewportKindId kind,
        string viewportId,
        int pixelWidth,
        int pixelHeight,
        float x,
        float y,
        int button)
    {
        if (!TryCreateContext(kind, viewportId, pixelWidth, pixelHeight, out EditorViewportContext? context))
            return;
        var failures = new List<string>();
        EditorViewportContributorRegistry.Registration[] contributors = GetApplicableContributors(context!, failures);
        EditorViewportContributorRegistry.Registration? registration = SelectController(viewportId, contributors);
        if (registration is null)
        {
            SetCompositionFailures(viewportId, failures);
            return;
        }
        try
        {
            registration.contributor.HandlePointer(new EditorViewportPointerContext(context!, x, y, button));
        }
        catch (Exception exception)
        {
            failures.Add(
                $"Viewport contributor '{registration.attribute.id}' pointer handler failed: {exception.Message}");
        }
        SetCompositionFailures(viewportId, failures);
    }

    /// <summary>
    /// Draws a ready output in the current panel.
    /// </summary>
    /// <param name="output">
    /// Opaque output returned by <see cref="TrySubmit"/>.
    /// </param>
    /// <param name="logicalSize">
    /// Destination size in logical UI pixels.
    /// </param>
    public void Draw(EditorViewportOutput output, Vector2 logicalSize)
        => m_host.Draw(output, logicalSize);

    /// <summary>
    /// Stops retaining one viewport target.
    /// </summary>
    /// <param name="viewportId">
    /// Stable viewport identity.
    /// </param>
    public void Release(string viewportId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(viewportId);
        m_manipulationSpaces.Remove(viewportId);
        m_contentScopes.Remove(viewportId);
        m_controllerIds.Remove(viewportId);
        m_host.Release(viewportId);
    }

    /// <summary>
    /// Initializes this feature when its owning runtime becomes active.
    /// </summary>
    /// <param name="context">
    /// The context that supplies state and services for this operation.
    /// </param>
    protected override void OnStart(EditorContext context)
    {
        m_context = context;
        _ = m_contributors.contributors;
    }

    /// <summary>
    /// Advances this feature using the current runtime state.
    /// </summary>
    /// <param name="context">
    /// The context that supplies state and services for this operation.
    /// </param>
    protected override void OnUpdate(EditorContext context)
    {
        RenderFrameStatistics? statistics = GraphicsSettings.frameStatistics;
        if (statistics is null)
            return;
        var groupId = new EditorStatisticGroupId("inno.rendering.frame");
        context.statistics.Publish(new EditorStatistic[]
        {
            CreateStatistic("frame", "Frame", statistics.frameIndex.ToString(), 0),
            CreateStatistic("views", "Views", statistics.viewCount.ToString(), 10),
            CreateStatistic("draws", "Draws", statistics.drawCount.ToString(), 20),
            CreateStatistic("dispatches", "Dispatches", statistics.dispatchCount.ToString(), 30),
            CreateStatistic("culled-passes", "Culled Passes", statistics.culledPassCount.ToString(), 40)
        });
        return;

        EditorStatistic CreateStatistic(string id, string label, string value, int order)
            => new(
                new EditorStatisticId($"inno.rendering.frame.{id}"),
                groupId,
                "Rendering",
                label,
                value,
                C_RENDERING_STATISTICS_ORDER,
                order);
    }

    /// <summary>
    /// Stops this feature before its owning runtime releases the active generation.
    /// </summary>
    /// <param name="context">
    /// The context that supplies state and services for this operation.
    /// </param>
    protected override void OnStop(EditorContext context)
    {
        _ = context;
        m_context = null;
        m_host.ReleaseAll();
        m_navigationStates.Clear();
        m_contentScopes.Clear();
        m_compositionErrors.Clear();
        m_controllerIds.Clear();
        m_manipulationSpaces.Clear();
        m_presentations.Clear();
    }

    /// <summary>
    /// Releases resources retained by this feature after it has stopped.
    /// </summary>
    protected override void OnDispose()
    {
        m_contributors.Dispose();
        m_navigationStates.Clear();
        m_contentScopes.Clear();
        m_compositionErrors.Clear();
        m_controllerIds.Clear();
        m_manipulationSpaces.Clear();
        m_presentations.Clear();
        m_host.ReleaseAll();
    }

    private bool TryCreateContext(
        EditorViewportKindId kind,
        string viewportId,
        int pixelWidth,
        int pixelHeight,
        out EditorViewportContext? context)
    {
        if (!kind.isValid || m_context is null)
        {
            context = null;
            return false;
        }
        ArgumentException.ThrowIfNullOrWhiteSpace(viewportId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pixelWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pixelHeight);
        context = new EditorViewportContext(
            m_context,
            m_interactions,
            kind,
            viewportId,
            pixelWidth,
            pixelHeight,
            GetNavigationState(viewportId),
            m_contentScopes.GetValueOrDefault(viewportId, RenderContentScope.empty),
            m_presentations.GetValueOrDefault(
                viewportId,
                new EditorViewportPresentation(EngineColor.DARKGRAY)));
        return true;
    }

    private void PublishViewportStatistics(
        EditorViewportKindId kind,
        string viewportId,
        int pixelWidth,
        int pixelHeight,
        string state,
        string contributorIds,
        IReadOnlyList<AcceptedContribution>? contributions = null)
    {
        if (m_context is null)
            return;
        string groupKey = $"inno.rendering.viewport.{viewportId}";
        var groupId = new EditorStatisticGroupId(groupKey);
        string groupName = GetDisplayName(viewportId);
        var statistics = new List<EditorStatistic>
        {
            CreateStatistic("kind", "Kind", kind.value, 0),
            CreateStatistic("contributors", "Contributors", contributorIds, 10),
            CreateStatistic("state", "State", state, 20),
            CreateStatistic("resolution", "Resolution", $"{pixelWidth} x {pixelHeight}", 30)
        };
        if (contributions is { Count: > 0 })
        {
            string pipeline = string.Join(", ", contributions.Select(static contribution =>
                string.IsNullOrWhiteSpace(contribution.contribution.pipeline?.pipelineTypeId)
                    ? GraphicsSettings.defaultPipeline?.pipelineTypeId ?? "Project Default"
                    : contribution.contribution.pipeline.pipelineTypeId));
            statistics.Add(CreateStatistic(
                "pipelines",
                "Pipelines",
                pipeline,
                40));
            statistics.Add(CreateStatistic(
                "format",
                "Target Format",
                contributions[0].contribution.targetFormat.ToString(),
                50));
            statistics.Add(CreateStatistic(
                "order",
                "Composition Order",
                string.Join(", ", contributions.Select(static contribution =>
                    contribution.registration.attribute.order.ToString())),
                60));
        }
        m_context.statistics.Publish(statistics);
        return;

        EditorStatistic CreateStatistic(string id, string label, string value, int order)
            => new(
                new EditorStatisticId($"{groupKey}.{id}"),
                groupId,
                groupName,
                label,
                value,
                C_VIEWPORT_STATISTICS_ORDER,
                order);
    }

    private EditorViewportContributorRegistry.Registration[] GetApplicableContributors(
        EditorViewportContext context,
        List<string> failures)
    {
        if (!m_contributors.contributors.byKind.TryGetValue(
                context.kind,
                out EditorViewportContributorRegistry.Registration[]? registrations))
        {
            return [];
        }

        var applicable = new List<EditorViewportContributorRegistry.Registration>(registrations.Length);
        foreach (EditorViewportContributorRegistry.Registration registration in registrations)
        {
            try
            {
                if (registration.contributor.CanContribute(context))
                    applicable.Add(registration);
            }
            catch (Exception exception)
            {
                failures.Add(
                    $"Viewport contributor '{registration.attribute.id}' participation check failed: " +
                    exception.Message);
            }
        }
        return applicable.ToArray();
    }

    private EditorViewportContributorRegistry.Registration? SelectController(
        string viewportId,
        IReadOnlyList<EditorViewportContributorRegistry.Registration> contributors)
    {
        if (m_controllerIds.TryGetValue(viewportId, out string? controllerId))
        {
            EditorViewportContributorRegistry.Registration? selected = contributors.FirstOrDefault(
                contributor => string.Equals(
                    contributor.attribute.id,
                    controllerId,
                    StringComparison.Ordinal));
            if (selected is not null)
                return selected;
        }

        return contributors
            .OrderByDescending(static contributor => contributor.attribute.controllerPriority)
            .ThenBy(static contributor => contributor.attribute.id, StringComparer.Ordinal)
            .FirstOrDefault();
    }

    private void HandleViewportFailure(
        EditorViewportKindId kind,
        string viewportId,
        int pixelWidth,
        int pixelHeight,
        IReadOnlyList<EditorViewportContributorRegistry.Registration> contributors,
        List<string> failures)
    {
        PublishViewportStatistics(
            kind,
            viewportId,
            pixelWidth,
            pixelHeight,
            "Failed",
            string.Join(", ", contributors.Select(static contributor => contributor.attribute.id)));
        m_host.Release(viewportId);
        m_controllerIds.Remove(viewportId);
        m_manipulationSpaces.Remove(viewportId);
        SetCompositionFailures(viewportId, failures);
    }

    private void SetCompositionFailures(string viewportId, IReadOnlyList<string> failures)
    {
        string[] distinct = failures.Distinct(StringComparer.Ordinal).ToArray();
        if (distinct.Length == 0)
            m_compositionErrors.Remove(viewportId);
        else
            m_compositionErrors[viewportId] = string.Join(Environment.NewLine, distinct);
    }

    private sealed record AcceptedContribution(
        EditorViewportContributorRegistry.Registration registration,
        EditorViewportContribution contribution);

    private static string GetDisplayName(string identifier)
    {
        char[] characters = identifier.Replace('-', ' ').Replace('_', ' ').ToCharArray();
        bool capitalize = true;
        for (int i = 0; i < characters.Length; i++)
        {
            if (characters[i] == ' ')
            {
                capitalize = true;
                continue;
            }
            if (!capitalize)
                continue;
            characters[i] = char.ToUpperInvariant(characters[i]);
            capitalize = false;
        }
        return new string(characters);
    }
}
