using Inno.Core.Diagnostics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Inno.Assets;
using Inno.Assets.Pipeline;
using Inno.Core.Serialization;
using Inno.Extensibility.Types;
using Inno.Rendering;
using Inno.Rendering.Assets;

namespace Inno.Editor.Rendering;

/// <summary>
/// Produces target-specific render artifacts from imported authoring assets without exposing source access
/// to the backend-neutral render runtime.
/// </summary>
public sealed class EditorRenderTargetArtifactProvider : IRenderTargetArtifactProvider, IDisposable
{
    private readonly object m_sync = new();
    private readonly AssetPipeline m_assets;
    private readonly SerializationRegistry m_serialization;
    private readonly TypeCatalog m_types;
    private readonly ShaderCompiler m_shaderCompiler;
    private readonly ITextureTargetCompiler m_textureCompiler;
    private readonly IDiagnosticReporter m_diagnostics;
    private readonly CancellationTokenSource m_lifetime = new();
    private readonly Inno.Core.Execution.LifetimeScope m_work = new();
    private readonly Dictionary<ShaderKey, ShaderEntry> m_shaders = [];
    private readonly Dictionary<TextureKey, TextureEntry> m_textures = [];
    private bool m_disposed;
    private bool m_stopping;

    /// <summary>
    /// Creates an Editor artifact provider backed by explicit shader and texture toolchains.
    /// </summary>
    /// <param name="shaderCompiler">
    /// The compiler that turns backend-neutral shader IR into device target programs.
    /// </param>
    /// <param name="assets">
    /// The authoring asset pipeline that owns source mounts.
    /// </param>
    /// <param name="serialization">
    /// The serialization registry that owns active Shader contracts.
    /// </param>
    /// <param name="textureCompiler">
    /// The compiler that turns artist texture sources into portable KTX artifacts.
    /// </param>
    /// <param name="diagnostics">
    /// The sink that receives compilation and last-good fallback diagnostics.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when any required service is null.
    /// </exception>
    /// <param name="types">The shared authoring type generation owner for graph and source extensions.</param>
    public EditorRenderTargetArtifactProvider(
        AssetPipeline assets,
        SerializationRegistry serialization,
        TypeCatalog types,
        ShaderCompiler shaderCompiler,
        ITextureTargetCompiler textureCompiler,
        IDiagnosticReporter diagnostics)
    {
        m_assets = assets ?? throw new ArgumentNullException(nameof(assets));
        m_serialization = serialization ?? throw new ArgumentNullException(nameof(serialization));
        m_types = types ?? throw new ArgumentNullException(nameof(types));
        m_shaderCompiler = shaderCompiler ?? throw new ArgumentNullException(nameof(shaderCompiler));
        m_textureCompiler = textureCompiler ?? throw new ArgumentNullException(nameof(textureCompiler));
        m_diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
    }

