using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using Inno.Rendering.Core;

namespace Inno.Rendering.Assets;

/// <summary>
/// Reports a strict rendering asset schema error with its JSON path.
/// </summary>
public sealed class RenderingAssetFormatException : FormatException
{
    /// <summary>
    /// Creates a rendering asset format exception.
    /// </summary>
    /// <param name="path">JSON path or source path that failed validation.</param>
    /// <param name="message">Specific schema failure.</param>
    public RenderingAssetFormatException(string path, string message)
        : base($"{path}: {message}")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        this.path = path;
    }

    /// <summary>Gets the JSON path or source path that failed validation.</summary>
    public string path { get; }
}

internal sealed class HandwrittenShaderParseResult
{
    internal HandwrittenShaderParseResult(ShaderIRModule module, IReadOnlyList<string> dependencies)
    {
        this.module = module;
        this.dependencies = dependencies;
    }

    internal ShaderIRModule module { get; }
    internal IReadOnlyList<string> dependencies { get; }
}

internal static partial class HandwrittenShaderParser
{
    private static readonly JsonDocumentOptions S_DOCUMENT_OPTIONS = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip
    };

    private static readonly HashSet<string> S_ROOT_PROPERTIES =
        new(["name", "properties", "keywords", "passes"], StringComparer.Ordinal);
    private static readonly HashSet<string> S_SHADER_PROPERTIES =
        new(["id", "displayName", "type", "stages", "default"], StringComparer.Ordinal);
    private static readonly HashSet<string> S_KEYWORD_PROPERTIES =
        new(["id", "options"], StringComparer.Ordinal);
    private static readonly HashSet<string> S_PASS_PROPERTIES = new(
        [
            "name",
            "tag",
            "vertex",
            "fragment",
            "compute",
            "varying",
            "requires",
            "renderState",
            "tags"
        ],
        StringComparer.Ordinal);
    private static readonly HashSet<string> S_RENDER_STATE_PROPERTIES = new(
        ["cull", "depthCompare", "depthWrite", "blend", "colorWriteMask"],
        StringComparer.Ordinal);

    internal static HandwrittenShaderParseResult Parse(
        string assetPath,
        string json,
        Func<string, string> sourceReader)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assetPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        ArgumentNullException.ThrowIfNull(sourceReader);
        using JsonDocument document = JsonDocument.Parse(json, S_DOCUMENT_OPTIONS);
        JsonElement root = RequireObject(document.RootElement, "$");
        RejectUnknown(root, S_ROOT_PROPERTIES, "$");

        string name = RequireString(root, "name", "$.name");
        ShaderPropertyDefinition[] properties = ParseProperties(root);
        ShaderKeywordDefinition[] keywords = ParseKeywords(root);
        var definitions = new List<ShaderPassDefinition>();
        var passes = new List<ShaderIRPass>();
        var dependencies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        JsonElement passArray = RequireArray(root, "passes", "$.passes");
        int passIndex = 0;
        foreach (JsonElement passElement in passArray.EnumerateArray())
        {
            string path = $"$.passes[{passIndex++}]";
            ShaderIRPass pass = ParsePass(assetPath, passElement, path, sourceReader, dependencies);
            definitions.Add(pass.definition);
            passes.Add(pass);
        }

        if (passes.Count == 0)
        {
            throw new RenderingAssetFormatException("$.passes", "At least one shader pass is required.");
        }

        var definition = new ShaderDefinition(name, properties, keywords, definitions);
        var module = new ShaderIRModule(definition, passes);
        ShaderIRValidationResult validation = ShaderIRValidator.Validate(module);
        ShaderDiagnostic? error = validation.diagnostics.FirstOrDefault(static value =>
            value.severity == ShaderDiagnosticSeverity.Error);
        if (error is not null)
        {
            throw new RenderingAssetFormatException(
                error.location?.assetPath ?? assetPath,
                error.message);
        }

        return new HandwrittenShaderParseResult(
            module,
            dependencies.OrderBy(static value => value, StringComparer.Ordinal).ToArray());
    }

    private static ShaderPropertyDefinition[] ParseProperties(JsonElement root)
    {
        if (!root.TryGetProperty("properties", out JsonElement propertyArray))
        {
            return [];
        }

        RequireKind(propertyArray, JsonValueKind.Array, "$.properties");
        var result = new List<ShaderPropertyDefinition>();
        int index = 0;
        foreach (JsonElement element in propertyArray.EnumerateArray())
        {
            string path = $"$.properties[{index++}]";
            JsonElement property = RequireObject(element, path);
            RejectUnknown(property, S_SHADER_PROPERTIES, path);
            string id = RequireString(property, "id", $"{path}.id");
            string displayName = GetOptionalString(property, "displayName") ?? id;
            ShaderPropertyType type = ParseEnum<ShaderPropertyType>(
                RequireString(property, "type", $"{path}.type"),
                $"{path}.type");
            ShaderStage stages = property.TryGetProperty("stages", out JsonElement stageArray)
                ? ParseStageMask(stageArray, $"{path}.stages")
                : ShaderStage.Vertex | ShaderStage.Fragment;
            if (!property.TryGetProperty("default", out JsonElement defaultValue))
            {
                throw new RenderingAssetFormatException($"{path}.default", "A default value is required.");
            }

            result.Add(new ShaderPropertyDefinition(
                new ShaderPropertyId(id),
                displayName,
                type,
                stages,
                defaultValue.GetRawText()));
        }

        return [.. result];
    }

    private static ShaderKeywordDefinition[] ParseKeywords(JsonElement root)
    {
        if (!root.TryGetProperty("keywords", out JsonElement keywordArray))
        {
            return [];
        }

        RequireKind(keywordArray, JsonValueKind.Array, "$.keywords");
        var result = new List<ShaderKeywordDefinition>();
        int index = 0;
        foreach (JsonElement element in keywordArray.EnumerateArray())
        {
            string path = $"$.keywords[{index++}]";
            JsonElement keyword = RequireObject(element, path);
            RejectUnknown(keyword, S_KEYWORD_PROPERTIES, path);
            string id = RequireString(keyword, "id", $"{path}.id");
            JsonElement optionArray = RequireArray(keyword, "options", $"{path}.options");
            string[] options = optionArray.EnumerateArray()
                .Select((value, optionIndex) => RequireString(value, $"{path}.options[{optionIndex}]"))
                .ToArray();
            if (options.Length == 0 || options.Distinct(StringComparer.Ordinal).Count() != options.Length)
            {
                throw new RenderingAssetFormatException(
                    $"{path}.options",
                    "Keyword options must be non-empty and unique.");
            }

            result.Add(new ShaderKeywordDefinition(id, options));
        }

        return [.. result];
    }

    private static ShaderIRPass ParsePass(
        string assetPath,
        JsonElement element,
        string path,
        Func<string, string> sourceReader,
        HashSet<string> dependencies)
    {
        JsonElement pass = RequireObject(element, path);
        RejectUnknown(pass, S_PASS_PROPERTIES, path);
        string name = RequireString(pass, "name", $"{path}.name");
        string tag = RequireString(pass, "tag", $"{path}.tag");
        string? vertexPath = NormalizeOptionalPath(pass, "vertex", $"{path}.vertex");
        string? fragmentPath = NormalizeOptionalPath(pass, "fragment", $"{path}.fragment");
        string? computePath = NormalizeOptionalPath(pass, "compute", $"{path}.compute");
        string? varyingPath = NormalizeOptionalPath(pass, "varying", $"{path}.varying");
        GraphicsFeature required = pass.TryGetProperty("requires", out JsonElement requires)
            ? ParseFeatureMask(requires, $"{path}.requires")
            : GraphicsFeature.None;
        ShaderRenderState renderState = pass.TryGetProperty("renderState", out JsonElement state)
            ? ParseRenderState(state, $"{path}.renderState")
            : ShaderRenderState.opaque;
        IReadOnlyDictionary<string, string> tags = pass.TryGetProperty("tags", out JsonElement tagObject)
            ? ParseTags(tagObject, $"{path}.tags")
            : new Dictionary<string, string>(StringComparer.Ordinal);

        bool hasCompute = computePath is not null;
        bool hasRaster = vertexPath is not null || fragmentPath is not null;
        if (hasCompute == hasRaster || (hasRaster && (vertexPath is null || fragmentPath is null)))
        {
            throw new RenderingAssetFormatException(
                path,
                "A pass must declare either compute, or both vertex and fragment sources.");
        }

        if (hasCompute)
        {
            required |= GraphicsFeature.Compute;
        }

        if (varyingPath is not null)
        {
            dependencies.Add(varyingPath);
        }

        var definition = new ShaderPassDefinition(
            name,
            tag,
            vertexPath,
            fragmentPath,
            computePath,
            varyingPath,
            required,
            renderState,
            tags);
        var stages = new List<ShaderIRStageModule>();
        AddStage(assetPath, name, ShaderStage.Vertex, vertexPath, sourceReader, dependencies, stages);
        AddStage(assetPath, name, ShaderStage.Fragment, fragmentPath, sourceReader, dependencies, stages);
        AddStage(assetPath, name, ShaderStage.Compute, computePath, sourceReader, dependencies, stages);
        return new ShaderIRPass(definition, stages);
    }

    private static void AddStage(
        string assetPath,
        string passName,
        ShaderStage stage,
        string? sourcePath,
        Func<string, string> sourceReader,
        HashSet<string> dependencies,
        List<ShaderIRStageModule> stages)
    {
        if (sourcePath is null)
        {
            return;
        }

        string source = ReadSourceTree(sourcePath, sourceReader, dependencies, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        stages.Add(new ShaderIRStageModule(
            stage,
            "main",
            source,
            ShaderIRSourceKind.Handwritten,
            new ShaderSourceLocation(sourcePath, passName, stage)));
    }

    private static string ReadSourceTree(
        string sourcePath,
        Func<string, string> sourceReader,
        HashSet<string> dependencies,
        HashSet<string> visiting)
    {
        if (!visiting.Add(sourcePath))
        {
            throw new RenderingAssetFormatException(sourcePath, "Shader include cycle detected.");
        }

        dependencies.Add(sourcePath);
        string source = sourceReader(sourcePath);
        string directory = GetProjectDirectory(sourcePath);
        foreach (Match match in IncludePattern().Matches(source))
        {
            string include = NormalizePath(CombineProjectPath(directory, match.Groups[1].Value), sourcePath);
            _ = ReadSourceTree(include, sourceReader, dependencies, visiting);
        }

        visiting.Remove(sourcePath);
        return source;
    }

    private static ShaderRenderState ParseRenderState(JsonElement element, string path)
    {
        JsonElement state = RequireObject(element, path);
        RejectUnknown(state, S_RENDER_STATE_PROPERTIES, path);
        byte colorMask = 0x0f;
        if (state.TryGetProperty("colorWriteMask", out JsonElement maskElement))
        {
            if (!maskElement.TryGetByte(out colorMask) || colorMask > 0x0f)
            {
                throw new RenderingAssetFormatException(
                    $"{path}.colorWriteMask",
                    "Color write mask must be an integer from 0 through 15.");
            }
        }

        return new ShaderRenderState
        {
            cull = state.TryGetProperty("cull", out JsonElement cull)
                ? ParseEnum<ShaderCullMode>(RequireString(cull, $"{path}.cull"), $"{path}.cull")
                : ShaderCullMode.Back,
            depthCompare = state.TryGetProperty("depthCompare", out JsonElement compare)
                ? ParseEnum<ShaderCompareFunction>(
                    RequireString(compare, $"{path}.depthCompare"),
                    $"{path}.depthCompare")
                : ShaderCompareFunction.LessEqual,
            depthWrite = !state.TryGetProperty("depthWrite", out JsonElement depthWrite)
                || RequireBoolean(depthWrite, $"{path}.depthWrite"),
            blend = state.TryGetProperty("blend", out JsonElement blend)
                ? ParseEnum<ShaderBlendMode>(RequireString(blend, $"{path}.blend"), $"{path}.blend")
                : ShaderBlendMode.Opaque,
            colorWriteMask = colorMask
        };
    }

    private static IReadOnlyDictionary<string, string> ParseTags(JsonElement element, string path)
    {
        JsonElement tags = RequireObject(element, path);
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (JsonProperty property in tags.EnumerateObject())
        {
            if (string.IsNullOrWhiteSpace(property.Name))
            {
                throw new RenderingAssetFormatException(path, "Tag names cannot be empty.");
            }

            result.Add(property.Name, RequireString(property.Value, $"{path}.{property.Name}"));
        }

        return result;
    }

    private static ShaderStage ParseStageMask(JsonElement element, string path)
    {
        RequireKind(element, JsonValueKind.Array, path);
        ShaderStage result = ShaderStage.None;
        int index = 0;
        foreach (JsonElement value in element.EnumerateArray())
        {
            result |= ParseEnum<ShaderStage>(RequireString(value, $"{path}[{index}]"), $"{path}[{index}]");
            index++;
        }

        if (result == ShaderStage.None)
        {
            throw new RenderingAssetFormatException(path, "At least one stage is required.");
        }

        return result;
    }

    private static GraphicsFeature ParseFeatureMask(JsonElement element, string path)
    {
        RequireKind(element, JsonValueKind.Array, path);
        GraphicsFeature result = GraphicsFeature.None;
        int index = 0;
        foreach (JsonElement value in element.EnumerateArray())
        {
            result |= ParseEnum<GraphicsFeature>(
                RequireString(value, $"{path}[{index}]"),
                $"{path}[{index}]");
            index++;
        }

        return result;
    }

    private static TEnum ParseEnum<TEnum>(string value, string path)
        where TEnum : struct, Enum
        => Enum.TryParse(value, ignoreCase: true, out TEnum result) && Enum.IsDefined(result)
            ? result
            : throw new RenderingAssetFormatException(
                path,
                $"'{value}' is not a supported {typeof(TEnum).Name} value.");

    private static JsonElement RequireObject(JsonElement element, string path)
    {
        RequireKind(element, JsonValueKind.Object, path);
        return element;
    }

    private static JsonElement RequireArray(JsonElement parent, string name, string path)
    {
        if (!parent.TryGetProperty(name, out JsonElement value))
        {
            throw new RenderingAssetFormatException(path, "A required array is missing.");
        }

        RequireKind(value, JsonValueKind.Array, path);
        return value;
    }

    private static string RequireString(JsonElement parent, string name, string path)
    {
        if (!parent.TryGetProperty(name, out JsonElement value))
        {
            throw new RenderingAssetFormatException(path, "A required string is missing.");
        }

        return RequireString(value, path);
    }

    private static string RequireString(JsonElement element, string path)
    {
        if (element.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(element.GetString()))
        {
            throw new RenderingAssetFormatException(path, "A non-empty string is required.");
        }

        return element.GetString()!;
    }

    private static string? GetOptionalString(JsonElement parent, string name)
        => parent.TryGetProperty(name, out JsonElement value)
            ? RequireString(value, $"$.{name}")
            : null;

    private static bool RequireBoolean(JsonElement element, string path)
        => element.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => throw new RenderingAssetFormatException(path, "A boolean is required.")
        };

    private static void RequireKind(JsonElement element, JsonValueKind kind, string path)
    {
        if (element.ValueKind != kind)
        {
            throw new RenderingAssetFormatException(path, $"Expected {kind}, found {element.ValueKind}.");
        }
    }

    private static void RejectUnknown(JsonElement element, HashSet<string> allowed, string path)
    {
        foreach (JsonProperty property in element.EnumerateObject())
        {
            if (!allowed.Contains(property.Name))
            {
                throw new RenderingAssetFormatException(
                    $"{path}.{property.Name}",
                    "Unknown properties are not allowed.");
            }
        }
    }

    private static string? NormalizeOptionalPath(JsonElement parent, string name, string path)
        => parent.TryGetProperty(name, out JsonElement value)
            ? NormalizePath(RequireString(value, path), path)
            : null;

    private static string NormalizePath(string value, string path)
    {
        string normalized = value.Replace('\\', '/').TrimStart('/');
        if (string.IsNullOrWhiteSpace(normalized)
            || normalized.Split('/', StringSplitOptions.RemoveEmptyEntries).Contains("..", StringComparer.Ordinal))
        {
            throw new RenderingAssetFormatException(path, "A project-relative path without '..' is required.");
        }

        return normalized;
    }

    private static string GetProjectDirectory(string sourcePath)
    {
        int separator = sourcePath.LastIndexOf('/');
        return separator < 0 ? string.Empty : sourcePath[..separator];
    }

    private static string CombineProjectPath(string directory, string relative)
        => string.IsNullOrEmpty(directory) ? relative : $"{directory}/{relative}";

    [GeneratedRegex("^[ \\t]*#include[ \\t]+\"([^\"]+)\"", RegexOptions.Multiline)]
    private static partial Regex IncludePattern();
}
