using System;
using System.Collections.Generic;
using System.Linq;
using Inno.Core.Serialization;
using Inno.Rendering.Shaders;

namespace Inno.Rendering.Assets;

/// <summary>
/// Persists only frozen function compilation inputs, never a parser, live asset, resolver or generated main function.
/// </summary>
public static class ShaderSourceBundle
{
    /// <summary>
    /// Identifies the authoring-only frozen function module output.
    /// </summary>
    public const string outputName = "shader-function";

    /// <summary>
    /// Reads an immutable function snapshot through its explicit artifact owner.
    /// </summary>
    /// <param name="function">
    /// Stable function asset identity.
    /// </param>
    /// <param name="artifacts">
    /// Current authoring artifact lookup.
    /// </param>
    /// <returns>
    /// Detached module bytes retained for the duration of reading.
    /// </returns>
    public static byte[] Read(ShaderFunctionAsset function, Inno.Assets.IAssetArtifactLookup artifacts)
    {
        ArgumentNullException.ThrowIfNull(function);
        ArgumentNullException.ThrowIfNull(artifacts);
        using Inno.Assets.ArtifactLease lease = artifacts.AcquireArtifact(function.identity.persistentId, outputName);
        return System.IO.File.ReadAllBytes(lease.info.absolutePath);
    }

    /// <summary>
    /// Captures every exported function, implementation and include resolution from a successful source import.
    /// </summary>
    /// <param name="functions">
    /// Complete source analyses keyed by explicit exported function name.
    /// </param>
    /// <param name="serialization">
    /// Native converter registry; this bundle contains pure values only.
    /// </param>
    /// <returns>
    /// Deterministic native authoring artifact bytes, not Player content.
    /// </returns>
    public static byte[] Encode(IReadOnlyDictionary<string, ShaderSourceModuleAnalysis> functions, SerializationRegistry serialization)
    {
        ArgumentNullException.ThrowIfNull(functions);
        ArgumentNullException.ThrowIfNull(serialization);
        if (functions.Count == 0 || functions.Any(static pair => string.IsNullOrWhiteSpace(pair.Key) || pair.Value is null))
            throw new ArgumentException("A shader source library requires named function analyses.", nameof(functions));
        return serialization.Serialize(new BundleData
        {
            functions = functions.OrderBy(static pair => pair.Key, StringComparer.Ordinal).Select(static pair => new FunctionData
            {
                name = pair.Key,
                implementations = pair.Value.implementations.Select(static value => new ImplementationData
                {
                    id = value.implementationId, language = value.languageId, root = value.sourcePath, entry = value.entryPoint,
                    paths = value.sources.Select(static file => file.assetPath).ToArray(),
                    texts = value.sources.Select(static file => file.text).ToArray(),
                    includeOwners = value.includes.Select(static edge => edge.includingFile).ToArray(),
                    includeNames = value.includes.Select(static edge => edge.include).ToArray(),
                    includePaths = value.includes.Select(static edge => edge.resolvedPath).ToArray(),
                    defineNames = value.defines.Keys.ToArray(), defineValues = value.defines.Values.ToArray()
                }).ToArray()
            }).ToArray()
        });
    }

    /// <summary>
    /// Restores requests whose resolvers can access only the captured files and edges.
    /// </summary>
    /// <param name="bytes">
    /// Current native authoring artifact.
    /// </param>
    /// <param name="function">
    /// Exact exported function selected by the graph node.
    /// </param>
    /// <param name="serialization">
    /// Native converter registry.
    /// </param>
    /// <param name="defines">
    /// Additional target/variant definitions; conflicting recorded values are rejected.
    /// </param>
    /// <returns>
    /// Detached requests without filesystem access or old-generation provider references.
    /// </returns>
    public static IReadOnlyList<ShaderSourceImplementationRequest> Decode(ReadOnlySpan<byte> bytes, string function, SerializationRegistry serialization,
        IReadOnlyDictionary<string, string>? defines = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(function);
        ArgumentNullException.ThrowIfNull(serialization);
        BundleData data = serialization.Deserialize<BundleData>(bytes);
        FunctionData selected = (data.functions ?? []).SingleOrDefault(value => value.name == function);
        if (selected.implementations is null)
            throw new InvalidOperationException($"Shader source library does not export function '{function}'.");
        return Array.AsReadOnly(selected.implementations.Select(value =>
        {
            if (value.paths is null || value.texts is null || value.includeOwners is null || value.includeNames is null
                || value.includePaths is null || value.defineNames is null || value.defineValues is null)
                throw new InvalidOperationException("The frozen shader source bundle is missing required collections.");
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
        /// <summary>
        /// Reads and validates the include value from its authoritative source.
        /// </summary>
        /// <param name="includingFile">
        /// The including file text validated by the read include operation.
        /// </param>
        /// <param name="include">
        /// The include text validated by the read include operation.
        /// </param>
        /// <returns>
        /// The validated shader source file that represents the completed operation.
        /// </returns>
public ShaderSourceFile ReadInclude(string includingFile, string include)
            => edges.TryGetValue((includingFile, include), out ShaderSourceInclude? edge) && files.TryGetValue(edge.resolvedPath, out ShaderSourceFile? file)
                ? file : throw new InvalidOperationException($"Source dependency '{include}' from '{includingFile}' was not captured by import.");
    }

    private sealed class BundleData : ISerializable
    {
        /// <summary>
        /// Gets the ordered shader functions in this source bundle.
        /// </summary>
[SerializableProperty] public FunctionData[] functions { get; set; } = [];
    }
    private struct FunctionData
    {
        /// <summary>
        /// Gets the human-readable name used for presentation and diagnostics.
        /// </summary>
public string name { get; set; }
        /// <summary>
        /// Gets the shader implementations associated with these functions.
        /// </summary>
public ImplementationData[] implementations { get; set; }
    }
    private struct ImplementationData
    {
        /// <summary>
        /// Gets the stable identity used to reference this value across subsystem boundaries.
        /// </summary>
public string id { get; set; }
        /// <summary>
        /// Gets the language text used by the current instance.
        /// </summary>
public string language { get; set; }
        /// <summary>
        /// Gets the root text used by the current instance.
        /// </summary>
public string root { get; set; }
        /// <summary>
        /// Gets the entry text used by the current instance.
        /// </summary>
public string entry { get; set; }
        /// <summary>
        /// Gets the source paths in this shader bundle.
        /// </summary>
public string[] paths { get; set; }
        /// <summary>
        /// Gets the source text corresponding to each path.
        /// </summary>
public string[] texts { get; set; }
        /// <summary>
        /// Gets the source that owns each included file.
        /// </summary>
public string[] includeOwners { get; set; }
        /// <summary>
        /// Gets the names used to reference included files.
        /// </summary>
public string[] includeNames { get; set; }
        /// <summary>
        /// Gets the resolved paths of included files.
        /// </summary>
public string[] includePaths { get; set; }
        /// <summary>
        /// Gets the names of compilation defines.
        /// </summary>
public string[] defineNames { get; set; }
        /// <summary>
        /// Gets the values corresponding to compilation defines.
        /// </summary>
public string[] defineValues { get; set; }
    }
}
