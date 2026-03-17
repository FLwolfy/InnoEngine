using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace Inno.Core.Logging;

public class FileLogSink : ILogSink, IDisposable
{
    public const string C_LOG_FILE_PREFIX = "log_";
    
    private readonly string m_logDirectory;
    private readonly long m_maxFileSize;
    private readonly int m_maxFiles;
    private string m_currentFile;
    private long m_currentSize;

    private readonly ConcurrentQueue<LogEntry> m_queue = new();
    private readonly Thread m_workerThread;
    private bool m_running = true;

    public FileLogSink(string logDirectory, long maxFileSizeBytes = 10 * 1024 * 1024, int maxFiles = 10)
    {
        m_logDirectory = logDirectory;
        m_maxFileSize = maxFileSizeBytes;
        m_maxFiles = maxFiles;

        Directory.CreateDirectory(m_logDirectory);
        CleanupOldFiles();
        
        m_currentFile = GetNewLogFilePath();
        m_currentSize = 0;

        m_workerThread = new Thread(ProcessQueue) { IsBackground = true };
        m_workerThread.Start();
    }

    public void Receive(LogEntry entry)
    {
        m_queue.Enqueue(entry);
    }

    private void ProcessQueue()
    {
        while (m_running)
        {
            while (m_queue.TryDequeue(out var entry))
            {
                try
                {
                    string line = FormatEntry(entry) + Environment.NewLine;
                    byte[] bytes = Encoding.UTF8.GetBytes(line);

                    File.AppendAllText(m_currentFile, line, Encoding.UTF8);
                    m_currentSize += bytes.Length;

                    if (m_currentSize > m_maxFileSize)
                    {
                        RotateFile();
                    }
                }
                catch
                {
                    // Ignore I/O exceptions
                }
            }
            Thread.Sleep(10); // reduce CPU usage
        }
    }

    private void RotateFile()
    {
        // Cleanup old files
        CleanupOldFiles();
        
        // Create a new log file
        m_currentFile = GetNewLogFilePath();
        m_currentSize = 0;
    }

    private void CleanupOldFiles()
    {
        try
        {
            var files = new DirectoryInfo(m_logDirectory)
                .GetFiles(C_LOG_FILE_PREFIX + "*.txt")
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
        return $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{entry.source}] [{entry.level}]: {entry.message} ({entry.file}:{entry.line})";
    }

    private string GetNewLogFilePath()
    {
        string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmssfff"); // millisecond precision
        return Path.Combine(m_logDirectory, $"{C_LOG_FILE_PREFIX}{timestamp}.txt");
    }

    public void Dispose()
    {
        m_running = false;
        m_workerThread.Join();

        // Flush remaining entries
        while (m_queue.TryDequeue(out var entry))
        {
            try
            {
                string line = FormatEntry(entry) + Environment.NewLine;
                File.AppendAllText(m_currentFile, line, Encoding.UTF8);
            }
            catch
            {
                // Ignore I/O exceptions
            }
        }
    }
}
