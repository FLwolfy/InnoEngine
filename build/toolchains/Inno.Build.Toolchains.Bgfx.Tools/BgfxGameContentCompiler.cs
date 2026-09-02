using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Inno.Assets;
using Inno.Assets.Pipeline;
using Inno.Core.Serialization;
using Inno.Rendering;
using Inno.Rendering.Assets;

namespace Inno.Build.Toolchains.Bgfx.Tools;

/// <summary>
/// Produces source-free BGFX shader and texture artifacts for one Player target.
/// </summary>
public sealed class BgfxGameContentCompiler
{
    private readonly AssetPipeline m_assets;
    private readonly BgfxShaderTargetPlatform m_platform;
    private readonly GraphicsBackend[] m_backends;
    private readonly SerializationRegistry m_serialization;

    private BgfxGameContentCompiler(
        AssetPipeline assets,
        SerializationRegistry serialization,
        BgfxShaderTargetPlatform platform,
        IEnumerable<GraphicsBackend> backends)
    {
        ArgumentNullException.ThrowIfNull(assets);
        ArgumentNullException.ThrowIfNull(serialization);
        ArgumentNullException.ThrowIfNull(backends);
        GraphicsBackend[] snapshot = backends.Distinct().ToArray();
        if (snapshot.Length == 0)
            throw new ArgumentException("At least one Player graphics backend is required.", nameof(backends));
        if (snapshot.Contains(GraphicsBackend.Noop))
            throw new ArgumentException("A deployable Player cannot target the Noop graphics backend.", nameof(backends));
        m_assets = assets;
        m_serialization = serialization;
        m_platform = platform;
        m_backends = snapshot;
    }

    /// <summary>
    /// Creates the canonical Metal compiler used by Apple Silicon macOS Players.
    /// </summary>
    /// <returns>
    /// A compiler configured for the complete macOS runtime backend set.
    /// </returns>
    /// <param name="assets">
    /// The authoring asset pipeline whose committed generation is compiled.
    /// </param>
    /// <param name="serialization">
    /// The serialization registry that owns Shader IR contracts.
    /// </param>
    public static BgfxGameContentCompiler CreateMacOSArm64(
        AssetPipeline assets,
        SerializationRegistry serialization)
        => new(
            assets,
            serialization,
            BgfxShaderTargetPlatform.MacOSArm64,
            [GraphicsBackend.Metal]);

    /// <summary>
    /// Creates the canonical compiler used by 64-bit Windows Players.
    /// </summary>
    /// <returns>
    /// A compiler configured for every supported Windows runtime backend.
    /// </returns>
    /// <param name="assets">
    /// The authoring asset pipeline whose committed generation is compiled.
    /// </param>
    /// <param name="serialization">
    /// The serialization registry that owns Shader IR contracts.
    /// </param>
    public static BgfxGameContentCompiler CreateWindowsX64(
        AssetPipeline assets,
        SerializationRegistry serialization)
        => new(
            assets,
            serialization,
            BgfxShaderTargetPlatform.WindowsX64,
            [GraphicsBackend.Direct3D11, GraphicsBackend.Direct3D12, GraphicsBackend.Vulkan]);

    /// <summary>
    /// Captures the active Asset generation and compiles every required runtime variant.
    /// </summary>
    /// <param name="context">
    /// The target staging path and immutable build generation services.
    /// </param>
    /// <param name="cancellationToken">
    /// Cancellation observed before every compiler invocation and artifact write.
    /// </param>
    /// <returns>
    /// An operation that completes when the source-free target closure is staged.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when an asset has no stable identity, source mount, or valid target compilation result.
    /// </exception>
    public async ValueTask CompileAsync(
        GameBuildContentContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ContentSnapshot snapshot = CaptureSnapshot();
        string outputRoot = Path.GetFullPath(context.outputDirectory);
        Directory.CreateDirectory(outputRoot);
        var shaderCompiler = new ShaderCompiler(new BgfxShadercToolchain(m_platform));
        foreach (GraphicsBackend backend in m_backends.Order())
        {
            GraphicsCapabilities capabilities = CreateCapabilities(backend);
            ShaderCompileTarget target = shaderCompiler.CreateTarget(
                capabilities,
                optimize: true,
                debugInformation: false);
            foreach (ShaderInput shader in snapshot.shaders)
            {
                foreach (RenderShaderVariant variant in shader.variants.OrderBy(static value => value.value, StringComparer.Ordinal))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    ShaderCompilationResult result = await shaderCompiler.CompileAsync(
                            ShaderAssetRuntime.GetModule(shader.asset, m_serialization),
                            target,
                            variant,
                            shader.sourceRoot,
                            cancellationToken)
                        .ConfigureAwait(false);
                    if (!result.succeeded)
                    {
                        string diagnostics = string.Join(
                            Environment.NewLine,
                            result.diagnostics.Select(static value => $"[{value.code}] {value.message}"));
                        throw new InvalidOperationException(
                            $"Shader '{shader.asset.assetPath}' failed target compilation for '{backend}':{Environment.NewLine}" +
                            diagnostics);
                    }
                    string destination = ResolveOutput(
                        outputRoot,
                        RenderTargetArtifactPath.GetShaderPath(
                            shader.asset.identity.persistentId,
                            backend,
                            variant));
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    await File.WriteAllBytesAsync(
                            destination,
                            RenderShaderArtifactCodec.Encode(result.artifact!.CreateRuntimeArtifact()),
                            cancellationToken)
                        .ConfigureAwait(false);
                }
            }
        }

