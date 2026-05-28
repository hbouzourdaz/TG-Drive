using Microsoft.UI.Xaml;
using TelegramDrive.Services;
using System.Threading;

namespace TelegramDrive;

/// <summary>
/// Application entry point. Initializes services and launches the main window.
/// </summary>
public partial class App : Application
{
    // ── Singleton Services (shared across the application) ──────
    public static DatabaseService Database { get; private set; } = null!;
    public static TelegramService Telegram { get; private set; } = null!;
    public static LocalStorageService LocalStorage { get; private set; } = null!;
    public static TransferQueueService TransferQueue { get; private set; } = null!;
    public static BackupWatcherService BackupWatcher { get; private set; } = null!;

    public static MainWindow? MainWindowInstance { get; private set; }

    // ── Single-instance enforcement ──────────────────────────────

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern IntPtr FindWindow(string? lpClassName, string lpWindowName);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    private const int SW_RESTORE = 9;

    public App()
    {
        this.InitializeComponent();
        this.UnhandledException += App_UnhandledException;
    }

    private void App_UnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        try
        {
            var crashPath = Path.Combine(AppContext.BaseDirectory, "ui_crash.log");
            File.WriteAllText(crashPath, e.Exception?.ToString() ?? e.Message);
        }
        catch { }
    }

    // ── Single-instance mutex ────────────────────────────────────
    private static System.Threading.Mutex? _singleInstanceMutex;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        // Enforce single instance using a named Mutex
        _singleInstanceMutex = new System.Threading.Mutex(
            initiallyOwned: true,
            name: "TelegramDrive_SingleInstance_Mutex",
            out bool createdNew);

        if (!createdNew)
        {
            // Another instance is running — find it by process name and restore its window
            var procs = System.Diagnostics.Process.GetProcessesByName(
                System.Diagnostics.Process.GetCurrentProcess().ProcessName);

            foreach (var proc in procs)
            {
                if (proc.Id == System.Diagnostics.Process.GetCurrentProcess().Id) continue;

                // Search all windows (including hidden ones) that belong to this process
                IntPtr foundHwnd = IntPtr.Zero;
                EnumWindows((hWnd, _) =>
                {
                    GetWindowThreadProcessId(hWnd, out uint pid);
                    if (pid == (uint)proc.Id)
                    {
                        foundHwnd = hWnd;
                        return false; // Stop enumeration
                    }
                    return true;
                }, IntPtr.Zero);

                if (foundHwnd != IntPtr.Zero)
                {
                    ShowWindow(foundHwnd, SW_RESTORE);
                    SetForegroundWindow(foundHwnd);
                }
            }

            System.Diagnostics.Process.GetCurrentProcess().Kill();
            return;
        }

        try
        {
            // Initialize all services
            Database = new DatabaseService();
            TelegramDrive.Helpers.LocalizationHelper.Initialize();
            Telegram = new TelegramService();
            LocalStorage = new LocalStorageService();
            TransferQueue = new TransferQueueService();
            BackupWatcher = new BackupWatcherService(Database);

            // Launch main window
            MainWindowInstance = new MainWindow();
            MainWindowInstance.Activate();
        }
        catch (Exception ex)
        {
            var crashPath = Path.Combine(AppContext.BaseDirectory, "crash.log");
            System.IO.File.WriteAllText(crashPath, ex.ToString());
            throw;
        }
    }

    public static void SwitchActiveAccount(TelegramDrive.Helpers.UserAccount account)
    {
        try
        {
            // Stop backup watcher if running
            try { BackupWatcher?.Stop(); } catch { }

            // Re-initialize Database with user-specific DB file
            var dbPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, account.DbFile);
            Database = new DatabaseService(dbPath);

            // Re-initialize Telegram client and set session path
            Telegram?.Dispose();
            Thread.Sleep(300); // Let OS release the session file handle
            Telegram = new TelegramService();
            Telegram.SetSessionFile(account.SessionFile);

            // Re-initialize BackupWatcher with new database reference
            BackupWatcher = new BackupWatcherService(Database);
        }
        catch (Exception ex)
        {
            var errPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "switch_account_error.log");
            System.IO.File.WriteAllText(errPath, ex.ToString());
        }
    }

    public static void ResetToDefaultDatabase()
    {
        try
        {
            // Stop backup watcher if running
            try { BackupWatcher?.Stop(); } catch { }

            Database = new DatabaseService();

            Telegram?.Dispose();
            Thread.Sleep(300); // Let OS release the session file handle
            Telegram = new TelegramService();

            BackupWatcher = new BackupWatcherService(Database);
        }
        catch (Exception ex)
        {
            var errPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "reset_db_error.log");
            System.IO.File.WriteAllText(errPath, ex.ToString());
        }
    }
}
