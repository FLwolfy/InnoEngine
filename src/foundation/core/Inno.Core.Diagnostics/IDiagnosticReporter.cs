using System;
using System.Collections.Generic;

namespace Inno.Core.Diagnostics;

/// <summary>
/// Publishes current issues through an owner-bound producer, independently of presentation sinks.
/// </summary>
public interface IDiagnosticReporter
{
    /// <summary>
    /// Replaces the issue with the same code and target, without using its message as identity.
    /// </summary>
    /// <param name="diagnostic">
    /// The immutable current issue.
    /// </param>
    void Publish(Diagnostic diagnostic);

    /// <summary>
    /// Resolves an issue after the represented condition no longer exists.
    /// </summary>
    /// <param name="code">
    /// The stable issue code.
    /// </param>
    /// <param name="semanticId">
    /// The optional protocol identifier used at publication.
    /// </param>
    /// <param name="objectId">
    /// The optional persistent object identity used at publication.
    /// </param>
    void Resolve(string code, string? semanticId = null, Guid? objectId = null);

    /// <summary>
    /// Atomically replaces this producer's complete current issue set.
    /// </summary>
    /// <param name="diagnostics">
    /// The full immutable issue set; empty clears the report.
    /// </param>
    void Replace(IEnumerable<Diagnostic> diagnostics);
}
