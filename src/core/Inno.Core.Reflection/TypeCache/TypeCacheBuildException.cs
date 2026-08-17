using System;
using System.Collections.Generic;

namespace Inno.Core.Reflection;

/// <summary>
/// Reports one or more failures encountered while building a type-cache snapshot.
/// </summary>
public sealed class TypeCacheBuildException : Exception
{
    /// <summary>
    /// Creates an exception for a failed type-cache snapshot build.
    /// </summary>
    /// <param name="message">A summary of the build failure.</param>
    /// <param name="loaderExceptions">The underlying type-loader failures.</param>
    public TypeCacheBuildException(string message, IReadOnlyList<Exception> loaderExceptions)
        : base(message, loaderExceptions.Count > 0 ? loaderExceptions[0] : null)
    {
        this.loaderExceptions = loaderExceptions;
    }

    /// <summary>
    /// Gets the individual type-loader failures.
    /// </summary>
    public IReadOnlyList<Exception> loaderExceptions { get; }
}
