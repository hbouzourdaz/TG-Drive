namespace TelegramDrive.Models;

/// <summary>
/// Represents a file stored in the virtual file system, mapped to a Telegram message ID.
/// </summary>
public class FileItem
{
    public int Id { get; set; }
    public string Filename { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public int TelegramMessageId { get; set; }
    public string UploadDate { get; set; } = string.Empty;
    public int? FolderId { get; set; }

    /// <summary>Used only in Local Storage backend — absolute path on disk.</summary>
    public string? LocalPath { get; set; }
}
