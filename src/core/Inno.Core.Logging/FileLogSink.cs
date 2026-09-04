using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace Inno.Core.Logging;

/// <summary>
/// Persists log entries to rotating log files on disk.
/// </summary>
public class FileLogSink : ILogSink, IDisposable
{
    /// <summary>
    /// Prefix used for generated log file names.
    /// </summary>
    public const string C_LOG_FILE_PREFIX = "log_";

    private const string C_LOG_FILE_EXTENSION = ".log";
    
    private readonly string m_logDirectory;
    private readonly long m_maxFileSize;
    private readonly int m_maxFiles;
    private readonly object m_lifecycleSync = new();
    private string m_currentFile;
    private long m_currentSize;
    private FileStream? m_stream;
    private StreamWriter? m_writer;

    private readonly ConcurrentQueue<LogEntry> m_queue = new();
    private readonly SemaphoreSlim m_signal = new(0);
    private readonly Thread m_workerThread;
    private volatile bool m_running = true;
    private bool m_disposed;

    /// <summary>
    /// Initializes a file sink.
    /// </summary>
    /// <param name="logDirectory">
    /// Directory where log files are stored.
    /// </param>
    /// <param name="maxFileSizeBytes">
    /// Maximum file size before rotation.
    /// </param>
    /// <param name="maxFiles">
    /// Maximum number of retained files.
    /// </param>
    public FileLogSink(string logDirectory, long maxFileSizeBytes = 10 * 1024 * 1024, int maxFiles = 10)
    {
        m_logDirectory = logDirectory;
        m_maxFileSize = maxFileSizeBytes;
        m_maxFiles = maxFiles;

        Directory.CreateDirectory(m_logDirectory);
        CleanupOldFiles();
        
        m_currentFile = GetNewLogFilePath();
        m_currentSize = 0;
        OpenWriter();

        m_workerThread = new Thread(ProcessQueue) { IsBackground = true };
        m_workerThread.Start();
    }

    /// <summary>
    /// Queues a log entry for asynchronous file writing.
    /// </summary>
    /// <param name="entry">
    /// The entry to persist.
    /// </param>
    public void Receive(LogEntry entry)
    {
        lock (m_lifecycleSync)
        {
            ObjectDisposedException.ThrowIf(m_disposed, this);
            m_queue.Enqueue(entry);
            m_signal.Release();
        }
    }

    private void ProcessQueue()
    {
        while (true)
        {
            m_signal.Wait();
            DrainQueue();

            if (!m_running && m_queue.IsEmpty)
                break;
        }
    }

    private void DrainQueue()
    {
        if (m_writer == null)
            return;

        var buffer = new StringBuilder(16 * 1024);
        while (m_queue.TryDequeue(out var entry))
        {
            try
            {
                var line = FormatEntry(entry) + Environment.NewLine;
                var bytes = Encoding.UTF8.GetByteCount(line);

                buffer.Append(line);
                m_currentSize += bytes;

                if (m_currentSize > m_maxFileSize)
                {
                    m_writer.Write(buffer.ToString());
                    m_writer.Flush();
                    buffer.Clear();
                    RotateFile();
                }
            }
            catch
            {
                // Ignore I/O exceptions
            }
        }

        if (buffer.Length > 0)
        {
            m_writer.Write(buffer.ToString());
            m_writer.Flush();
        }
    }

    private void RotateFile()
    {
        // Cleanup old files
        CleanupOldFiles();

        CloseWriter();

        // Create a new log file
        m_currentFile = GetNewLogFilePath();
        m_currentSize = 0;
        OpenWriter();
    }

    private void CleanupOldFiles()
    {
        try
        {
            var files = new DirectoryInfo(m_logDirectory)
                .GetFiles(C_LOG_FILE_PREFIX + "*" + C_LOG_FILE_EXTENSION)
                .OrderBy(f => f.CreationTime)
                .ToList();

            while (files.Count >= m_maxFiles)
            {
                try
                {
                    files[0].Delete();
                }
                catch
                {
                    // Ignore delete exceptions
                }
                files.RemoveAt(0);
            }
        }
        catch
        {
            // Ignore directory scanning exceptions
        }
    }

    private string FormatEntry(LogEntry entry)
    {
        return $"[{entry.time:yyyy-MM-dd HH:mm:ss.fff}] [{entry.domain}/{entry.scope}] [{entry.level}]: {entry.message} ({entry.file}:{entry.line})";
    }

    private string GetNewLogFilePath()
    {
        string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmssfff"); // millisecond precision
        return Path.Combine(m_logDirectory, $"{C_LOG_FILE_PREFIX}{timestamp}{C_LOG_FILE_EXTENSION}");
    }

    /// <summary>
    /// Stops the sink worker and flushes remaining queued entries.
    /// </summary>
    public void Dispose()
    {
        lock (m_lifecycleSync)
        {
            if (m_disposed)
                return;
            m_disposed = true;
            m_running = false;
            m_signal.Release();
        }

        m_workerThread.Join();

        DrainQueue();
        CloseWriter();
        m_signal.Dispose();
    }

    private void OpenWriter()
    {
        m_stream = new FileStream(
            m_currentFile,
            FileMode.Append,
            FileAccess.Write,
            FileShare.Read,
            bufferSize: 16 * 1024,
            FileOptions.SequentialScan);

        m_writer = new StreamWriter(m_stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
        {
            AutoFlush = false
        };
    }

    private void CloseWriter()
    {
        try
        {
            m_writer?.Flush();
        }
        catch
        {
            // Ignore flush exceptions
        }
        finally
        {
            m_writer?.Dispose();
            m_stream?.Dispose();
            m_writer = null;
            m_stream = null;
        }
    }
}
