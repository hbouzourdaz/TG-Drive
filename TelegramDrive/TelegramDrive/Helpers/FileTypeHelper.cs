using Microsoft.UI;
using Windows.UI;

namespace TelegramDrive.Helpers;

/// <summary>
/// Maps file extensions to categories and badge colors for the UI.
/// </summary>
public static class FileTypeHelper
{
    private static readonly HashSet<string> DocExtensions = new(StringComparer.OrdinalIgnoreCase)
        { "pdf", "txt", "doc", "docx", "xls", "xlsx", "ppt", "pptx", "epub", "csv" };

    private static readonly HashSet<string> MediaExtensions = new(StringComparer.OrdinalIgnoreCase)
        { "png", "jpg", "jpeg", "gif", "bmp", "svg", "webp", "mp4", "mkv", "avi", "mov", "mp3", "wav", "flac", "ogg" };

    private static readonly HashSet<string> ZipExtensions = new(StringComparer.OrdinalIgnoreCase)
        { "zip", "rar", "7z", "tar", "gz", "bz2" };

    private static readonly HashSet<string> TextPreviewExtensions = new(StringComparer.OrdinalIgnoreCase)
        { "txt", "py", "json", "csv", "log", "ini", "md", "html", "css", "js", "xml", "yaml", "yml", "sh", "bat", "cs", "xaml" };

    private static readonly HashSet<string> ImagePreviewExtensions = new(StringComparer.OrdinalIgnoreCase)
        { "png", "jpg", "jpeg", "gif", "bmp" };

    public static string GetCategory(string filename)
    {
        string ext = Path.GetExtension(filename).TrimStart('.').ToLowerInvariant();
        if (DocExtensions.Contains(ext)) return "docs";
        if (MediaExtensions.Contains(ext)) return "media";
        if (ZipExtensions.Contains(ext)) return "zips";
        return "others";
    }

    public static bool MatchesFilter(string filename, string filter)
    {
        if (filter == "all") return true;
        return GetCategory(filename) == filter;
    }

    public static Color GetBadgeColor(string filename)
    {
        string ext = Path.GetExtension(filename).TrimStart('.').ToLowerInvariant();
        if (DocExtensions.Contains(ext)) return ColorHelper.FromArgb(255, 30, 107, 85);     // Teal-green
        if (MediaExtensions.Contains(ext)) return ColorHelper.FromArgb(255, 91, 63, 166);    // Purple
        if (ZipExtensions.Contains(ext)) return ColorHelper.FromArgb(255, 166, 95, 30);      // Orange
        return ColorHelper.FromArgb(255, 46, 74, 107);                                        // Steel-blue
    }

    public static string GetShortExtension(string filename)
    {
        string ext = Path.GetExtension(filename).TrimStart('.').ToUpperInvariant();
        if (string.IsNullOrEmpty(ext) || ext.Length > 4) return "DOC";
        return ext.Length > 3 ? ext[..3] : ext;
    }

    public static bool CanPreviewAsText(string filename) =>
        TextPreviewExtensions.Contains(Path.GetExtension(filename).TrimStart('.'));

    public static bool CanPreviewAsImage(string filename) =>
        ImagePreviewExtensions.Contains(Path.GetExtension(filename).TrimStart('.'));
}
