using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Inno.Build.Toolchains.Bgfx.Tools;
using Inno.Rendering.Shaders;
using Xunit;

namespace Inno.Rendering.Shaders.Tests;

public sealed class ShaderSourceFrontendTests
{
    [Theory]
    [InlineData("vec4 Shade() { return gl_FragCoord; }")]
    [InlineData("vec4 Shade(vec4 position) { return mul(u_viewProj, position); }")]
    [InlineData("vec4 Shade() { return a_position; }")]
    [InlineData("vec4 Shade() { return v_color0; }")]
    public void SourceModulesCannotReadGeneratedStageBindingsImplicitly(string source)
    {
        ShaderSourceAnalysis result = Analyze(source);
        Assert.False(result.succeeded);
        Assert.Contains(result.diagnostics, diagnostic => diagnostic.message.Contains("explicit parameter", StringComparison.Ordinal));
    }

    [Fact]
    public void ExplicitParametersAndStructureMembersDoNotBecomeImplicitBindings()
    {
        ShaderSourceAnalysis result = Analyze("struct Data { vec4 a_position; }; vec4 Shade(Data value, mat4 u_viewProj) { return mul(u_viewProj, value.a_position); }");
        Assert.True(result.succeeded, Errors(result));
    }

    [Fact]
    public void SourceFunctionDerivesInputOutputAndInOutFromOneDeclaration()
    {
        ShaderSourceAnalysis result = Analyze("vec3 Shade(vec2 uv, out float alpha, inout vec4 color) { alpha = 1.0; return color.rgb; }");
        Assert.True(result.succeeded, Errors(result));
        Assert.Equal("float3", result.function!.returnType.id);
        Assert.Equal(["uv", "alpha", "color"], result.function.parameters.Select(static parameter => parameter.name));
        Assert.Equal([ShaderSourceParameterDirection.Input, ShaderSourceParameterDirection.Output, ShaderSourceParameterDirection.InputOutput],
            result.function.parameters.Select(static parameter => parameter.direction));
    }

    [Fact]
    public void StructuresArraysAliasesAndConstantLengthsRemainTyped()
    {
        ShaderSourceAnalysis result = Analyze("""
            #define BASE_COUNT 2
            const int COUNT = (BASE_COUNT << 1);
            struct Sample { vec2 uv; vec4 weights[COUNT]; };
            typedef vec3 Direction;
            Sample Shade(Direction normal, inout Sample samples[2][3]) { return samples[0][0]; }
            """);
        Assert.True(result.succeeded, Errors(result));
        ShaderSourceType aggregate = result.function!.returnType;
        Assert.Equal("float2", aggregate.fields[0].type.id);
        Assert.Equal(4, aggregate.fields[1].type.elementCount);
        Assert.Equal("float3", result.function.parameters[0].type.id);
        Assert.Equal(2, result.function.parameters[1].type.elementCount);
        Assert.Equal(3, result.function.parameters[1].type.elementType!.elementCount);
    }

    [Fact]
    public void CommentTextAndPrototypeAreNotMistakenForExports()
    {
        ShaderSourceAnalysis result = Analyze("""
            // float Shade(float incorrect) {}
            /* vec4 Shade(vec4 incorrect) {} */
            float Shade(float value);
            float Helper(float value) { return value * value; }
            float Shade(float value) { return Helper(value); }
            """);
        Assert.True(result.succeeded, Errors(result));
        Assert.Equal(5, result.function!.location.line);
        Assert.Single(result.function.parameters);
    }

    [Fact]
    public void MacroReplacementRescansAliasesWithFollowingFunctionArguments()
    {
        ShaderSourceAnalysis result = Analyze("""
            #define JOIN(a, b) a ## b
            #define ALIAS JOIN
            #define RETURN_TYPE vec ## 3
            RETURN_TYPE Shade(ALIAS(vec, 2) uv, JOIN(, float) value) { return vec3(uv, value); }
            """);
        Assert.True(result.succeeded, Errors(result));
        Assert.Equal("float3", result.function!.returnType.id);
        Assert.Equal("float2", result.function.parameters[0].type.id);
        Assert.Equal("float", result.function.parameters[1].type.id);
    }

