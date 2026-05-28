using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Windows.Storage.Pickers;
using WinRT.Interop;
using System.IO;
using TelegramDrive.Helpers;

namespace TelegramDrive.Views;

public sealed partial class SetupPage : Page
{
    private string _selectedDownloadsDir = "";
    private string _selectedBackupDir = "";
    private int _currentStep = 0; // 0 = welcome, 1 = downloads, 2 = backup

    public SetupPage()
    {
        this.InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        // Pre-fill any already-saved values
        var savedDownloads = App.Database.GetStatusVal("downloads_dir",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"));
        if (!string.IsNullOrEmpty(savedDownloads))
        {
            _selectedDownloadsDir = savedDownloads;
            DownloadsFolderText.Text = savedDownloads;
        }

        var savedBackup = App.Database.GetStatusVal("auto_backup_dir", "");
        if (!string.IsNullOrEmpty(savedBackup))
        {
            _selectedBackupDir = savedBackup;
            BackupFolderText.Text = savedBackup;
            BackupToggle.IsOn = true;
            BackupFolderSection.Visibility = Visibility.Visible;
        }

        ShowStep(0);
    }

    // ── Step navigation ─────────────────────────────────────────

    private void ShowStep(int step)
    {
        _currentStep = step;

        WelcomePanel.Visibility   = step == 0 ? Visibility.Visible : Visibility.Collapsed;
        DownloadsPanel.Visibility = step == 1 ? Visibility.Visible : Visibility.Collapsed;
        BackupPanel.Visibility    = step == 2 ? Visibility.Visible : Visibility.Collapsed;

        // Update step dots
        Step1Dot.Fill = step == 0
            ? new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 42, 171, 238))
            : new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 42, 59, 78));

        Step2Dot.Fill = step == 1
            ? new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 42, 171, 238))
            : new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 42, 59, 78));

        Step3Dot.Fill = step == 2
            ? new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 63, 210, 167))
            : new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 42, 59, 78));
    }

    // ── Welcome page ────────────────────────────────────────────

    private void OnWelcomeNext(object sender, RoutedEventArgs e) => ShowStep(1);

    private void OnSkipSetup(object sender, RoutedEventArgs e) => CompleteSetup();

    // ── Downloads page ──────────────────────────────────────────

    private void OnDownloadsBack(object sender, RoutedEventArgs e) => ShowStep(0);
    private void OnDownloadsNext(object sender, RoutedEventArgs e) => ShowStep(2);

    private async void OnBrowseDownloads(object sender, RoutedEventArgs e)
    {
        var picker = new FolderPicker();
        picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.Downloads;
        picker.FileTypeFilter.Add("*");

        var hwnd = WindowNative.GetWindowHandle(App.MainWindowInstance!);
        InitializeWithWindow.Initialize(picker, hwnd);

        var folder = await picker.PickSingleFolderAsync();
        if (folder != null)
        {
            _selectedDownloadsDir = folder.Path;
            DownloadsFolderText.Text = folder.Path;

            // Save immediately
            App.Database.SetStatusVal("downloads_dir", folder.Path);
        }
    }

    // ── Backup page ─────────────────────────────────────────────

    private void OnBackupBack(object sender, RoutedEventArgs e) => ShowStep(1);

    private void OnBackupToggled(object sender, RoutedEventArgs e)
    {
        BackupFolderSection.Visibility =
            BackupToggle.IsOn ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void OnBrowseBackup(object sender, RoutedEventArgs e)
    {
        var picker = new FolderPicker();
        picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary;
        picker.FileTypeFilter.Add("*");

        var hwnd = WindowNative.GetWindowHandle(App.MainWindowInstance!);
        InitializeWithWindow.Initialize(picker, hwnd);

        var folder = await picker.PickSingleFolderAsync();
        if (folder != null)
        {
            _selectedBackupDir = folder.Path;
            BackupFolderText.Text = folder.Path;

            // Save immediately
            App.Database.SetStatusVal("auto_backup_dir", folder.Path);
        }
    }

    private void OnFinish(object sender, RoutedEventArgs e) => CompleteSetup();

    // ── Finalize ────────────────────────────────────────────────

    private void CompleteSetup()
    {
        // Persist defaults if nothing was explicitly chosen
        if (string.IsNullOrEmpty(_selectedDownloadsDir))
        {
            string def = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            App.Database.SetStatusVal("downloads_dir", def);
        }

        if (!BackupToggle.IsOn)
        {
            // Clear backup dir if user disabled it
            App.Database.SetStatusVal("auto_backup_dir", "");
        }

        // Mark setup as completed so we skip this screen next time
        App.Database.SetStatusVal("setup_completed", "1");

        // Start the backup watcher if a dir was configured
        var backupDir = App.Database.GetStatusVal("auto_backup_dir", "");
        if (!string.IsNullOrEmpty(backupDir))
        {
            App.BackupWatcher.Start(backupDir);
        }

        // Navigate to dashboard
        if (App.MainWindowInstance is MainWindow win)
            win.NavigateToDashboard();
    }
}