    /// <summary>
    /// Returns a matching compiled shader when available and schedules a new candidate when source state changed.
    /// </summary>
    /// <param name="shader">
    /// The imported backend-neutral shader asset.
    /// </param>
    /// <param name="variant">
    /// The exact material keyword selection.
    /// </param>
    /// <param name="capabilities">
    /// The active device capability snapshot used to select the target compiler profile.
    /// </param>
    /// <param name="artifact">
    /// Receives the current candidate or the last-good artifact while a replacement is compiling.
    /// </param>
    /// <returns>
    /// <see cref="RenderTargetArtifactStatus.Ready"/> when a current or last-good artifact is usable;
    /// otherwise, the exact pending or failed state of the first candidate.
    /// </returns>
    /// <exception cref="ObjectDisposedException">
    /// Thrown when this provider has been disposed.
    /// </exception>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="shader"/> or <paramref name="capabilities"/> is <see langword="null"/>.
    /// </exception>
    public RenderTargetArtifactStatus GetShaderArtifact(
        ShaderAsset shader,
        RenderShaderVariant variant,
        GraphicsCapabilities capabilities,
        out RenderShaderArtifact? artifact)
    {
        ArgumentNullException.ThrowIfNull(shader);
        ArgumentNullException.ThrowIfNull(capabilities);
        lock (m_sync)
        {
            EnsureActive();
            ShaderCompileTarget target = m_shaderCompiler.CreateTarget(
                capabilities,
                optimize: false,
                debugInformation: true);
            var key = new ShaderKey(shader.identity.persistentId, target.key, variant.value);
            if (!m_shaders.TryGetValue(key, out ShaderEntry? entry))
            {
                entry = new ShaderEntry();
                m_shaders.Add(key, entry);
            }
            long generation = m_types.current.version;
            if (entry.attemptedContentVersion != shader.contentVersion || entry.extensionGeneration != generation)
            {
                string semanticHash = ShaderGraphArtifact.GetSemanticHash(ShaderGraphArtifact.Read(shader, m_assets), m_serialization);
                if (entry.semanticHash != semanticHash || entry.extensionGeneration != generation)
                {
                    StartShader(shader, target, variant, entry);
                    entry.semanticHash = semanticHash;
                }
                entry.attemptedContentVersion = shader.contentVersion;
                entry.extensionGeneration = generation;
            }
            CompleteShader(key, entry);
            artifact = entry.artifact;
            return artifact is not null
                ? RenderTargetArtifactStatus.Ready
                : entry.status;
        }
    }

    /// <inheritdoc />
    public ShaderDefinition ReadShaderDefinition(RenderShaderArtifact artifact)
    {
        ArgumentNullException.ThrowIfNull(artifact);
        EnsureActive();
        return m_serialization.Deserialize<ShaderDefinition>(artifact.definitionData.Span,
            AssetSerializationContext.Create(m_assets));
    }

    /// <summary>Schedules compilation if required and reports saving-independent state for the exact shader, variant and device target.</summary>
    /// <param name="shader">Current imported shader.</param>
    /// <param name="variant">Selected keyword variant.</param>
    /// <param name="capabilities">Current device capabilities.</param>
    /// <returns>A detached status and diagnostic snapshot; last-good is explicit and never implies current-source success.</returns>
    public EditorShaderCompilationSnapshot RequestShaderCompilation(ShaderAsset shader, RenderShaderVariant variant, GraphicsCapabilities capabilities)
    {
        lock (m_sync)
        {
            _ = GetShaderArtifact(shader, variant, capabilities, out _);
            ShaderCompileTarget target = m_shaderCompiler.CreateTarget(capabilities, optimize: false, debugInformation: true);
            ShaderEntry entry = m_shaders[new(shader.identity.persistentId, target.key, variant.value)];
            return new(entry.pending is not null ? EditorShaderCompilationState.Compiling : entry.latestSucceeded
                ? EditorShaderCompilationState.Succeeded : EditorShaderCompilationState.Failed,
                entry.artifact is not null && (entry.pending is not null || !entry.latestSucceeded), entry.sourceDiagnostics);
        }
    }

    /// <summary>
    /// Returns a matching compiled texture when available and schedules a replacement when source state changed.
    /// </summary>
    /// <param name="texture">
    /// The imported texture asset whose authoring source is compiled.
    /// </param>
    /// <param name="artifact">
    /// Receives the current candidate or the last-good KTX artifact while a replacement is compiling.
    /// </param>
    /// <returns>
    /// <see cref="RenderTargetArtifactStatus.Ready"/> when a current or last-good KTX artifact is usable;
    /// otherwise, the exact pending or failed state of the first candidate.
    /// </returns>
    /// <exception cref="ObjectDisposedException">
    /// Thrown when this provider has been disposed.
    /// </exception>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="texture"/> is <see langword="null"/>.
    /// </exception>
    public RenderTargetArtifactStatus GetTextureArtifact(
        RenderTextureArtifactReference texture,
        out ReadOnlyMemory<byte> artifact)
    {
        if (texture.assetId == Guid.Empty || string.IsNullOrWhiteSpace(texture.slot.id))
            throw new ArgumentException("A valid texture artifact reference is required.", nameof(texture));
        lock (m_sync)
        {
            EnsureActive();
            var key = new TextureKey(texture.assetId, texture.slot.id, texture.slot.colorSpace);
            if (!m_textures.TryGetValue(key, out TextureEntry? entry))
            {
                entry = new TextureEntry();
                m_textures.Add(key, entry);
            }
            CompleteTexture(key, entry);
            if (entry.attemptedContentVersion != texture.contentRevision)
                StartTexture(texture, entry);
            artifact = entry.artifact ?? ReadOnlyMemory<byte>.Empty;
            return !artifact.IsEmpty
                ? RenderTargetArtifactStatus.Ready
                : entry.status;
        }
    }

