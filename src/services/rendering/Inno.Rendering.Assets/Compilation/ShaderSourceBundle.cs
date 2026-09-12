using System;
using System.Collections.Generic;
using System.Linq;
using Inno.Core.Serialization;
using Inno.Rendering.Shaders;

namespace Inno.Rendering.Assets;

/// <summary>Persists only frozen function compilation inputs, never a parser, live asset, resolver or generated main function.</summary>
public static class ShaderSourceBundle
{
    /// <summary>Identifies the authoring-only frozen function module output.</summary>
    public const string outputName = "shader-function";

    /// <summary>Reads an immutable function snapshot through its explicit artifact owner.</summary>
    /// <param name="function">Stable function asset identity.</param>
    /// <param name="artifacts">Current authoring artifact lookup.</param>
    /// <returns>Detached module bytes retained for the duration of reading.</returns>
    public static byte[] Read(ShaderFunctionAsset function, Inno.Assets.IAssetArtifactLookup artifacts)
    {
        ArgumentNullException.ThrowIfNull(function);
        ArgumentNullException.ThrowIfNull(artifacts);
        using Inno.Assets.ArtifactLease lease = artifacts.AcquireArtifact(function.identity.persistentId, outputName);
        return System.IO.File.ReadAllBytes(lease.info.absolutePath);
    }

    /// <summary>Captures every implementation and include resolution from a successful source import.</summary>
    /// <param name="module">Complete source analysis.</param>
    /// <param name="serialization">Native converter registry; this bundle contains pure values only.</param>
    /// <returns>Deterministic native authoring artifact bytes, not Player content.</returns>
    public static byte[] Encode(ShaderSourceModuleAnalysis module, SerializationRegistry serialization)
    {
        ArgumentNullException.ThrowIfNull(module);
        ArgumentNullException.ThrowIfNull(serialization);
        return serialization.Serialize(new BundleData
        {
            implementations = module.implementations.Select(static value => new ImplementationData
            {
                id = value.implementationId, language = value.languageId, root = value.sourcePath, entry = value.entryPoint,
                paths = value.sources.Select(static file => file.assetPath).ToArray(),
                texts = value.sources.Select(static file => file.text).ToArray(),
                includeOwners = value.includes.Select(static edge => edge.includingFile).ToArray(),
                includeNames = value.includes.Select(static edge => edge.include).ToArray(),
                includePaths = value.includes.Select(static edge => edge.resolvedPath).ToArray(),
                defineNames = value.defines.Keys.ToArray(), defineValues = value.defines.Values.ToArray()
            }).ToArray()
        });
    }

    /// <summary>Restores requests whose resolvers can access only the captured files and edges.</summary>
    /// <param name="bytes">Current native authoring artifact.</param>
    /// <param name="serialization">Native converter registry.</param>
    /// <param name="defines">Additional target/variant definitions; conflicting recorded values are rejected.</param>
    /// <returns>Detached requests without filesystem access or old-generation provider references.</returns>
    public static IReadOnlyList<ShaderSourceImplementationRequest> Decode(ReadOnlySpan<byte> bytes, SerializationRegistry serialization,
        IReadOnlyDictionary<string, string>? defines = null)
    {
        ArgumentNullException.ThrowIfNull(serialization);
        BundleData data = serialization.Deserialize<BundleData>(bytes);
        return Array.AsReadOnly(data.implementations.Select(value =>
        {
            if (value.paths.Length != value.texts.Length || value.includeOwners.Length != value.includeNames.Length
                || value.includeOwners.Length != value.includePaths.Length || value.defineNames.Length != value.defineValues.Length)
                throw new InvalidOperationException("The frozen shader source bundle has inconsistent collection lengths.");
            var files = value.paths.Select((path, index) => new ShaderSourceFile(path, value.texts[index])).ToDictionary(static file => file.assetPath, StringComparer.Ordinal);
            var edges = value.includeOwners.Select((owner, index) => new ShaderSourceInclude(owner, value.includeNames[index], value.includePaths[index]))
                .ToDictionary(static edge => (edge.includingFile, edge.include));
            var macros = value.defineNames.Select((name, index) => new KeyValuePair<string, string>(name, value.defineValues[index])).ToDictionary(StringComparer.Ordinal);
            foreach ((string name, string text) in defines ?? new Dictionary<string, string>())
            {
                if (macros.TryGetValue(name, out string? previous) && previous != text)
                    throw new InvalidOperationException($"The target changes recorded source definition '{name}'.");
                macros[name] = text;
            }
            return new ShaderSourceImplementationRequest(value.id, value.language,
                new(files[value.root], value.entry, new FrozenResolver(files, edges), macros));
        }).ToArray());
    }

    private sealed class FrozenResolver(Dictionary<string, ShaderSourceFile> files,
        Dictionary<(string, string), ShaderSourceInclude> edges) : IShaderSourceResolver
    {
        public ShaderSourceFile ReadInclude(string includingFile, string include)
            => edges.TryGetValue((includingFile, include), out ShaderSourceInclude? edge) && files.TryGetValue(edge.resolvedPath, out ShaderSourceFile? file)
                ? file : throw new InvalidOperationException($"Source dependency '{include}' from '{includingFile}' was not captured by import.");
    }

    private sealed class BundleData : ISerializable
    {
        [SerializableProperty] public ImplementationData[] implementations { get; set; } = [];
    }
    private struct ImplementationData
    {
        public string id { get; set; }
        public string language { get; set; }
        public string root { get; set; }
        public string entry { get; set; }
        public string[] paths { get; set; }
        public string[] texts { get; set; }
        public string[] includeOwners { get; set; }
        public string[] includeNames { get; set; }
        public string[] includePaths { get; set; }
        public string[] defineNames { get; set; }
        public string[] defineValues { get; set; }
    }
}
