using System;
using System.Collections.Generic;

namespace Inno.Rendering.Core;

/// <summary>
/// Identifies an open render phase protocol value.
/// </summary>
public readonly record struct RenderPhaseId
{
    /// <summary>
    /// Creates an open render phase identifier.
    /// </summary>
    /// <param name="value">Globally stable phase value.</param>
    public RenderPhaseId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        this.value = value;
    }

    /// <summary>Gets the globally stable phase value.</summary>
    public string value { get; }

    /// <inheritdoc />
    public override string ToString() => value;
}

/// <summary>
/// Provides stable injection points used by built-in pipelines and custom features.
/// </summary>
public static class BuiltinRenderPhases
{
    /// <summary>Runs before depth and shadow preparation.</summary>
    public static RenderPhaseId beforeRendering { get; } = new("inno.before-rendering");

    /// <summary>Renders directional and local-light shadow maps.</summary>
    public static RenderPhaseId shadows { get; } = new("inno.shadows");

    /// <summary>Prepares depth and visibility resources.</summary>
    public static RenderPhaseId depthPrepass { get; } = new("inno.depth-prepass");

    /// <summary>Renders opaque surface data or direct lighting.</summary>
    public static RenderPhaseId opaque { get; } = new("inno.opaque");

    /// <summary>Resolves scene lighting before transparent rendering.</summary>
    public static RenderPhaseId lighting { get; } = new("inno.lighting");

    /// <summary>Renders transparent geometry.</summary>
    public static RenderPhaseId transparent { get; } = new("inno.transparent");

    /// <summary>Applies image-space post-processing.</summary>
    public static RenderPhaseId postProcessing { get; } = new("inno.post-processing");

    /// <summary>Renders editor overlays, picking and gizmos.</summary>
    public static RenderPhaseId editorOverlay { get; } = new("inno.editor-overlay");

    /// <summary>Runs after the final display-ready target is produced.</summary>
    public static RenderPhaseId afterRendering { get; } = new("inno.after-rendering");

    /// <summary>Composites engine and editor user interfaces into presentation surfaces.</summary>
    public static RenderPhaseId userInterface { get; } = new("inno.user-interface");
}

/// <summary>
/// Distinguishes pass command domains for validation and backend execution.
/// </summary>
public enum RenderPassKind
{
    /// <summary>Raster draw commands with optional attachments.</summary>
    Raster,
    /// <summary>Compute dispatch commands.</summary>
    Compute,
    /// <summary>Resource copy commands.</summary>
    Copy
}

/// <summary>
/// Controls how existing attachment contents enter a raster pass.
/// </summary>
public enum RenderLoadAction
{
    /// <summary>Preserves prior attachment contents.</summary>
    Load,
    /// <summary>Clears the attachment before rendering.</summary>
    Clear,
    /// <summary>Does not require prior attachment contents.</summary>
    Discard
}

/// <summary>
/// Controls whether attachment contents remain valid after a raster pass.
/// </summary>
public enum RenderStoreAction
{
    /// <summary>Preserves rendered contents.</summary>
    Store,
    /// <summary>Allows the backend to discard rendered contents.</summary>
    Discard
}

/// <summary>
/// Stores a linear clear color without depending on an engine math assembly.
/// </summary>
public readonly record struct RenderClearColor
{
    /// <summary>
    /// Creates a linear clear color.
    /// </summary>
    /// <param name="r">Red channel.</param>
    /// <param name="g">Green channel.</param>
    /// <param name="b">Blue channel.</param>
    /// <param name="a">Alpha channel.</param>
    public RenderClearColor(float r, float g, float b, float a = 1f)
    {
        this.r = r;
        this.g = g;
        this.b = b;
        this.a = a;
    }

    /// <summary>Gets the red channel.</summary>
    public float r { get; }

    /// <summary>Gets the green channel.</summary>
    public float g { get; }

    /// <summary>Gets the blue channel.</summary>
    public float b { get; }

    /// <summary>Gets the alpha channel.</summary>
    public float a { get; }
}

/// <summary>
/// Indicates the impact of a render-graph compilation diagnostic.
/// </summary>
public enum RenderGraphDiagnosticSeverity
{
    /// <summary>Provides non-blocking information.</summary>
    Info,
    /// <summary>Reports a recoverable quality or capability reduction.</summary>
    Warning,
    /// <summary>Prevents graph execution.</summary>
    Error
}

/// <summary>
/// Reports one structured render-graph compilation problem.
/// </summary>
public sealed class RenderGraphDiagnostic
{
    /// <summary>
    /// Creates a render-graph diagnostic.
    /// </summary>
    /// <param name="code">Stable machine-readable code.</param>
    /// <param name="message">Actionable diagnostic text.</param>
    /// <param name="severity">Diagnostic impact.</param>
    /// <param name="passName">Optional related pass name.</param>
    /// <param name="resourceName">Optional related resource name.</param>
    public RenderGraphDiagnostic(
        string code,
        string message,
        RenderGraphDiagnosticSeverity severity,
        string? passName = null,
        string? resourceName = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        this.code = code;
        this.message = message;
        this.severity = severity;
        this.passName = passName;
        this.resourceName = resourceName;
    }

    /// <summary>Gets the stable machine-readable code.</summary>
    public string code { get; }

    /// <summary>Gets actionable diagnostic text.</summary>
    public string message { get; }

    /// <summary>Gets diagnostic impact.</summary>
    public RenderGraphDiagnosticSeverity severity { get; }

    /// <summary>Gets the related pass name, if any.</summary>
    public string? passName { get; }

    /// <summary>Gets the related resource name, if any.</summary>
    public string? resourceName { get; }
}

internal enum RenderResourceAccess
{
    Read,
    Write,
    ReadWrite
}

internal readonly record struct RenderResourceKey(bool isTexture, int index);

internal sealed record RenderResourceUse(RenderResourceKey key, RenderResourceAccess access);

internal sealed record RenderAttachment(
    RenderTextureHandle texture,
    int slot,
    bool isDepth,
    int mipLevel,
    int arrayLayer,
    RenderLoadAction loadAction,
    RenderStoreAction storeAction,
    RenderClearColor clearColor,
    float clearDepth,
    byte clearStencil);

internal sealed class RenderPassRecord
{
    public required string name { get; init; }
    public required RenderPhaseId phase { get; init; }
    public required RenderPassKind kind { get; init; }
    public required Action<RenderPassContext> execute { get; init; }
    public List<RenderPhaseId> before { get; } = [];
    public List<RenderPhaseId> after { get; } = [];
    public List<RenderResourceUse> resources { get; } = [];
    public List<RenderAttachment> attachments { get; } = [];
    public RenderSurfaceHandle surface { get; set; }
    public RenderClearColor presentationClearColor { get; set; }
    public RenderViewTransform? viewTransform { get; set; }
    public bool clearsPresentationTarget { get; set; }
    public bool hasSideEffect { get; set; }
}

internal sealed record RenderTextureRecord(
    string name,
    RenderTextureDescriptor descriptor,
    bool imported,
    PersistentTextureHandle persistentHandle);

internal sealed record RenderBufferRecord(
    string name,
    RenderBufferDescriptor descriptor,
    bool imported,
    PersistentBufferHandle persistentHandle);