        var textureCompiler = new BgfxTextureTargetCompiler();
        foreach (TextureInput texture in snapshot.textures)
        {
            cancellationToken.ThrowIfCancellationRequested();
            byte[] bytes = await textureCompiler.CompileKtxAsync(
                    texture.sourcePath,
                    texture.asset.colorSpace,
                    cancellationToken)
                .ConfigureAwait(false);
            string destination = ResolveOutput(
                outputRoot,
                RenderTargetArtifactPath.GetTexturePath(texture.asset.identity.persistentId));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            await File.WriteAllBytesAsync(destination, bytes, cancellationToken).ConfigureAwait(false);
        }
    }

    private ContentSnapshot CaptureSnapshot()
    {
        if (!m_assets.isInitialized)
            throw new InvalidOperationException("BGFX target compilation requires an active authoring Asset database.");
        Dictionary<AssetSourceId, AssetSourceMount> mounts = m_assets.sourceMounts.ToDictionary(
            static mount => mount.id);
        var shaders = new Dictionary<Guid, ShaderInput>();
        var textures = new Dictionary<Guid, TextureInput>();
        var materialVariants = new List<(ShaderAsset shader, RenderShaderVariant variant)>();
        AssetFileEntry[] entries = m_assets.GetFileSystemEntries(includeDirectories: false)
            .OrderBy(static entry => entry.assetPath.ToString(), StringComparer.Ordinal)
            .ToArray();
        foreach (AssetFileEntry entry in entries)
        {
            if (!m_assets.TryGetAssetType(entry.assetPath, out Type? assetType) || assetType is null)
                continue;
            if (typeof(MaterialAsset).IsAssignableFrom(assetType))
            {
                MaterialAsset material = m_assets.Load<MaterialAsset>(entry.assetPath);
                if (material.shader is not null)
                    materialVariants.Add((material.shader, RenderShaderVariant.FromMaterial(material)));
            }
            if (typeof(ShaderAsset).IsAssignableFrom(assetType))
            {
                ShaderAsset shader = m_assets.Load<ShaderAsset>(entry.assetPath);
                AddShader(shaders, mounts, shader, RenderShaderVariant.empty);
            }
            if (typeof(TextureAsset).IsAssignableFrom(assetType))
            {
                TextureAsset texture = m_assets.Load<TextureAsset>(entry.assetPath);
                Guid id = RequireIdentity(texture);
                AssetSourceMount mount = GetMount(mounts, texture.assetPath);
                textures.TryAdd(id, new TextureInput(texture, mount.Resolve(texture.assetPath.localPath)));
            }
        }
        foreach ((ShaderAsset shader, RenderShaderVariant variant) in materialVariants)
            AddShader(shaders, mounts, shader, variant);
        return new ContentSnapshot(
            shaders.Values.OrderBy(static value => value.asset.identity.persistentId).ToArray(),
            textures.Values.OrderBy(static value => value.asset.identity.persistentId).ToArray());
    }

    private static void AddShader(
        IDictionary<Guid, ShaderInput> shaders,
        IReadOnlyDictionary<AssetSourceId, AssetSourceMount> mounts,
        ShaderAsset shader,
        RenderShaderVariant variant)
    {
        Guid id = RequireIdentity(shader);
        if (!shaders.TryGetValue(id, out ShaderInput? input))
        {
            input = new ShaderInput(shader, GetMount(mounts, shader.assetPath).rootPath);
            shaders.Add(id, input);
        }
        input.variants.Add(variant);
    }

    private static Guid RequireIdentity(AssetObject asset)
    {
        Guid id = asset.identity.persistentId;
        if (id == Guid.Empty)
            throw new InvalidOperationException($"Runtime asset '{asset.assetPath}' has no persistent identity.");
        return id;
    }

    private static AssetSourceMount GetMount(
        IReadOnlyDictionary<AssetSourceId, AssetSourceMount> mounts,
        AssetPath path)
        => mounts.TryGetValue(path.source, out AssetSourceMount? mount)
            ? mount
            : throw new InvalidOperationException($"Asset source mount '{path.source}' is not active.");

    private static GraphicsCapabilities CreateCapabilities(GraphicsBackend backend)
    {
        RenderTextureFormat[] formats = Enum.GetValues<RenderTextureFormat>();
        GraphicsFeature features = Enum.GetValues<GraphicsFeature>()
            .Aggregate(GraphicsFeature.None, static (current, value) => current | value);
        return new GraphicsCapabilities(
            backend,
            features,
            new GraphicsLimits(256, 8, 16384, 16),
            formats,
            formats,
            formats,
            formats,
            originBottomLeft: backend == GraphicsBackend.OpenGL,
            homogeneousDepth: backend == GraphicsBackend.OpenGL,
            formats,
            formats,
            formats);
    }

    private static string ResolveOutput(string root, string relativePath)
    {
        string result = Path.GetFullPath(Path.Combine(root, relativePath));
        string prefix = Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar;
        StringComparison comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (!result.StartsWith(prefix, comparison))
            throw new InvalidOperationException("A target artifact path escaped its staging directory.");
        return result;
    }

    private sealed record ContentSnapshot(ShaderInput[] shaders, TextureInput[] textures);

    private sealed class ShaderInput
    {
        internal ShaderInput(ShaderAsset asset, string sourceRoot)
        {
            this.asset = asset;
            this.sourceRoot = sourceRoot;
        }

        internal ShaderAsset asset { get; }
        internal string sourceRoot { get; }
        internal HashSet<RenderShaderVariant> variants { get; } = [];
    }

    private sealed record TextureInput(TextureAsset asset, string sourcePath);
}
