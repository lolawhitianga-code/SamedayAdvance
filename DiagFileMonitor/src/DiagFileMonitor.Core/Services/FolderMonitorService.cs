using System.Collections.Concurrent;
using DiagFileMonitor.Core.Models;

namespace DiagFileMonitor.Core.Services;

/// <summary>
/// Watches one or more folders for new files matching a configurable extension filter, waits
/// for each file to finish being written, then hands it to the DiagFileProcessor on a single
/// background worker (so two zips landing at once don't race each other into the database).
/// </summary>
public class FolderMonitorService : IDisposable
{
    private readonly DiagFileProcessor _processor;
    private readonly ConcurrentQueue<string> _queue = new();
    private readonly SemaphoreSlim _signal = new(0);
    private readonly List<FileSystemWatcher> _watchers = new();

    private CancellationTokenSource? _cts;

    public IReadOnlyList<string> WatchFolders { get; private set; } = Array.Empty<string>();
    public IReadOnlyList<string> Extensions { get; private set; } = new List<string> { ".zip" };
    public bool IsRunning => _watchers.Count > 0;

    public event EventHandler<DiagnosticFile>? FileProcessed;
    public event EventHandler<string>? FileFailed;

    public FolderMonitorService(DiagFileProcessor processor)
    {
        _processor = processor;
    }

    public void Start(IEnumerable<string> folderPaths, IEnumerable<string> extensions)
    {
        Stop();

        WatchFolders = folderPaths
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        Extensions = NormalizeExtensions(extensions);

        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        _ = Task.Run(() => ProcessQueueAsync(token), token);

        foreach (var folder in WatchFolders)
        {
            Directory.CreateDirectory(folder);

            var watcher = new FileSystemWatcher(folder)
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
                IncludeSubdirectories = false
            };
            watcher.Created += OnFileEvent;
            watcher.Renamed += OnFileEvent;
            watcher.EnableRaisingEvents = true;
            _watchers.Add(watcher);

            // Pick up anything that arrived before monitoring was started.
            foreach (var existing in Directory.EnumerateFiles(folder))
            {
                EnqueueIfMatches(existing);
            }
        }
    }

    public void Stop()
    {
        foreach (var watcher in _watchers)
        {
            watcher.EnableRaisingEvents = false;
            watcher.Created -= OnFileEvent;
            watcher.Renamed -= OnFileEvent;
            watcher.Dispose();
        }

        _watchers.Clear();
        _cts?.Cancel();
        _cts = null;
    }

    private void OnFileEvent(object sender, FileSystemEventArgs e) => EnqueueIfMatches(e.FullPath);

    private void EnqueueIfMatches(string fullPath)
    {
        var ext = Path.GetExtension(fullPath);
        if (Extensions.Contains(ext, StringComparer.OrdinalIgnoreCase))
        {
            _queue.Enqueue(fullPath);
            _signal.Release();
        }
    }

    private async Task ProcessQueueAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                await _signal.WaitAsync(token);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            if (!_queue.TryDequeue(out var path))
            {
                continue;
            }

            try
            {
                if (!await WaitForFileReadyAsync(path, token))
                {
                    SimpleLogger.Error($"Gave up waiting for '{path}' to finish arriving.");
                    FileFailed?.Invoke(this, path);
                    continue;
                }

                var result = await _processor.ProcessAsync(path, token);
                FileProcessed?.Invoke(this, result);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                SimpleLogger.Error($"Failed to process '{path}'", ex);
                FileFailed?.Invoke(this, path);
            }
        }
    }

    /// <summary>Polls until the file's size stops changing and it can be opened exclusively (i.e. the copy is done).</summary>
    private static async Task<bool> WaitForFileReadyAsync(string path, CancellationToken token, int timeoutMs = 30000)
    {
        var start = DateTime.UtcNow;
        long lastSize = -1;

        while ((DateTime.UtcNow - start).TotalMilliseconds < timeoutMs)
        {
            if (!File.Exists(path))
            {
                return false;
            }

            try
            {
                var info = new FileInfo(path);
                if (info.Length > 0 && info.Length == lastSize)
                {
                    using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.None);
                    return true;
                }

                lastSize = info.Length;
            }
            catch (IOException)
            {
                // Still being written to / locked by the producer - keep polling.
            }

            await Task.Delay(250, token);
        }

        return false;
    }

    private static List<string> NormalizeExtensions(IEnumerable<string> extensions)
    {
        return extensions
            .Select(e => e.Trim())
            .Where(e => e.Length > 0)
            .Select(e => e.StartsWith('.') ? e : "." + e)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public void Dispose()
    {
        Stop();
        _signal.Dispose();
    }
}
