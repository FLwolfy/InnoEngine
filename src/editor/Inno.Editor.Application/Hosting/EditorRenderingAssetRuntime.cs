using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Inno.Assets;
using Inno.Assets.Core;
using Inno.Assets.File;
using Inno.Rendering;
using Inno.Rendering.Assets;
using Inno.Rendering.Core;
using Inno.Rendering.Pipelines;
using Inno.Rendering.ShaderGraph;

namespace Inno.Editor.Application;

internal sealed class EditorRenderingAssetRuntime : IDisposable
{
    private readonly RenderPipelineArtifactRegistry m_artifacts;
    private readonly IRenderDevice m_device;
    private readonly ShaderNodeRegistry m_nodes;
    private readonly IRenderDiagnosticSink m_diagnostics;
    private readonly ShaderCompiler m_compiler = new();
    private readonly TextureTargetCompiler m_textureCompiler = new();
    private readonly Dictionary<Guid, TextureResident> m_textures = [];
    private readonly ShaderCompileTarget m_target;
    private readonly string m_sourceRoot;
    private ulong m_nodeGeneration;
    private bool m_dirty = true;
    private bool m_disposed;

    internal EditorRenderingAssetRuntime(
        GraphicsCapabilities capabilities,
        IRenderDevice device,
        RenderPipelineArtifactRegistry artifacts,
        ShaderNodeRegistry nodes,
        IRenderDiagnosticSink diagnostics)
    {
        ArgumentNullException.ThrowIfNull(capabilities);
        m_device = device ?? throw new ArgumentNullException(nameof(device));
        m_artifacts = artifacts ?? throw new ArgumentNullException(nameof(artifacts));
        m_nodes = nodes ?? throw new ArgumentNullException(nameof(nodes));
        m_diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
        m_sourceRoot = AssetManager.assetRoot;
        ShaderTargetPlatform platform = OperatingSystem.IsWindows()
            ? ShaderTargetPlatform.WindowsX64
            : OperatingSystem.IsMacOS()
                ? ShaderTargetPlatform.MacOSArm64
                : throw new PlatformNotSupportedException(
                    "The first rendering milestone supports Windows x64 and macOS arm64 editors.");
        m_target = new ShaderCompileTarget(
            RendererProfileCatalog.Resolve(platform, capabilities),
            capabilities,
            optimize: false,
            debugInformation: true);
        AssetManager.Changed += OnAssetsChanged;
        AssetManager.AssetReloaded += OnAssetReloaded;
        Update();
    }

    internal void Update()
    {
        ObjectDisposedException.ThrowIf(m_disposed, this);
        if (m_nodeGeneration != m_nodes.generation)
        {
            m_nodeGeneration = m_nodes.generation;
            m_dirty = true;
        }

        if (!m_dirty)
        {
            return;
        }

        m_dirty = false;
        RefreshAll();
    }

    public void Dispose()
    {
        if (m_disposed)
        {
            return;
        }

        AssetManager.Changed -= OnAssetsChanged;
        AssetManager.AssetReloaded -= OnAssetReloaded;
        foreach (TextureResident resident in m_textures.Values)
        {
            m_device.DestroyTexture(resident.handle);
        }

        m_textures.Clear();
        m_disposed = true;
    }

    private void RefreshAll()
    {
        ShaderAsset[] shaders = AssetManager.GetFileSystemEntries(includeDirectories: false)
            .Where(static entry => entry.extension is ".ishader" or ".ishadergraph")
            .Select(static entry => TryLoadShader(entry))
            .Where(static shader => shader is not null)
            .Cast<ShaderAsset>()
            .ToArray();
        Dictionary<Guid, List<ShaderVariantKey>> variants = CollectMaterialVariants();
        foreach (ShaderAsset shader in shaders)
        {
            CompileAndInstall(shader, ShaderVariantKey.empty);
            if (!variants.TryGetValue(shader.identity.persistentId, out List<ShaderVariantKey>? requested))
            {
                continue;
            }

            foreach (ShaderVariantKey variant in requested.Where(static value => value != ShaderVariantKey.empty))
            {
                CompileAndInstall(shader, variant);
            }
        }

        RefreshTextures();
    }

    private void RefreshTextures()
    {
        foreach (AssetFileEntry entry in AssetManager.GetFileSystemEntries(includeDirectories: false)
                     .Where(static entry => entry.extension is ".png" or ".jpg" or ".jpeg" or ".tga" or ".hdr"))
        {
            try
            {
                if (!AssetManager.TryLoad(entry.relativePath, out TextureAsset? texture) || texture is null)
                {
                    continue;
                }

                Guid persistentId = texture.identity.persistentId;
                if (m_textures.TryGetValue(persistentId, out TextureResident? active)
                    && active.contentVersion == texture.contentVersion)
                {
                    continue;
                }

                string sourcePath = Path.GetFullPath(Path.Combine(
                    m_sourceRoot,
                    entry.relativePath.Replace('/', Path.DirectorySeparatorChar)));
                byte[] container = m_textureCompiler.CompileKtx(sourcePath, texture.colorSpace);
                PersistentTextureHandle candidate = m_device.CreateTexture(
                    RenderTextureContainer.Ktx,
                    container,
                    texture.colorSpace == TextureColorSpace.Srgb,
                    texture.name);
                m_artifacts.InstallTexture(texture, candidate);
                m_textures[persistentId] = new TextureResident(texture.contentVersion, candidate);
                if (active is not null)
                {
                    m_device.DestroyTexture(active.handle);
                }
            }
            catch (Exception exception)
            {
                PublishFailure("RENDER_TEXTURE_CANDIDATE_FAILED", entry.relativePath, exception.Message);
            }
        }
    }

