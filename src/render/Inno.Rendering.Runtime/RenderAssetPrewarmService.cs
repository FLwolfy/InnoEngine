using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Inno.Assets;
using Inno.Assets.Core;
using Inno.Assets.File;
using Inno.Rendering.Assets;
using Inno.Rendering.Core;

namespace Inno.Rendering.Runtime;

internal sealed class RenderAssetPrewarmService : IDisposable
{
    private const ulong C_UNUSED_FRAME_LIMIT = 240;

    private readonly ShaderCompiler? m_shaderCompiler;
    private readonly ITextureTargetCompiler? m_textureCompiler;
    private readonly IRenderDiagnosticSink m_diagnostics;
    private readonly CancellationTokenSource m_cancellation = new();
    private readonly Dictionary<ShaderJobKey, ShaderJobEntry> m_shaders = [];
    private readonly Dictionary<TextureJobKey, TextureJobEntry> m_textures = [];
    private ulong m_frameIndex;
    private bool m_disposed;

    internal RenderAssetPrewarmService(
        ShaderCompiler? shaderCompiler,
        ITextureTargetCompiler? textureCompiler,
        IRenderDiagnosticSink diagnostics)
    {
        m_shaderCompiler = shaderCompiler;
        m_textureCompiler = textureCompiler;
        m_diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
    }

    internal PreparedShaderSelection GetOrRequestShader(
        ShaderAsset shader,
        ShaderCompileTarget target,
        ShaderVariantKey variant)
    {
        ObjectDisposedException.ThrowIf(m_disposed, this);
        ArgumentNullException.ThrowIfNull(shader);
        if (m_shaderCompiler is null)
            throw new InvalidOperationException("No shader target compiler is configured for this render runtime.");
        var key = new ShaderJobKey(shader.identity.persistentId, target.key, variant.value);
        if (!m_shaders.TryGetValue(key, out ShaderJobEntry? entry))
        {
            entry = new ShaderJobEntry();
            m_shaders.Add(key, entry);
        }
        entry.lastRequestedFrame = m_frameIndex;
        if (entry.attemptedContentVersion != shader.contentVersion)
        {
            RetirePending(entry.pending, entry.cancellation);
            entry.pending = null;
            entry.cancellation = null;
            entry.attemptedContentVersion = shader.contentVersion;
            try
            {
                if (shader.runtimePayload.IsEmpty)
                {
                    throw new InvalidOperationException(
                        $"Shader asset '{shader.name}' has no committed IR payload.");
                }
                byte[] modulePayload = shader.runtimePayload.ToArray();
                string sourceRoot = ResolveSourceRoot(shader.assetPath);
                var cancellation = CancellationTokenSource.CreateLinkedTokenSource(m_cancellation.Token);
                entry.cancellation = cancellation;
                entry.pending = Task.Run(
                    async () => await m_shaderCompiler!.CompileAsync(
                        ShaderIRArtifactSerialization.Decode(modulePayload),
                        target,
                        variant,
                        sourceRoot,
                        cancellation.Token).ConfigureAwait(false),
                    cancellation.Token);
            }
            catch (Exception exception)
            {
                entry.cancellation?.Dispose();
                entry.cancellation = null;
                entry.pending = Task.FromResult(new ShaderCompilationResult(
                    null,
                    [CreateShaderException(shader, exception)]));
            }
        }

        return new PreparedShaderSelection(
            entry.artifact,
            entry.artifactContentVersion,
            entry.pending is not null);
    }

