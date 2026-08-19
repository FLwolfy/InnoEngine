using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Threading;
using System.Threading.Tasks;

using Inno.Assets.Loader;

namespace Inno.Editor.Scripting;

[AssetImporterExtension]
internal sealed class ManagedPluginImporter : AssetImporter<ManagedPluginAsset>
{
    public override string importerId => "inno.editor.managed-plugin";
    public override IReadOnlyList<string> supportedExtensions { get; } = [".dll"];

    protected override async ValueTask ImportAsync(
        AssetImportContext context,
        AssetImportWriter<ManagedPluginAsset> output,
        CancellationToken cancellationToken)
    {
        using var stream = new MemoryStream(context.sourceBytes.ToArray(), writable: false);
        using var peReader = new PEReader(stream);
        if (!peReader.HasMetadata)
            throw new InvalidDataException($"Plugin '{context.relativePath}' is not a managed .NET assembly.");
        MetadataReader metadata = peReader.GetMetadataReader();
        string assemblyName = metadata.GetString(metadata.GetAssemblyDefinition().Name);
        ScriptAssemblyScope scope = context.relativePath.EndsWith(
            ".editor.dll",
            StringComparison.OrdinalIgnoreCase)
            ? ScriptAssemblyScope.Editor
            : ScriptAssemblyScope.Runtime;
        output.SetAsset(new ManagedPluginAsset(assemblyName, scope));
        await output.WriteArtifactAsync(
            "assembly",
            context.sourceBytes,
            cancellationToken).ConfigureAwait(false);

        string basePath = Path.ChangeExtension(context.absolutePath, null);
        string relativeDirectory = Path.GetDirectoryName(context.relativePath)?.Replace('\\', '/') ?? string.Empty;
        await WriteCompanionAsync(
                output,
                basePath + ".pdb",
                Combine(relativeDirectory, Path.GetFileName(basePath) + ".pdb"),
                "symbols",
                cancellationToken)
            .ConfigureAwait(false);
        await WriteCompanionAsync(
                output,
                basePath + ".deps.json",
                Combine(relativeDirectory, Path.GetFileName(basePath) + ".deps.json"),
                "dependencies",
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static async ValueTask WriteCompanionAsync(
        AssetImportWriter<ManagedPluginAsset> output,
        string path,
        string relativePath,
        string outputName,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
            return;
        output.DependsOnSource(relativePath);
        byte[] bytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        await output.WriteArtifactAsync(outputName, bytes, cancellationToken).ConfigureAwait(false);
    }

    private static string Combine(string directory, string name)
        => string.IsNullOrEmpty(directory) ? name : directory + "/" + name;
}
