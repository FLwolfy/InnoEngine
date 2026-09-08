using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Inno.Core.Diagnostics;

/// <summary>
/// Provides concise state-oriented diagnostic publication for the calling type.
/// </summary>
public static class Diagnostics
{
    /// <summary>
    /// Atomically sets the complete current state of one diagnostic group owned by the calling type.
    /// </summary>
    /// <param name="group">
    /// The non-empty responsibility name scoped to the calling type.
    /// </param>
    /// <param name="diagnostic">
    /// The single current diagnostic. It replaces every previous entry in the group.
    /// </param>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="group"/> is empty.
    /// </exception>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="diagnostic"/> is <see langword="null"/>.
    /// </exception>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void Set(string group, Diagnostic diagnostic)
    {
        ArgumentNullException.ThrowIfNull(diagnostic);
        Set(group, [diagnostic]);
    }

    /// <summary>
    /// Atomically sets the complete current state of one diagnostic group owned by the calling type.
    /// </summary>
    /// <param name="group">
    /// The non-empty responsibility name scoped to the calling type.
    /// </param>
    /// <param name="diagnostics">
    /// The complete current diagnostic collection. An empty collection clears the group.
    /// </param>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="group"/> is empty or the collection contains a null entry.
    /// </exception>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="diagnostics"/> is <see langword="null"/>.
    /// </exception>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void Set(string group, IEnumerable<Diagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);
        DiagnosticHub.current.Set(
            DiagnosticCallerResolver.Resolve(group),
            diagnostics);
    }

    /// <summary>
    /// Atomically sets the complete current state of one targeted diagnostic group owned by the calling type.
    /// </summary>
    /// <param name="targetId">
    /// The stable identity of the asset, scene element, or other dynamic target.
    /// </param>
    /// <param name="group">
    /// The non-empty responsibility name scoped to the calling type and target.
    /// </param>
    /// <param name="diagnostic">
    /// The single current diagnostic. It replaces every previous entry in the targeted group.
    /// </param>
    /// <param name="displayName">
    /// The optional target name presented by diagnostic consumers.
    /// </param>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="targetId"/> is empty or <paramref name="group"/> is empty.
    /// </exception>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="diagnostic"/> is <see langword="null"/>.
    /// </exception>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void Set(
        Guid targetId,
        string group,
        Diagnostic diagnostic,
        string? displayName = null)
    {
        ArgumentNullException.ThrowIfNull(diagnostic);
        Set(targetId, group, [diagnostic], displayName);
    }

    /// <summary>
    /// Atomically sets the complete current state of one targeted diagnostic group owned by the calling type.
    /// </summary>
    /// <param name="targetId">
    /// The stable identity of the asset, scene element, or other dynamic target.
    /// </param>
    /// <param name="group">
    /// The non-empty responsibility name scoped to the calling type and target.
    /// </param>
    /// <param name="diagnostics">
    /// The complete current diagnostic collection. An empty collection clears the targeted group.
    /// </param>
    /// <param name="displayName">
    /// The optional target name presented by diagnostic consumers.
    /// </param>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="targetId"/> is empty, <paramref name="group"/> is empty, or the collection contains a null entry.
    /// </exception>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="diagnostics"/> is <see langword="null"/>.
    /// </exception>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void Set(
        Guid targetId,
        string group,
        IEnumerable<Diagnostic> diagnostics,
        string? displayName = null)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);
        DiagnosticHub.current.Set(
            DiagnosticCallerResolver.Resolve(group, targetId, displayName),
            diagnostics);
    }

    /// <summary>
    /// Clears one diagnostic group owned by the calling type.
    /// </summary>
    /// <param name="group">
    /// The non-empty responsibility name scoped to the calling type.
    /// </param>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="group"/> is empty.
    /// </exception>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void Clear(string group)
        => DiagnosticHub.current.Clear(DiagnosticCallerResolver.Resolve(group));

    /// <summary>
    /// Clears one targeted diagnostic group owned by the calling type.
    /// </summary>
    /// <param name="targetId">
    /// The stable identity of the asset, scene element, or other dynamic target.
    /// </param>
    /// <param name="group">
    /// The non-empty responsibility name scoped to the calling type and target.
    /// </param>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="targetId"/> is empty or <paramref name="group"/> is empty.
    /// </exception>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void Clear(Guid targetId, string group)
        => DiagnosticHub.current.Clear(DiagnosticCallerResolver.Resolve(group, targetId));
}
