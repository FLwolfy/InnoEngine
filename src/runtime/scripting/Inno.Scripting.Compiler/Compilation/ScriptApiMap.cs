using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading;

using Microsoft.CodeAnalysis;

namespace Inno.Scripting.Compiler;

internal sealed class ScriptApiMap
{
    internal const string C_FILE_EXTENSION = ".i-script-api";

    private ScriptApiMap(ImmutableArray<ScriptApiNamespaceMap> namespaces)
    {
        this.namespaces = namespaces;
    }

    internal ImmutableArray<ScriptApiNamespaceMap> namespaces { get; }

    internal static ScriptApiMap Read(
        IEnumerable<AdditionalText> additionalFiles,
        CancellationToken cancellationToken)
    {
        var mappings = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (AdditionalText file in additionalFiles)
        {
            if (!file.Path.EndsWith(C_FILE_EXTENSION, StringComparison.OrdinalIgnoreCase))
                continue;
            string text = file.GetText(cancellationToken)?.ToString() ?? string.Empty;
            using var reader = new StringReader(text);
            while (reader.ReadLine() is { } line)
            {
                if (string.IsNullOrWhiteSpace(line) || line[0] == '#')
                    continue;
                string[] parts = line.Split('\t');
                if (parts.Length != 2 || string.IsNullOrWhiteSpace(parts[0]) || string.IsNullOrWhiteSpace(parts[1]))
                    continue;
                if (!mappings.TryGetValue(parts[0], out HashSet<string>? implementations))
                {
                    implementations = new HashSet<string>(StringComparer.Ordinal);
                    mappings.Add(parts[0], implementations);
                }
                implementations.Add(parts[1]);
            }
        }

        ImmutableArray<ScriptApiNamespaceMap> namespaces = mappings
            .OrderBy(static pair => pair.Key, StringComparer.Ordinal)
            .Select(static pair => new ScriptApiNamespaceMap(
                pair.Key,
                pair.Value.OrderBy(static value => value, StringComparer.Ordinal).ToImmutableArray()))
            .ToImmutableArray();
        return new ScriptApiMap(namespaces);
    }
}

internal sealed class ScriptApiNamespaceMap
{
    internal ScriptApiNamespaceMap(string apiNamespace, ImmutableArray<string> implementationNamespaces)
    {
        this.apiNamespace = apiNamespace;
        this.implementationNamespaces = implementationNamespaces;
    }

    internal string apiNamespace { get; }
    internal ImmutableArray<string> implementationNamespaces { get; }
}