    /// <summary>
    /// Cancels pending toolchain work and releases every cached authoring artifact.
    /// </summary>
    public void Dispose()
    {
        lock (m_sync)
        {
            if (m_disposed)
                return;
            m_stopping = true;
            m_lifetime.Cancel();
            foreach (ShaderEntry entry in m_shaders.Values)
            {
                Retire(entry.pending, entry.cancellation);
                entry.cancellation = null;
                ClearDiagnostics(entry.diagnostics);
            }
            foreach (TextureEntry entry in m_textures.Values)
            {
                Retire(entry.pending, entry.cancellation);
                entry.cancellation = null;
                ClearDiagnostics(entry.diagnostics);
            }
            m_work.Dispose();
            m_shaders.Clear();
            m_textures.Clear();
            m_lifetime.Dispose();
            m_disposed = true;
        }
    }

    private void StartShader(
        ShaderAsset shader,
        ShaderCompileTarget target,
        RenderShaderVariant variant,
        ShaderEntry entry)
    {
        Retire(entry.pending, entry.cancellation);
        entry.pending = null;
        entry.cancellation = CancellationTokenSource.CreateLinkedTokenSource(m_lifetime.Token);
        entry.attemptedContentVersion = shader.contentVersion;
        entry.status = entry.artifact is null
            ? RenderTargetArtifactStatus.Pending
            : RenderTargetArtifactStatus.Ready;
        try
        {
            CancellationToken token = entry.cancellation.Token;
            entry.pending = RunOwned(token => m_shaderCompiler.CompileGraphAsync(shader, target, variant, m_types, m_serialization,
                AssetSerializationContext.Create(m_assets), m_assets, token), token);
        }
        catch (Exception exception) when (Inno.Core.Execution.RetirementPendingException.Find(exception) is null)
        {
            entry.cancellation.Dispose();
            entry.cancellation = null;
            entry.pending = Task.FromResult(new ShaderCompilationResult(
                null,
                [new ShaderDiagnostic(
                    "SHADER_COMPILE_EXCEPTION",
                    DiagnosticSeverity.Error,
                    exception.Message)]));
        }
    }

