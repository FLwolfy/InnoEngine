using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Inno.Native.Bgfx.Tools;
using Inno.Rendering.Assets;

namespace Inno.Rendering.Bgfx;

/// <summary>Converts artist texture sources into validated KTX containers with BGFX texturec.</summary>
public sealed class BgfxTextureTargetCompiler : ITextureTargetCompiler
{
    /// <inheritdoc />
    public async ValueTask<byte[]> CompileKtxAsync(
        string sourcePath,
        TextureColorSpace colorSpace,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        string temporaryDirectory = Path.Combine(
            Path.GetTempPath(),
            "InnoEngine",
            "Texturec",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporaryDirectory);
        try
        {
            string outputPath = Path.Combine(temporaryDirectory, "texture.ktx");
            var arguments = new List<string>
            {
                "-f", Path.GetFullPath(sourcePath),
                "-o", outputPath,
                "-t", "RGBA8",
                "--mips",
                "--validate"
            };
            if (colorSpace == TextureColorSpace.Linear)
                arguments.Add("--linear");

            ToolRunResult result = await ToolRunner.RunAsync(
                BgfxTool.Texturec,
                arguments,
                temporaryDirectory,
                cancellationToken).ConfigureAwait(false);
            if (!result.succeeded || !File.Exists(outputPath))
            {
                string diagnostics = string.Join(
                    Environment.NewLine,
                    new[] { result.standardOutput, result.standardError });
                throw new InvalidOperationException(
                    $"texturec failed for '{sourcePath}' with exit code {result.exitCode}: {diagnostics}");
            }

            byte[] artifact = await File.ReadAllBytesAsync(outputPath, cancellationToken)
                .ConfigureAwait(false);
            if (artifact.Length == 0)
                throw new InvalidOperationException($"texturec produced an empty artifact for '{sourcePath}'.");
            return artifact;
        }
        finally
        {
            if (Directory.Exists(temporaryDirectory))
                Directory.Delete(temporaryDirectory, recursive: true);
        }
    }
}
