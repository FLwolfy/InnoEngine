using System;
using System.Collections.Generic;

namespace Inno.Rendering;

/// <summary>
/// Identifies an open render phase protocol value.
/// </summary>
public readonly record struct RenderPhaseId
{
    /// <summary>
    /// Creates an open render phase identifier.
    /// </summary>
    /// <param name="value">
    /// Globally stable phase value.
    /// </param>
    public RenderPhaseId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        this.value = value;
    }

    /// <summary>
    /// Gets the globally stable phase value.
    /// </summary>
    public string value { get; }

    /// <summary>
    /// Formats this value as a human-readable representation.
    /// </summary>
    /// <returns>
    /// The human-readable representation of this value.
    /// </returns>
    public override string ToString() => value;
}

/// <summary>
/// Distinguishes pass command domains for validation and backend execution.
/// </summary>
public enum RenderPassKind
{
    /// <summary>
    /// Raster draw commands with optional attachments.
    /// </summary>
    Raster,
    /// <summary>
    /// Compute dispatch commands.
    /// </summary>
    Compute,
    /// <summary>
    /// Resource copy commands.
    /// </summary>
    Copy
}

/// <summary>
/// Controls where a frame-local pass callback records backend-neutral commands.
/// </summary>
public enum RenderPassRecordingMode
{
    /// <summary>
    /// Records directly on the graph execution thread.
    /// </summary>
    Serial,
    /// <summary>
    /// Records into an isolated command list on a worker thread, then replays in graph order.
    /// </summary>
    Parallel
}

/// <summary>
/// Controls how existing attachment contents enter a raster pass.
/// </summary>
public enum RenderLoadAction
{
    /// <summary>
    /// Preserves prior attachment contents.
    /// </summary>
    Load,
    /// <summary>
    /// Clears the attachment before rendering.
    /// </summary>
    Clear,
    /// <summary>
    /// Does not require prior attachment contents.
    /// </summary>
    Discard
}

/// <summary>
/// Controls whether attachment contents remain valid after a raster pass.
/// </summary>
public enum RenderStoreAction
{
    /// <summary>
    /// Preserves rendered contents.
    /// </summary>
    Store,
    /// <summary>
    /// Allows the backend to discard rendered contents.
    /// </summary>
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
    /// <param name="r">
    /// Red channel.
    /// </param>
    /// <param name="g">
    /// Green channel.
    /// </param>
    /// <param name="b">
    /// Blue channel.
    /// </param>
    /// <param name="a">
    /// Alpha channel.
    /// </param>
    public RenderClearColor(float r, float g, float b, float a = 1f)
    {
        this.r = r;
        this.g = g;
        this.b = b;
        this.a = a;
    }

    /// <summary>
    /// Gets the red channel.
    /// </summary>
    public float r { get; }

    /// <summary>
    /// Gets the green channel.
    /// </summary>
    public float g { get; }

    /// <summary>
    /// Gets the blue channel.
    /// </summary>
    public float b { get; }

    /// <summary>
    /// Gets the alpha channel.
    /// </summary>
    public float a { get; }
}

/// <summary>
/// Indicates the impact of a render-graph compilation diagnostic.
/// </summary>
public enum RenderGraphDiagnosticSeverity
{
    /// <summary>
    /// Provides non-blocking information.
    /// </summary>
    Info,
    /// <summary>
    /// Reports a recoverable quality or capability reduction.
    /// </summary>
    Warning,
    /// <summary>
    /// Prevents graph execution.
    /// </summary>
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
    /// <param name="code">
    /// Stable machine-readable code.
    /// </param>
    /// <param name="message">
    /// Actionable diagnostic text.
    /// </param>
    /// <param name="severity">
    /// Diagnostic impact.
    /// </param>
    /// <param name="passName">
    /// Optional related pass name.
    /// </param>
    /// <param name="resourceName">
    /// Optional related resource name.
    /// </param>
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

    /// <summary>
    /// Gets the stable machine-readable code.
    /// </summary>
    public string code { get; }

    /// <summary>
    /// Gets actionable diagnostic text.
    /// </summary>
    public string message { get; }

    /// <summary>
    /// Gets diagnostic impact.
    /// </summary>
    public RenderGraphDiagnosticSeverity severity { get; }

    /// <summary>
    /// Gets the related pass name, if any.
    /// </summary>
    public string? passName { get; }

    /// <summary>
    /// Gets the related resource name, if any.
    /// </summary>
    public string? resourceName { get; }
}

internal enum RenderResourceAccess
{
    Read,
    Write,
    ReadWrite
}

internal enum RenderResourceUseKind
{
    GenericRead,
    StorageRead,
    StorageWrite,
    StorageReadWrite,
    CopySource,
    CopyDestination,
    ColorAttachment,
    DepthStencilAttachment
}

internal readonly record struct RenderResourceKey(bool isTexture, int index);

internal sealed record RenderResourceUse(
    RenderResourceKey key,
    RenderResourceAccess access,
    RenderResourceUseKind kind);

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
    /// <summary>
    /// Gets the human-readable name used for presentation and diagnostics.
    /// </summary>
    public required string name { get; init; }
    /// <summary>
    /// Gets the render phase that owns this pass declaration.
    /// </summary>
    public required RenderPhaseId phase { get; init; }
    /// <summary>
    /// Gets the operation kind that determines how this value is interpreted.
    /// </summary>
    public required RenderPassKind kind { get; init; }
    /// <summary>
    /// Gets the callback that records commands for this render pass.
    /// </summary>
    public required Action<RenderPassContext> execute { get; init; }
    /// <summary>
    /// Gets the render phases that must execute after this pass.
    /// </summary>
    public List<RenderPhaseId> before { get; } = [];
    /// <summary>
    /// Gets the render phases that must execute before this pass.
    /// </summary>
    public List<RenderPhaseId> after { get; } = [];
    /// <summary>
    /// Gets the complete resource access declarations used for graph dependency analysis.
    /// </summary>
    public List<RenderResourceUse> resources { get; } = [];
    /// <summary>
    /// Gets the color and depth attachments written by this render pass.
    /// </summary>
    public List<RenderAttachment> attachments { get; } = [];
    /// <summary>
    /// Gets the presentation surface targeted by this render pass.
    /// </summary>
    public RenderSurfaceHandle surface { get; set; }
    /// <summary>
    /// Gets the color used when the presentation surface is cleared.
    /// </summary>
    public RenderClearColor presentationClearColor { get; set; }
    /// <summary>
    /// Gets the optional view and projection transform applied by this pass.
    /// </summary>
    public RenderViewTransform? viewTransform { get; set; }
    /// <summary>
    /// Gets whether the caller-visible condition represented by this property is satisfied.
    /// </summary>
    public bool clearsPresentationTarget { get; set; }
    /// <summary>
    /// Gets whether this value has side effect.
    /// </summary>
    public bool hasSideEffect { get; set; }
    /// <summary>
    /// Gets whether the pass records graphics, compute, or transfer commands.
    /// </summary>
    public RenderPassRecordingMode recordingMode { get; set; }
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
