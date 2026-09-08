using System;

namespace Inno.Core.Execution;

/// <summary>
/// Reports an expired retirement deadline without granting permission to release dependent resources.
/// </summary>
public sealed class RetirementTimeoutException : RetirementPendingException
{
    /// <summary>
    /// Creates a terminal retirement failure whose owner must remain retained until host shutdown.
    /// </summary>
    /// <param name="message">
    /// The owner and unfinished work that exceeded the deadline.
    /// </param>
    /// <param name="innerException">
    /// The complete failure that prevented retirement, or null when no underlying exception is available.
    /// </param>
    public RetirementTimeoutException(string message, Exception? innerException = null) : base(message, innerException) { }
}
