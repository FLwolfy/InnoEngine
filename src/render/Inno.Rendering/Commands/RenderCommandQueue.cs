namespace Inno.Rendering;

/// <summary>
/// Represents a thread-safe high-level render command queue.
/// </summary>
public sealed class RenderCommandQueue
{
    private readonly Queue<Action> m_commands = new();
    private readonly object m_lock = new();

    public int count
    {
        get
        {
            lock (m_lock)
            {
                return m_commands.Count;
            }
        }
    }

    public void Enqueue(Action command)
    {
        ArgumentNullException.ThrowIfNull(command);
        lock (m_lock)
        {
            m_commands.Enqueue(command);
        }
    }

    public bool TryDequeue(out Action? command)
    {
        lock (m_lock)
        {
            if (m_commands.Count == 0)
            {
                command = null;
                return false;
            }

            command = m_commands.Dequeue();
            return true;
        }
    }

    public int ExecuteAll()
    {
        List<Action> commands;
        lock (m_lock)
        {
            commands = new List<Action>(m_commands);
            m_commands.Clear();
        }

        foreach (var command in commands)
        {
            command();
        }

        return commands.Count;
    }

    public void Clear()
    {
        lock (m_lock)
        {
            m_commands.Clear();
        }
    }
}
