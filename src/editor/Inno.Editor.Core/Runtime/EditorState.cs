using System;

namespace Inno.Editor.Core;

/// <summary>
/// Provides the serialization-neutral parameter used to capture or restore one editor extension's
/// project state.
/// </summary>
/// <remarks>
/// The editor supplies this object to the protected state hooks on <see cref="EditorModule"/> and
/// <see cref="EditorPanel"/>. Extensions should write values during capture and read values during
/// restore; storage format and persistence remain private runtime details.
/// </remarks>
public abstract class EditorState
{
    /// <summary>
    /// Initializes the base contract for a runtime-owned editor state parameter.
    /// </summary>
    protected EditorState()
    {
    }

    /// <summary>
    /// Reads a compatible value or returns the caller-provided fallback.
    /// </summary>
    /// <typeparam name="T">The neutral value type expected by the caller.</typeparam>
    /// <param name="key">The stable key local to the owning module or panel.</param>
    /// <param name="fallback">
    /// The value returned when the key is absent, malformed, or incompatible with
    /// <typeparamref name="T"/>.
    /// </param>
    /// <returns>The stored compatible value, or <paramref name="fallback"/>.</returns>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="key"/> is empty.
    /// </exception>
    public abstract T Get<T>(string key, T fallback);

    /// <summary>
    /// Writes one neutral value under a stable extension-local key.
    /// </summary>
    /// <typeparam name="T">The neutral value type supplied by the caller.</typeparam>
    /// <param name="key">The stable key local to the owning module or panel.</param>
    /// <param name="value">The value to persist.</param>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="key"/> is empty.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the state parameter was supplied for restoration and is therefore read-only.
    /// </exception>
    /// <exception cref="NotSupportedException">
    /// Thrown when <paramref name="value"/> cannot be represented by the runtime state serializer.
    /// </exception>
    public abstract void Set<T>(string key, T value);
}
