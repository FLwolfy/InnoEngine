namespace Inno.Core.Diagnostics;

/// <summary>
/// Receives complete diagnostic-state changes from a <see cref="DiagnosticHub"/>.
/// </summary>
public interface IDiagnosticSink
{
    /// <summary>
    /// Replaces the current report for one diagnostic source.
    /// </summary>
    /// <param name="report">
    /// The complete current report to store or present.
    /// </param>
    void Replace(DiagnosticReport report);

    /// <summary>
    /// Removes the current report for one diagnostic source.
    /// </summary>
    /// <param name="source">
    /// The source whose current report was cleared.
    /// </param>
    void Clear(DiagnosticSource source);
}
