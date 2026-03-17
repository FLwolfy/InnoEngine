using System;
using System.Diagnostics;
using Inno.Core.Events;
using Inno.Core.Logging;
using Inno.Core.Reflection;

namespace Inno.Core.Framework;

public class Shell
{
    private Action? m_onLoad;
    private Action? m_onSetup;
    private Action? m_onStep;
    private Action? m_onDraw;
    private Action<EventDispatcher>? m_onEvent;
    private Action? m_onClose;

    private readonly Stopwatch m_timer;
    private readonly EventDispatcher m_eventDispatcher;
    
    private double m_lastTime;
    private bool m_isRunning;

    public void SetOnLoad(Action onLoad) => m_onLoad = onLoad;
    public void SetOnSetup(Action onSetup) => m_onSetup = onSetup;
    public void SetOnStep(Action onStep) => m_onStep = onStep;
    public void SetOnDraw(Action onDraw) => m_onDraw = onDraw;
    public void SetOnEvent(Action<EventDispatcher>? onEvent) => m_onEvent = onEvent;
    public void SetOnClose(Action onClose) => m_onClose = onClose;

    public Shell()
    {
        m_timer = new Stopwatch();
        m_eventDispatcher = new EventDispatcher();
        
        LogManager.RegisterSink(new ConsoleLogSink());
        TypeCacheManager.Initialize();
        LogManager.Initialize();
    }

    public void Run()
    {
        if (m_isRunning) return;
        m_isRunning = true;
            
        m_onLoad?.Invoke();
        m_onSetup?.Invoke();

        m_timer.Start();
        m_lastTime = 0.0;

        while (m_isRunning)
        {
            double now = m_timer.Elapsed.TotalSeconds;
            float delta = (float)(now - m_lastTime);
            m_lastTime = now;

            // Inputs
            m_onEvent?.Invoke(m_eventDispatcher);
            
            // Logic Step
            Time.Update((float)now, delta);
            m_onStep?.Invoke();
            
            // Render
            // TODO: This should probably be moved to different thread
            Time.RenderUpdate(delta);
            m_onDraw?.Invoke();
        }

        m_onClose?.Invoke();
        LogManager.Shutdown();
    }

    public void Terminate()
    {
        m_isRunning = false;
    }
}