    private void CompleteShader(ShaderKey key, ShaderEntry entry)
    {
        Task<ShaderCompilationResult>? pending = entry.pending;
        if (pending is null || !pending.IsCompleted)
            return;
        if (pending.Exception is Exception failure && Inno.Core.Execution.RetirementPendingException.Find(failure) is not null)
            throw failure;
        entry.pending = null;
        entry.cancellation?.Dispose();
        entry.cancellation = null;
        if (pending.IsCanceled)
            return;
        ShaderCompilationResult result = pending.IsCompletedSuccessfully
            ? pending.Result
            : new ShaderCompilationResult(
                null,
                [new ShaderDiagnostic(
                    "SHADER_COMPILE_EXCEPTION",
                    DiagnosticSeverity.Error,
                    pending.Exception?.GetBaseException().Message ?? "Shader compilation failed without an exception.")]);
        entry.latestSucceeded = result.succeeded;
        entry.sourceDiagnostics = Array.AsReadOnly(result.diagnostics.ToArray());
        var diagnostics = result.diagnostics
            .Select(diagnostic => new Diagnostic(
                diagnostic.code,
                diagnostic.message,
                diagnostic.severity == DiagnosticSeverity.Error
                    ? DiagnosticSeverity.Error
                    : diagnostic.severity == DiagnosticSeverity.Warning
                        ? DiagnosticSeverity.Warning
                        : DiagnosticSeverity.Info,
                diagnostic.location?.assetPath ?? key.shaderId.ToString("D")))
            .ToList();
        if (result.succeeded)
        {
            entry.artifact = result.artifact!.CreateRuntimeArtifact();
            entry.status = RenderTargetArtifactStatus.Ready;
        }
        else if (entry.artifact is not null)
        {
            diagnostics.Add(new Diagnostic(
                "RENDER_SHADER_USING_LAST_GOOD",
                $"Shader '{key.shaderId:D}' kept its last-good target artifact.",
                DiagnosticSeverity.Warning,
                key.shaderId.ToString("D")));
            entry.status = RenderTargetArtifactStatus.Ready;
        }
        else
        {
            entry.status = RenderTargetArtifactStatus.Failed;
        }
        ReplaceDiagnostics(entry.diagnostics, diagnostics);
    }

    private void StartTexture(RenderTextureArtifactReference texture, TextureEntry entry)
    {
        Retire(entry.pending, entry.cancellation);
        entry.pending = null;
        entry.cancellation = CancellationTokenSource.CreateLinkedTokenSource(m_lifetime.Token);
        entry.attemptedContentVersion = texture.contentRevision;
        entry.status = entry.artifact is null
            ? RenderTargetArtifactStatus.Pending
            : RenderTargetArtifactStatus.Ready;
        try
        {
            CancellationToken token = entry.cancellation.Token;
            entry.pending = RunOwned(async token =>
            {
                using ArtifactLease lease = m_assets.AcquireArtifact(texture.assetId, texture.slot.sourceOutputName);
                return await Task.Run(
                    async () =>
                    {
                        return await m_textureCompiler.CompileKtxAsync(
                            lease.info.absolutePath,
                            texture.slot.colorSpace,
                            token).ConfigureAwait(false);
                    }).ConfigureAwait(false);
            }, token);
        }
        catch (Exception exception) when (Inno.Core.Execution.RetirementPendingException.Find(exception) is null)
        {
            entry.cancellation.Dispose();
            entry.cancellation = null;
            entry.pending = Task.FromException<byte[]>(exception);
        }
    }

    private void CompleteTexture(TextureKey key, TextureEntry entry)
    {
        Task<byte[]>? pending = entry.pending;
        if (pending is null || !pending.IsCompleted)
            return;
        if (pending.Exception is Exception failure && Inno.Core.Execution.RetirementPendingException.Find(failure) is not null)
            throw failure;
        entry.pending = null;
        entry.cancellation?.Dispose();
        entry.cancellation = null;
        if (pending.IsCompletedSuccessfully && pending.Result.Length > 0)
        {
            entry.artifact = pending.Result;
            entry.status = RenderTargetArtifactStatus.Ready;
            ClearDiagnostics(entry.diagnostics);
            return;
        }
        if (pending.IsCanceled)
            return;
        string message = pending.IsCompletedSuccessfully
            ? "The texture compiler produced an empty target artifact."
            : pending.Exception?.GetBaseException().Message ?? "Texture compilation failed without an exception.";
        ReplaceDiagnostics(entry.diagnostics, [new Diagnostic(
            "RENDER_TEXTURE_PREWARM_FAILED",
            $"Texture '{key.textureId:D}' kept its last-good target artifact: {message}",
            DiagnosticSeverity.Error,
            key.textureId.ToString("D"))]);
        entry.status = entry.artifact is null
            ? RenderTargetArtifactStatus.Failed
            : RenderTargetArtifactStatus.Ready;
    }

