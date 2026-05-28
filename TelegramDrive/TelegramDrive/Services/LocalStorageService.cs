namespace TelegramDrive.Services;

/// <summary>
/// Manages local disk storage operations for the "Local Disk Explorer" backend.
/// </summary>
public class LocalStorageService
{
    public string RootPath { get; }

    public LocalStorageService()
    {
        RootPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "local_storage_drive");
        Directory.CreateDirectory(RootPath);
    }

    public string GetAbsolutePath(string relativePath)
    {
        return Path.Combine(RootPath, relativePath);
    }

    public (List<(string Name, string FullPath, string Date)> Folders,
            List<(string Name, string FullPath, long Size, string Date)> Files)
        ScanDirectory(string relativePath, string? searchQuery = null)
    {
        var absPath = GetAbsolutePath(relativePath);
        Directory.CreateDirectory(absPath);

        var folders = new List<(string Name, string FullPath, string Date)>();
        var files = new List<(string Name, string FullPath, long Size, string Date)>();

        try
        {
            foreach (var entry in new DirectoryInfo(absPath).EnumerateFileSystemInfos())
            {
                string date = entry.LastWriteTime.ToString("yyyy-MM-dd HH:mm");
                bool matchesSearch = string.IsNullOrEmpty(searchQuery) ||
                    entry.Name.Contains(searchQuery, StringComparison.OrdinalIgnoreCase);

                if (!matchesSearch) continue;

                if (entry is DirectoryInfo dir)
                {
                    folders.Add((dir.Name, dir.FullName, date));
                }
                else if (entry is FileInfo file)
                {
                    files.Add((file.Name, file.FullName, file.Length, date));
                }
            }
        }
        catch { /* Ignore access errors */ }

        folders.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        files.Sort((a, b) => string.Compare(b.Date, a.Date, StringComparison.Ordinal));

        return (folders, files);
    }

    public (int FileCount, long TotalSize) GetStorageStats()
    {
        int count = 0;
        long size = 0;

        try
        {
            foreach (var file in Directory.EnumerateFiles(RootPath, "*", SearchOption.AllDirectories))
            {
                count++;
                try { size += new FileInfo(file).Length; } catch { }
            }
        }
        catch { }

        return (count, size);
    }

    public int GetTotalItemCount()
    {
        try
        {
            int count = 0;
            foreach (var _ in Directory.EnumerateFileSystemEntries(RootPath, "*", SearchOption.AllDirectories))
                count++;
            return count;
        }
        catch { return 0; }
    }

    public void CopyFile(string sourcePath, string destRelativePath, string filename)
    {
        string destDir = GetAbsolutePath(destRelativePath);
        Directory.CreateDirectory(destDir);
        File.Copy(sourcePath, Path.Combine(destDir, filename), overwrite: true);
    }

    public void CreateFolder(string relativePath, string folderName)
    {
        Directory.CreateDirectory(Path.Combine(GetAbsolutePath(relativePath), folderName));
    }

    public void DeleteFile(string fullPath)
    {
        if (File.Exists(fullPath))
            File.Delete(fullPath);
    }

    public void DeleteFolder(string fullPath)
    {
        if (Directory.Exists(fullPath))
            Directory.Delete(fullPath, recursive: true);
    }

    public void RenameFile(string fullPath, string newName)
    {
        if (File.Exists(fullPath))
        {
            string dir = Path.GetDirectoryName(fullPath)!;
            File.Move(fullPath, Path.Combine(dir, newName));
        }
    }
}
