using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Threading;

using Inno.Core.Logging;

using Xunit;

namespace Inno.Core.Logging.Tests;

[CollectionDefinition("Logging", DisableParallelization = true)]
public sealed class LoggingCollection;

[Collection("Logging")]
public sealed class LoggingBehaviorTests : IDisposable
{
    private readonly LogRouter m_router = new();
    private readonly IDisposable m_scope;

    public LoggingBehaviorTests()
    {
        m_scope = m_router.EnterScope();
        m_router.SetMinimumLevel(LogLevel.Debug);
    }

    [Fact]
    public void LogRouter_DispatchesToRegisteredSink()
    {
        using var sink = new ProbeSink();
        m_router.RegisterSink(sink);

        Log.Info("message-{0}", 42);

        Assert.True(sink.WaitForCount(1, TimeSpan.FromSeconds(2)));
        Assert.Contains(sink.entries, e => e.message == "message-42" && e.level == LogLevel.Info);
        m_router.UnregisterSink(sink);
    }

    [Fact]
    public void LogRouter_RespectsMinimumLevel()
    {
        using var sink = new ProbeSink();
        m_router.RegisterSink(sink);
        m_router.SetMinimumLevel(LogLevel.Warn);

        Log.Info("suppressed");
        Log.Warn("visible");

        Assert.True(sink.WaitForCount(1, TimeSpan.FromSeconds(2)));
        Assert.Single(sink.entries);
        Assert.Equal(LogLevel.Warn, sink.entries[0].level);
        Assert.Equal("visible", sink.entries[0].message);
        m_router.UnregisterSink(sink);
    }

    [Fact]
    public void LogRouterFlushDeliversEveryPreviouslyQueuedEntry()
    {
        using var sink = new ProbeSink();
        m_router.RegisterSink(sink);

        Log.Info("before-flush-1");
        Log.Info("before-flush-2");
        m_router.Flush();

        Assert.Contains(sink.entries, static entry => entry.message == "before-flush-1");
        Assert.Contains(sink.entries, static entry => entry.message == "before-flush-2");
        m_router.UnregisterSink(sink);
    }

    [Fact]
    public void LogRouterFlushCompletesWithoutRegisteredSinks()
    {
        Log.Info("discarded-before-flush");

        m_router.Flush();
    }

    [Fact]
    public void FailingSinkIsReportedQuarantinedAndDoesNotBlockHealthySinks()
    {
        var failing = new FailingSink();
        using var healthy = new ProbeSink();
        ILogSink? reportedSink = null;
        Exception? reportedFailure = null;
        m_router.sinkFailed += (sink, failure) =>
        {
            reportedSink = sink;
            reportedFailure = failure;
        };
        m_router.RegisterSink(failing);
        m_router.RegisterSink(healthy);

        Log.Info("first");
        Log.Info("second");
        m_router.Flush();

        Assert.Same(failing, reportedSink);
        Assert.IsType<InvalidOperationException>(reportedFailure);
        Assert.Equal(1, failing.receiveCount);
        Assert.Equal(["first", "second"], healthy.entries.Select(static entry => entry.message));
        m_router.UnregisterSink(healthy);
    }

    [Fact]
    public void FileLogSink_WritesEntriesAndRotates()
    {
        var dir = Path.Combine(Path.GetTempPath(), "InnoLoggingTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);

        using var sink = new FileLogSink(dir, maxFileSizeBytes: 256, maxFiles: 3);
        m_router.RegisterSink(sink);

        for (var i = 0; i < 80; i++)
            Log.Warn("line-{0}", i);

        m_router.Flush();
        m_router.UnregisterSink(sink);
        sink.Dispose();

        var files = Directory.GetFiles(dir, FileLogSink.C_LOG_FILE_PREFIX + "*.log");
        var combined = string.Join(Environment.NewLine, files.Select(File.ReadAllText));
        Assert.Contains("line-79", combined);
        Assert.True(files.Length <= 3);
        Assert.All(files, static file => Assert.Equal(".log", Path.GetExtension(file)));
    }

    public void Dispose()
    {
        m_scope.Dispose();
        m_router.Dispose();
    }

    private sealed class ProbeSink : ILogSink, IDisposable
    {
        private readonly ConcurrentQueue<LogEntry> m_entries = new();
        private readonly ManualResetEventSlim m_signal = new(initialState: false);

        public LogEntry[] entries => m_entries.ToArray();

        public void Receive(LogEntry entry)
        {
            m_entries.Enqueue(entry);
            m_signal.Set();
        }

        public bool WaitForCount(int count, TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                if (m_entries.Count >= count)
                    return true;

                m_signal.Wait(TimeSpan.FromMilliseconds(20));
                m_signal.Reset();
            }

            return m_entries.Count >= count;
        }

        public void Dispose()
        {
            m_signal.Dispose();
        }
    }

    private sealed class FailingSink : ILogSink
    {
        internal int receiveCount { get; private set; }

        public void Receive(LogEntry entry)
        {
            _ = entry;
            receiveCount++;
            throw new InvalidOperationException("Expected sink failure.");
        }
    }
}
