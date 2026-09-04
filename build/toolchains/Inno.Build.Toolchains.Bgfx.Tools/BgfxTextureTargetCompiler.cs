using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Inno.Native.Bgfx.Tools;
using Inno.Rendering;
using Inno.Rendering.Assets;

namespace Inno.Build.Toolchains.Bgfx.Tools;

/// <summary>
/// Converts artist texture sources into validated KTX containers with BGFX texturec.
/// </summary>
public sealed class BgfxTextureTargetCompiler : ITextureTargetCompiler
{
    /// <summary>
    /// Compiles the supplied source into a validated runtime artifact.
    /// </summary>
    /// <param name="sourcePath">
    /// The normalized file-system path used by this operation.
    /// </param>
    /// <param name="colorSpace">
    /// The color transfer convention preserved in the compiled texture artifact.
    /// </param>
    /// <param name="cancellationToken">
    /// The token that cancels the operation before it commits.
    /// </param>
    /// <returns>
    /// An asynchronous operation that completes after all requested work has finished.
    /// </returns>
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
