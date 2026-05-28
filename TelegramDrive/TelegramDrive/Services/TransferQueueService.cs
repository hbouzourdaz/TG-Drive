using TelegramDrive.Models;

namespace TelegramDrive.Services;

/// <summary>
/// Sequential transfer queue coordinator. Executes upload/download jobs one at a time.
/// Port of the Python transfer_queue system.
/// </summary>
public class TransferQueueService
{
    private readonly List<TransferJob> _queue = new();
    private int _jobCounter;
    private TransferJob? _activeJob;
    private CancellationTokenSource? _activeCts;
    private bool _isProcessing;

    public IReadOnlyList<TransferJob> Queue => _queue.AsReadOnly();
    public TransferJob? ActiveJob => _activeJob;
    public bool HasActiveJob => _activeJob != null;

    // Events for UI binding
    public event Action<TransferJob>? JobStarted;
    public event Action<TransferJob, long, long, double>? JobProgress;
    public event Action<TransferJob, string>? JobCompleted;
    public event Action<TransferJob, string>? JobFailed;
    public event Action<TransferJob, string>? JobCancelled;
    public event Action? QueueChanged;

    public TransferJob AddJob(TransferType type, string? filePath = null, string? filename = null,
        long fileSize = 0, int? folderId = null, int? messageId = null,
        string? destDir = null, string? sourcePath = null)
    {
        var job = new TransferJob
        {
            Id = ++_jobCounter,
            Type = type,
            FilePath = filePath,
            Filename = filename ?? "Unknown",
            FileSize = fileSize,
            FolderId = folderId,
            MessageId = messageId,
            DestDir = destDir,
            SourcePath = sourcePath,
            Status = TransferStatus.Pending
        };
        _queue.Add(job);
        QueueChanged?.Invoke();
        _ = ProcessQueueAsync();
        return job;
    }

    public void CancelJob(string filename)
    {
        foreach (var job in _queue)
        {
            if (job.Filename == filename && job.Status is TransferStatus.Pending or TransferStatus.Running)
            {
                job.Status = TransferStatus.Cancelled;
                if (job == _activeJob)
                {
                    _activeCts?.Cancel();
                }
                else
                {
                    JobCancelled?.Invoke(job, $"'{filename}' removed from queue.");
                    QueueChanged?.Invoke();
                }
                break;
            }
        }
    }

    public void CancelActiveTransfer()
    {
        _activeCts?.Cancel();
    }

    private Task ProcessQueueAsync()
    {
        if (_isProcessing || _activeJob != null) return Task.CompletedTask;
        _isProcessing = true;

        try
        {
            var pendingJob = _queue.FirstOrDefault(j => j.Status == TransferStatus.Pending);
            if (pendingJob == null)
            {
                QueueChanged?.Invoke();
                return Task.CompletedTask;
            }

            _activeJob = pendingJob;
            pendingJob.Status = TransferStatus.Running;
            _activeCts = new CancellationTokenSource();
            JobStarted?.Invoke(pendingJob);
            QueueChanged?.Invoke();

            // The actual transfer is performed by the caller (ViewModel) subscribing to JobStarted
            // and calling ExecuteJob. This service just manages the queue state.
        }
        finally
        {
            _isProcessing = false;
        }
        return Task.CompletedTask;
    }

    public void ReportProgress(long current, long total, double percent)
    {
        if (_activeJob != null)
        {
            _activeJob.Percent = percent;
            JobProgress?.Invoke(_activeJob, current, total, percent);
        }
    }

    public void MarkCompleted(string message)
    {
        if (_activeJob != null)
        {
            _activeJob.Status = TransferStatus.Completed;
            var job = _activeJob;
            _activeJob = null;
            _activeCts = null;
            JobCompleted?.Invoke(job, message);
            QueueChanged?.Invoke();
            _ = ProcessQueueAsync();
        }
    }

    public void MarkFailed(string message)
    {
        if (_activeJob != null)
        {
            _activeJob.Status = TransferStatus.Failed;
            var job = _activeJob;
            _activeJob = null;
            _activeCts = null;
            JobFailed?.Invoke(job, message);
            QueueChanged?.Invoke();
            _ = ProcessQueueAsync();
        }
    }

    public void MarkCancelled(string message)
    {
        if (_activeJob != null)
        {
            _activeJob.Status = TransferStatus.Cancelled;
            var job = _activeJob;
            _activeJob = null;
            _activeCts = null;
            JobCancelled?.Invoke(job, message);
            QueueChanged?.Invoke();
            _ = ProcessQueueAsync();
        }
    }

    public CancellationToken GetActiveCancellationToken()
    {
        return _activeCts?.Token ?? CancellationToken.None;
    }

    public (int Pending, int Running, int Completed, int Failed) GetStats()
    {
        int pending = 0, running = 0, completed = 0, failed = 0;
        foreach (var job in _queue)
        {
            switch (job.Status)
            {
                case TransferStatus.Pending: pending++; break;
                case TransferStatus.Running: running++; break;
                case TransferStatus.Completed: completed++; break;
                case TransferStatus.Failed: failed++; break;
            }
        }
        return (pending, running, completed, failed);
    }
}
