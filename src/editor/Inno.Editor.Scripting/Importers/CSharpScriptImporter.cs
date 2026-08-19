using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Inno.Assets.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Inno.Editor.Scripting;

[AssetImporterExtension]
internal sealed class CSharpScriptImporter : AssetImporter<ScriptSourceAsset>
{
    public override string importerId => "inno.editor.csharp-script";
    public override IReadOnlyList<string> supportedExtensions { get; } = [".cs"];

    protected override async ValueTask ImportAsync(
        AssetImportContext context,
        AssetImportWriter<ScriptSourceAsset> output,
        CancellationToken cancellationToken)
    {
        string source = context.ReadUtf8Text();
        SyntaxTree tree = CSharpSyntaxTree.ParseText(
            source,
            new CSharpParseOptions(LanguageVersion.Latest, DocumentationMode.Parse),
            context.relativePath,
            Encoding.UTF8,
            cancellationToken);
        string[] declaredTypes = tree.GetRoot(cancellationToken)
            .DescendantNodes()
            .OfType<BaseTypeDeclarationSyntax>()
            .Select(static declaration => declaration.Identifier.ValueText)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static value => value, StringComparer.Ordinal)
            .ToArray();
        string[] diagnostics = tree.GetDiagnostics(cancellationToken)
            .Where(static diagnostic => diagnostic.Severity != DiagnosticSeverity.Hidden)
            .Select(static diagnostic => diagnostic.ToString())
            .ToArray();
        ScriptAssemblyScope scope = context.relativePath.EndsWith(
            ".editor.cs",
            StringComparison.OrdinalIgnoreCase)
            ? ScriptAssemblyScope.Editor
            : ScriptAssemblyScope.Runtime;
        output.SetAsset(new ScriptSourceAsset(scope, declaredTypes, diagnostics));
        await output.WriteArtifactAsync(
            "source",
            Encoding.UTF8.GetBytes(source),
            cancellationToken).ConfigureAwait(false);
        await output.WriteArtifactAsync(
            "diagnostics",
            Encoding.UTF8.GetBytes(string.Join(Environment.NewLine, diagnostics)),
            cancellationToken).ConfigureAwait(false);
    }
}
