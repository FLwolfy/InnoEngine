using System;
using System.IO;
using Inno.Build.Toolchains.Bgfx.Tools;
using Inno.Extensibility.Modules;
using Inno.Extensibility.Types;
using Xunit;

namespace Inno.Rendering.Shaders.Tests;

[Collection("Shader registry fault injection")]
public sealed class ShaderSourceFrontendRegistryTests
{
    [Fact]
    public void SharedRegistryDiscoversIndependentLanguagesAndRetiresItsProviders()
    {
        _ = new BgfxShaderSourceFrontend();
        using var fixture = new Fixture();
        using (var registry = new ShaderSourceFrontendRegistry(fixture.types))
        {
            Assert.Contains("inno.shader-language.bgfx-sc", registry.languageIds);
            Assert.Contains("tests.registry.language", registry.languageIds);
            var request = new ShaderSourceRequest(new("value.ishadersource", "source"), "Value", new NoIncludes());
            Assert.True(registry.Analyze("tests.registry.language", request).succeeded);
            fixture.modules.Rebuild();
            Assert.True(registry.Analyze("tests.registry.language", request).succeeded);
            Assert.True(RegistryFrontendProbe.created > 1);
            Assert.Equal(RegistryFrontendProbe.created - 1, RegistryFrontendProbe.disposed);
        }
        Assert.Equal(RegistryFrontendProbe.created, RegistryFrontendProbe.disposed);
    }

    [Fact]
    public void DuplicateCandidateReleasesNewProvidersAndKeepsPreviousSnapshotUsable()
    {
        _ = new BgfxShaderSourceFrontend();
        using var fixture = new Fixture();
        using var registry = new ShaderSourceFrontendRegistry(fixture.types);
        Assert.Contains("tests.registry.language", registry.languageIds);
        RegistryFrontendProbe.conflict = true;
        try { Assert.ThrowsAny<ArgumentException>(fixture.modules.Rebuild); }
        finally { RegistryFrontendProbe.conflict = false; }
        Assert.Contains("tests.registry.language", registry.languageIds);
        Assert.Equal(1, RegistryFrontendProbe.created - RegistryFrontendProbe.disposed);
    }

    private sealed class NoIncludes : IShaderSourceResolver
    {
        public ShaderSourceFile ReadInclude(string includingFile, string include) => throw new FileNotFoundException(include);
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string m_root = Path.Combine(Path.GetTempPath(), "InnoShaderRegistry", Guid.NewGuid().ToString("N"));
        internal ModuleHost modules { get; }
        internal TypeCatalog types { get; }
        internal Fixture()
        {
            RegistryFrontendProbe.created = 0;
            RegistryFrontendProbe.disposed = 0;
            RegistryFrontendProbe.conflict = false;
            modules = new ModuleHost(new ModuleHostOptions { cacheDirectory = m_root });
            types = new TypeCatalog(modules);
        }
        public void Dispose()
        {
            types.Dispose();
            modules.Dispose();
            if (Directory.Exists(m_root)) Directory.Delete(m_root, true);
        }
    }
}

[ShaderSourceFrontendExtension]
internal sealed class RegistryFrontendProbe : IShaderSourceFrontend, IDisposable
{
    internal static int created;
    internal static int disposed;
    internal static bool conflict;
    private readonly string m_languageId;
    public RegistryFrontendProbe()
    {
        created++;
        m_languageId = conflict ? "inno.shader-language.bgfx-sc" : "tests.registry.language";
    }
    public string languageId => m_languageId;
    public ShaderSourceAnalysis Analyze(ShaderSourceRequest request)
        => new(new(request.entryPoint, ShaderSourceType.Atomic("float"), [], new(request.source.assetPath, 1, 1)), [], []);
    public void Dispose() => disposed++;
}