    private AssetSourceMount GetMount(AssetSourceId source)
        => m_assets.sourceMounts.FirstOrDefault(mount => mount.id == source)
            ?? throw new InvalidOperationException($"Asset source mount '{source}' is not active.");

    private Task<T> RunOwned<T>(Func<CancellationToken, ValueTask<T>> operation, CancellationToken token)
    {
        // Admit before starting work or acquiring leases. Ordinary compilation errors are data;
        // retirement-pending failures stay in the lifetime and retain every dependent owner.
        return Unwrap(m_work.RunAsync<(T value, Exception? failure)>(async cancellation =>
        {
            try { return (await operation(cancellation).ConfigureAwait(false), null); }
            catch (Exception failure) when (Inno.Core.Execution.RetirementPendingException.Find(failure) is null)
            { return (default!, failure); }
        }, token));

        static async Task<T> Unwrap(Task<(T value, Exception? failure)> task)
        {
            var result = await task.ConfigureAwait(false);
            if (result.failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(result.failure).Throw();
            return result.value;
        }
    }

    private static void Retire(Task? task, CancellationTokenSource? cancellation)
    {
        if (cancellation is null)
            return;
        cancellation.Cancel();
        if (task is null)
        {
            cancellation.Dispose();
            return;
        }
        _ = task.ContinueWith(
            static (completed, state) =>
            {
                _ = completed.Exception;
                ((CancellationTokenSource)state!).Dispose();
            },
            cancellation,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private void ReplaceDiagnostics(
        ISet<DiagnosticIdentity> active,
        IEnumerable<Diagnostic> diagnostics)
    {
        ClearDiagnostics(active);
        foreach (Diagnostic diagnostic in diagnostics)
        {
            m_diagnostics.Publish(diagnostic);
            active.Add(new DiagnosticIdentity(diagnostic.code, diagnostic.semanticId));
        }
    }

    private void ClearDiagnostics(ISet<DiagnosticIdentity> active)
    {
        foreach (DiagnosticIdentity diagnostic in active)
            m_diagnostics.Resolve(diagnostic.code, diagnostic.semanticId);
        active.Clear();
    }

    private void EnsureActive()
    {
        ObjectDisposedException.ThrowIf(m_disposed, this);
        if (m_stopping) throw new InvalidOperationException("A retiring artifact provider cannot accept compilation requests.");
    }

    private readonly record struct ShaderKey(Guid shaderId, string targetKey, string variantKey);
    private readonly record struct TextureKey(
        Guid textureId,
        string slotId,
        TextureColorSpace colorSpace);
    private readonly record struct DiagnosticIdentity(string code, string? semanticId);

    private sealed class ShaderEntry
    {
        internal string semanticHash { get; set; } = "";
        internal long extensionGeneration { get; set; } = long.MinValue;
        internal bool latestSucceeded { get; set; }
        internal IReadOnlyList<ShaderDiagnostic> sourceDiagnostics { get; set; } = Array.Empty<ShaderDiagnostic>();
        internal long attemptedContentVersion { get; set; } = long.MinValue;
        internal RenderShaderArtifact? artifact { get; set; }
        internal RenderTargetArtifactStatus status { get; set; } = RenderTargetArtifactStatus.Unavailable;
        internal Task<ShaderCompilationResult>? pending { get; set; }
        internal CancellationTokenSource? cancellation { get; set; }
        internal HashSet<DiagnosticIdentity> diagnostics { get; } = [];
    }

    private sealed class TextureEntry
    {
        internal long attemptedContentVersion { get; set; } = long.MinValue;
        internal byte[]? artifact { get; set; }
        internal RenderTargetArtifactStatus status { get; set; } = RenderTargetArtifactStatus.Unavailable;
        internal Task<byte[]>? pending { get; set; }
        internal CancellationTokenSource? cancellation { get; set; }
        internal HashSet<DiagnosticIdentity> diagnostics { get; } = [];
    }
}
