using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Inno.Core.Coroutines;

/// <summary>
/// Thread-safe coroutine scheduler with start/stop APIs.
/// </summary>
public sealed class CoroutineScheduler : IDisposable
{
    private readonly ConcurrentQueue<Command> m_commands = new();
    private readonly ConcurrentDictionary<long, byte> m_liveIds = new();
    private readonly Dictionary<long, CoroutineState> m_states = new();
    private readonly List<long> m_active = new();
    private readonly Lock m_tickGate = new();
    private readonly WeakReference<CoroutineScheduler> m_selfRef;
    private long m_nextId;
    private int m_disposed;
    private double m_now;
    private ulong m_frame;

    public CoroutineScheduler()
    {
        m_selfRef = new WeakReference<CoroutineScheduler>(this);
    }

    /// <summary>
    /// Starts a coroutine.
    /// </summary>
    /// <param name="routine">Coroutine enumerator.</param>
    /// <returns>Coroutine handle.</returns>
    public CoroutineHandle StartCoroutine(IEnumerator routine)
    {
        return StartCoroutine(null, routine);
    }

    /// <summary>
    /// Starts a coroutine with an owner token.
    /// </summary>
    /// <param name="owner">Owner key used by <see cref="StopAllCoroutines(object)"/>.</param>
    /// <param name="routine">Coroutine enumerator.</param>
    /// <returns>Coroutine handle.</returns>
    public CoroutineHandle StartCoroutine(object? owner, IEnumerator routine)
    {
        ArgumentNullException.ThrowIfNull(routine);

        lock (m_tickGate)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref m_disposed) != 0, this);

            long id = Interlocked.Increment(ref m_nextId);
            m_liveIds[id] = 0;
            m_commands.Enqueue(Command.Start(id, owner, routine));
            return new CoroutineHandle(id, m_selfRef);
        }
    }

    /// <summary>
    /// Requests stopping a coroutine by handle.
    /// </summary>
    /// <param name="handle">Target coroutine handle.</param>
    /// <returns><see langword="true"/> if the handle is valid.</returns>
    public bool StopCoroutine(CoroutineHandle handle)
    {
        if (!handle.IsOwnedBy(this) || !handle.isValid || Volatile.Read(ref m_disposed) != 0)
        {
            return false;
        }

        m_commands.Enqueue(Command.Stop(handle.id));
        return true;
    }

    /// <summary>
    /// Requests stopping all coroutines owned by the specified owner token.
    /// </summary>
    /// <param name="owner">Owner key used when starting coroutines.</param>
    public void StopAllCoroutines(object owner)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref m_disposed) != 0, this);
        ArgumentNullException.ThrowIfNull(owner);
        m_commands.Enqueue(Command.StopByOwner(owner));
    }

    /// <summary>
    /// Requests stopping all active coroutines.
    /// </summary>
    public void StopAllCoroutines()
    {
        if (Volatile.Read(ref m_disposed) != 0)
        {
            return;
        }

        m_commands.Enqueue(Command.StopAll());
    }

    /// <summary>
    /// Disposes the scheduler and clears all active/pending coroutines.
    /// </summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref m_disposed, 1) != 0)
        {
            return;
        }

        lock (m_tickGate)
        {
            while (m_commands.TryDequeue(out _))
            {
            }

            m_states.Clear();
            m_active.Clear();
            m_liveIds.Clear();
        }
    }

    internal double now => m_now;
    internal ulong frame => m_frame;

    /// <summary>
    /// Advances coroutine execution by one frame.
    /// </summary>
    /// <param name="deltaTime">Frame delta time in seconds.</param>
    public void Tick(float deltaTime)
    {
        if (deltaTime < 0f)
        {
            deltaTime = 0f;
        }

        lock (m_tickGate)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref m_disposed) != 0, this);

            m_now += deltaTime;
            m_frame++;

            ApplyPendingCommands();
            TickActiveCoroutines();
        }
    }

    internal bool Contains(CoroutineHandle handle)
    {
        if (handle.IsOwnedBy(this))
        {
            return IsHandleValid(handle.id);
        }

        return handle.isValid;
    }

    internal bool IsHandleValid(long id)
    {
        return Volatile.Read(ref m_disposed) == 0 && id > 0 && m_liveIds.ContainsKey(id);
    }

    private void ApplyPendingCommands()
    {
        while (m_commands.TryDequeue(out Command cmd))
        {
            switch (cmd.kind)
            {
                case CommandKind.Start:
                    if (cmd.routine is null)
                    {
                        break;
                    }

                    CoroutineState state = new(cmd.id, cmd.owner, cmd.routine);
                    m_states[state.id] = state;
                    m_active.Add(state.id);
                    break;
                case CommandKind.Stop:
                    m_liveIds.TryRemove(cmd.id, out _);
                    RemoveCoroutine(cmd.id);
                    break;
                case CommandKind.StopByOwner:
                    if (cmd.owner is null)
                    {
                        break;
                    }

                    for (int i = m_active.Count - 1; i >= 0; i--)
                    {
                        long id = m_active[i];
                        if (!m_states.TryGetValue(id, out CoroutineState? st))
                        {
                            m_active.RemoveAt(i);
                            continue;
                        }

                        if (ReferenceEquals(st.owner, cmd.owner))
                        {
                            m_liveIds.TryRemove(id, out _);
                            RemoveAtActiveIndex(i, id);
                        }
                    }

                    break;
                case CommandKind.StopAll:
                    m_states.Clear();
                    m_active.Clear();
                    m_liveIds.Clear();
                    break;
            }
        }
    }

    private void TickActiveCoroutines()
    {
        for (int i = 0; i < m_active.Count;)
        {
            long id = m_active[i];
            if (!m_states.TryGetValue(id, out CoroutineState? state))
            {
                m_active.RemoveAt(i);
                continue;
            }

            bool finished = !StepCoroutine(state);
            if (!finished)
            {
                i++;
                continue;
            }

            RemoveAtActiveIndex(i, id);
        }
    }

    private bool StepCoroutine(CoroutineState state)
    {
        if (state.waiter is not null)
        {
            if (state.waiter.KeepWaiting(this))
            {
                return true;
            }

            state.waiter = null;
        }

        while (true)
        {
            if (state.stack.Count == 0)
            {
                return false;
            }

            IEnumerator current = state.stack.Peek();
            bool moved;
            try
            {
                moved = current.MoveNext();
            }
            catch
            {
                return false;
            }

            if (!moved)
            {
                state.stack.Pop();
                continue;
            }

            object? yielded = current.Current;
            if (yielded is null)
            {
                state.waiter = new NextFrameWaiter(m_frame + 1);
                return true;
            }

            if (yielded is IEnumerator nested)
            {
                state.stack.Push(nested);
                continue;
            }

            if (yielded is YieldInstruction instruction)
            {
                state.waiter = instruction.CreateWaiter(m_now, m_frame);
                return true;
            }

            if (yielded is Task task)
            {
                state.waiter = new WaitForTaskWaiter(task);
                return true;
            }

            if (yielded is CoroutineHandle handle)
            {
                state.waiter = new WaitForCoroutineWaiter(handle);
                return true;
            }

            return false;
        }
    }

    private void RemoveCoroutine(long id)
    {
        if (!m_states.Remove(id))
        {
            return;
        }

        for (int i = m_active.Count - 1; i >= 0; i--)
        {
            if (m_active[i] != id)
            {
                continue;
            }

            m_active.RemoveAt(i);
            break;
        }
    }

    private void RemoveAtActiveIndex(int activeIndex, long id)
    {
        m_states.Remove(id);
        m_liveIds.TryRemove(id, out _);
        m_active.RemoveAt(activeIndex);
    }

    private enum CommandKind
    {
        Start,
        Stop,
        StopByOwner,
        StopAll
    }

    private readonly struct Command(
        CommandKind kind,
        long id,
        object? owner,
        IEnumerator? routine)
    {
        public CommandKind kind { get; } = kind;
        public long id { get; } = id;
        public object? owner { get; } = owner;
        public IEnumerator? routine { get; } = routine;

        public static Command Start(long id, object? owner, IEnumerator routine)
            => new(CommandKind.Start, id, owner, routine);

        public static Command Stop(long id)
            => new(CommandKind.Stop, id, null, null);

        public static Command StopByOwner(object owner)
            => new(CommandKind.StopByOwner, 0, owner, null);

        public static Command StopAll()
            => new(CommandKind.StopAll, 0, null, null);
    }

    private sealed class CoroutineState(long id, object? owner, IEnumerator routine)
    {
        public long id { get; } = id;
        public object? owner { get; } = owner;
        public Stack<IEnumerator> stack { get; } = new([routine]);
        public ICoroutineWaiter? waiter { get; set; }
    }
}
