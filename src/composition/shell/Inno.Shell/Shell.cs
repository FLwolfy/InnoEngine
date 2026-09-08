using System;
using System.Collections.Generic;
using System.Diagnostics;
using Inno.Adapter;
using Inno.Adapter.Input;
using Inno.Adapter.Rendering;
using Inno.Core.Events;
using Inno.Core.Execution;
using Inno.Platform;
using Inno.Rendering;
using Inno.Runtime;
using Inno.Runtime.Contracts;

namespace Inno.Shell;

/// <summary>
/// Owns the backend-neutral window, event pump, input source, rendering device, and host frame lifecycle.
/// </summary>
public abstract class Shell : IDisposable
{
    private readonly IAdapterCatalog m_adapterCatalog;
    private readonly AdapterSelection m_adapterSelection;
    private IPlatformApplication? m_platformApplication;
    private IPlatformWindow? m_primaryWindow;
    private IInputEventSource? m_inputSource;
    private IRenderDevice? m_renderDevice;
    private RuntimeSubsystemPipeline? m_subsystems;
    private bool m_exitRequested;
    private bool m_hasRun;
    private bool m_stoppingNotified;
    private bool m_disposed;
    private readonly RetirementBarrier m_retirement = new("Composition Shell");

    /// <summary>
    /// Creates the backend-neutral host resources shared by Player and Editor products.
    /// </summary>
    /// <param name="adapterCatalog">
    /// Catalog that resolves explicit backend selections without leaking implementation types.
    /// </param>
    /// <param name="options">
    /// Primary-window and rendering policy.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="adapterCatalog"/> or <paramref name="options"/> is null.
    /// </exception>
    protected Shell(IAdapterCatalog adapterCatalog, ShellOptions options)
    {
        m_adapterCatalog = adapterCatalog ?? throw new ArgumentNullException(nameof(adapterCatalog));
        ArgumentNullException.ThrowIfNull(options);
        m_adapterSelection = options.adapters;
        this.options = options;
    }

    /// <summary>
    /// Gets the implementation-neutral adapter catalog used by this product host.
    /// </summary>
    protected IAdapterCatalog adapters => m_adapterCatalog;

    /// <summary>
    /// Gets the immutable backend selection used by this product host.
    /// </summary>
    protected AdapterSelection adapterSelection => m_adapterSelection;

    /// <summary>
    /// Gets the active backend-neutral platform application.
    /// </summary>
    protected IPlatformApplication platformApplication
        => m_platformApplication
           ?? throw new InvalidOperationException("The composition shell adapter resources are not initialized.");

    /// <summary>
    /// Gets the active primary platform window.
    /// </summary>
    protected IPlatformWindow primaryWindow
        => m_primaryWindow
           ?? throw new InvalidOperationException("The composition shell adapter resources are not initialized.");

    /// <summary>
    /// Gets the shared input event source used to create isolated runtime-session backends.
    /// </summary>
    protected IInputEventSource inputSource
        => m_inputSource
           ?? throw new InvalidOperationException("The composition shell adapter resources are not initialized.");

    /// <summary>
    /// Gets the active backend-neutral rendering device.
    /// </summary>
    protected IRenderDevice renderDevice
        => m_renderDevice
           ?? throw new InvalidOperationException("The composition shell adapter resources are not initialized.");

    /// <summary>
    /// Gets the immutable shell creation options.
    /// </summary>
    protected ShellOptions options { get; }

    /// <summary>
    /// Gets whether at least one complete product frame has run.
    /// </summary>
    protected bool hasCompletedFrame { get; private set; }

    /// <summary>
    /// Borrows the EngineHost-owned pipeline used for application-wide output and services.
    /// </summary>
    /// <param name="pipeline">
    /// The started pipeline; its EngineHost remains responsible for retirement.
    /// </param>
    /// <exception cref="InvalidOperationException">
    /// A host pipeline is already configured.
    /// </exception>
    protected void UseHostPipeline(RuntimeSubsystemPipeline pipeline)
    {
        ArgumentNullException.ThrowIfNull(pipeline);
        if (m_subsystems is not null)
            throw new InvalidOperationException("The Shell already has a host subsystem pipeline.");
        m_subsystems = pipeline;
    }

