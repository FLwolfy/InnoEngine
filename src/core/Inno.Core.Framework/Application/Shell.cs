using System;
using System.IO;
using System.Threading;

using Inno.Assets;
using Inno.Core.Coroutines;
using Inno.Core.Events;
using Inno.Core.Job;
using Inno.Core.Identity;
using Inno.Core.Logging;
using Inno.Core.Reflection;
using Inno.Core.Serialization;

namespace Inno.Core.Framework;

/// <summary>
/// Engine runtime shell that advances one frame at a time via <see cref="Tick"/>.
/// </summary>
public sealed class Shell
{
    private const string DEFAULT_ASSET_DIRECTORY = "Assets";
    private const string DEFAULT_ARTIFACT_DIRECTORY = "Artifacts";
    private const string DEFAULT_LOG_DIRECTORY = "Logs";

    private static readonly Lock S_LIFECYCLE_LOCK = new();
    private static Shell? s_instance;

    private readonly EventDispatcher m_events;
    private readonly CoroutineScheduler m_coroutines;
    private readonly LayerStack m_layers;
    private readonly float m_fixedDeltaTime;
    private readonly float m_maxFrameDeltaTime;
    private readonly int m_maxUpdateStepsPerTick;

    private float m_fixedAccumulator;
    private bool m_disposed;

    /// <summary>
    /// Gets whether the singleton shell is initialized.
    /// </summary>
    public static bool isInitialized
    {
        get
        {
            lock (S_LIFECYCLE_LOCK)
            {
                return s_instance is not null;
            }
        }
    }

    /// <summary>
    /// Gets the initialized singleton shell.
    /// </summary>
    public static Shell instance
    {
        get
        {
            lock (S_LIFECYCLE_LOCK)
            {
                if (s_instance is null)
                    throw new InvalidOperationException("Shell is not initialized.");

                return s_instance;
            }
        }
    }

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

    private Shell(in ShellSettings settings)
    {
        var fixedDeltaTime = settings.fixedDeltaTime;
        if (fixedDeltaTime <= 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(settings.fixedDeltaTime), "fixedDeltaTime must be greater than zero.");
        }

        var maxFrameDeltaTime = settings.maxFrameDeltaTime;
        if (maxFrameDeltaTime <= 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(settings.maxFrameDeltaTime), "maxFrameDeltaTime must be greater than zero.");
        }

        var maxUpdateStepsPerTick = settings.maxUpdateStepsPerTick;
        if (maxUpdateStepsPerTick <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(settings.maxUpdateStepsPerTick), "maxUpdateStepsPerTick must be greater than zero.");
        }

        try
        {
            m_fixedDeltaTime = fixedDeltaTime;
            m_maxFrameDeltaTime = maxFrameDeltaTime;
            m_maxUpdateStepsPerTick = maxUpdateStepsPerTick;
            m_events = new EventDispatcher();
            m_coroutines = new CoroutineScheduler();
            m_layers = new LayerStack(() => m_events.CreateHub());
            
            IdentityManager.Initialize();
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
            LogManager.RegisterSink(new FileLogSink(Path.Combine(settings.projectRootDirectory, DEFAULT_LOG_DIRECTORY)));
            
            TypeCacheManager.Initialize();
            SerializationManager.Initialize();

            AssetManager.Initialize(AssetManagerOptions.Create(
                Path.Combine(settings.projectRootDirectory, DEFAULT_ASSET_DIRECTORY),
                Path.Combine(settings.projectRootDirectory, DEFAULT_ARTIFACT_DIRECTORY)
            ));
        }
        catch
        {
            AssetManager.Shutdown();
            SerializationManager.Shutdown();
            TypeCacheManager.Shutdown();
            LogManager.Shutdown();
            JobSystemManager.Shutdown();
            IdentityManager.Shutdown();
            throw;
        }
    }

    /// <summary>
    /// Initializes the singleton shell with settings.
    /// </summary>
    public static Shell Initialize(in ShellSettings settings)
    {
        lock (S_LIFECYCLE_LOCK)
        {
            if (s_instance is not null)
                throw new InvalidOperationException("Shell is already initialized.");

            s_instance = new Shell(settings);
            return s_instance;
        }
    }

    /// <summary>
    /// Shuts down the singleton shell if it is initialized.
    /// </summary>
    public static void Shutdown()
    {
        Shell? shell;
        lock (S_LIFECYCLE_LOCK)
        {
            shell = s_instance;
        }

        shell?.DisposeResources();
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
    
    private void DisposeResources()
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
            AssetManager.Shutdown();
            SerializationManager.Shutdown();
            TypeCacheManager.Shutdown();
            LogManager.Shutdown();
            JobSystemManager.Shutdown();
            IdentityManager.Shutdown();
        }
        finally
        {
            lock (S_LIFECYCLE_LOCK)
            {
                if (ReferenceEquals(s_instance, this))
                    s_instance = null;
            }
        }
    }
}
