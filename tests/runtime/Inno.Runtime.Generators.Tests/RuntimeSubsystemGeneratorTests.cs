using System;
using System.IO;
using System.Linq;
using Inno.Runtime.Contracts;
using Inno.Runtime.Generators;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Inno.Runtime.Generators.Tests;

public sealed class RuntimeSubsystemGeneratorTests
{
    private const string C_CATALOG = """
        using System;
        using System.Collections.Generic;
        using Inno.Runtime.Contracts;
        namespace Example;
        public sealed class HostInputs { }
        public static partial class Catalog
        {
            [RuntimeSubsystemCatalog]
            public static partial IReadOnlyList<IRuntimeSubsystemFactory> Build(HostInputs context);
        }
        """;

    [Fact]
    public void AddingLocalDeclarationUpdatesStronglyTypedCatalogWithoutEditingHost()
    {
        string source = C_CATALOG + """

            public static class AudioModule
            {
                [RuntimeSubsystemRegistration("audio")]
                public static IRuntimeSubsystemFactory Create(HostInputs context) => throw new InvalidOperationException();
            }
            public static class AddedEngineModule
            {
                [RuntimeSubsystemRegistration("animation")]
                public static IRuntimeSubsystemFactory Create(HostInputs context) => throw new InvalidOperationException();
            }
            """;
        (Compilation compilation, GeneratorDriverRunResult result) = Generate(source);
        Assert.Empty(compilation.GetDiagnostics().Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
        Assert.Empty(result.Diagnostics);
        string generated = Assert.Single(Assert.Single(result.Results).GeneratedSources).SourceText.ToString();
        Assert.Contains("global::Example.AddedEngineModule.Create(context)", generated, StringComparison.Ordinal);
        Assert.Contains("global::Example.AudioModule.Create(context)", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("Reflection", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void DuplicateStableIdsFailDuringCompilation()
    {
        (_, GeneratorDriverRunResult result) = Generate(C_CATALOG + """

            public static class DuplicateModule
            {
                [RuntimeSubsystemRegistration("same")]
                public static IRuntimeSubsystemFactory First(HostInputs context) => throw new InvalidOperationException();
                [RuntimeSubsystemRegistration("same")]
                public static IRuntimeSubsystemFactory Second(HostInputs context) => throw new InvalidOperationException();
            }
            """);
        Assert.Contains(result.Diagnostics, static diagnostic => diagnostic.Id == "INNORUN001" &&
            diagnostic.GetMessage().Contains("duplicated", StringComparison.Ordinal));
    }

    [Fact]
    public void InvalidFactoryBoundaryFailsInsteadOfBeingSilentlyDiscovered()
    {
        (_, GeneratorDriverRunResult result) = Generate(C_CATALOG + """

            public static class InvalidModule
            {
                [RuntimeSubsystemRegistration("invalid")]
                public static object Create(HostInputs context) => new object();
            }
            """);
        Assert.Contains(result.Diagnostics, static diagnostic => diagnostic.Id == "INNORUN001");
    }

    [Fact]
    public void CatalogPreservesCallerParameterNames()
    {
        string source = C_CATALOG.Replace("HostInputs context", "HostInputs inputs", StringComparison.Ordinal) + """

            public static class AudioModule
            {
                [RuntimeSubsystemRegistration("audio")]
                public static IRuntimeSubsystemFactory Create(HostInputs context) => throw new InvalidOperationException();
            }
            """;
        (Compilation compilation, GeneratorDriverRunResult result) = Generate(source);
        Assert.Empty(compilation.GetDiagnostics().Where(static diagnostic =>
            diagnostic.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning));
        Assert.Empty(result.Diagnostics);
        Assert.Contains("Create(inputs)", Assert.Single(Assert.Single(result.Results).GeneratedSources).SourceText.ToString(),
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("public static partial class Catalog", "public partial class Catalog")]
    [InlineData("public static partial class Catalog", "public static partial class Catalog<T>")]
    [InlineData("Build(HostInputs context)", "Build<T>(HostInputs context)")]
    [InlineData("Build(HostInputs context)", "Build(ref HostInputs context)")]
    public void UnsupportedCatalogShapeProducesAnExplicitDiagnostic(string original, string replacement)
    {
        (_, GeneratorDriverRunResult result) = Generate(C_CATALOG.Replace(original, replacement, StringComparison.Ordinal));
        Assert.Contains(result.Diagnostics, static diagnostic => diagnostic.Id == "INNORUN001");
        Assert.Empty(Assert.Single(result.Results).GeneratedSources);
    }

    private static (Compilation, GeneratorDriverRunResult) Generate(string source)
    {
        string[] platformAssemblies = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")
            ?? throw new InvalidOperationException("The test host has no platform assembly list.")).Split(Path.PathSeparator);
        MetadataReference[] references = platformAssemblies.Append(typeof(IRuntimeSubsystem).Assembly.Location)
            .Distinct(StringComparer.Ordinal).Select(static path => MetadataReference.CreateFromFile(path)).ToArray();
        CSharpCompilation input = CSharpCompilation.Create("GeneratedCompositionTests",
            [CSharpSyntaxTree.ParseText(source)], references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new RuntimeSubsystemGenerator().AsSourceGenerator());
        driver = driver.RunGeneratorsAndUpdateCompilation(input, out Compilation output, out _);
        return (output, driver.GetRunResult());
    }
}
