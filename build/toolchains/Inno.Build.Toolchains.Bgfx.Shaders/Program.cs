using System;
using System.IO;
using System.Linq;
using Inno.Assets;
using Inno.Assets.Pipeline;
using Inno.Build.Toolchains.Bgfx.Tools;
using Inno.Core.Diagnostics;
using Inno.Core.Identity;
using Inno.Core.IO;
using Inno.Core.Logging;
using Inno.Core.Serialization;
using Inno.Extensibility.Modules;
using Inno.Extensibility.Types;
using Inno.Rendering;
using Inno.Rendering.Assets;

namespace Inno.Build.Toolchains.Bgfx.Shaders;

internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length != 5 || !Enum.TryParse(args[2], true, out BgfxShaderTargetPlatform platform)
            || !Enum.TryParse(args[3], true, out GraphicsApi backend))
        {
            Console.Error.WriteLine("Usage: Inno.Build.Toolchains.Bgfx.Shaders <asset-root> <shader-path> <MacOSArm64|WindowsX64> <Metal|Direct3D11|Direct3D12|Vulkan|OpenGL> <output-file>");
            return 2;
        }
        string scratch = Path.Combine(Path.GetTempPath(), "InnoShaderBuild", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratch);
        try
        {
            var identities = new IdentityAllocator();
            using IDisposable identityScope = identities.EnterScope();
            var diagnostics = new DiagnosticHub();
            using IDisposable diagnosticScope = diagnostics.EnterScope();
            diagnostics.RegisterSink(new DiagnosticOutput());
            using var logs = new LogRouter();
            using var modules = new ModuleHost(new() { cacheDirectory = Path.Combine(scratch, "Modules") });
            using var types = new TypeCatalog(modules);
            using var serialization = new SerializationRegistry(types);
            using var assets = new AssetPipeline(modules, types, serialization, identities, diagnostics, logs,
                new() { assetRoot = Path.GetFullPath(args[0]), libraryRoot = Path.Combine(scratch, "Library") });
            AssetPath path = AssetPath.Project(args[1]);
            // A fresh source import must succeed. An earlier build artifact never masks current source errors.
            if (!assets.Import(path)) throw new InvalidDataException($"Shader source '{path}' failed to import.");
            ShaderAsset shader = assets.Load<ShaderAsset>(path);
            RenderTextureFormat[] formats = Enum.GetValues<RenderTextureFormat>();
            var capabilities = new GraphicsCapabilities(backend,
                Enum.GetValues<GraphicsCapability>().Aggregate(GraphicsCapability.None, static (all, feature) => all | feature),
                new(256, 8, 16384, 16), formats, formats, formats, formats, false, false, formats, formats, formats);
            var compiler = new ShaderCompiler(new BgfxShadercToolchain(platform));
            ShaderCompileTarget target = compiler.CreateTarget(capabilities);
            ShaderCompilationResult result = compiler.CompileGraphAsync(shader, target, RenderShaderVariant.empty,
                types, serialization, AssetSerializationContext.Create(assets), assets).AsTask().GetAwaiter().GetResult();
            foreach (ShaderDiagnostic diagnostic in result.diagnostics)
                Console.Error.WriteLine($"{diagnostic.code}: {diagnostic.message}");
            if (!result.succeeded || result.artifact is null) return 1;
            AtomicFile.WriteAllBytes(Path.GetFullPath(args[4]), RenderShaderArtifactCodec.Encode(result.artifact.CreateRuntimeArtifact()));
            Console.WriteLine($"Compiled {path}: {result.artifact.passes.Count} passes, {target.key}");
            return 0;
        }
        catch (Exception failure)
        {
            Console.Error.WriteLine(failure);
            return 1;
        }
        finally { Directory.Delete(scratch, recursive: true); }
    }

    private sealed class DiagnosticOutput : IDiagnosticSink
    {
        public void Replace(DiagnosticReport report)
        {
            foreach (Diagnostic diagnostic in report.diagnostics)
                if (diagnostic.severity == DiagnosticSeverity.Error)
                    Console.Error.WriteLine($"{diagnostic.code}: {diagnostic.message}");
        }
        public void Clear(DiagnosticSource source) { }
    }
}
