using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Inno.Build.Toolchains.Bgfx.Tools;
using Inno.Core.Execution;
using Xunit;

namespace Inno.Rendering.Shaders.Tests;

public sealed class ShaderSourceModuleTests
{
    private const string C_SC = "inno.shader-language.bgfx-sc";

    [Fact]
    public void IndependentLanguagesShareOneInterfaceWithoutTranslatingSource()
    {
        var alternate = new AlternateFrontend();
        var catalog = new ShaderSourceFrontendCatalog([new BgfxShaderSourceFrontend(), alternate]);
        ShaderSourceModuleAnalysis result = catalog.AnalyzeModule([
            Request("adapter.first", "float Shade(float value) { return value; }"),
            new("adapter.second", alternate.languageId, new(new("Other.ishadersource", "native function implementation"), "ShadeOther", new Includes()))
        ]);
        Assert.True(result.succeeded, Errors(result));
        Assert.Equal("value", Assert.Single(result.function!.parameters).name);
        Assert.Equal("ShadeOther", result.implementations[1].entryPoint);
        Assert.Equal("native function implementation", result.implementations[1].sources[0].text);
        Assert.Equal(1, alternate.calls);
    }

    [Theory]
    [InlineData("float Shade(float renamed) { return renamed; }")]
    [InlineData("vec2 Shade(float value) { return vec2(value); }")]
    [InlineData("float Shade(vec2 value) { return value.x; }")]
    [InlineData("float Shade(inout float value) { return value; }")]
    public void InterfaceChangesInvalidateWholeModuleWithoutSelectingAnArbitraryImplementation(string source)
    {
        var catalog = new ShaderSourceFrontendCatalog([new BgfxShaderSourceFrontend()]);
        ShaderSourceModuleAnalysis result = catalog.AnalyzeModule([
            Request("first", "float Shade(float value) { return value; }"), Request("second", source)
        ]);
        Assert.False(result.succeeded);
        Assert.Null(result.function);
        Assert.Equal(2, result.implementations.Count);
        Assert.All(result.implementations, value => Assert.True(value.analysis.succeeded));
        Assert.Contains(result.diagnostics, value => value.code == "SHADER_SOURCE_IMPLEMENTATION_INTERFACE" && value.location.line == 1);
    }

    [Fact]
    public void ConditionalVariantsMustExposeIdenticalPorts()
    {
        const string source = """
            #if COMPACT
            float Shade(float value) { return value; }
            #else
            vec2 Shade(vec2 value) { return value; }
            #endif
            """;
        var catalog = new ShaderSourceFrontendCatalog([new BgfxShaderSourceFrontend()]);
        ShaderSourceModuleAnalysis result = catalog.AnalyzeModule([
            Request("compact", source, defines: new Dictionary<string, string> { ["COMPACT"] = "1" }),
            Request("full", source, defines: new Dictionary<string, string> { ["COMPACT"] = "0" })
        ]);
        Assert.False(result.succeeded);
        Assert.Contains(result.diagnostics, value => value.code == "SHADER_SOURCE_IMPLEMENTATION_INTERFACE");
    }

    [Fact]
    public void SourcesAndDefinesAreFrozenAndIncludedInTheContentHash()
    {
        var includes = new Includes();
        includes.values["data.inc"] = "#pragma once\nconst float Scale = 1.0;";
        var defines = new Dictionary<string, string> { ["FIRST"] = "1", ["SECOND"] = "2" };
        const string source = "#include \"data.inc\"\nfloat Shade(float value) { return value * Scale; }";
        var catalog = new ShaderSourceFrontendCatalog([new BgfxShaderSourceFrontend()]);
        ShaderSourceImplementationRequest request = Request("first", source, includes, defines);
        defines["FIRST"] = "42";
        ShaderSourceImplementationAnalysis first = Assert.Single(catalog.AnalyzeModule([request]).implementations);
        Assert.True(first.analysis.succeeded);
        Assert.Equal("1", first.defines["FIRST"]);
        Assert.Equal(2, first.sources.Count);
        Assert.Equal("data.inc", Assert.Single(first.analysis.dependencies));

        ShaderSourceImplementationAnalysis reordered = Assert.Single(catalog.AnalyzeModule([Request("different-owner-key", source, includes,
            new Dictionary<string, string> { ["SECOND"] = "2", ["FIRST"] = "1" })]).implementations);
        Assert.Equal(first.contentHash, reordered.contentHash);
        includes.values["data.inc"] = "#pragma once\nconst float Scale = 2.0;";
        ShaderSourceImplementationAnalysis changed = Assert.Single(catalog.AnalyzeModule([request]).implementations);
        Assert.NotEqual(first.contentHash, changed.contentHash);
        Assert.Contains(first.sources, value => value.text.EndsWith("Scale = 1.0;", StringComparison.Ordinal));
    }

    [Fact]
    public void MissingLanguageRetainsSourceAndRecoversWhenItsProviderReturns()
    {
        var implementation = new ShaderSourceImplementationRequest("alternate", "tests.alternate", new(new("Other.ishadersource", "native source"), "Other", new Includes()));
        ShaderSourceModuleAnalysis missing = new ShaderSourceFrontendCatalog([]).AnalyzeModule([implementation]);
        Assert.False(missing.succeeded);
        Assert.Null(missing.function);
        Assert.Equal("native source", Assert.Single(missing.implementations).sources[0].text);
        Assert.Equal("SHADER_SOURCE_LANGUAGE_MISSING", Assert.Single(missing.diagnostics).code);
        Assert.True(new ShaderSourceFrontendCatalog([new AlternateFrontend()]).AnalyzeModule([implementation]).succeeded);
    }