    internal PreparedTextureSelection GetOrRequestTexture(TextureAsset texture)
    {
        ObjectDisposedException.ThrowIf(m_disposed, this);
        ArgumentNullException.ThrowIfNull(texture);
        if (m_textureCompiler is null)
            throw new InvalidOperationException("No texture target compiler is configured for this render runtime.");
        var key = new TextureJobKey(texture.identity.persistentId, texture.colorSpace);
        if (!m_textures.TryGetValue(key, out TextureJobEntry? entry))
        {
            entry = new TextureJobEntry();
            m_textures.Add(key, entry);
        }
        entry.lastRequestedFrame = m_frameIndex;
        if (entry.attemptedContentVersion != texture.contentVersion)
        {
            RetirePending(entry.pending, entry.cancellation);
            entry.pending = null;
            entry.cancellation = null;
            entry.attemptedContentVersion = texture.contentVersion;
            try
            {
                string sourcePath = ResolvePhysicalSource(texture.assetPath);
                var cancellation = CancellationTokenSource.CreateLinkedTokenSource(m_cancellation.Token);
                entry.cancellation = cancellation;
                entry.pending = Task.Run(
                    async () => await m_textureCompiler!.CompileKtxAsync(
                        sourcePath,
                        texture.colorSpace,
                        cancellation.Token).ConfigureAwait(false),
                    cancellation.Token);
            }
            catch (Exception exception)
            {
                entry.cancellation?.Dispose();
                entry.cancellation = null;
                entry.pending = Task.FromException<byte[]>(exception);
            }
        }

        return new PreparedTextureSelection(
            entry.artifact,
            entry.artifactContentVersion,
            entry.pending is not null);
    }

    internal void BeginFrame(ulong frameIndex)
    {
        ObjectDisposedException.ThrowIf(m_disposed, this);
        m_frameIndex = frameIndex;
        DrainShaders();
        DrainTextures();
    }

    internal void SweepUnused()
    {
        ObjectDisposedException.ThrowIf(m_disposed, this);
        if (m_frameIndex < C_UNUSED_FRAME_LIMIT)
            return;
        ulong oldest = m_frameIndex - C_UNUSED_FRAME_LIMIT;
        foreach (ShaderJobKey key in m_shaders
                     .Where(pair => pair.Value.pending is null && pair.Value.lastRequestedFrame < oldest)
                     .Select(static pair => pair.Key)
                     .ToArray())
        {
            m_shaders.Remove(key);
        }
        foreach (TextureJobKey key in m_textures
                     .Where(pair => pair.Value.pending is null && pair.Value.lastRequestedFrame < oldest)
                     .Select(static pair => pair.Key)
                     .ToArray())
        {
            m_textures.Remove(key);
        }
    }

    public void Dispose()
    {
        if (m_disposed)
            return;
        m_disposed = true;
        m_cancellation.Cancel();
        foreach (ShaderJobEntry entry in m_shaders.Values)
            RetirePending(entry.pending, entry.cancellation);
        foreach (TextureJobEntry entry in m_textures.Values)
            RetirePending(entry.pending, entry.cancellation);
        m_cancellation.Dispose();
        m_shaders.Clear();
        m_textures.Clear();
    }

    private void DrainShaders()
    {
        foreach ((ShaderJobKey key, ShaderJobEntry entry) in m_shaders)
        {
            Task<ShaderCompilationResult>? pending = entry.pending;
            if (pending is null || !pending.IsCompleted)
                continue;
            entry.pending = null;
            entry.cancellation?.Dispose();
            entry.cancellation = null;
            ShaderCompilationResult result;
            if (pending.IsCompletedSuccessfully)
            {
                result = pending.Result;
            }
            else if (pending.IsCanceled)
            {
                continue;
            }
            else
            {
                Exception exception = pending.Exception?.GetBaseException()
                    ?? new InvalidOperationException("Shader prewarm failed without an exception.");
                result = new ShaderCompilationResult(
                    null,
                    [new ShaderDiagnostic(
                        "SHADER_COMPILE_EXCEPTION",
                        ShaderDiagnosticSeverity.Error,
                        exception.Message)]);
            }

            PublishShaderDiagnostics(key, result.diagnostics);
            if (result.succeeded)
            {
                entry.artifact = result.artifact;
                entry.artifactContentVersion = entry.attemptedContentVersion;
            }
            else if (entry.artifact is not null)
            {
                m_diagnostics.Publish(new RenderDiagnostic(
                    "RENDER_SHADER_USING_LAST_GOOD",
                    $"Shader '{key.shaderId}' is using its last-good compiled artifact.",
                    RenderDiagnosticSeverity.Warning,
                    key.shaderId.ToString("D")));
            }
        }
    }