    /// <summary>
    /// Creates common adapter resources after a derived product has prepared its non-window bootstrap state.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when common adapter resources are already initialized.
    /// </exception>
    protected void InitializeAdapterResources()
    {
        ObjectDisposedException.ThrowIf(m_disposed, this);
        if (m_platformApplication is not null)
            throw new InvalidOperationException("The composition shell adapter resources are already initialized.");

        try
        {
            m_platformApplication = m_adapterCatalog.platform.CreateApplication(m_adapterSelection.platform);
            m_primaryWindow = m_platformApplication.CreateWindow(options.window);
            m_inputSource = m_adapterCatalog.input.CreateEventSource(m_adapterSelection.input, m_primaryWindow);
            m_renderDevice = m_adapterCatalog.rendering.CreateDevice(
                m_adapterSelection.rendering,
                new RenderingBackendOptions
                {
                    window = m_primaryWindow,
                    preferredGraphicsApi = options.preferredGraphicsApi,
                    verticalSync = options.verticalSync,
                    sRgbBackbuffer = options.sRgbBackbuffer,
                    forceSingleThreaded = options.forceSingleThreadedRendering
                });
        }
        catch (Exception failure)
        {
            List<Exception>? failures = null;
            if (m_renderDevice is not null)
                Release(m_renderDevice.Dispose, ref failures);
            m_renderDevice = null;
            if (m_inputSource is not null)
                Release(m_inputSource.Dispose, ref failures);
            m_inputSource = null;
            if (m_primaryWindow is not null)
                Release(m_primaryWindow.Dispose, ref failures);
            m_primaryWindow = null;
            if (m_platformApplication is not null)
                Release(m_platformApplication.Dispose, ref failures);
            m_platformApplication = null;
            if (failures is not null)
            {
                failures.Insert(0, failure);
                throw new AggregateException("Adapter initialization and rollback failed.", failures);
            }
            throw;
        }
    }

    /// <summary>
    /// Executes the common event and frame loop until the product or primary window requests exit.
    /// </summary>
    /// <param name="smokeFrameLimit">
    /// Optional positive frame count used by native smoke tests; <see langword="null"/> runs interactively.
    /// </param>
    /// <returns>
    /// Zero after an orderly product shutdown.
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="smokeFrameLimit"/> is not positive.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when this shell has already run.
    /// </exception>
    public int Run(int? smokeFrameLimit = null)
    {
        ObjectDisposedException.ThrowIf(m_disposed, this);
        if (m_platformApplication is null || m_primaryWindow is null ||
            m_inputSource is null || m_renderDevice is null)
        {
            throw new InvalidOperationException("The composition shell adapter resources are not initialized.");
        }
        if (smokeFrameLimit is <= 0)
            throw new ArgumentOutOfRangeException(nameof(smokeFrameLimit));
        if (m_hasRun)
            throw new InvalidOperationException("A composition shell can run only once.");
        m_hasRun = true;

        Stopwatch timer = Stopwatch.StartNew();
        double previousTime = 0d;
        int frameCount = 0;
        try
        {
            OnStarting();
            while (!m_exitRequested && !primaryWindow.isClosed)
            {
                PumpEvents();
                if (m_exitRequested || primaryWindow.isClosed)
                    break;

                double totalTime = timer.Elapsed.TotalSeconds;
                float deltaTime = Math.Max(0f, (float)(totalTime - previousTime));
                var frame = new ShellFrame(frameCount, totalTime, deltaTime);
                var runtimeFrame = new RuntimeFrame(frameCount, (float)totalTime, (float)totalTime,
                    deltaTime, deltaTime, 1f, false);
                try
                {
                    m_subsystems?.BeginFrame(runtimeFrame);
                    OnFrame(frame);
                    m_subsystems?.Update(runtimeFrame);
                    m_subsystems?.LateUpdate(runtimeFrame);
                    if (m_subsystems is not null)
                        m_subsystems.RenderFrame(runtimeFrame, () => OnPresentation(frame));
                    else
                        OnPresentation(frame);
                }
                finally
                {
                    m_subsystems?.EndFrame(runtimeFrame);
                }
                previousTime = totalTime;
                frameCount++;
                hasCompletedFrame = true;
                if (smokeFrameLimit.HasValue && frameCount >= smokeFrameLimit.Value)
                    RequestExit();
            }
            if (smokeFrameLimit.HasValue)
                OnSmokeCompleted(frameCount);
            return 0;
        }
        finally
        {
            NotifyStopping();
        }
    }