    [Fact]
    public void MacroRescanningDoesNotDisableIndependentInvocations()
    {
        ShaderSourceAnalysis result = Analyze("""
            #define ID(x) x
            #define TYPE() ID(float)
            TYPE() Shade(ID(ID(float)) first, TYPE() second) { return first + second; }
            """);
        Assert.True(result.succeeded, Errors(result));
        Assert.All(result.function!.parameters, parameter => Assert.Equal("float", parameter.type.id));
    }

    [Fact]
    public void NamedNegativeConstantsKeepTheirArithmeticPrecedence()
    {
        ShaderSourceAnalysis result = Analyze("const int NEGATIVE = -2; float Shade(float values[NEGATIVE * -3]) {}");
        Assert.True(result.succeeded, Errors(result));
        Assert.Equal(6, result.function!.parameters[0].type.elementCount);
    }

    [Fact]
    public void PreprocessorHonorsIncludesAndFunctionMacros()
    {
        var includes = new Dictionary<string, string>
        {
            ["types.inc"] = "#ifndef TYPES_INCLUDED\n#define TYPES_INCLUDED\nstruct Data { vec3 normal; };\n#endif"
        };
        ShaderSourceAnalysis result = Analyze("""
            #include "types.inc"
            #include "types.inc"
            #define TYPE_NAME(prefix, suffix) prefix ## suffix
            #define PARAM(type, name) type name
            #if defined(USE_VECTOR) && (USE_VECTOR + 1) == 2
            TYPE_NAME(vec, 3) Shade(PARAM(Data, data)) { return data.normal; }
            #else
            float Shade(float value) { return value; }
            #endif
            """, includes, new Dictionary<string, string> { ["USE_VECTOR"] = "1" });
        Assert.True(result.succeeded, Errors(result));
        Assert.Equal("float3", result.function!.returnType.id);
        Assert.Equal("struct:Data", result.function.parameters[0].type.id);
        Assert.Single(result.dependencies);
    }

    [Fact]
    public void PreprocessorHandlesElseIfAndShortCircuitWithoutReadingInactiveIncludes()
    {
        ShaderSourceAnalysis result = Analyze("""
            #if 0 && (1 / 0)
            #include "does-not-exist.inc"
            #elif (1 ? 4 : (1 / 0)) == 4
            vec2 Shade(vec2 value) { return value; }
            #else
            #error Wrong branch
            #endif
            """);
        Assert.True(result.succeeded, Errors(result));
        Assert.Empty(result.dependencies);
    }

    [Theory]
    [InlineData("void main() {}", "main")]
    [InlineData("float Shade(float v) {}\nvec2 Shade(vec2 v) {}", "Shade")]
    [InlineData("float Shade(float a, float a) {}", "Shade")]
    [InlineData("Unknown Shade(float value) {}", "Shade")]
    [InlineData("float Shade(float values[]) {}", "Shade")]
    [InlineData("float Shade(float values[-1]) {}", "Shade")]
    [InlineData("float Shade(float values[MISSING]) {}", "Shade")]
    [InlineData("float Shade(float value) {", "Shade")]
    [InlineData("/* not closed", "Shade")]
    [InlineData("#if 1\nfloat Shade(float v) {}", "Shade")]
    [InlineData("#else\nfloat Shade(float v) {}", "Shade")]
    [InlineData("uniform vec4 global;\nfloat Shade(float v) {}", "Shade")]
    [InlineData("#define F(a,) a\nfloat Shade(float value) {}", "Shade")]
    public void InvalidSourceReturnsLocatedDiagnosticInsteadOfGuessingPorts(string source, string entry)
    {
        ShaderSourceAnalysis result = new BgfxShaderSourceFrontend().Analyze(
            new(new("bad.ishadersource", source), entry, new Includes([])));
        Assert.False(result.succeeded);
        Assert.NotEmpty(result.diagnostics);
        Assert.Equal("bad.ishadersource", result.diagnostics[0].location.assetPath);
        Assert.True(result.diagnostics[0].location.line > 0);
    }