    private void DrainTextures()
    {
        foreach ((TextureJobKey key, TextureJobEntry entry) in m_textures)
        {
            Task<byte[]>? pending = entry.pending;
            if (pending is null || !pending.IsCompleted)
                continue;
            entry.pending = null;
            entry.cancellation?.Dispose();
            entry.cancellation = null;
            if (pending.IsCompletedSuccessfully)
            {
                byte[] artifact = pending.Result;
                if (artifact.Length == 0)
                {
                    m_diagnostics.Publish(new RenderDiagnostic(
                        "RENDER_TEXTURE_PREWARM_FAILED",
                        $"Texture '{key.textureId}' kept its last-good target artifact: " +
                        "the target compiler produced an empty artifact.",
                        RenderDiagnosticSeverity.Error,
                        key.textureId.ToString("D")));
                }
                else
                {
                    entry.artifact = artifact;
                    entry.artifactContentVersion = entry.attemptedContentVersion;
                }
            }
            else if (pending.IsCanceled)
            {
            }
            else
            {
                Exception exception = pending.Exception?.GetBaseException()
                    ?? new InvalidOperationException("Texture prewarm failed without an exception.");
                m_diagnostics.Publish(new RenderDiagnostic(
                    "RENDER_TEXTURE_PREWARM_FAILED",
                    $"Texture '{key.textureId}' kept its last-good target artifact: {exception.Message}",
                    RenderDiagnosticSeverity.Error,
                    key.textureId.ToString("D")));
            }
        }
    }

    private void PublishShaderDiagnostics(
        ShaderJobKey key,
        IReadOnlyList<ShaderDiagnostic> diagnostics)
    {
        foreach (ShaderDiagnostic diagnostic in diagnostics)
        {
            m_diagnostics.Publish(new RenderDiagnostic(
                diagnostic.code,
                diagnostic.message,
                diagnostic.severity == ShaderDiagnosticSeverity.Error
                    ? RenderDiagnosticSeverity.Error
                    : diagnostic.severity == ShaderDiagnosticSeverity.Warning
                        ? RenderDiagnosticSeverity.Warning
                        : RenderDiagnosticSeverity.Info,
                diagnostic.location?.assetPath ?? key.shaderId.ToString("D")));
        }
    }

    private static void RetirePending(Task? task, CancellationTokenSource? cancellation)
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

    private static ShaderDiagnostic CreateShaderException(ShaderAsset shader, Exception exception)
        => new(
            "SHADER_COMPILE_EXCEPTION",
            ShaderDiagnosticSeverity.Error,
            exception.Message,
            new ShaderSourceLocation(shader.assetPath.ToString(), "Shader", ShaderStage.None));

    private static string ResolveSourceRoot(AssetPath assetPath)
        => GetMount(assetPath.source).rootPath;

    private static string ResolvePhysicalSource(AssetPath assetPath)
        => GetMount(assetPath.source).Resolve(assetPath.localPath);

    private static AssetSourceMount GetMount(AssetSourceId source)
        => AssetManager.sourceMounts.FirstOrDefault(mount => mount.id == source)
            ?? throw new InvalidOperationException($"Asset source mount '{source}' is not active.");

    private readonly record struct ShaderJobKey(Guid shaderId, string targetKey, string variantKey);
    private readonly record struct TextureJobKey(Guid textureId, TextureColorSpace colorSpace);

    private sealed class ShaderJobEntry
    {
        internal long attemptedContentVersion { get; set; } = long.MinValue;
        internal long artifactContentVersion { get; set; } = long.MinValue;
        internal ulong lastRequestedFrame { get; set; }
        internal CompiledShaderArtifact? artifact { get; set; }
        internal Task<ShaderCompilationResult>? pending { get; set; }
        internal CancellationTokenSource? cancellation { get; set; }
    }

    private sealed class TextureJobEntry
    {
        internal long attemptedContentVersion { get; set; } = long.MinValue;
        internal long artifactContentVersion { get; set; } = long.MinValue;
        internal ulong lastRequestedFrame { get; set; }
        internal byte[]? artifact { get; set; }
        internal Task<byte[]>? pending { get; set; }
        internal CancellationTokenSource? cancellation { get; set; }
    }
}

internal readonly record struct PreparedShaderSelection(
    CompiledShaderArtifact? artifact,
    long contentVersion,
    bool isPending);

internal readonly record struct PreparedTextureSelection(
    byte[]? artifact,
    long contentVersion,
    bool isPending);
