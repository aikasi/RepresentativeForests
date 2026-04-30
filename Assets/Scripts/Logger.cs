using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

public class Logger : MonoBehaviour
{
    [Tooltip("Initialize automatically on Awake, saving logs in PersistantDataPath/Logs")]
    [SerializeField]
    private bool autoInitialize = false;

    [Tooltip("Whether to log Debug.Log/Exception automatically")]
    [SerializeField]
    private bool logConsole = false;

    [Tooltip("Maximum Log File count before deleting the oldest. Set it to 0 to not delete")]
    [SerializeField, Min(0)]
    private int maxFileCount = 1000;

    [Tooltip("Whether to append the current time automatically")]
    [SerializeField]
    private bool appendTime = true;

    private StreamWriter writer;
    // 스레드 안전 큐 (logMessageReceivedThreaded가 백그라운드 스레드에서 호출되므로 필수)
    private readonly ConcurrentQueue<string> logQueue = new();
    private Task writeTask;
    private bool finishing = false;

    private void Awake()
    {
        if (logConsole)
            Application.logMessageReceivedThreaded += (msg, _, type) => Enqueue($"[{type}] {msg}");
        if (autoInitialize)
            SetPath(Path.Combine(Application.persistentDataPath, "Logs", DateTime.Now.ToString("yyMMdd-HHmmss") + ".log"));
    }

    /// <summary>
    /// Set path to log file, automatically initializing it
    /// </summary>
    public void SetPath(string newPath)
    {
        var dir = Path.GetDirectoryName(newPath);
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
        if (maxFileCount > 0)
        {
            if (!string.IsNullOrEmpty(dir))
            {
                var logFiles = Directory
                    .GetFiles(dir, "*.log")
                    .Select(path => new FileInfo(path))
                    // sort ascending by creation time (oldest first)
                    .OrderBy(fi => fi.CreationTimeUtc)
                    .ToList();

                int excess = logFiles.Count - maxFileCount + 1;
                // +1 because we're about to create/open a new one
                for (int i = 0; i < excess; i++)
                {
                    try
                    {
                        logFiles[i].Delete();
                    }
                    catch (IOException e)
                    {
                        Debug.LogWarning($"Logger: failed to delete old log {logFiles[i].Name}: {e.Message}");
                    }
                }
            }
        }

        finishing = true;
        FinishWriting();

        writer = new StreamWriter(newPath, append: true, encoding: Encoding.UTF8)
        { AutoFlush = false };

        finishing = false;
        writeTask = null;
    }

    /// <summary>
    /// Enqueue new log manually
    /// </summary>
    public void Enqueue(string log)
    {
        if (appendTime) log = $"{DateTime.Now:HH:mm:ss.fff}) {log}";
        logQueue.Enqueue(log);
    }

    // ★ StringBuilder를 필드로 캐싱하여 GC 압력 제거 (매 프레임 힙 할당 방지)
    private readonly StringBuilder _logBuffer = new StringBuilder(1024);

    private void Update()
    {
        if (writer == null || finishing) return;

        if (writeTask != null && !writeTask.IsCompleted) return;

        if (logQueue.IsEmpty) return;

        // 큐에서 안전하게 꺼내기 (스레드 안전)
        _logBuffer.Clear();
        while (logQueue.TryDequeue(out string entry))
            _logBuffer.AppendLine(entry);
        string text = _logBuffer.ToString();
        writeTask = WriteAndFlushAsyncInternal(text);
    }

    // ★ 로컬 함수 대신 인스턴스 메서드로 분리 (async 상태 머신의 불필요한 클로저 캡처 방지)
    private async Task WriteAndFlushAsyncInternal(string text)
    {
        await writer.WriteAsync(text);
        await writer.FlushAsync();
    }

    private void FinishWriting()
    {
        if (writer == null) return;

        writeTask?.Wait();
        writeTask = null;

        if (!logQueue.IsEmpty)
        {
            var sb = new StringBuilder();
            while (logQueue.TryDequeue(out string entry))
                sb.AppendLine(entry);
            writer.Write(sb.ToString());
        }
        writer.Flush();
        writer.Dispose();
        writer = null;
    }

    private void OnApplicationQuit()
    {
        finishing = true;
        FinishWriting();
    }
}