    [Fact]
    public void IncludedFailureReportsOriginalFileAndKeepsDependency()
    {
        ShaderSourceAnalysis result = Analyze("#include \"bad.inc\"\nfloat Shade(float v) {}",
            new Dictionary<string, string> { ["bad.inc"] = "struct Broken { Undefined x; };" });
        Assert.False(result.succeeded);
        Assert.Equal("bad.inc", result.diagnostics[0].location.assetPath);
        Assert.Equal("bad.inc", Assert.Single(result.dependencies));
    }

    [Fact]
    public void PragmaOnceBreaksGuardedIncludeCycles()
    {
        ShaderSourceAnalysis result = Analyze("#include \"a.inc\"\nfloat Shade(Data value) { return value.x; }",
            new Dictionary<string, string> { ["a.inc"] = "#pragma once\n#include \"a.inc\"\nstruct Data { float x; };" });
        Assert.True(result.succeeded, Errors(result));
    }

    [Fact]
    public void AlternativeImplementationsMustAgreeOnAllPortSemantics()
    {
        ShaderSourceFunction original = Analyze("vec3 Shade(vec2 uv, out float alpha) {}").function!;
        ShaderSourceFunction equivalent = Analyze("float3 Shade(float2 uv, out float alpha) {}").function!;
        ShaderSourceFunction renamed = Analyze("vec3 Shade(vec2 different, out float alpha) {}").function!;
        ShaderSourceFunction redirected = Analyze("vec3 Shade(vec2 uv, inout float alpha) {}").function!;
        Assert.True(original.HasSameInterface(equivalent));
        Assert.False(original.HasSameInterface(renamed));
        Assert.False(original.HasSameInterface(redirected));
    }

    [Fact]
    public void LanguageCatalogAcceptsAnIndependentFrontendAndRejectsConflicts()
    {
        var catalog = new ShaderSourceFrontendCatalog([new BgfxShaderSourceFrontend(), new IndependentFrontend()]);
        Assert.Equal(2, catalog.languageIds.Count);
        var request = new ShaderSourceRequest(new("custom.ishadersource", "custom"), "custom", new Includes([]));
        Assert.True(catalog.Analyze("tests.language", request).succeeded);
        Assert.Throws<NotSupportedException>(() => catalog.Analyze("missing.language", request));
        Assert.Throws<ArgumentException>(() => new ShaderSourceFrontendCatalog([new IndependentFrontend(), new IndependentFrontend()]));
    }

    private static ShaderSourceAnalysis Analyze(string source, Dictionary<string, string>? includes = null,
        IReadOnlyDictionary<string, string>? defines = null)
        => new BgfxShaderSourceFrontend().Analyze(new(new("module.ishadersource", source), "Shade", new Includes(includes ?? []), defines));

    private static string Errors(ShaderSourceAnalysis result) => string.Join("; ", result.diagnostics.Select(static item => item.message));

    private sealed class Includes(Dictionary<string, string> sources) : IShaderSourceResolver
    {
        public ShaderSourceFile ReadInclude(string includingFile, string include)
            => sources.TryGetValue(include, out string? source) ? new(include, source) : throw new FileNotFoundException(include);
    }

    private sealed class IndependentFrontend : IShaderSourceFrontend
    {
        public string languageId => "tests.language";
        public ShaderSourceAnalysis Analyze(ShaderSourceRequest request)
            => new(new(request.entryPoint, ShaderSourceType.Atomic("float"), [], new(request.source.assetPath, 1, 1)), [], []);
    }
}
