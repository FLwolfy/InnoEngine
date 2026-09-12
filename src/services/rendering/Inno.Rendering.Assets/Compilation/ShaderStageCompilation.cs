using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Inno.Core.Diagnostics;
using Inno.Rendering.Shaders;

namespace Inno.Rendering.Assets;

/// <summary>Supplies typed stage semantics and frozen source modules to a target compiler.</summary>
/// <param name="stage">Typed stage with target-assigned interface/resource locations.</param>
/// <param name="target">Exact toolchain profile and capability snapshot.</param>
public sealed record ShaderStageToolRequest(ShaderIrStage stage, ShaderCompileTarget target);

/// <summary>Maps one logical binding to the adapter-generated native name, without exposing a GPU handle.</summary>
/// <param name="id">Stable logical binding identity from the typed stage.</param>
/// <param name="nativeName">Generated name used for reflection and runtime uniform binding.</param>
/// <param name="location">Assigned texture slot; uniform locations are resolved by the runtime.</param>
public sealed record ShaderStageBinding(string id, string nativeName, int location);

/// <summary>Returns a frozen typed-stage compilation candidate; failure never carries a usable artifact.</summary>
public sealed class ShaderStageToolResult
{
    private readonly byte[] m_bytes;

    /// <summary>Creates a candidate result from adapter-owned generation, compilation and diagnostics.</summary>
    /// <param name="bytes">Compiled binary, or empty after failure.</param>
    /// <param name="bindings">Logical-to-native resource layout used by this exact binary.</param>
    /// <param name="diagnostics">Structured adapter diagnostics with original source positions.</param>
    public ShaderStageToolResult(ReadOnlySpan<byte> bytes, IEnumerable<ShaderStageBinding> bindings,
        IEnumerable<ShaderSourceDiagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        ArgumentNullException.ThrowIfNull(diagnostics);
        this.bindings = Array.AsReadOnly(bindings.ToArray());
        this.diagnostics = Array.AsReadOnly(diagnostics.ToArray());
        if (!bytes.IsEmpty && this.diagnostics.Any(static value => value.severity == DiagnosticSeverity.Error))
            throw new ArgumentException("A failed candidate cannot publish compiled bytes.", nameof(bytes));
        m_bytes = bytes.ToArray();
    }

    /// <summary>Gets immutable target binary data, empty on failure.</summary>
    public ReadOnlyMemory<byte> bytes => m_bytes;
    /// <summary>Gets the exact generated logical-to-native resource layout.</summary>
    public IReadOnlyList<ShaderStageBinding> bindings { get; }
    /// <summary>Gets generation and native compiler diagnostics.</summary>
    public IReadOnlyList<ShaderSourceDiagnostic> diagnostics { get; }
    /// <summary>Gets whether a nonempty binary was compiled without error diagnostics.</summary>
    public bool succeeded => !bytes.IsEmpty && diagnostics.All(static value => value.severity != DiagnosticSeverity.Error);
}

public sealed partial class ShaderCompiler
{
    private readonly object m_stageCacheLock = new();
    private readonly Dictionary<(string stage, string target, GraphicsCapabilities capabilities), ShaderStageToolResult> m_stageCache = [];
    private long m_stageCacheBytes;
    /// <summary>Compiles a typed stage through the configured adapter without an intermediate complete-source authoring asset.</summary>
    /// <param name="stage">Typed stage and frozen function modules.</param>
    /// <param name="target">Configured target capability/profile snapshot.</param>
    /// <param name="cancellationToken">Cancellation before publishing the candidate.</param>
    /// <returns>Compiled stage bytes, exact generated binding names and structured diagnostics.</returns>
    public async ValueTask<ShaderStageToolResult> CompileAsync(ShaderIrStage stage, ShaderCompileTarget target,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stage);
        ArgumentNullException.ThrowIfNull(target);
        cancellationToken.ThrowIfCancellationRequested();
        var key = (stage.contentHash, target.key, target.capabilities);
        lock (m_stageCacheLock)
            if (m_stageCache.TryGetValue(key, out ShaderStageToolResult? previous)) return previous;
        ShaderStageToolResult result = await m_toolchain.CompileAsync(new ShaderStageToolRequest(stage, target), cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        // The bounded cache contains immutable backend results only, never IR, providers or source assets.
        // Failed and cancelled requests are not cached, and layout-only graph edits reuse the semantic key.
        if (result.succeeded && result.bytes.Length <= 32 * 1024 * 1024)
            lock (m_stageCacheLock)
            {
                if (m_stageCache.Count >= 256 || m_stageCacheBytes + result.bytes.Length > 32 * 1024 * 1024)
                { m_stageCache.Clear(); m_stageCacheBytes = 0; }
                if (m_stageCache.TryAdd(key, result)) m_stageCacheBytes += result.bytes.Length;
            }
        return result;
    }
}
