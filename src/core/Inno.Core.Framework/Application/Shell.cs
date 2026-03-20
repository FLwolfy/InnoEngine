using System;
using System.Diagnostics;
using Inno.Core.Coroutines;
using Inno.Core.Events;
using Inno.Core.Logging;
using Inno.Core.Reflection;

namespace Inno.Core.Framework;

public sealed class Shell : IDisposable
{
    private Action? m_onLoad;
    private Action? m_onSetup;
    private Action? m_onStep;
    private Action? m_onDraw;
    private Action? m_onClose;

    private readonly Stopwatch m_timer;
    private readonly EventDispatcher m_events;
    private readonly CoroutineScheduler m_coroutines;
    private readonly LayerStack m_layers;

    private double m_lastTime;
    private bool m_isRunning;
    private bool m_disposed;

    public void SetOnLoad(Action onLoad) => m_onLoad = onLoad;
    public void SetOnSetup(Action onSetup) => m_onSetup = onSetup;
    public void SetOnStep(Action onStep) => m_onStep = onStep;
    public void SetOnDraw(Action onDraw) => m_onDraw = onDraw;
    public void SetOnClose(Action onClose) => m_onClose = onClose;

    public EventDispatcher eventDispatcher => m_events;
    public CoroutineScheduler coroutineScheduler => m_coroutines;
    public LayerStack layerStack => m_layers;

    public Shell()
    {
        m_timer = new Stopwatch();
        m_events = new EventDispatcher();
        m_coroutines = new CoroutineScheduler();
        m_layers = new LayerStack(() => m_events.CreateHub());
        
        LogManager.RegisterSink(new ConsoleLogSink());
        TypeCacheManager.Initialize();
        LogManager.Initialize();
    }

    public void Run()
    {
        ObjectDisposedException.ThrowIf(m_disposed, this);
        if (m_isRunning) return;
        m_isRunning = true;
            
        m_onLoad?.Invoke();
        m_onSetup?.Invoke();

        m_timer.Start();
        m_lastTime = 0.0;

        try
        {
            while (m_isRunning)
            {
                double now = m_timer.Elapsed.TotalSeconds;
                float delta = (float)(now - m_lastTime);
                m_lastTime = now;

                m_events.Flush();

                Time.Update((float)now, delta);
                m_coroutines.Tick(delta);
                m_onStep?.Invoke();
                m_layers.OnUpdate(delta);

                Time.RenderUpdate(delta);
                m_onDraw?.Invoke();
                m_layers.OnRender(Time.renderDeltaTime);
            }
        }
        finally
        {
            m_onClose?.Invoke();
            Dispose();
        }
    }

    public void Terminate()
    {
        m_isRunning = false;
    }

    public void Dispose()
    {
        if (m_disposed)
        {
            return;
        }

        m_disposed = true;
        m_isRunning = false;

        m_layers.Dispose();
        m_coroutines.Dispose();
        LogManager.Shutdown();
    }
}
