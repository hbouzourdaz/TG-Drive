namespace TelegramDrive.Services;

/// <summary>
/// Monitors a local directory for new files and auto-queues them for upload.
/// Uses FileSystemWatcher (superior to Python's polling approach).
/// </summary>
public class BackupWatcherService : IDisposable
{
    private FileSystemWatcher? _watcher;
    private readonly DatabaseService _db;
    private bool _isRunning;

    public event Action<string, string, long>? NewFileDetected;

    public bool IsRunning => _isRunning;
    public string? WatchDirectory { get; private set; }

    public BackupWatcherService(DatabaseService db)
    {
        _db = db;
    }

    public void Start(string directoryPath)
    {
        if (_isRunning) Stop();
        if (!Directory.Exists(directoryPath)) return;

        WatchDirectory = directoryPath;
        _watcher = new FileSystemWatcher(directoryPath)
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.CreationTime,
            EnableRaisingEvents = true,
            IncludeSubdirectories = false
        };

        _watcher.Created += OnFileCreated;
        _isRunning = true;
    }

    public void Stop()
    {
        if (_watcher != null)
        {
            _watcher.EnableRaisingEvents = false;
            _watcher.Created -= OnFileCreated;
            _watcher.Dispose();
            _watcher = null;
        }
        _isRunning = false;
    }

    private void OnFileCreated(object sender, FileSystemEventArgs e)
    {
        try
        {
            if (!File.Exists(e.FullPath)) return;

            // Wait briefly for file to finish writing
            Task.Delay(500).Wait();

            var fileInfo = new FileInfo(e.FullPath);
            if (fileInfo.Length == 0) return;

            // Check if already indexed
            var existingFiles = _db.SearchFiles(fileInfo.Name);
            if (existingFiles.Any(f => f.Filename == fileInfo.Name))
                return;

            NewFileDetected?.Invoke(fileInfo.Name, e.FullPath, fileInfo.Length);
        }
        catch { /* Ignore errors in watcher */ }
    }

    public void Dispose()
    {
        Stop();
    }
}
