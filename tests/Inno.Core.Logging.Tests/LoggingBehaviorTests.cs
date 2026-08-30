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
    public LoggingBehaviorTests()
    {
        LogManager.Initialize();
        LogManager.SetMinimumLevel(LogLevel.Debug);
    }

    [Fact]
    public void LogManager_DispatchesToRegisteredSink()
    {
        using var sink = new ProbeSink();
        LogManager.RegisterSink(sink);

        Log.Info("message-{0}", 42);

        Assert.True(sink.WaitForCount(1, TimeSpan.FromSeconds(2)));
        Assert.Contains(sink.entries, e => e.message == "message-42" && e.level == LogLevel.Info);
        LogManager.UnregisterSink(sink);
    }

    [Fact]
    public void LogManager_RespectsMinimumLevel()
    {
        using var sink = new ProbeSink();
        LogManager.RegisterSink(sink);
        LogManager.SetMinimumLevel(LogLevel.Warn);

        Log.Info("suppressed");
        Log.Warn("visible");

        Assert.True(sink.WaitForCount(1, TimeSpan.FromSeconds(2)));
        Assert.Single(sink.entries);
        Assert.Equal(LogLevel.Warn, sink.entries[0].level);
        Assert.Equal("visible", sink.entries[0].message);
        LogManager.UnregisterSink(sink);
    }

    [Fact]
    public void LogManagerFlushDeliversEveryPreviouslyQueuedEntry()
    {
        using var sink = new ProbeSink();
        LogManager.RegisterSink(sink);

        Log.Info("before-flush-1");
        Log.Info("before-flush-2");
        LogManager.Flush();

        Assert.Contains(sink.entries, static entry => entry.message == "before-flush-1");
        Assert.Contains(sink.entries, static entry => entry.message == "before-flush-2");
        LogManager.UnregisterSink(sink);
    }

    [Fact]
    public void LogManagerFlushCompletesWithoutRegisteredSinks()
    {
        Log.Info("discarded-before-flush");

        LogManager.Flush();
    }

    [Fact]
    public void FileLogSink_WritesEntriesAndRotates()
    {
        var dir = Path.Combine(Path.GetTempPath(), "InnoLoggingTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);

        using var sink = new FileLogSink(dir, maxFileSizeBytes: 256, maxFiles: 3);
        LogManager.RegisterSink(sink);

        for (var i = 0; i < 80; i++)
            Log.Warn("line-{0}", i);

        var timeout = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < timeout)
        {
            var files = Directory.GetFiles(dir, FileLogSink.C_LOG_FILE_PREFIX + "*.log");
            var combined = string.Join(Environment.NewLine, files.Select(File.ReadAllText));
            if (combined.Contains("line-79"))
            {
                Assert.True(files.Length <= 3);
                Assert.All(files, static file => Assert.Equal(".log", Path.GetExtension(file)));
                LogManager.UnregisterSink(sink);
                return;
            }

            Thread.Sleep(20);
        }

        LogManager.UnregisterSink(sink);
        throw new TimeoutException("Timed out waiting for file sink output.");
    }

    public void Dispose()
    {
        LogManager.Shutdown();
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
}
