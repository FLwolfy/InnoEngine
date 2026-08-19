using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Inno.Assets.Loader;

namespace Inno.Editor.Scripting;

[AssetImporterExtension]
internal sealed class ScriptAssemblyDefinitionImporter : AssetImporter<ScriptAssemblyDefinitionAsset>
{
    public override string importerId => "inno.editor.script-assembly-definition";
    public override IReadOnlyList<string> supportedExtensions { get; } = [".innoasmdef"];

    protected override async ValueTask ImportAsync(
        AssetImportContext context,
        AssetImportWriter<ScriptAssemblyDefinitionAsset> output,
        CancellationToken cancellationToken)
    {
        DefinitionModel model = JsonSerializer.Deserialize<DefinitionModel>(
            context.sourceBytes.Span,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidOperationException("Assembly definition source is empty.");
        if (string.IsNullOrWhiteSpace(model.name))
            throw new InvalidOperationException("Assembly definition name is required.");
        if (!Enum.TryParse(model.scope, ignoreCase: true, out ScriptAssemblyScope scope))
            throw new InvalidOperationException("Assembly definition scope must be Runtime or Editor.");
        output.SetAsset(new ScriptAssemblyDefinitionAsset(
            model.name.Trim(),
            scope,
            model.references ?? [],
            model.defines ?? [],
            model.nullable,
            model.allowUnsafe));
        await output.WriteArtifactAsync("source", context.sourceBytes, cancellationToken)
            .ConfigureAwait(false);
    }

    private sealed class DefinitionModel
    {
        public string name { get; set; } = string.Empty;
        public string scope { get; set; } = "Runtime";
        public string[]? references { get; set; }
        public string[]? defines { get; set; }
        public bool nullable { get; set; } = true;
        public bool allowUnsafe { get; set; }
    }
}
