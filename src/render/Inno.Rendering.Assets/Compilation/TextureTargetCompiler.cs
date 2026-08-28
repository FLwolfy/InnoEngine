using System;
using System.Collections.Generic;
using System.IO;
using Inno.Native.Bgfx.Tools;

namespace Inno.Rendering.Assets;

/// <summary>
/// Converts supported artist texture sources into a validated portable runtime container.
/// </summary>
public sealed class TextureTargetCompiler
{
    /// <summary>
    /// Compiles one source texture into an uncompressed KTX artifact with a complete mip chain.
    /// </summary>
    /// <param name="sourcePath">Absolute path to a supported source image.</param>
    /// <param name="colorSpace">Sampling color-space contract.</param>
    /// <returns>Complete KTX bytes suitable for a backend-neutral texture container upload.</returns>
    /// <exception cref="InvalidOperationException">Thrown when texturec rejects the source.</exception>
    public byte[] CompileKtx(string sourcePath, TextureColorSpace colorSpace)
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
            {
                arguments.Add("--linear");
            }

            ToolRunResult result = ToolRunner.Run(BgfxTool.Texturec, arguments, temporaryDirectory);
            if (!result.succeeded || !File.Exists(outputPath))
            {
                string diagnostics = string.Join(
                    Environment.NewLine,
                    new[] { result.standardOutput, result.standardError });
                throw new InvalidOperationException(
                    $"texturec failed for '{sourcePath}' with exit code {result.exitCode}: {diagnostics}");
            }

            byte[] artifact = File.ReadAllBytes(outputPath);
            if (artifact.Length == 0)
            {
                throw new InvalidOperationException($"texturec produced an empty artifact for '{sourcePath}'.");
            }

            return artifact;
        }
        finally
        {
            if (Directory.Exists(temporaryDirectory))
            {
                Directory.Delete(temporaryDirectory, recursive: true);
            }
        }
    }
}