    private Dictionary<Guid, List<ShaderVariantKey>> CollectMaterialVariants()
    {
        var result = new Dictionary<Guid, List<ShaderVariantKey>>();
        foreach (AssetFileEntry entry in AssetManager.GetFileSystemEntries(includeDirectories: false)
                     .Where(static entry => entry.extension == ".imaterial"))
        {
            try
            {
                if (!AssetManager.TryLoad(entry.relativePath, out MaterialAsset? material)
                    || material?.shader?.definition is not ShaderDefinition definition)
                {
                    continue;
                }

                ShaderVariantKey variant = CreateVariant(definition, material.keywords);
                Guid shaderId = material.shader.identity.persistentId;
                if (!result.TryGetValue(shaderId, out List<ShaderVariantKey>? variants))
                {
                    variants = [];
                    result.Add(shaderId, variants);
                }

                if (!variants.Contains(variant))
                {
                    variants.Add(variant);
                }
            }
            catch (Exception exception)
            {
                PublishFailure(
                    "RENDER_MATERIAL_VARIANT_DISCOVERY_FAILED",
                    entry.relativePath,
                    exception.Message);
            }
        }

        return result;
    }

    private void CompileAndInstall(ShaderAsset shader, ShaderVariantKey variant)
    {
        try
        {
            ShaderIRModule module;
            if (shader is ShaderGraphAsset graph)
            {
                ShaderGraphCompileResult graphResult = ShaderGraphCompiler.Compile(graph, m_nodes);
                if (!graphResult.succeeded || graphResult.module is null)
                {
                    PublishShaderDiagnostics(shader, graphResult.diagnostics);
                    return;
                }

                module = graphResult.module;
            }
            else
            {
                module = ShaderAssetRuntime.GetModule(shader);
            }

            ShaderCompilationResult compilation = m_compiler.CompileAsync(
                    module,
                    m_target,
                    variant,
                    m_sourceRoot)
                .AsTask()
                .GetAwaiter()
                .GetResult();
            PublishShaderDiagnostics(shader, compilation.diagnostics);
            if (compilation.succeeded && compilation.artifact is not null)
            {
                m_artifacts.InstallShader(shader, compilation.artifact);
                if (variant == ShaderVariantKey.empty)
                {
                    _ = m_artifacts.InstallTaggedOperations(compilation.artifact);
                }
            }
        }
        catch (Exception exception)
        {
            PublishFailure("RENDER_SHADER_CANDIDATE_FAILED", shader.sourcePath, exception.Message);
        }
    }

    private void PublishShaderDiagnostics(
        ShaderAsset shader,
        IReadOnlyList<ShaderDiagnostic> diagnostics)
    {
        foreach (ShaderDiagnostic diagnostic in diagnostics.Where(static value =>
                     value.severity != ShaderDiagnosticSeverity.Info))
        {
            m_diagnostics.Publish(new RenderDiagnostic(
                diagnostic.code,
                diagnostic.message,
                diagnostic.severity == ShaderDiagnosticSeverity.Error
                    ? RenderDiagnosticSeverity.Error
                    : RenderDiagnosticSeverity.Warning,
                diagnostic.location?.assetPath ?? shader.sourcePath));
        }
    }

    private void PublishFailure(string code, string sourcePath, string message)
        => m_diagnostics.Publish(new RenderDiagnostic(
            code,
            $"Rendering asset '{sourcePath}' kept its last-good result: {message}",
            RenderDiagnosticSeverity.Error,
            sourcePath));

    private void OnAssetsChanged(AssetChangeSet changes)
    {
        if (changes.changes.Any(static change => IsRenderingSource(change.relativePath)
                || IsRenderingSource(change.oldRelativePath)))
        {
            m_dirty = true;
        }
    }

    private void OnAssetReloaded(AssetObject asset)
    {
        if (asset is ShaderAsset or MaterialAsset or TextureAsset)
        {
            m_dirty = true;
        }
    }

    private static ShaderAsset? TryLoadShader(AssetFileEntry entry)
    {
        try
        {
            return AssetManager.TryLoad(entry.relativePath, out ShaderAsset? shader) ? shader : null;
        }
        catch
        {
            return null;
        }
    }

    private static ShaderVariantKey CreateVariant(
        ShaderDefinition definition,
        IReadOnlySet<string> enabledOptions)
    {
        var selections = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (ShaderKeywordDefinition keyword in definition.keywords)
        {
            string? option = keyword.options.FirstOrDefault(enabledOptions.Contains);
            if (option is not null)
            {
                selections.Add(keyword.id, option);
            }
        }

        return selections.Count == 0 ? ShaderVariantKey.empty : new ShaderVariantKey(selections);
    }

    private static bool IsRenderingSource(string path)
        => Path.GetExtension(path).ToLowerInvariant() is ".ishader"
            or ".ishadergraph"
            or ".imaterial"
            or ".sc"
            or ".png"
            or ".jpg"
            or ".jpeg"
            or ".tga"
            or ".hdr";

    private sealed record TextureResident(long contentVersion, PersistentTextureHandle handle);
}