    /// <summary>
    /// Requests an orderly exit after the current event or frame callback completes.
    /// </summary>
    protected void RequestExit() => m_exitRequested = true;

    /// <summary>
    /// Runs once immediately before the common event and frame loop starts.
    /// </summary>
    protected virtual void OnStarting()
    {
    }

    /// <summary>
    /// Receives one backend-neutral platform event after shared input routing and close evaluation.
    /// </summary>
    /// <param name="evnt">
    /// Event produced by the active platform adapter.
    /// </param>
    protected virtual void OnEvent(Event evnt)
    {
    }

    /// <summary>
    /// Determines whether one event requests orderly product shutdown.
    /// </summary>
    /// <param name="evnt">
    /// Event produced by the active platform adapter.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the common loop should stop.
    /// </returns>
    protected virtual bool ShouldExit(Event evnt)
        => evnt is ApplicationQuitEvent
           || evnt is WindowCloseEvent close && close.windowId == primaryWindow.windowId;

    /// <summary>
    /// Advances one product-specific frame after all pending platform events have been dispatched.
    /// </summary>
    /// <param name="frame">
    /// Immutable timing and identity for the current shell frame.
    /// </param>
    protected abstract void OnFrame(ShellFrame frame);

    /// <summary>
    /// Submits product UI requests while the host output pipeline is open.
    /// </summary>
    /// <param name="frame">
    /// The current application frame.
    /// </param>
    protected virtual void OnPresentation(ShellFrame frame) { }

    /// <summary>
    /// Receives the final frame count when a bounded smoke run completes.
    /// </summary>
    /// <param name="frameCount">
    /// Number of product frames completed by the smoke run.
    /// </param>
    protected virtual void OnSmokeCompleted(int frameCount)
    {
    }

    /// <summary>
    /// Runs once when the main loop is stopping while all product and adapter resources remain alive.
    /// </summary>
    protected virtual void OnStopping()
    {
    }

    /// <summary>
    /// Releases resources owned by the derived product before common adapter resources are destroyed.
    /// </summary>
    protected virtual void DisposeProductResources()
    {
    }

    /// <summary>
    /// Releases product resources followed by rendering, input, window, and platform resources.
    /// </summary>
    public void Dispose()
    {
        if (m_disposed)
            return;
        List<Exception>? failures = null;
        Release(NotifyStopping, ref failures);
        Release(RetireProductResources, ref failures);
        if (m_renderDevice is not null)
            Release(m_renderDevice.Dispose, ref failures);
        if (m_inputSource is not null)
            Release(m_inputSource.Dispose, ref failures);
        if (m_primaryWindow is not null)
            Release(m_primaryWindow.Dispose, ref failures);
        if (m_platformApplication is not null)
            Release(m_platformApplication.Dispose, ref failures);
        m_disposed = true;
        if (failures is not null)
            throw new AggregateException("One or more composition-shell resources could not be released.", failures);
        GC.SuppressFinalize(this);
    }

    private void PumpEvents()
    {
        while (platformApplication.PollEvent(out Event? evnt))
        {
            if (evnt is null)
                continue;
            inputSource.ProcessEvent(evnt);
            if (evnt is WindowResizeEvent resize && resize.windowId == primaryWindow.windowId)
                renderDevice.ResizeBackbuffer(primaryWindow.pixelWidth, primaryWindow.pixelHeight);
            if (ShouldExit(evnt))
            {
                RequestExit();
                break;
            }
            OnEvent(evnt);
        }
    }

    private void RetireProductResources()
    {
        m_retirement.Wait(DisposeProductResources);
    }

    private void NotifyStopping()
    {
        if (m_stoppingNotified)
            return;
        m_stoppingNotified = true;
        OnStopping();
    }

    private static void Release(Action release, ref List<Exception>? failures)
    {
        try
        {
            release();
        }
        catch (Exception pendingRetirement) when (RetirementPendingException.Find(pendingRetirement) is not null) { throw; }
        catch (Exception exception)
        {
            failures ??= [];
            failures.Add(exception);
        }
    }
}
