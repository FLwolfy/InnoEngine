using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Inno.Rendering;

/// <summary>
/// Identifies one deterministic selection of static shader keyword options shared by authoring and runtime.
/// </summary>
public readonly struct RenderShaderVariant : IEquatable<RenderShaderVariant>
{
    private static readonly IReadOnlyDictionary<string, string> S_EMPTY_OPTIONS =
        new ReadOnlyDictionary<string, string>(new Dictionary<string, string>());

    private readonly IReadOnlyDictionary<string, string>? m_options;
    private readonly string? m_value;

    /// <summary>
    /// Creates a canonical shader variant from stable keyword selections.
    /// </summary>
    /// <param name="options">
    /// Stable keyword ID to selected option mappings.
    /// </param>
    /// <exception cref="ArgumentException">
    /// Thrown when an identifier or option is empty or contains a reserved canonical separator.
    /// </exception>
    public RenderShaderVariant(IReadOnlyDictionary<string, string> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        foreach ((string keyword, string option) in options)
        {
            ValidatePart(keyword, nameof(options));
            ValidatePart(option, nameof(options));
        }

        m_options = new ReadOnlyDictionary<string, string>(options
            .OrderBy(static value => value.Key, StringComparer.Ordinal)
            .ToDictionary(static value => value.Key, static value => value.Value, StringComparer.Ordinal));
        m_value = string.Join(";", m_options.Select(static pair => $"{pair.Key}={pair.Value}"));
    }

    /// <summary>
    /// Gets the empty default variant.
    /// </summary>
    public static RenderShaderVariant empty { get; } = new(new Dictionary<string, string>());

    /// <summary>
    /// Gets the immutable stable keyword selections.
    /// </summary>
    public IReadOnlyDictionary<string, string> options => m_options ?? S_EMPTY_OPTIONS;

    /// <summary>
    /// Gets the canonical cache-key representation.
    /// </summary>
    public string value => m_value ?? string.Empty;

    /// <summary>
    /// Parses the canonical representation stored in a deployed artifact.
    /// </summary>
    /// <param name="value">
    /// The canonical semicolon-separated keyword selections.
    /// </param>
    /// <returns>
    /// The validated shader variant represented by <paramref name="value"/>.
    /// </returns>
    /// <exception cref="FormatException">
    /// Thrown when the representation is malformed, unordered, or contains duplicate keyword IDs.
    /// </exception>
    public static RenderShaderVariant Parse(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Length == 0)
            return empty;

        var options = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string selection in value.Split(';', StringSplitOptions.None))
        {
            int separator = selection.IndexOf('=', StringComparison.Ordinal);
            if (separator <= 0 || separator == selection.Length - 1 || selection.IndexOf('=', separator + 1) >= 0)
                throw new FormatException($"Shader variant selection '{selection}' is malformed.");
            string keyword = selection[..separator];
            string option = selection[(separator + 1)..];
            if (!options.TryAdd(keyword, option))
                throw new FormatException($"Shader variant repeats keyword '{keyword}'.");
        }

        var result = new RenderShaderVariant(options);
        if (!string.Equals(result.value, value, StringComparison.Ordinal))
            throw new FormatException("Shader variant selections are not in canonical order.");
        return result;
    }

    /// <summary>
    /// Resolves the deterministic variant selected by a material.
    /// </summary>
    /// <param name="material">
    /// The material whose declared keyword options are evaluated.
    /// </param>
    /// <returns>
    /// The canonical variant shared by authoring compilation and runtime lookup.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the material has no shader definition or selects an unknown or conflicting option.
    /// </exception>
    public static RenderShaderVariant FromMaterial(MaterialAsset material)
    {
        ArgumentNullException.ThrowIfNull(material);
        ShaderAsset shader = material.shader
            ?? throw new InvalidOperationException($"Material '{material.assetPath}' has no shader reference.");
        ShaderDefinition definition = shader.definition
            ?? throw new InvalidOperationException($"Shader '{shader.assetPath}' has no committed definition.");
        HashSet<string> enabled = material.keywords.ToHashSet(StringComparer.Ordinal);
        var selections = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (ShaderKeywordDefinition keyword in definition.keywords)
        {
            string[] selected = keyword.options.Where(enabled.Contains).ToArray();
            if (selected.Length > 1)
            {
                throw new InvalidOperationException(
                    $"Material '{material.assetPath}' selects multiple options for keyword '{keyword.id}'.");
            }
            if (selected.Length == 1)
                selections.Add(keyword.id, selected[0]);
        }

        string? unknown = enabled.FirstOrDefault(option => !definition.keywords.Any(keyword =>
            keyword.options.Contains(option, StringComparer.Ordinal)));
        if (unknown is not null)
        {
            throw new InvalidOperationException(
                $"Material '{material.assetPath}' enables unknown option '{unknown}'.");
        }
        return new RenderShaderVariant(selections);
    }

    /// <summary>
    /// Determines whether this instance and the supplied value represent the same logical state.
    /// </summary>
    /// <param name="other">
    /// The value to compare with this instance.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when both values select identical options; otherwise, <see langword="false"/>.
    /// </returns>
    public bool Equals(RenderShaderVariant other)
        => string.Equals(value, other.value, StringComparison.Ordinal);

    /// <summary>
    /// Determines whether this instance and the supplied object represent the same logical state.
    /// </summary>
    /// <param name="obj">
    /// The object to compare with this instance.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the object is an identical variant; otherwise, <see langword="false"/>.
    /// </returns>
    public override bool Equals(object? obj)
        => obj is RenderShaderVariant other && Equals(other);

    /// <summary>
    /// Computes a hash code from the canonical variant representation.
    /// </summary>
    /// <returns>
    /// A hash code consistent with logical equality.
    /// </returns>
    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(value);

    /// <summary>
    /// Determines whether two variants select identical stable options.
    /// </summary>
    /// <param name="left">
    /// The first canonical shader variant to compare.
    /// </param>
    /// <param name="right">
    /// The second canonical shader variant to compare.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when both variants contain identical stable options; otherwise, <see langword="false"/>.
    /// </returns>
    public static bool operator ==(RenderShaderVariant left, RenderShaderVariant right) => left.Equals(right);

    /// <summary>
    /// Determines whether two variants select different stable options.
    /// </summary>
    /// <param name="left">
    /// The first canonical shader variant to compare.
    /// </param>
    /// <param name="right">
    /// The second canonical shader variant to compare.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the stable options differ; otherwise, <see langword="false"/>.
    /// </returns>
    public static bool operator !=(RenderShaderVariant left, RenderShaderVariant right) => !left.Equals(right);

    /// <summary>
    /// Formats this value as its canonical representation.
    /// </summary>
    /// <returns>
    /// The canonical representation used by caches and deployed artifact paths.
    /// </returns>
    public override string ToString() => value;

    private static void ValidatePart(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Contains(';') || value.Contains('='))
        {
            throw new ArgumentException(
                "Shader variant identifiers and options must be non-empty and cannot contain ';' or '='.",
                parameterName);
        }
    }
}
