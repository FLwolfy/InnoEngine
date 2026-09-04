using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Inno.Assets;
using Inno.Assets.Pipeline;
using Inno.Core.Serialization;
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
    private readonly ShaderCompiler m_shaderCompiler;
    private readonly ITextureTargetCompiler m_textureCompiler;
    private readonly IRenderDiagnosticSink m_diagnostics;
    private readonly CancellationTokenSource m_lifetime = new();
    private readonly Dictionary<ShaderKey, ShaderEntry> m_shaders = [];
    private readonly Dictionary<TextureKey, TextureEntry> m_textures = [];
    private bool m_disposed;

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
    public EditorRenderTargetArtifactProvider(
        AssetPipeline assets,
        SerializationRegistry serialization,
        ShaderCompiler shaderCompiler,
        ITextureTargetCompiler textureCompiler,
        IRenderDiagnosticSink diagnostics)
    {
        m_assets = assets ?? throw new ArgumentNullException(nameof(assets));
        m_serialization = serialization ?? throw new ArgumentNullException(nameof(serialization));
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
            CompleteShader(key, entry);
            if (entry.attemptedContentVersion != shader.contentVersion)
                StartShader(shader, target, variant, entry);
            artifact = entry.artifact;
            return artifact is not null
                ? RenderTargetArtifactStatus.Ready
                : entry.status;
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
        TextureAsset texture,
        out ReadOnlyMemory<byte> artifact)
    {
        ArgumentNullException.ThrowIfNull(texture);
        lock (m_sync)
        {
            EnsureActive();
            var key = new TextureKey(texture.identity.persistentId, texture.colorSpace);
            if (!m_textures.TryGetValue(key, out TextureEntry? entry))
            {
                entry = new TextureEntry();
                m_textures.Add(key, entry);
            }
            CompleteTexture(key, entry);
            if (entry.attemptedContentVersion != texture.contentVersion)
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
            m_disposed = true;
            m_lifetime.Cancel();
            foreach (ShaderEntry entry in m_shaders.Values)
            {
                Retire(entry.pending, entry.cancellation);
                ClearDiagnostics(entry.diagnostics);
            }
            foreach (TextureEntry entry in m_textures.Values)
            {
                Retire(entry.pending, entry.cancellation);
                ClearDiagnostics(entry.diagnostics);
            }
            m_shaders.Clear();
            m_textures.Clear();
            m_lifetime.Dispose();
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
            ShaderIRModule module = ShaderAssetRuntime.GetModule(shader, m_serialization);
            string sourceRoot = GetMount(shader.assetPath.source).rootPath;
            CancellationToken token = entry.cancellation.Token;
            entry.pending = Task.Run(
                async () => await m_shaderCompiler.CompileAsync(
                    module,
                    target,
                    variant,
                    sourceRoot,
                    token).ConfigureAwait(false),
                token);
        }
        catch (Exception exception)
        {
            entry.cancellation.Dispose();
            entry.cancellation = null;
            entry.pending = Task.FromResult(new ShaderCompilationResult(
                null,
                [new ShaderDiagnostic(
                    "SHADER_COMPILE_EXCEPTION",
                    ShaderDiagnosticSeverity.Error,
                    exception.Message)]));
        }
    }

    private void CompleteShader(ShaderKey key, ShaderEntry entry)
    {
        Task<ShaderCompilationResult>? pending = entry.pending;
        if (pending is null || !pending.IsCompleted)
            return;
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
                    ShaderDiagnosticSeverity.Error,
                    pending.Exception?.GetBaseException().Message ?? "Shader compilation failed without an exception.")]);
        var diagnostics = result.diagnostics
            .Select(diagnostic => new RenderDiagnostic(
                diagnostic.code,
                diagnostic.message,
                diagnostic.severity == ShaderDiagnosticSeverity.Error
                    ? RenderDiagnosticSeverity.Error
                    : diagnostic.severity == ShaderDiagnosticSeverity.Warning
                        ? RenderDiagnosticSeverity.Warning
                        : RenderDiagnosticSeverity.Info,
                diagnostic.location?.assetPath ?? key.shaderId.ToString("D")))
            .ToList();
        if (result.succeeded)
        {
            entry.artifact = result.artifact!.CreateRuntimeArtifact();
            entry.status = RenderTargetArtifactStatus.Ready;
        }
        else if (entry.artifact is not null)
        {
            diagnostics.Add(new RenderDiagnostic(
                "RENDER_SHADER_USING_LAST_GOOD",
                $"Shader '{key.shaderId:D}' kept its last-good target artifact.",
                RenderDiagnosticSeverity.Warning,
                key.shaderId.ToString("D")));
            entry.status = RenderTargetArtifactStatus.Ready;
        }
        else
        {
            entry.status = RenderTargetArtifactStatus.Failed;
        }
        ReplaceDiagnostics(entry.diagnostics, diagnostics);
    }

    private void StartTexture(TextureAsset texture, TextureEntry entry)
    {
        Retire(entry.pending, entry.cancellation);
        entry.pending = null;
        entry.cancellation = CancellationTokenSource.CreateLinkedTokenSource(m_lifetime.Token);
        entry.attemptedContentVersion = texture.contentVersion;
        entry.status = entry.artifact is null
            ? RenderTargetArtifactStatus.Pending
            : RenderTargetArtifactStatus.Ready;
        try
        {
            string sourcePath = GetMount(texture.assetPath.source).Resolve(texture.assetPath.localPath);
            CancellationToken token = entry.cancellation.Token;
            entry.pending = Task.Run(
                async () => await m_textureCompiler.CompileKtxAsync(
                    sourcePath,
                    texture.colorSpace,
                    token).ConfigureAwait(false),
                token);
        }
        catch (Exception exception)
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
        ReplaceDiagnostics(entry.diagnostics, [new RenderDiagnostic(
            "RENDER_TEXTURE_PREWARM_FAILED",
            $"Texture '{key.textureId:D}' kept its last-good target artifact: {message}",
            RenderDiagnosticSeverity.Error,
            key.textureId.ToString("D"))]);
        entry.status = entry.artifact is null
            ? RenderTargetArtifactStatus.Failed
            : RenderTargetArtifactStatus.Ready;
    }

    private AssetSourceMount GetMount(AssetSourceId source)
        => m_assets.sourceMounts.FirstOrDefault(mount => mount.id == source)
            ?? throw new InvalidOperationException($"Asset source mount '{source}' is not active.");

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
        IEnumerable<RenderDiagnostic> diagnostics)
    {
        ClearDiagnostics(active);
        foreach (RenderDiagnostic diagnostic in diagnostics)
        {
            m_diagnostics.Publish(diagnostic);
            active.Add(new DiagnosticIdentity(diagnostic.code, diagnostic.sourceId));
        }
    }

    private void ClearDiagnostics(ISet<DiagnosticIdentity> active)
    {
        foreach (DiagnosticIdentity diagnostic in active)
            m_diagnostics.Resolve(diagnostic.code, diagnostic.sourceId);
        active.Clear();
    }

    private void EnsureActive()
        => ObjectDisposedException.ThrowIf(m_disposed, this);

    private readonly record struct ShaderKey(Guid shaderId, string targetKey, string variantKey);
    private readonly record struct TextureKey(Guid textureId, TextureColorSpace colorSpace);
    private readonly record struct DiagnosticIdentity(string code, string? sourceId);

    private sealed class ShaderEntry
    {
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
