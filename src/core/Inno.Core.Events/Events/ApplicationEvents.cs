namespace Inno.Core.Events;

/// <summary>
/// Base class for application lifecycle events.
/// </summary>
public abstract class ApplicationEvent : Event
{
}

/// <summary>
/// Raised when the application requests shutdown.
/// </summary>
public class ApplicationQuitEvent : ApplicationEvent
{
}
