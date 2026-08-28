using System;
using System.Collections.Generic;
using Inno.Core.Logging;
using Inno.Rendering;

namespace Inno.Editor.Application;

internal sealed class EditorRenderDiagnosticSink : IRenderDiagnosticSink
{
    private readonly HashSet<string> m_published = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public void Publish(RenderDiagnostic diagnostic)
    {
        ArgumentNullException.ThrowIfNull(diagnostic);
        string key = $"{diagnostic.code}:{diagnostic.sourceId}:{diagnostic.message}";
        if (!m_published.Add(key))
        {
            return;
        }

        string message = $"[{diagnostic.code}] {diagnostic.message}";
        if (diagnostic.severity == RenderDiagnosticSeverity.Error)
        {
            Log.Error(message);
        }
        else if (diagnostic.severity == RenderDiagnosticSeverity.Warning)
        {
            Log.Warn(message);
        }
        else
        {
            Log.Info(message);
        }
    }
}
