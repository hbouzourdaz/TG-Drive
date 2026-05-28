using Microsoft.Data.Sqlite;
using TelegramDrive.Models;

namespace TelegramDrive.Services;

/// <summary>
/// Manages the SQLite virtual file system that maps files and folders to Telegram message IDs.
/// Direct port of DBHelper from the Python version.
/// </summary>
public class DatabaseService
{
    private readonly string _dbPath;

    public DatabaseService(string? dbPath = null)
    {
        _dbPath = dbPath ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "telegram_drive.db");
        InitDb();
    }

    private string ConnectionString => $"Data Source={_dbPath}";

    private SqliteConnection GetConnection()
    {
        var conn = new SqliteConnection(ConnectionString);
        conn.Open();
        return conn;
    }

    private void InitDb()
    {
        using var conn = GetConnection();

        using var cmd1 = conn.CreateCommand();
        cmd1.CommandText = @"
            CREATE TABLE IF NOT EXISTS folders (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                name TEXT NOT NULL,
                parent_id INTEGER DEFAULT NULL,
                FOREIGN KEY(parent_id) REFERENCES folders(id)
            )";
        cmd1.ExecuteNonQuery();

        using var cmd2 = conn.CreateCommand();
        cmd2.CommandText = @"
            CREATE TABLE IF NOT EXISTS files (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                filename TEXT NOT NULL,
                file_size INTEGER NOT NULL,
                telegram_message_id INTEGER NOT NULL,
                upload_date TEXT NOT NULL,
                folder_id INTEGER DEFAULT NULL,
                FOREIGN KEY(folder_id) REFERENCES folders(id)
            )";
        cmd2.ExecuteNonQuery();

        using var cmd3 = conn.CreateCommand();
        cmd3.CommandText = @"
            CREATE TABLE IF NOT EXISTS status (
                key TEXT PRIMARY KEY,
                value TEXT
            )";
        cmd3.ExecuteNonQuery();

        // Migration: add folder_id if missing
        try
        {
            using var cmdMigrate = conn.CreateCommand();
            cmdMigrate.CommandText = "ALTER TABLE files ADD COLUMN folder_id INTEGER DEFAULT NULL";
            cmdMigrate.ExecuteNonQuery();
        }
        catch (SqliteException) { /* Column already exists */ }
    }

    // ─── Status/Config Key-Value Store ─────────────────────────────

    public void SetStatusVal(string key, string value)
    {
        using var conn = GetConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT OR REPLACE INTO status (key, value) VALUES ($key, $value)";
        cmd.Parameters.AddWithValue("$key", key);
        cmd.Parameters.AddWithValue("$value", value);
        cmd.ExecuteNonQuery();
    }

    public string? GetStatusVal(string key, string? defaultVal = null)
    {
        try
        {
            using var conn = GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT value FROM status WHERE key = $key";
            cmd.Parameters.AddWithValue("$key", key);
            var result = cmd.ExecuteScalar();
            return result?.ToString() ?? defaultVal;
        }
        catch
        {
            return defaultVal;
        }
    }

    // ─── File Operations ───────────────────────────────────────────

    public int AddFile(string filename, long fileSize, int telegramMessageId, string uploadDate, int? folderId = null)
    {
        using var conn = GetConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO files (filename, file_size, telegram_message_id, upload_date, folder_id)
            VALUES ($filename, $file_size, $telegram_message_id, $upload_date, $folder_id)";
        cmd.Parameters.AddWithValue("$filename", filename);
        cmd.Parameters.AddWithValue("$file_size", fileSize);
        cmd.Parameters.AddWithValue("$telegram_message_id", telegramMessageId);
        cmd.Parameters.AddWithValue("$upload_date", uploadDate);
        cmd.Parameters.AddWithValue("$folder_id", (object?)folderId ?? DBNull.Value);
        cmd.ExecuteNonQuery();

        using var lastIdCmd = conn.CreateCommand();
        lastIdCmd.CommandText = "SELECT last_insert_rowid()";
        return Convert.ToInt32(lastIdCmd.ExecuteScalar());
    }

    public void DeleteFile(int fileId)
    {
        using var conn = GetConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM files WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", fileId);
        cmd.ExecuteNonQuery();
    }

    public void RenameFile(int fileId, string newName)
    {
        using var conn = GetConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE files SET filename = $name WHERE id = $id";
        cmd.Parameters.AddWithValue("$name", newName);
        cmd.Parameters.AddWithValue("$id", fileId);
        cmd.ExecuteNonQuery();
    }

    // ─── Folder Operations ─────────────────────────────────────────

    public int CreateFolder(string name, int? parentId = null)
    {
        using var conn = GetConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT INTO folders (name, parent_id) VALUES ($name, $parent_id)";
        cmd.Parameters.AddWithValue("$name", name);
        cmd.Parameters.AddWithValue("$parent_id", (object?)parentId ?? DBNull.Value);
        cmd.ExecuteNonQuery();

        using var lastIdCmd = conn.CreateCommand();
        lastIdCmd.CommandText = "SELECT last_insert_rowid()";
        return Convert.ToInt32(lastIdCmd.ExecuteScalar());
    }

    public List<FolderItem> GetFoldersInFolder(int? folderId)
    {
        using var conn = GetConnection();
        using var cmd = conn.CreateCommand();
        if (folderId is null)
        {
            cmd.CommandText = "SELECT id, name, parent_id FROM folders WHERE parent_id IS NULL ORDER BY name ASC";
        }
        else
        {
            cmd.CommandText = "SELECT id, name, parent_id FROM folders WHERE parent_id = $parent_id ORDER BY name ASC";
            cmd.Parameters.AddWithValue("$parent_id", folderId.Value);
        }

        var folders = new List<FolderItem>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            folders.Add(new FolderItem
            {
                Id = reader.GetInt32(0),
                Name = reader.GetString(1),
                ParentId = reader.IsDBNull(2) ? null : reader.GetInt32(2)
            });
        }
        return folders;
    }

    public List<FileItem> GetFilesInFolder(int? folderId)
    {
        using var conn = GetConnection();
        using var cmd = conn.CreateCommand();
        if (folderId is null)
        {
            cmd.CommandText = "SELECT id, filename, file_size, telegram_message_id, upload_date, folder_id FROM files WHERE folder_id IS NULL ORDER BY id DESC";
        }
        else
        {
            cmd.CommandText = "SELECT id, filename, file_size, telegram_message_id, upload_date, folder_id FROM files WHERE folder_id = $folder_id ORDER BY id DESC";
            cmd.Parameters.AddWithValue("$folder_id", folderId.Value);
        }

        var files = new List<FileItem>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            files.Add(new FileItem
            {
                Id = reader.GetInt32(0),
                Filename = reader.GetString(1),
                FileSize = reader.GetInt64(2),
                TelegramMessageId = reader.GetInt32(3),
                UploadDate = reader.GetString(4),
                FolderId = reader.IsDBNull(5) ? null : reader.GetInt32(5)
            });
        }
        return files;
    }

    public List<(string Name, int? Id)> GetBreadcrumbPath(int? folderId)
    {
        if (folderId is null)
            return new List<(string, int?)> { ("Root", null) };

        var path = new List<(string Name, int? Id)>();
        int? currId = folderId;
        using var conn = GetConnection();

        while (currId is not null)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT id, name, parent_id FROM folders WHERE id = $id";
            cmd.Parameters.AddWithValue("$id", currId.Value);
            using var reader = cmd.ExecuteReader();
            if (reader.Read())
            {
                path.Insert(0, (reader.GetString(1), reader.GetInt32(0)));
                currId = reader.IsDBNull(2) ? null : reader.GetInt32(2);
            }
            else break;
        }
        path.Insert(0, ("Root", null));
        return path;
    }

    public List<FileItem> GetFilesInFolderRecursive(int folderId)
    {
        var files = new List<FileItem>();
        using var conn = GetConnection();

        using var fileCmd = conn.CreateCommand();
        fileCmd.CommandText = "SELECT id, telegram_message_id, filename FROM files WHERE folder_id = $folder_id";
        fileCmd.Parameters.AddWithValue("$folder_id", folderId);
        using var fileReader = fileCmd.ExecuteReader();
        while (fileReader.Read())
        {
            files.Add(new FileItem
            {
                Id = fileReader.GetInt32(0),
                TelegramMessageId = fileReader.GetInt32(1),
                Filename = fileReader.GetString(2)
            });
        }

        using var folderCmd = conn.CreateCommand();
        folderCmd.CommandText = "SELECT id FROM folders WHERE parent_id = $parent_id";
        folderCmd.Parameters.AddWithValue("$parent_id", folderId);
        var subFolderIds = new List<int>();
        using var folderReader = folderCmd.ExecuteReader();
        while (folderReader.Read())
            subFolderIds.Add(folderReader.GetInt32(0));

        foreach (var subId in subFolderIds)
            files.AddRange(GetFilesInFolderRecursive(subId));

        return files;
    }

    public void DeleteFolderRecursive(int folderId)
    {
        using var conn = GetConnection();
        using var folderCmd = conn.CreateCommand();
        folderCmd.CommandText = "SELECT id FROM folders WHERE parent_id = $parent_id";
        folderCmd.Parameters.AddWithValue("$parent_id", folderId);
        var subFolderIds = new List<int>();
        using var reader = folderCmd.ExecuteReader();
        while (reader.Read())
            subFolderIds.Add(reader.GetInt32(0));

        foreach (var subId in subFolderIds)
            DeleteFolderRecursive(subId);

        using var delFilesCmd = conn.CreateCommand();
        delFilesCmd.CommandText = "DELETE FROM files WHERE folder_id = $folder_id";
        delFilesCmd.Parameters.AddWithValue("$folder_id", folderId);
        delFilesCmd.ExecuteNonQuery();

        using var delFolderCmd = conn.CreateCommand();
        delFolderCmd.CommandText = "DELETE FROM folders WHERE id = $id";
        delFolderCmd.Parameters.AddWithValue("$id", folderId);
        delFolderCmd.ExecuteNonQuery();
    }

    // ─── Storage Statistics ────────────────────────────────────────

    public (int Count, long Size) GetStorageStats()
    {
        using var conn = GetConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(id), COALESCE(SUM(file_size), 0) FROM files";
        using var reader = cmd.ExecuteReader();
        if (reader.Read())
            return (reader.GetInt32(0), reader.GetInt64(1));
        return (0, 0);
    }

    // ─── Search ────────────────────────────────────────────────────

    public List<FolderItem> SearchFolders(string query)
    {
        using var conn = GetConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, name, parent_id FROM folders WHERE name LIKE $query ORDER BY name ASC";
        cmd.Parameters.AddWithValue("$query", $"%{query}%");
        var folders = new List<FolderItem>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            folders.Add(new FolderItem
            {
                Id = reader.GetInt32(0),
                Name = reader.GetString(1),
                ParentId = reader.IsDBNull(2) ? null : reader.GetInt32(2)
            });
        }
        return folders;
    }

    public List<FileItem> SearchFiles(string query)
    {
        using var conn = GetConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, filename, file_size, telegram_message_id, upload_date, folder_id FROM files WHERE filename LIKE $query ORDER BY id DESC";
        cmd.Parameters.AddWithValue("$query", $"%{query}%");
        var files = new List<FileItem>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            files.Add(new FileItem
            {
                Id = reader.GetInt32(0),
                Filename = reader.GetString(1),
                FileSize = reader.GetInt64(2),
                TelegramMessageId = reader.GetInt32(3),
                UploadDate = reader.GetString(4),
                FolderId = reader.IsDBNull(5) ? null : reader.GetInt32(5)
            });
        }
        return files;
    }

    public HashSet<int> GetAllMessageIds()
    {
        using var conn = GetConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT telegram_message_id FROM files";
        var ids = new HashSet<int>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            ids.Add(reader.GetInt32(0));
        return ids;
    }

    public void DeleteDatabase()
    {
        if (File.Exists(_dbPath))
            File.Delete(_dbPath);
        InitDb();
    }
}
