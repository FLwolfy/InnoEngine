using System;
using System.Collections.Generic;
using Inno.Rendering.Core;
using Xunit;

namespace Inno.Rendering.Assets.Tests;

public sealed class HandwrittenShaderParserTests
{
    [Fact]
    public void Parse_AllowsCommentsAndTrailingCommasAndTracksIncludes()
    {
        Dictionary<string, string> sources = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Shaders/basic-vs.sc"] = "#include \"common.sc\"\nvoid main() {}",
            ["Shaders/basic-fs.sc"] = "void main() {}",
            ["Shaders/common.sc"] = "vec4 helper() { return vec4(1.0); }"
        };
        const string source = """
        {
          // Artist-readable comments are allowed.
          "name": "Tests/Basic",
          "properties": [
            { "id": "baseColor", "displayName": "Base Color", "type": "Color", "stages": ["Fragment"], "default": [1, 1, 1, 1], },
          ],
          "keywords": [
            { "id": "SURFACE", "options": ["Opaque", "Transparent"] },
          ],
          "passes": [
            {
              "name": "Forward",
              "tag": "ForwardLit",
              "vertex": "Shaders/basic-vs.sc",
              "fragment": "Shaders/basic-fs.sc",
              "varying": "Shaders/varying.def.sc",
              "renderState": { "cull": "Back", "depthWrite": true, },
              "tags": { "Queue": "Geometry" },
            },
          ],
        }
        """;

        HandwrittenShaderParseResult result = HandwrittenShaderParser.Parse(
            "Shaders/basic.ishader",
            source,
            path => sources[path]);

        Assert.Equal("Tests/Basic", result.module.definition.name);
        Assert.Equal(2, result.module.passes[0].stages.Count);
        Assert.Contains("Shaders/common.sc", result.dependencies);
        Assert.Contains("Shaders/varying.def.sc", result.dependencies);
        Assert.Equal(ShaderCullMode.Back, result.module.definition.passes[0].renderState.cull);
        Assert.Equal("Geometry", result.module.definition.passes[0].tags["Queue"]);
    }

    [Fact]
    public void Parse_RejectsUnknownSchemaProperties()
    {
        const string source = """
        {
          "name": "Tests/Invalid",
          "passes": [],
          "schemaVersion": 1
        }
        """;

        RenderingAssetFormatException exception = Assert.Throws<RenderingAssetFormatException>(() =>
            HandwrittenShaderParser.Parse("invalid.ishader", source, static _ => string.Empty));

        Assert.Equal("$.schemaVersion", exception.path);
    }

    [Fact]
    public void Parse_SystemInclude_IsLeftForShadercAndNotTrackedAsProjectDependency()
    {
        const string source = """
        {
          "name": "Tests/System Include",
          "passes": [
            {
              "name": "Forward",
              "tag": "ForwardLit",
              "vertex": "Shaders/test-vs.sc",
              "fragment": "Shaders/test-fs.sc"
            }
          ]
        }
        """;
        Dictionary<string, string> sources = new(StringComparer.Ordinal)
        {
            ["Shaders/test-vs.sc"] = "#include <bgfx_shader.sh>\nvoid main() {}",
            ["Shaders/test-fs.sc"] = "#include <bgfx_shader.sh>\nvoid main() {}"
        };

        HandwrittenShaderParseResult result = HandwrittenShaderParser.Parse(
            "Shaders/test.ishader",
            source,
            path => sources[path]);

        Assert.DoesNotContain(result.dependencies, static path => path.EndsWith("bgfx_shader.sh"));
        Assert.Contains("#include <bgfx_shader.sh>", result.module.passes[0].stages[0].source);
    }

    [Fact]
    public void Parse_RejectsIncludeCycles()
    {
        Dictionary<string, string> sources = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Shaders/a.sc"] = "#include \"b.sc\"",
            ["Shaders/b.sc"] = "#include \"a.sc\"",
            ["Shaders/f.sc"] = "void main() {}"
        };
        const string source = """
        {
          "name": "Tests/Cycle",
          "passes": [
            { "name": "Forward", "tag": "ForwardLit", "vertex": "Shaders/a.sc", "fragment": "Shaders/f.sc" }
          ]
        }
        """;

        Assert.Throws<RenderingAssetFormatException>(() =>
            HandwrittenShaderParser.Parse("Shaders/cycle.ishader", source, path => sources[path]));
    }

    [Fact]
    public void ArtifactCodec_PreservesSharedIrContract()
    {
        ShaderIRModule module = CreateModule("void main() {}", ShaderIRSourceKind.Handwritten);

        ShaderIRModule restored = ShaderIRArtifactCodec.Decode(ShaderIRArtifactCodec.Encode(module));

        Assert.Equal(module.definition.name, restored.definition.name);
        Assert.Equal(module.definition.passes[0].renderState.depthCompare,
            restored.definition.passes[0].renderState.depthCompare);
        Assert.Equal(module.passes[0].stages[0].source, restored.passes[0].stages[0].source);
    }

    internal static ShaderIRModule CreateModule(string source, ShaderIRSourceKind sourceKind)
    {
        var pass = new ShaderPassDefinition(
            "Forward",
            BuiltinShaderPassTags.ForwardLit,
            "Shaders/v.sc",
            "Shaders/f.sc",
            null,
            "Shaders/varying.def.sc");
        var definition = new ShaderDefinition(
            "Tests/Compiler",
            [new ShaderPropertyDefinition(
                new ShaderPropertyId("baseColor"),
                "Base Color",
                ShaderPropertyType.Color,
                ShaderStage.Fragment,
                "[1,1,1,1]")],
            [new ShaderKeywordDefinition("SURFACE", ["Opaque", "Transparent"])],
            [pass]);
        return new ShaderIRModule(
            definition,
            [new ShaderIRPass(
                pass,
                [
                    new ShaderIRStageModule(
                        ShaderStage.Vertex,
                        "main",
                        source,
                        sourceKind,
                        new ShaderSourceLocation("Shaders/v.sc", "Forward", ShaderStage.Vertex),
                        new Dictionary<int, string> { [2] = "node-v" }),
                    new ShaderIRStageModule(
                        ShaderStage.Fragment,
                        "main",
                        "void main() {}",
                        sourceKind,
                        new ShaderSourceLocation("Shaders/f.sc", "Forward", ShaderStage.Fragment))
                ])]);
    }
}