    [Fact]
    public void SuccessfulFrontendsCannotClaimDependenciesOutsideTheirFrozenSourceSet()
    {
        var alternate = new AlternateFrontend { inventedDependency = true };
        var request = new ShaderSourceImplementationRequest("alternate", alternate.languageId,
            new(new("Other.ishadersource", "native source"), "Other", new Includes()));
        ShaderSourceModuleAnalysis result = new ShaderSourceFrontendCatalog([alternate]).AnalyzeModule([request]);
        Assert.False(result.succeeded);
        Assert.Contains(result.diagnostics, value => value.code == "SHADER_SOURCE_DEPENDENCY_UNCAPTURED");
    }

    [Fact]
    public void DuplicateOrEmptyImplementationSetsAreRejectedBeforeProvidersRun()
    {
        var catalog = new ShaderSourceFrontendCatalog([new BgfxShaderSourceFrontend()]);
        var request = Request("same", "float Shade(float value) { return value; }");
        Assert.Throws<ArgumentException>(() => catalog.AnalyzeModule([]));
        Assert.Throws<ArgumentException>(() => catalog.AnalyzeModule([request, request]));
    }

    [Fact]
    public void IncludeReadsCannotMixDifferentContentsForTheSameSourcePath()
    {
        var request = Request("first", "#include \"data.inc\"\n#include \"data.inc\"\nfloat Shade(float value) { return value; }",
            new ChangingIncludes());
        ShaderSourceModuleAnalysis result = new ShaderSourceFrontendCatalog([new BgfxShaderSourceFrontend()]).AnalyzeModule([request]);
        Assert.False(result.succeeded);
        Assert.Contains(result.diagnostics, value => value.message.Contains("changed during module analysis", StringComparison.Ordinal));
    }

    [Fact]
    public void SourceErrorsCannotSwallowTheSharedRetirementBarrier()
    {
        var failure = new IOException("Source candidate is retiring.", new RetirementPendingException("Pending source owner."));
        var request = Request("first", "#include \"data.inc\"\nfloat Shade(float value) { return value; }", new FailingIncludes(failure));
        IOException actual = Assert.Throws<IOException>(() => new ShaderSourceFrontendCatalog([new BgfxShaderSourceFrontend()]).AnalyzeModule([request]));
        Assert.Same(failure, actual);
    }

    [Fact]
    public void AStageMainPrototypeIsNotAnAllowedFunctionModule()
    {
        ShaderSourceModuleAnalysis result = new ShaderSourceFrontendCatalog([new BgfxShaderSourceFrontend()]).AnalyzeModule([
            Request("first", "void main(); float Shade(float value) { return value; }")]);
        Assert.False(result.succeeded);
        Assert.Contains(result.diagnostics, value => value.message.Contains("cannot declare main()", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("float Shade(void value) { return 1.0; }")]
    [InlineData("float Shade(void values[2]) { return 1.0; }")]
    [InlineData("typedef void Empty[2]; float Shade(float value) { return value; }")]
    [InlineData("struct Data { void values[2]; }; float Shade(float value) { return value; }")]
    public void VoidCannotBeSmuggledIntoTheValueInterfaceViaAnArrayOrAlias(string source)
    {
        ShaderSourceModuleAnalysis result = new ShaderSourceFrontendCatalog([new BgfxShaderSourceFrontend()]).AnalyzeModule([Request("test", source)]);
        Assert.False(result.succeeded);
        Assert.Contains(result.diagnostics, value => value.code == "SHADER_SOURCE_INTERFACE" && value.message.Contains("void", StringComparison.Ordinal));
        Assert.Throws<ArgumentException>(() => ShaderSourceType.ArrayOf(ShaderSourceType.Atomic("void"), 2));
        Assert.Throws<ArgumentException>(() => new ShaderSourceField("value", ShaderSourceType.Atomic("void")));
        Assert.Throws<ArgumentException>(() => new ShaderSourceParameter("value", ShaderSourceType.Atomic("void"), ShaderSourceParameterDirection.Output));
    }

    private static ShaderSourceImplementationRequest Request(string id, string source, IShaderSourceResolver? includes = null,
        IReadOnlyDictionary<string, string>? defines = null)
        => new(id, C_SC, new(new("Module.ishadersource", source), "Shade", includes ?? new Includes(), defines));

    private static string Errors(ShaderSourceModuleAnalysis result) => string.Join("\n", result.diagnostics.Select(static value => value.message));

    private sealed class Includes : IShaderSourceResolver
    {
        internal Dictionary<string, string> values { get; } = new(StringComparer.Ordinal);
        public ShaderSourceFile ReadInclude(string includingFile, string include)
            => new(include, values.TryGetValue(include, out string? value) ? value : throw new FileNotFoundException(include));
    }

    private sealed class ChangingIncludes : IShaderSourceResolver
    {
        private int m_count;
        public ShaderSourceFile ReadInclude(string includingFile, string include)
            => new(include, ++m_count == 1 ? "#pragma once\nconst float Scale = 1.0;" : "#pragma once\nconst float Scale = 2.0;");
    }

    private sealed class FailingIncludes(Exception failure) : IShaderSourceResolver
    {
        public ShaderSourceFile ReadInclude(string includingFile, string include) => throw failure;
    }

    private sealed class AlternateFrontend : IShaderSourceFrontend
    {
        internal int calls;
        internal bool inventedDependency;
        public string languageId => "tests.alternate";
        public ShaderSourceAnalysis Analyze(ShaderSourceRequest request)
        {
            calls++;
            return new(new(request.entryPoint, ShaderSourceType.Atomic("float"),
                [new("value", ShaderSourceType.Atomic("float"), ShaderSourceParameterDirection.Input)], new(request.source.assetPath, 1, 1)),
                inventedDependency ? ["unread.inc"] : [], []);
        }
    }
}
