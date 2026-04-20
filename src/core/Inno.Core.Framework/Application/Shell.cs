using System;
using System.Threading;

using Inno.Core.Coroutines;
using Inno.Core.Events;
using Inno.Core.Job;
using Inno.Core.Logging;
using Inno.Core.Reflection;

namespace Inno.Core.Framework;

/// <summary>
/// Engine runtime shell that advances one frame at a time via <see cref="Tick"/>.
/// </summary>
public sealed class Shell : IDisposable
{
    private static int s_isShellAlive;
    private const float DEFAULT_FIXED_DELTA_TIME = 1f / 60f;
    private const float DEFAULT_MAX_FRAME_DELTA_TIME = 0.25f;
    private const int DEFAULT_MAX_UPDATE_STEPS_PER_TICK = 8;

    private readonly EventDispatcher m_events;
    private readonly CoroutineScheduler m_coroutines;
    private readonly LayerStack m_layers;
    private readonly float m_fixedDeltaTime;
    private readonly float m_maxFrameDeltaTime;
    private readonly int m_maxUpdateStepsPerTick;

    private float m_fixedAccumulator;
    private bool m_disposed;

    /// <summary>
    /// Gets the event dispatcher owned by this shell.
    /// </summary>
    public EventDispatcher eventDispatcher => m_events;

    /// <summary>
    /// Gets the coroutine scheduler owned by this shell.
    /// </summary>
    public CoroutineScheduler coroutineScheduler => m_coroutines;

    /// <summary>
    /// Gets the layer stack.
    /// </summary>
    public LayerStack layerStack => m_layers;

    /// <summary>
    /// Creates a shell with explicit fixed-step interval.
    /// </summary>
    public Shell(float fixedDeltaTime = DEFAULT_FIXED_DELTA_TIME)
        : this(new ShellSettings
        {
            fixedDeltaTime = fixedDeltaTime,
            maxFrameDeltaTime = DEFAULT_MAX_FRAME_DELTA_TIME,
            maxUpdateStepsPerTick = DEFAULT_MAX_UPDATE_STEPS_PER_TICK,
            useSingleThreadJobSystem = false,
            jobWorkerCount = 0
        })
    {
    }

    /// <summary>
    /// Creates a shell with settings.
    /// </summary>
    public Shell(in ShellSettings settings)
    {
        var fixedDeltaTime = settings.fixedDeltaTime > 0f ? settings.fixedDeltaTime : DEFAULT_FIXED_DELTA_TIME;
        if (fixedDeltaTime <= 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(settings.fixedDeltaTime), "fixedDeltaTime must be greater than zero.");
        }

        var maxFrameDeltaTime = settings.maxFrameDeltaTime > 0f ? settings.maxFrameDeltaTime : DEFAULT_MAX_FRAME_DELTA_TIME;
        if (maxFrameDeltaTime <= 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(settings.maxFrameDeltaTime), "maxFrameDeltaTime must be greater than zero.");
        }

        var maxUpdateStepsPerTick = settings.maxUpdateStepsPerTick > 0
            ? settings.maxUpdateStepsPerTick
            : DEFAULT_MAX_UPDATE_STEPS_PER_TICK;
        if (maxUpdateStepsPerTick <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(settings.maxUpdateStepsPerTick), "maxUpdateStepsPerTick must be greater than zero.");
        }

        if (Interlocked.CompareExchange(ref s_isShellAlive, 1, 0) != 0)
        {
            throw new InvalidOperationException("Only one Shell instance can exist at a time.");
        }

        try
        {
            m_fixedDeltaTime = fixedDeltaTime;
            m_maxFrameDeltaTime = maxFrameDeltaTime;
            m_maxUpdateStepsPerTick = maxUpdateStepsPerTick;
            m_events = new EventDispatcher();
            m_coroutines = new CoroutineScheduler();
            m_layers = new LayerStack(() => m_events.CreateHub());
            
            JobSystemManager.Initialize();
            JobSystemManager.SetJobSystem(settings.useSingleThreadJobSystem
                ? new SingleThreadJobSystem()
                : new WorkStealingJobSystem(new JobSystemOptions
                {
                    workerCount = settings.jobWorkerCount
                })
            );

            LogManager.Initialize();
            LogManager.RegisterSink(new ConsoleLogSink());
            
            TypeCacheManager.Initialize();
        }
        catch
        {
            JobSystemManager.Shutdown();
            Interlocked.Exchange(ref s_isShellAlive, 0);
            throw;
        }
    }

    /// <summary>
    /// Advances the shell by one frame.
    /// </summary>
    /// <param name="totalTime">Absolute runtime time in seconds.</param>
    /// <param name="deltaTime">Frame delta in seconds.</param>
    public void Tick(float totalTime, float deltaTime)
    {
        ObjectDisposedException.ThrowIf(m_disposed, this);

        var delta = deltaTime < 0f ? 0f : deltaTime;
        if (delta > m_maxFrameDeltaTime)
        {
            delta = m_maxFrameDeltaTime;
        }

        JobSystemManager.current.BeginFrame();

        try
        {
            m_events.Flush();
            Time.Update(totalTime, delta);
            m_coroutines.Tick(delta);

            m_fixedAccumulator += delta;
            var updateSteps = 0;
            while (m_fixedAccumulator >= m_fixedDeltaTime && updateSteps < m_maxUpdateStepsPerTick)
            {
                Time.FixedUpdate(m_fixedDeltaTime);
                m_layers.OnFixedUpdate(m_fixedDeltaTime);
                m_fixedAccumulator -= m_fixedDeltaTime;
                updateSteps++;
            }

            if (updateSteps == m_maxUpdateStepsPerTick && m_fixedAccumulator >= m_fixedDeltaTime)
            {
                // Avoid spiral-of-death behavior on long stalls by dropping stale simulation debt.
                m_fixedAccumulator = 0f;
            }

            m_layers.OnUpdate(delta);
            m_layers.OnLateUpdate(delta);
        }
        finally
        {
            try
            {
                JobSystemManager.current.EndFrame();
            }
            finally
            {
                JobSystemManager.current.DrainMainThreadQueue();
            }
        }
    }

    /// <summary>
    /// Releases shell resources.
    /// </summary>
    public void Dispose()
    {
        if (m_disposed)
        {
            return;
        }

        m_disposed = true;
        try
        {
            m_layers.Dispose();
            m_coroutines.Dispose();
            JobSystemManager.Shutdown();
            LogManager.Shutdown();
        }
        finally
        {
            Interlocked.Exchange(ref s_isShellAlive, 0);
        }
    }
}
