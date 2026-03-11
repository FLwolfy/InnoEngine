using System;
using System.IO;
using System.Text;

using Inno.Assets.AssetType;
using Inno.Platform.Graphics;

using Veldrid.SPIRV;

namespace Inno.Assets.Loader;

internal class ShaderAssetLoader : InnoAssetLoader<ShaderAsset>
{
    public override string[] validExtensions => [".vert", ".frag"];
    
    protected override byte[] OnLoadBinaries(string assetName, byte[] rawBytes, out ShaderAsset asset)
    {
        string glsl = Encoding.UTF8.GetString(rawBytes);
        var stage = DetectShaderStage(assetName);

        var compileResult = SpirvCompilation.CompileGlslToSpirv(
            glsl,
            assetName,
            (Veldrid.ShaderStages) stage,
            new GlslCompileOptions(true)
        );

        asset = new ShaderAsset(stage, glsl);

        return compileResult.SpirvBytes;
    }

    protected override byte[] OnSaveSource(string assetName, in ShaderAsset asset)
    {
        if (asset.glslCode == null)
        {
            throw new InvalidOperationException("ShaderAsset.glsl is null.");
        }

        // Optional: normalize line endings to keep diffs stable across platforms.
        // If you want exact preservation, remove this.
        string text = asset.glslCode.Replace("\r\n", "\n");

        // Optional: make sure stage matches extension (avoid saving wrong file type)
        var expectedStage = DetectShaderStage(assetName);
        if (asset.shaderStage != expectedStage)
            throw new InvalidOperationException($"Shader stage mismatch. Asset stage={asset.shaderStage}, file expects {expectedStage}.");

        // Write UTF8 without BOM
        return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(text);
    }


    private static ShaderStage DetectShaderStage(string fileName)
    {
        string ext = Path.GetExtension(fileName).ToLowerInvariant();
        return ext switch
        {
            ".vert" => ShaderStage.Vertex,
            ".frag" => ShaderStage.Fragment,
            _ => throw new Exception("Unknown shader stage: " + ext)
        };
    }

}