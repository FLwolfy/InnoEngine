using System;
using System.Collections.Generic;
using System.Linq;

namespace Inno.Core.Diagnostics;

/// <summary>
/// Represents the complete current diagnostic state published by one source.
/// </summary>
public sealed class DiagnosticReport
{
    internal DiagnosticReport(
        DiagnosticSource source,
        IReadOnlyList<Diagnostic> diagnostics,
        DateTime publishedAt)
    {
        this.source = source;
        this.diagnostics = Array.AsReadOnly(diagnostics.ToArray());
        this.publishedAt = publishedAt;
    }

    /// <summary>
    /// Gets the producer that owns this report.
    /// </summary>
    public DiagnosticSource source { get; }

    /// <summary>
    /// Gets the immutable diagnostics currently reported by the producer.
    /// </summary>
    public IReadOnlyList<Diagnostic> diagnostics { get; }

    /// <summary>
    /// Gets the time at which the report was published.
    /// </summary>
    public DateTime publishedAt { get; }
}
