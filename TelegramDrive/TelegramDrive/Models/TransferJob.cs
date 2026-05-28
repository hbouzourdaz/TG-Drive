namespace TelegramDrive.Models;

/// <summary>
/// Represents a queued upload or download transfer job.
/// </summary>
public class TransferJob
{
    public int Id { get; set; }
    public TransferType Type { get; set; }
    public string? FilePath { get; set; }
    public string Filename { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public int? FolderId { get; set; }
    public int? MessageId { get; set; }
    public string? DestDir { get; set; }
    public string? SourcePath { get; set; }
    public TransferStatus Status { get; set; } = TransferStatus.Pending;
    public double Percent { get; set; }
}

public enum TransferType
{
    Upload,
    Download
}

public enum TransferStatus
{
    Pending,
    Running,
    Completed,
    Cancelled,
    Failed
}
