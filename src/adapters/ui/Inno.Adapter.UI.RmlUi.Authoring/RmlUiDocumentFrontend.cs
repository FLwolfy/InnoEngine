using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

using Inno.Assets;
using Inno.Text;
using Inno.UI;
using Inno.UI.Assets;

namespace Inno.Adapter.UI.RmlUi.Authoring;

/// <summary>
/// Validates RML source for the bundled RmlUi runtime adapter.
/// </summary>
public sealed class RmlUiDocumentFrontend : IUiDocumentFrontend
{
    private static readonly Regex S_FONT_FACE = new(@"@font-face\s*\{(?<body>[^{}]*)\}",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex S_PROPERTY = new(@"(?<name>[a-z-]+)\s*:\s*(?<value>[^;]+);?",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex S_FAMILY = new(@"(?im)(font-family\s*:\s*)(?<value>[^;}]+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex S_URL = new(@"^url\(\s*['""]?(?<path>[^)'""]+)['""]?\s*\)$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex S_LINK = new(@"<link\b[^>]*>",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex S_HREF = new(@"\bhref\s*=\s*['""](?<path>[^'""]+)['""]",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex S_IMPORT = new(@"@import\s+(?:url\(\s*)?['""](?<path>[^'""]+)['""]\s*\)?\s*;",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex S_FONT_SOURCE = new(@"(?<prefix>\bsrc\s*:\s*url\(\s*['""]?)(?<path>[^)'""]+)(?<suffix>['""]?\s*\))",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>
    /// Gets the RML source language identifier accepted by this frontend.
    /// </summary>
    public UiDocumentLanguageId languageId => RmlUiIdentifiers.documentLanguage;

    /// <summary>
    /// Analyzes source text and returns validated output with diagnostics.
    /// </summary>
    /// <param name="source">
    /// The source value or location read by this operation.
    /// </param>
    /// <returns>
    /// The validated ui document analysis that represents the completed operation.
    /// </returns>
    public UiDocumentAnalysis Analyze(UiDocumentSourceFile source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (string.IsNullOrWhiteSpace(source.text))
            return new(null, [new("RML_EMPTY", "The RML document is empty.", 1, 1)]);
        if (!source.text.Contains("<rml", StringComparison.OrdinalIgnoreCase))
            return new(null, [new("RML_ROOT_MISSING", "The RML document has no root rml element.", 1, 1)]);
        var diagnostics = new List<UiDocumentDiagnostic>();
        var fonts = new List<UiDocumentFontDeclaration>();
        var families = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        AssetPath documentPath = AssetPath.Parse(source.assetPath);
        string expanded;
        try
        {
            expanded = ExpandStyles(source, documentPath, new HashSet<AssetPath>());
        }
        catch (Exception exception) when (exception is IOException or ArgumentException or InvalidOperationException)
        {
            return new(null, [new("RML_STYLESHEET_UNAVAILABLE", exception.Message, 1, 1)]);
        }
        string withoutFaces = S_FONT_FACE.Replace(expanded, match =>
        {
            var properties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (Match property in S_PROPERTY.Matches(match.Groups["body"].Value))
                properties[property.Groups["name"].Value] = property.Groups["value"].Value.Trim();
            properties.TryGetValue("src", out string? sourceValue);
            Match url = S_URL.Match(sourceValue ?? string.Empty);
            if (!properties.TryGetValue("font-family", out string? logical)
                || !url.Success)
            {
                diagnostics.Add(new("RML_FONT_FACE_INVALID", "@font-face requires font-family and src: url(...).", 1, 1));
                return string.Empty;
            }
            logical = logical.Trim('\'', '"', ' ');
            if (logical.Length == 0 || logical.Equals("none", StringComparison.OrdinalIgnoreCase))
            {
                diagnostics.Add(new("RML_FONT_FAMILY_INVALID", "A font face needs a named family other than none.", 1, 1));
                return string.Empty;
            }
            string physical = families.TryGetValue(logical, out string? existing)
                ? existing : families[logical] = PrivateFamily(source.assetPath, logical);
            string path = url.Groups["path"].Value.Trim();
            try
            {
                AssetPath fontPath = path.Contains("::", StringComparison.Ordinal)
                    ? AssetPath.Parse(path)
                    : new AssetPath(documentPath.source,
                        Path.Combine(Path.GetDirectoryName(documentPath.localPath) ?? string.Empty, path)
                            .Replace('\\', '/'));
                int weight = properties.TryGetValue("font-weight", out string? weightText)
                    ? weightText.ToLowerInvariant() switch
                    {
                        "normal" => 400,
                        "bold" => 700,
                        _ when int.TryParse(weightText, out int numeric) => numeric,
                        _ => -1
                    } : 400;
                if (weight is < 100 or > 1000)
                    throw new ArgumentException("font-weight must be 100–1000, normal, or bold.");
                TextFontStyle style = properties.TryGetValue("font-style", out string? styleText)
                    && styleText.Equals("italic", StringComparison.OrdinalIgnoreCase)
                    ? TextFontStyle.Italic : TextFontStyle.Normal;
                fonts.Add(new(fontPath.ToString(), physical, style, weight));
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
            {
                diagnostics.Add(new("RML_FONT_ASSET_INVALID", exception.Message, 1, 1));
            }
            return string.Empty;
        });
        string canonical = S_FAMILY.Replace(withoutFaces, match =>
        {
            string[] requested = match.Groups["value"].Value.Split(',', StringSplitOptions.TrimEntries);
            for (int index = 0; index < requested.Length; index++)
            {
                string logical = requested[index].Trim('\'', '"', ' ');
                requested[index] = families.TryGetValue(logical, out string? physical)
                    ? physical : PrivateFamily(source.assetPath, "missing/" + logical);
            }
            return match.Groups[1].Value + string.Join(", ", requested);
        });
        return diagnostics.Count == 0 ? new(canonical, diagnostics, fonts) : new(null, diagnostics);
    }

    private static string PrivateFamily(string source, string logical)
    {
        byte[] digest = SHA256.HashData(Encoding.UTF8.GetBytes(source + "\n" + logical.ToLowerInvariant()));
        return "__inno_ui_" + Convert.ToHexString(digest.AsSpan(0, 12)).ToLowerInvariant();
    }

    private static string ExpandStyles(UiDocumentSourceFile source, AssetPath documentPath,
        HashSet<AssetPath> active)
        => S_LINK.Replace(source.text, match =>
        {
            Match href = S_HREF.Match(match.Value);
            if (!href.Success || !href.Groups["path"].Value.EndsWith(".rcss", StringComparison.OrdinalIgnoreCase))
                return match.Value;
            if (source.readSource is null)
                throw new InvalidOperationException("External RCSS requires an import source reader.");
            AssetPath sheetPath = ResolvePath(documentPath, href.Groups["path"].Value);
            return "<style>" + ExpandSheet(source.readSource, sheetPath, active) + "</style>";
        });

    private static string ExpandSheet(Func<AssetPath, string> readSource, AssetPath path,
        HashSet<AssetPath> active)
    {
        if (!active.Add(path))
            throw new InvalidDataException($"RCSS import cycle includes '{path}'.");
        try
        {
            string text = readSource(path);
            text = S_IMPORT.Replace(text, match =>
                ExpandSheet(readSource, ResolvePath(path, match.Groups["path"].Value), active));
            return S_FONT_FACE.Replace(text, match => S_FONT_SOURCE.Replace(match.Value, font =>
                font.Groups["prefix"].Value
                + FormatExplicitPath(ResolvePath(path, font.Groups["path"].Value.Trim()))
                + font.Groups["suffix"].Value));
        }
        finally
        {
            active.Remove(path);
        }
    }

    private static AssetPath ResolvePath(AssetPath owner, string relative)
    {
        if (relative.Contains("::", StringComparison.Ordinal))
            return AssetPath.Parse(relative);
        if (Path.IsPathRooted(relative))
            throw new ArgumentException("An RCSS path must be source-relative.", nameof(relative));
        var segments = new List<string>(owner.localPath.Replace('\\', '/').Split('/'));
        segments.RemoveAt(segments.Count - 1);
        foreach (string segment in relative.Replace('\\', '/').Split('/'))
        {
            if (segment is "" or ".")
                continue;
            if (segment == "..")
            {
                if (segments.Count == 0)
                    throw new ArgumentException("An RCSS path escapes its asset source.", nameof(relative));
                segments.RemoveAt(segments.Count - 1);
            }
            else
                segments.Add(segment);
        }
        return new AssetPath(owner.source, string.Join('/', segments));
    }

    private static string FormatExplicitPath(AssetPath path) => $"{path.source}::{path.localPath}";
}
