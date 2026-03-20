using System;
using System.Diagnostics;
using System.Threading;
using Inno.Core.Coroutines;
using Inno.Core.Events;
using Inno.Core.Logging;
using Inno.Core.Reflection;

namespace Inno.Core.Framework;

/// <summary>
/// Engine runtime shell that owns the core loop, event dispatcher, coroutine scheduler, and layer stack.
/// </summary>
public sealed class Shell : IDisposable
{
    // Global lifecycle.
    private static int s_isShellAlive;

    // Timing constants.
    private const float DEFAULT_FIXED_DELTA_TIME = 1f / 60f;
    private const float DEFAULT_MAX_FRAME_DELTA_TIME = 0.25f;

    // User callbacks.
    private Action? m_onLoad;
    private Action? m_onSetup;
    private Action? m_onFixedStep;
    private Action? m_onStep;
    private Action? m_onDraw;
    private Action? m_onClose;

    // Core subsystems.
    private readonly Stopwatch m_timer;
    private readonly EventDispatcher m_events;
    private readonly CoroutineScheduler m_coroutines;
    private readonly LayerStack m_layers;

    // Runtime settings.
    private readonly bool m_useBackgroundRenderThread;
    private readonly float m_fixedDeltaTime;
    private readonly int m_maxFrameRate;
    private readonly double m_minFrameDurationSeconds;

    // Render-thread infrastructure.
    private readonly AutoResetEvent? m_renderWakeEvent;
    private readonly Lock m_renderRequestGate = new();
    private readonly Stopwatch? m_renderTimer;
    private readonly Thread? m_renderThread;

    // Mutable runtime state.
    private double m_lastTime;
    private double m_lastRenderTime;
    private float m_fixedAccumulator;
    private float m_lastSubmittedRenderDelta;
    private bool m_isRunning;
    private bool m_disposed;
    private int m_renderThreadRunning;
    private int m_hasPendingRenderRequest;
    private Exception? m_renderThreadException;

    /// <summary>
    /// Sets a callback invoked once before setup when <see cref="Run"/> starts.
    /// </summary>
    /// <param name="onLoad">Load callback.</param>
    public void SetOnLoad(Action onLoad) => m_onLoad = onLoad;

    /// <summary>
    /// Sets a callback invoked once after load when <see cref="Run"/> starts.
    /// </summary>
    /// <param name="onSetup">Setup callback.</param>
    public void SetOnSetup(Action onSetup) => m_onSetup = onSetup;

    /// <summary>
    /// Sets the fixed-step callback, invoked at a fixed simulation interval.
    /// </summary>
    /// <param name="onFixedStep">Fixed-step callback.</param>
    public void SetOnFixedStep(Action onFixedStep) => m_onFixedStep = onFixedStep;

    /// <summary>
    /// Sets the per-frame update callback executed on the main loop thread.
    /// </summary>
    /// <param name="onStep">Frame update callback.</param>
    public void SetOnStep(Action onStep) => m_onStep = onStep;

    /// <summary>
    /// Sets the render callback.
    /// </summary>
    /// <param name="onDraw">
    /// Render callback. Runs on the main loop thread when <c>useBackgroundRenderThread</c> is <c>false</c>,
    /// or on the dedicated render thread when <c>useBackgroundRenderThread</c> is <c>true</c>.
    /// </param>
    public void SetOnDraw(Action onDraw) => m_onDraw = onDraw;

    /// <summary>
    /// Sets a callback invoked when the shell stops running.
    /// </summary>
    /// <param name="onClose">Shutdown callback.</param>
    public void SetOnClose(Action onClose) => m_onClose = onClose;

    /// <summary>
    /// Gets the engine-level event dispatcher.
    /// </summary>
    public EventDispatcher eventDispatcher => m_events;

    /// <summary>
    /// Gets the coroutine scheduler.
    /// </summary>
    public CoroutineScheduler coroutineScheduler => m_coroutines;

    /// <summary>
    /// Gets the layer stack.
    /// </summary>
    public LayerStack layerStack => m_layers;

