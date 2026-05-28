namespace TelegramDrive.Models;

/// <summary>
/// Represents a virtual folder in the hierarchical file system.
/// </summary>
public class FolderItem
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int? ParentId { get; set; }

    /// <summary>Used only in Local Storage backend — absolute path on disk.</summary>
    public string? LocalPath { get; set; }
    public string? UploadDate { get; set; }
}
