using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Inno.Native.Bgfx.Tools;

namespace Inno.Rendering.Assets;

internal sealed class BgfxShadercToolchain : IShaderCompilerToolchain
{
    public async ValueTask<ShaderToolResult> CompileAsync(
        ShaderToolRequest request,
        CancellationToken cancellationToken)
    {
        string temporaryDirectory = Path.Combine(
            Path.GetTempPath(),
            "InnoEngine",
            "Shaderc",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporaryDirectory);
        try
        {
            string sourcePath = Path.Combine(temporaryDirectory, "stage.sc");
            string outputPath = Path.Combine(temporaryDirectory, "stage.bin");
            string bgfxShaderInclude = ResolveBgfxShaderInclude();
            File.Copy(
                bgfxShaderInclude,
                Path.Combine(temporaryDirectory, "bgfx_shader.sh"),
                overwrite: true);
            File.Copy(
                Path.Combine(
                    Path.GetDirectoryName(bgfxShaderInclude)
                        ?? throw new InvalidOperationException("BGFX shader include has no parent directory."),
                    "bgfx_compute.sh"),
                Path.Combine(temporaryDirectory, "bgfx_compute.sh"),
                overwrite: true);
            await File.WriteAllTextAsync(
                sourcePath,
                request.stage.source,
                cancellationToken).ConfigureAwait(false);

            List<string> arguments =
            [
                "-f",
                sourcePath,
                "-o",
                outputPath,
                "--type",
                ToShadercStage(request.stage.stage),
                "--platform",
                request.target.profile.shadercPlatform,
                "--profile",
                request.target.profile.GetStageProfile(request.stage.stage),
                "-i",
                request.sourceRoot
            ];
            if (request.stage.stage != ShaderStage.Compute
                && request.stagePass.generatedVaryingSource is not null)
            {
                string varyingPath = Path.Combine(temporaryDirectory, "varying.def.sc");
                await File.WriteAllTextAsync(
                    varyingPath,
                    request.stagePass.generatedVaryingSource,
                    cancellationToken).ConfigureAwait(false);
                arguments.Add("--varyingdef");
                arguments.Add(varyingPath);
            }
            else if (request.pass.varyingSource is not null && request.stage.stage != ShaderStage.Compute)
            {
                arguments.Add("--varyingdef");
                arguments.Add(ResolveProjectPath(request.sourceRoot, request.pass.varyingSource));
            }

            if (request.variant.options.Count != 0)
            {
                arguments.Add("--define");
                arguments.Add(string.Join(
                    ";",
                    request.variant.options.Select(static value => $"{value.Key}_{value.Value}=1")));
            }

            if (request.target.optimize)
            {
                arguments.Add("-O");
                arguments.Add("3");
            }

            if (request.target.debugInformation)
            {
                arguments.Add("--debug");
            }

            ToolRunResult result = await ToolRunner.RunAsync(
                BgfxTool.Shaderc,
                arguments,
                temporaryDirectory,
                cancellationToken).ConfigureAwait(false);
            byte[]? bytes = result.succeeded && File.Exists(outputPath)
                ? await File.ReadAllBytesAsync(outputPath, cancellationToken).ConfigureAwait(false)
                : null;
            return new ShaderToolResult(
                bytes,
                result.exitCode,
                result.standardOutput,
                result.standardError);
        }
        finally
        {
            if (Directory.Exists(temporaryDirectory))
            {
                Directory.Delete(temporaryDirectory, recursive: true);
            }
        }
    }

    private static string ToShadercStage(ShaderStage stage)
        => stage switch
        {
            ShaderStage.Vertex => "vertex",
            ShaderStage.Fragment => "fragment",
            ShaderStage.Compute => "compute",
            _ => throw new ArgumentOutOfRangeException(nameof(stage), stage, "A single shader stage is required.")
        };

    private static string ResolveProjectPath(string root, string relativePath)
    {
        string fullPath = Path.GetFullPath(Path.Combine(
            root,
            relativePath.Replace('/', Path.DirectorySeparatorChar)));
        string prefix = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Shader source '{relativePath}' resolves outside the project Assets directory.");
        }

        return fullPath;
    }

    private static string ResolveBgfxShaderInclude()
    {
        string[] starts = [AppContext.BaseDirectory, Directory.GetCurrentDirectory()];
        foreach (string start in starts)
        {
            for (DirectoryInfo? directory = new(start); directory is not null; directory = directory.Parent)
            {
                string candidate = Path.Combine(
                    directory.FullName,
                    "extern",
                    "bgfx",
                    "src",
                    "bgfx_shader.sh");
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        throw new FileNotFoundException(
            "Unable to resolve the BGFX shader include 'bgfx_shader.sh' from the application or repository root.");
    }
}