    /// <summary>
    /// Creates a shell instance.
    /// </summary>
    /// <param name="fixedDeltaTime">Fixed simulation timestep in seconds.</param>
    /// <param name="useBackgroundRenderThread">
    /// Whether render callbacks run on a dedicated background thread using a fully async pipeline.
    /// </param>
    /// <param name="maxFrameRate">Maximum frame rate. Set to 0 to disable frame limiting.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="fixedDeltaTime"/> is not greater than zero.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="maxFrameRate"/> is negative.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when another live shell instance already exists.
    /// </exception>
    public Shell(
        float fixedDeltaTime = DEFAULT_FIXED_DELTA_TIME,
        bool useBackgroundRenderThread = false,
        int maxFrameRate = 0)
    {
        if (fixedDeltaTime <= 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(fixedDeltaTime), "fixedDeltaTime must be greater than zero.");
        }

        if (maxFrameRate < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxFrameRate), "maxFrameRate cannot be negative.");
        }

        if (Interlocked.CompareExchange(ref s_isShellAlive, 1, 0) != 0)
        {
            throw new InvalidOperationException("Only one Shell instance can exist at a time.");
        }

        try
        {
            m_fixedDeltaTime = fixedDeltaTime;
            m_useBackgroundRenderThread = useBackgroundRenderThread;
            m_maxFrameRate = maxFrameRate;
            m_minFrameDurationSeconds = m_maxFrameRate > 0 ? 1.0 / m_maxFrameRate : 0.0;
            m_timer = new Stopwatch();
            m_events = new EventDispatcher();
            m_coroutines = new CoroutineScheduler();
            m_layers = new LayerStack(() => m_events.CreateHub());

            if (m_useBackgroundRenderThread)
            {
                m_renderWakeEvent = new AutoResetEvent(false);
                m_renderTimer = new Stopwatch();
                m_renderThreadRunning = 1;
                m_renderThread = new Thread(RenderThreadLoop)
                {
                    IsBackground = true,
                    Name = "Inno.RenderThread"
                };
                m_renderThread.Start();
            }

            LogManager.RegisterSink(new ConsoleLogSink());
            TypeCacheManager.Initialize();
            LogManager.Initialize();
        }
        catch
        {
            Interlocked.Exchange(ref s_isShellAlive, 0);
            throw;
        }
    }

    /// <summary>
    /// Creates a shell instance from settings.
    /// </summary>
    /// <param name="settings">Shell settings.</param>
    public Shell(in ShellSettings settings)
        : this(
            fixedDeltaTime: settings.fixedDeltaTime > 0f ? settings.fixedDeltaTime : DEFAULT_FIXED_DELTA_TIME,
            useBackgroundRenderThread: settings.useBackgroundRenderThread,
            maxFrameRate: settings.maxFrameRate)
    {
    }

    /// <summary>
    /// Runs the main loop until <see cref="Terminate"/> is called.
    /// </summary>
    /// <exception cref="ObjectDisposedException">
    /// Thrown when the shell has already been disposed.
    /// </exception>
    public void Run()
    {
        ObjectDisposedException.ThrowIf(m_disposed, this);
        if (m_isRunning) return;
        m_isRunning = true;
            
        m_onLoad?.Invoke();
        m_onSetup?.Invoke();

        m_timer.Start();
        m_lastTime = 0.0;
        m_fixedAccumulator = 0f;

        try
        {
            while (m_isRunning)
            {
                double now = m_timer.Elapsed.TotalSeconds;
                double frameStartTime = now;
                float delta = (float)(now - m_lastTime);
                m_lastTime = now;
                if (delta < 0f)
                {
                    delta = 0f;
                }

                if (delta > DEFAULT_MAX_FRAME_DELTA_TIME)
                {
                    delta = DEFAULT_MAX_FRAME_DELTA_TIME;
                }

                m_events.Flush();
                ThrowIfRenderThreadFaulted();

                Time.Update((float)now, delta);
                m_coroutines.Tick(delta);

                m_fixedAccumulator += delta;
                while (m_fixedAccumulator >= m_fixedDeltaTime)
                {
                    Time.FixedUpdate(m_fixedDeltaTime);
                    m_onFixedStep?.Invoke();
                    m_layers.OnFixedUpdate(m_fixedDeltaTime);
                    m_fixedAccumulator -= m_fixedDeltaTime;
                }

                m_onStep?.Invoke();
                m_layers.OnUpdate(delta);

                if (m_useBackgroundRenderThread)
                {
                    DispatchRender(delta);
                }
                else
                {
                    Time.RenderUpdate(delta);
                    DispatchRender(Time.renderDeltaTime);
                }

                ThrottleFrameRate(frameStartTime);
            }
        }
        finally
        {
            m_onClose?.Invoke();
            Dispose();
        }
    }

    /// <summary>
    /// Requests the running main loop to stop.
    /// </summary>
    public void Terminate()
    {
        m_isRunning = false;
    }

    /// <summary>
    /// Disposes runtime resources and releases the shell singleton guard.
    /// </summary>
    public void Dispose()
    {
        if (m_disposed)
        {
            return;
        }

        m_disposed = true;
        m_isRunning = false;

        try
        {
            if (m_useBackgroundRenderThread)
            {
                Interlocked.Exchange(ref m_renderThreadRunning, 0);
                m_renderWakeEvent?.Set();
                m_renderThread?.Join();
            }

            m_layers.Dispose();
            m_coroutines.Dispose();
            LogManager.Shutdown();
        }
        finally
        {
            Interlocked.Exchange(ref s_isShellAlive, 0);
        }
    }

    private void DispatchRender(float renderDeltaTime)
    {
        if (!m_useBackgroundRenderThread)
        {
            m_onDraw?.Invoke();
            m_layers.OnRender(renderDeltaTime);
            return;
        }

        lock (m_renderRequestGate)
        {
            m_lastSubmittedRenderDelta = renderDeltaTime;
            Volatile.Write(ref m_hasPendingRenderRequest, 1);
        }

        (m_renderWakeEvent ?? throw new InvalidOperationException("Render wake event is not initialized.")).Set();
    }

    private void RenderThreadLoop()
    {
        AutoResetEvent wake = m_renderWakeEvent ?? throw new InvalidOperationException("Render wake event is not initialized.");

        while (Volatile.Read(ref m_renderThreadRunning) != 0 || Volatile.Read(ref m_hasPendingRenderRequest) != 0)
        {
            wake.WaitOne();

            while (Volatile.Read(ref m_hasPendingRenderRequest) != 0)
            {
                lock (m_renderRequestGate)
                {
                    if (Volatile.Read(ref m_hasPendingRenderRequest) == 0)
                    {
                        break;
                    }

                    Volatile.Write(ref m_hasPendingRenderRequest, 0);
                }

                try
                {
                    Stopwatch timer = m_renderTimer ?? throw new InvalidOperationException("Render timer is not initialized.");
                    if (!timer.IsRunning)
                    {
                        timer.Start();
                        m_lastRenderTime = timer.Elapsed.TotalSeconds;
                    }

                    double now = timer.Elapsed.TotalSeconds;
                    float renderDelta = (float)(now - m_lastRenderTime);
                    m_lastRenderTime = now;
                    if (renderDelta <= 0f)
                    {
                        renderDelta = m_lastSubmittedRenderDelta;
                    }

                    if (renderDelta < 0f)
                    {
                        renderDelta = 0f;
                    }

                    Time.RenderUpdate(renderDelta);
                    m_onDraw?.Invoke();
                    m_layers.OnRender(Time.renderDeltaTime);
                }
                catch (Exception ex)
                {
                    Interlocked.CompareExchange(ref m_renderThreadException, ex, null);
                    Interlocked.Exchange(ref m_renderThreadRunning, 0);
                    return;
                }
            }
        }
    }

    private void ThrowIfRenderThreadFaulted()
    {
        Exception? renderThreadException = Volatile.Read(ref m_renderThreadException);
        if (renderThreadException is null)
        {
            return;
        }

        throw new InvalidOperationException("Render thread failed.", renderThreadException);
    }

    private void ThrottleFrameRate(double frameStartTime)
    {
        if (m_maxFrameRate <= 0)
        {
            return;
        }

        double frameEndTarget = frameStartTime + m_minFrameDurationSeconds;
        while (m_isRunning)
        {
            double now = m_timer.Elapsed.TotalSeconds;
            double remaining = frameEndTarget - now;
            if (remaining <= 0.0)
            {
                return;
            }

            if (remaining > 0.002)
            {
                int sleepMs = (int)(remaining * 1000.0) - 1;
                if (sleepMs > 0)
                {
                    Thread.Sleep(sleepMs);
                    continue;
                }
            }

            Thread.SpinWait(100);
        }
    }
}
