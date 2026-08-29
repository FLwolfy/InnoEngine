using System;

using Inno.Rendering.Core;

namespace Inno.Rendering;

/// <summary>Marks a reloadable provider that produces model-neutral render requests each frame.</summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class RenderRequestProviderExtensionAttribute : Attribute
{
    /// <summary>Creates a render request provider declaration.</summary>
    /// <param name="id">Globally stable provider identifier.</param>
    /// <param name="priority">Provider invocation priority; lower values run first.</param>
    public RenderRequestProviderExtensionAttribute(string id, int priority = 0)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        this.id = id;
        this.priority = priority;
    }

    /// <summary>Gets the globally stable provider identifier.</summary>
    public string id { get; }

    /// <summary>Gets the provider invocation priority.</summary>
    public int priority { get; }
}

/// <summary>Supplies frame timing, capabilities and the request sink to one provider invocation.</summary>
public sealed class RenderRequestProviderContext
{
    /// <summary>Creates a frame-scoped provider context.</summary>
    /// <param name="requests">Sink accepting requests for the current frame.</param>
    /// <param name="capabilities">Active backend-neutral capability snapshot.</param>
    /// <param name="frameIndex">Monotonic render frame index.</param>
    /// <param name="deltaTime">Elapsed frame time in seconds.</param>
    public RenderRequestProviderContext(
        IRenderRequestSink requests,
        GraphicsCapabilities capabilities,
        ulong frameIndex,
        float deltaTime)
    {
        this.requests = requests ?? throw new ArgumentNullException(nameof(requests));
        this.capabilities = capabilities ?? throw new ArgumentNullException(nameof(capabilities));
        this.frameIndex = frameIndex;
        this.deltaTime = deltaTime;
    }

    /// <summary>Gets the sink accepting requests for the current frame.</summary>
    public IRenderRequestSink requests { get; }

    /// <summary>Gets the active backend-neutral capability snapshot.</summary>
    public GraphicsCapabilities capabilities { get; }

    /// <summary>Gets the monotonic render frame index.</summary>
    public ulong frameIndex { get; }

    /// <summary>Gets the elapsed frame time in seconds.</summary>
    public float deltaTime { get; }
}

/// <summary>Produces arbitrary render requests without prescribing a scene or rendering model.</summary>
public abstract class RenderRequestProvider : IDisposable
{
    private bool m_disposed;

    /// <summary>Submits zero or more requests for the current frame.</summary>
    /// <param name="context">Frame-scoped provider context.</param>
    public abstract void Submit(RenderRequestProviderContext context);

    /// <summary>Releases generation-scoped provider state.</summary>
    public void Dispose()
    {
        if (m_disposed)
            return;
        m_disposed = true;
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>Releases managed generation-scoped state.</summary>
    /// <param name="disposing">Always true for explicit disposal.</param>
    protected virtual void Dispose(bool disposing) { }
}
