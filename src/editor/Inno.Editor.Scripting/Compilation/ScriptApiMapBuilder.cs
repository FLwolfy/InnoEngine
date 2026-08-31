using System;
using System.Linq;
using System.Text;

namespace Inno.Editor.Scripting;

internal static class ScriptApiMapBuilder
{
    internal const string C_FILE_EXTENSION = ".i-script-api";

    internal static string Build(ScriptApiProfile profile)
    {
        var source = new StringBuilder("# Script namespace\tImplementation namespace\n");
        foreach (ScriptApiNamespaceMapping mapping in profile.namespaceMappings
                     .Distinct()
                     .OrderBy(static value => value.apiNamespace, StringComparer.Ordinal)
                     .ThenBy(static value => value.implementationNamespace, StringComparer.Ordinal))
        {
            source.Append(mapping.apiNamespace)
                .Append('\t')
                .Append(mapping.implementationNamespace)
                .Append('\n');
        }
        return source.ToString();
    }
}
