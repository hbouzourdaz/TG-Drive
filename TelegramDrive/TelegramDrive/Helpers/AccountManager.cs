using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace TelegramDrive.Helpers;

public class UserAccount
{
    public string Phone { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string ApiId { get; set; } = string.Empty;
    public string ApiHash { get; set; } = string.Empty;
    public string SessionFile { get; set; } = string.Empty;
    public string DbFile { get; set; } = string.Empty;
}

public static class AccountManager
{
    private static readonly string AccountsFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "accounts.json");
    private static List<UserAccount> _accounts = new();

    static AccountManager()
    {
        LoadAccounts();
    }

    public static List<UserAccount> GetAccounts()
    {
        LoadAccounts();
        return _accounts;
    }

    public static void LoadAccounts()
    {
        try
        {
            if (File.Exists(AccountsFilePath))
            {
                var json = File.ReadAllText(AccountsFilePath);
                _accounts = JsonSerializer.Deserialize<List<UserAccount>>(json) ?? new List<UserAccount>();
            }
            else
            {
                _accounts = new List<UserAccount>();
            }
        }
        catch
        {
            _accounts = new List<UserAccount>();
        }
    }

    public static void SaveAccounts()
    {
        try
        {
            var options = new JsonSerializerOptions { WriteIndented = true };
            var json = JsonSerializer.Serialize(_accounts, options);
            File.WriteAllText(AccountsFilePath, json);
        }
        catch { }
    }

    public static void SaveAccount(UserAccount account)
    {
        LoadAccounts();
        var existing = _accounts.Find(a => a.Phone == account.Phone);
        if (existing != null)
        {
            existing.Name = account.Name;
            existing.Username = account.Username;
            existing.ApiId = account.ApiId;
            existing.ApiHash = account.ApiHash;
            existing.SessionFile = account.SessionFile;
            existing.DbFile = account.DbFile;
        }
        else
        {
            _accounts.Add(account);
        }
        SaveAccounts();
    }

    public static void RemoveAccount(string phone)
    {
        LoadAccounts();
        var account = _accounts.Find(a => a.Phone == phone);
        if (account != null)
        {
            _accounts.Remove(account);
            SaveAccounts();

            // Delete session and db files associated with the account
            try
            {
                var sessionPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, account.SessionFile);
                if (File.Exists(sessionPath)) File.Delete(sessionPath);
            }
            catch { }

            try
            {
                var dbPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, account.DbFile);
                if (File.Exists(dbPath)) File.Delete(dbPath);
            }
            catch { }
        }
    }
}
