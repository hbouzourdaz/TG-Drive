using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using TelegramDrive.Helpers;
using TelegramDrive.Models;
using Windows.Storage.Pickers;
using WinRT.Interop;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace TelegramDrive.Views;

public sealed partial class DashboardPage : Page
{
    private string _activeBackend = "cloud";
    private int? _currentFolderId;
    private string _localRelPath = "";
    private string _activeFilter = "all";
    private string _downloadsDir = "";
    private readonly Dictionary<string, FileItem> _selectedFiles = new();
    private DispatcherTimer? _searchTimer;
    private DispatcherTimer? _pulseTimer;

    public DashboardPage()
    {
        this.InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        ApplyLocalization();

        // Set user info
        var (name, handle) = App.Telegram.GetUserInfo();
        UserNameText.Text = name;
        UserHandleText.Text = handle;

        _downloadsDir = App.Database.GetStatusVal("downloads_dir",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads")) ?? "";

        // Set cloud as active
        UpdateBackendHighlight();
        UpdateFilterStyles();
        RefreshFiles();

        // Setup pulse animation timer
        _pulseTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(800) };
        _pulseTimer.Tick += PulseTimer_Tick;
        _pulseTimer.Start();

        // Start auto-backup watcher if configured
        var backupDir = App.Database.GetStatusVal("auto_backup_dir", "");
        if (!string.IsNullOrEmpty(backupDir))
        {
            App.BackupWatcher.NewFileDetected += OnBackupFileDetected;
            App.BackupWatcher.Start(backupDir);
        }

        // Subscribe to transfer events
        App.TransferQueue.JobCompleted += OnJobCompleted;
        App.TransferQueue.JobFailed += OnJobFailed;
        App.TransferQueue.JobCancelled += OnJobCancelled;
        App.TransferQueue.JobProgress += OnJobProgress;
        App.TransferQueue.JobStarted += OnJobStarted;
        App.TransferQueue.QueueChanged += OnQueueChanged;
    }

    // ── Pulse Animation ────────────────────────────────────────
    private bool _pulseState;
    private void PulseTimer_Tick(object? sender, object e)
    {
        if (_activeBackend == "cloud")
        {
            _pulseState = !_pulseState;
            StatusDot.Foreground = new SolidColorBrush(_pulseState
                ? ColorHelper.FromArgb(255, 63, 242, 103)
                : ColorHelper.FromArgb(255, 43, 202, 79));
        }
    }

    // ── Backend Toggle ─────────────────────────────────────────
    private void OnCloudClick(object sender, RoutedEventArgs e) => SwitchBackend("cloud");
    private void OnLocalClick(object sender, RoutedEventArgs e) => SwitchBackend("local");

    private void SwitchBackend(string backend)
    {
        _activeBackend = backend;
        UpdateBackendHighlight();

        if (backend == "cloud")
        {
            ChatTitle.Text = LocalizationHelper.Get("saved_messages");
            ChatSubtitle.Text = LocalizationHelper.Get("unlimited_cloud");
            StatusDot.Foreground = (Brush)Application.Current.Resources["TgSuccessBrush"];
            StorageTitle.Text = LocalizationHelper.CurrentLanguage == "ar" ? "إحصائيات التخزين السحابي" : "Cloud Storage Stats";
            ProfileCard.Visibility = Visibility.Visible;
            SyncBtn.Visibility = Visibility.Visible;
        }
        else
        {
            ChatTitle.Text = LocalizationHelper.Get("local_disk");
            ChatSubtitle.Text = LocalizationHelper.CurrentLanguage == "ar" ? "مستكشف المجلد • قرص مساحة العمل" : "folder explorer • workspace disk";
            StatusDot.Foreground = (Brush)Application.Current.Resources["TgTextSecondaryBrush"];
            StorageTitle.Text = LocalizationHelper.CurrentLanguage == "ar" ? "تخزين القرص المحلي" : "Local Disk Storage";
            ProfileCard.Visibility = Visibility.Collapsed;
            SyncBtn.Visibility = Visibility.Collapsed;
        }

        SearchBox.Text = "";
        _selectedFiles.Clear();
        UpdateSelectionBar();
        RefreshFiles();
    }

    private void UpdateBackendHighlight()
    {
        CloudBtn.Background = _activeBackend == "cloud"
            ? (Brush)Application.Current.Resources["TgPrimaryHoverBrush"]
            : new SolidColorBrush(Colors.Transparent);
        LocalBtn.Background = _activeBackend == "local"
            ? (Brush)Application.Current.Resources["TgPrimaryHoverBrush"]
            : new SolidColorBrush(Colors.Transparent);
    }

    // ── Filter Capsules ────────────────────────────────────────
    private void OnFilterClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string filter)
        {
            _activeFilter = filter;
            UpdateFilterStyles();
            RefreshFiles();
        }
    }

    private void UpdateFilterStyles()
    {
        foreach (var child in FilterPanel.Children)
        {
            if (child is Button btn && btn.Tag is string tag)
            {
                if (tag == _activeFilter)
                    btn.Style = (Style)Application.Current.Resources["TgCapsuleActiveStyle"];
                else
                    btn.Style = (Style)Application.Current.Resources["TgCapsuleInactiveStyle"];
            }
        }
    }

    // ── Search ─────────────────────────────────────────────────
    private void OnSearchChanged(object sender, TextChangedEventArgs e)
    {
        _searchTimer?.Stop();
        _searchTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        _searchTimer.Tick += (s, args) =>
        {
            _searchTimer.Stop();
            RefreshFiles();
        };
        _searchTimer.Start();
    }

    // ── Breadcrumbs ────────────────────────────────────────────
    private void RenderBreadcrumbs()
    {
        BreadcrumbPanel.Children.Clear();

        if (_activeBackend == "cloud")
        {
            var path = App.Database.GetBreadcrumbPath(_currentFolderId);
            for (int i = 0; i < path.Count; i++)
            {
                if (i > 0)
                {
                    BreadcrumbPanel.Children.Add(new TextBlock
                    {
                        Text = " › ",
                        Foreground = (Brush)Application.Current.Resources["TgTextSecondaryBrush"],
                        FontSize = 13, FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                        VerticalAlignment = VerticalAlignment.Center
                    });
                }

                var (name, id) = path[i];
                var captured = id;
                var btn = new Button
                {
                    Content = name,
                    Background = new SolidColorBrush(Colors.Transparent),
                    Foreground = id == _currentFolderId
                        ? (Brush)Application.Current.Resources["TgTextPrimaryBrush"]
                        : (Brush)Application.Current.Resources["TgPrimaryBrush"],
                    BorderThickness = new Thickness(0),
                    FontSize = 12,
                    FontWeight = id == _currentFolderId
                        ? Microsoft.UI.Text.FontWeights.Bold
                        : Microsoft.UI.Text.FontWeights.Normal,
                    Padding = new Thickness(4, 0, 4, 0),
                    Height = 26
                };
                btn.Click += (s, e) => NavigateToFolder(captured);
                BreadcrumbPanel.Children.Add(btn);
            }
        }
        else
        {
            // Local path breadcrumbs
            var rootBtn = new Button
            {
                Content = "Local Root",
                Background = new SolidColorBrush(Colors.Transparent),
                Foreground = string.IsNullOrEmpty(_localRelPath)
                    ? (Brush)Application.Current.Resources["TgTextPrimaryBrush"]
                    : (Brush)Application.Current.Resources["TgPrimaryBrush"],
                BorderThickness = new Thickness(0),
                FontSize = 12, Height = 26
            };
            rootBtn.Click += (s, e) => NavigateToLocalPath("");
            BreadcrumbPanel.Children.Add(rootBtn);

            if (!string.IsNullOrEmpty(_localRelPath))
            {
                var parts = _localRelPath.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
                string accumulated = "";
                for (int i = 0; i < parts.Length; i++)
                {
                    BreadcrumbPanel.Children.Add(new TextBlock
                    {
                        Text = " › ",
                        Foreground = (Brush)Application.Current.Resources["TgTextSecondaryBrush"],
                        FontSize = 13, VerticalAlignment = VerticalAlignment.Center
                    });

                    accumulated = Path.Combine(accumulated, parts[i]);
                    var capturedPath = accumulated;
                    var partBtn = new Button
                    {
                        Content = parts[i],
                        Background = new SolidColorBrush(Colors.Transparent),
                        Foreground = i == parts.Length - 1
                            ? (Brush)Application.Current.Resources["TgTextPrimaryBrush"]
                            : (Brush)Application.Current.Resources["TgPrimaryBrush"],
                        BorderThickness = new Thickness(0),
                        FontSize = 12, Height = 26
                    };
                    partBtn.Click += (s, e) => NavigateToLocalPath(capturedPath);
                    BreadcrumbPanel.Children.Add(partBtn);
                }
            }
        }
    }

    private void NavigateToFolder(int? folderId)
    {
        _currentFolderId = folderId;
        SearchBox.Text = "";
        RefreshFiles();
    }

    private void NavigateToLocalPath(string relPath)
    {
        _localRelPath = relPath;
        SearchBox.Text = "";
        RefreshFiles();
    }

    // ── Main File List Render ──────────────────────────────────
    private void RefreshFiles()
    {
        FileListPanel.Children.Clear();
        RenderBreadcrumbs();

        string searchQuery = SearchBox.Text.Trim();

        List<FolderItem> folders;
        List<FileItem> files;

        if (_activeBackend == "cloud")
        {
            // Update storage stats
            var (count, size) = App.Database.GetStorageStats();
            StorageUsage.Text = LocalizationHelper.CurrentLanguage == "ar"
                ? $"{count} ملفات • {SizeFormatter.Format(size)}"
                : $"{count} files • {SizeFormatter.Format(size)}";
            double gdriveCap = 15.0 * 1024 * 1024 * 1024;
            StorageBar.Value = size > 0 ? Math.Min(size / gdriveCap * 100, 100) : 0;
            StorageCap.Text = LocalizationHelper.CurrentLanguage == "ar"
                ? $"{SizeFormatter.Format(size)} من مساحة غير محدودة (يعادل 15 جيجابايت جوجل درايف)"
                : $"{SizeFormatter.Format(size)} of Unlimited (15GB GDrive Equivalent)";

            // Update counters
            CloudCountText.Text = count.ToString();
            CloudCountBadge.Visibility = count > 0 ? Visibility.Visible : Visibility.Collapsed;

            var localCount = App.LocalStorage.GetTotalItemCount();
            LocalCountText.Text = localCount.ToString();
            LocalCountBadge.Visibility = localCount > 0 ? Visibility.Visible : Visibility.Collapsed;

            if (!string.IsNullOrEmpty(searchQuery))
            {
                folders = App.Database.SearchFolders(searchQuery);
                files = App.Database.SearchFiles(searchQuery);
                StatusFooter.Text = LocalizationHelper.CurrentLanguage == "ar"
                    ? $"البحث السحابي العام: {folders.Count} مجلدات و {files.Count} ملفات."
                    : $"Global Cloud Search: {folders.Count} folders and {files.Count} files.";
            }
            else
            {
                folders = App.Database.GetFoldersInFolder(_currentFolderId);
                files = App.Database.GetFilesInFolder(_currentFolderId);
                StatusFooter.Text = LocalizationHelper.CurrentLanguage == "ar"
                    ? $"المجلد السحابي النشط: {folders.Count} مجلدات، {files.Count} ملفات."
                    : $"Active Cloud Folder: {folders.Count} folders, {files.Count} files.";
            }
        }
        else
        {
            // Local storage
            var (localFileCount, localSize) = App.LocalStorage.GetStorageStats();
            StorageUsage.Text = LocalizationHelper.CurrentLanguage == "ar"
                ? $"{localFileCount} ملفات • {SizeFormatter.Format(localSize)}"
                : $"{localFileCount} files • {SizeFormatter.Format(localSize)}";
            double localCap = 50.0 * 1024 * 1024 * 1024;
            StorageBar.Value = localSize > 0 ? Math.Min(localSize / localCap * 100, 100) : 0;
            StorageCap.Text = LocalizationHelper.CurrentLanguage == "ar"
                ? $"{SizeFormatter.Format(localSize)} من {SizeFormatter.Format((long)localCap)} الحد الأقصى المحلي"
                : $"{SizeFormatter.Format(localSize)} of 50.0 GB Local limit";

            LocalCountText.Text = localFileCount.ToString();
            LocalCountBadge.Visibility = localFileCount > 0 ? Visibility.Visible : Visibility.Collapsed;
            try
            {
                var (cloudCount, _) = App.Database.GetStorageStats();
                CloudCountText.Text = cloudCount.ToString();
                CloudCountBadge.Visibility = cloudCount > 0 ? Visibility.Visible : Visibility.Collapsed;
            }
            catch { }

            var (localFolders, localFiles) = App.LocalStorage.ScanDirectory(_localRelPath, searchQuery);

            folders = localFolders.Select(f => new FolderItem
            {
                Name = f.Name, LocalPath = f.FullPath, UploadDate = f.Date
            }).ToList();

            files = localFiles.Select(f => new FileItem
            {
                Filename = f.Name, LocalPath = f.FullPath, FileSize = f.Size, UploadDate = f.Date
            }).ToList();

            StatusFooter.Text = LocalizationHelper.CurrentLanguage == "ar"
                ? $"المجلد المحلي النشط: {folders.Count} مجلدات، {files.Count} ملفات."
                : $"Active Local Directory: {folders.Count} folders, {files.Count} files.";
        }

        // Apply type filter
        if (_activeFilter != "all")
        {
            folders.Clear();
            files = files.Where(f => FileTypeHelper.MatchesFilter(f.Filename, _activeFilter)).ToList();
        }

        // Clean stale selections
        var activeFilenames = files.Select(f => f.Filename).ToHashSet();
        foreach (var key in _selectedFiles.Keys.Where(k => !activeFilenames.Contains(k)).ToList())
            _selectedFiles.Remove(key);

        // Update select-all checkbox
        SelectAllCheck.IsChecked = files.Count > 0 && files.All(f => _selectedFiles.ContainsKey(f.Filename));
        UpdateSelectionBar();

        // Empty state
        if (folders.Count == 0 && files.Count == 0)
        {
            FileListPanel.Children.Add(new TextBlock
            {
                Text = LocalizationHelper.CurrentLanguage == "ar"
                    ? "سياق هذا المجلد فارغ.\nانقر فوق 'رفع ملف' أو 'مجلد جديد' لبدء إضافة العناصر."
                    : "This folder context is empty.\nClick 'Upload File' or 'New Folder' to start adding items.",
                Foreground = (Brush)Application.Current.Resources["TgTextSecondaryBrush"],
                FontSize = 13, FontStyle = Windows.UI.Text.FontStyle.Italic,
                HorizontalAlignment = HorizontalAlignment.Center,
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 80, 0, 0)
            });
            return;
        }

        // Render folders
        foreach (var folder in folders)
            AddFolderCard(folder);

        // Render files
        foreach (var file in files)
            AddFileCard(file);
    }

    // ── Folder Card ────────────────────────────────────────────
    private void AddFolderCard(FolderItem folder)
    {
        var card = new Border
        {
            Background = (Brush)Application.Current.Resources["TgBgCardBrush"],
            CornerRadius = new CornerRadius(12),
            Height = 60, Padding = new Thickness(12, 9, 12, 9)
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(42) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // Badge
        var badge = new Border
        {
            Width = 42, Height = 42, CornerRadius = new CornerRadius(21),
            Background = new SolidColorBrush(ColorHelper.FromArgb(255, 40, 95, 80)),
            Child = new TextBlock
            {
                Text = "📁", FontSize = 16,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
        Grid.SetColumn(badge, 0);
        grid.Children.Add(badge);

        // Details
        var details = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
        details.Children.Add(new TextBlock
        {
            Text = folder.Name,
            Foreground = (Brush)Application.Current.Resources["TgPrimaryBrush"],
            FontSize = 13, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        });
        details.Children.Add(new TextBlock
        {
            Text = LocalizationHelper.CurrentLanguage == "ar" ? "مجلد • انقر مرتين للفتح" : "Folder • Double-click to open",
            Foreground = (Brush)Application.Current.Resources["TgTextSecondaryBrush"],
            FontSize = 11
        });
        Grid.SetColumn(details, 1);
        grid.Children.Add(details);

        // Actions
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        var openBtn = new Button
        {
            Content = LocalizationHelper.CurrentLanguage == "ar" ? "فتح" : "Open", Width = 65, Height = 28, CornerRadius = new CornerRadius(6),
            Background = (Brush)Application.Current.Resources["TgBgInputBrush"],
            Foreground = (Brush)Application.Current.Resources["TgPrimaryBrush"],
            BorderThickness = new Thickness(0), FontSize = 11, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        };
        openBtn.Click += (s, e) =>
        {
            if (_activeBackend == "cloud") NavigateToFolder(folder.Id);
            else if (folder.LocalPath != null) NavigateToLocalPath(Path.GetRelativePath(App.LocalStorage.RootPath, folder.LocalPath));
        };
        actions.Children.Add(openBtn);

        var delBtn = new Button
        {
            Content = LocalizationHelper.CurrentLanguage == "ar" ? "حذف" : "Delete", Width = 65, Height = 28, CornerRadius = new CornerRadius(6),
            Background = new SolidColorBrush(Colors.Transparent),
            Foreground = (Brush)Application.Current.Resources["TgErrorBrush"],
            BorderThickness = new Thickness(0), FontSize = 11
        };
        delBtn.Click += async (s, e) =>
        {
            var dialog = new ContentDialog
            {
                Title = "Confirm Delete",
                Content = $"Are you sure you want to delete folder '{folder.Name}'?",
                PrimaryButtonText = "Delete", CloseButtonText = "Cancel",
                XamlRoot = this.XamlRoot, RequestedTheme = ElementTheme.Dark
            };
            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            {
                if (_activeBackend == "cloud")
                {
                    var filesToDel = App.Database.GetFilesInFolderRecursive(folder.Id);
                    var msgIds = filesToDel.Select(f => f.TelegramMessageId).ToList();
                    if (msgIds.Count > 0) await App.Telegram.DeleteMessagesAsync(msgIds);
                    App.Database.DeleteFolderRecursive(folder.Id);
                }
                else if (folder.LocalPath != null)
                {
                    App.LocalStorage.DeleteFolder(folder.LocalPath);
                }
                RefreshFiles();
            }
        };
        actions.Children.Add(delBtn);
        Grid.SetColumn(actions, 2);
        grid.Children.Add(actions);

        // Timestamp
        string time = ExtractTime(folder.UploadDate ?? "");
        string check = _activeBackend == "cloud" ? " ✓✓" : " ✓";
        var timeLbl = new TextBlock
        {
            Text = $"{time}{check}",
            Foreground = (Brush)Application.Current.Resources["TgTextAccentBrush"],
            FontSize = 9, FontStyle = Windows.UI.Text.FontStyle.Italic,
            VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(10, 0, 0, 0)
        };
        Grid.SetColumn(timeLbl, 3);
        grid.Children.Add(timeLbl);

        card.Child = grid;

        // Double-click
        card.DoubleTapped += (s, e) =>
        {
            if (_activeBackend == "cloud") NavigateToFolder(folder.Id);
            else if (folder.LocalPath != null) NavigateToLocalPath(Path.GetRelativePath(App.LocalStorage.RootPath, folder.LocalPath));
        };

        // Hover
        card.PointerEntered += (s, e) => card.Background = new SolidColorBrush(ColorHelper.FromArgb(255, 32, 48, 64));
        card.PointerExited += (s, e) => card.Background = (Brush)Application.Current.Resources["TgBgCardBrush"];

        FileListPanel.Children.Add(card);
    }

    // ── File Card ──────────────────────────────────────────────
    private void AddFileCard(FileItem file)
    {
        bool isSelected = _selectedFiles.ContainsKey(file.Filename);

        var card = new Border
        {
            Background = isSelected
                ? (Brush)Application.Current.Resources["TgBgSelectedBrush"]
                : (Brush)Application.Current.Resources["TgBgCardBrush"],
            CornerRadius = new CornerRadius(12),
            Height = 60, Padding = new Thickness(10, 9, 12, 9)
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // Checkbox
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(42) }); // Badge
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // Details
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // Actions
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // Time

        // Checkbox
        var chk = new CheckBox
        {
            IsChecked = isSelected,
            MinWidth = 0, Padding = new Thickness(0),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 4, 0)
        };
        chk.Checked += (s, e) => { _selectedFiles[file.Filename] = file; UpdateSelectionBar(); };
        chk.Unchecked += (s, e) => { _selectedFiles.Remove(file.Filename); UpdateSelectionBar(); };
        Grid.SetColumn(chk, 0);
        grid.Children.Add(chk);

        // Badge
        string ext = FileTypeHelper.GetShortExtension(file.Filename);
        var badgeColor = FileTypeHelper.GetBadgeColor(file.Filename);
        var badge = new Border
        {
            Width = 42, Height = 42, CornerRadius = new CornerRadius(21),
            Background = new SolidColorBrush(badgeColor),
            Margin = new Thickness(0, 0, 0, 0),
            Child = new TextBlock
            {
                Text = ext, FontSize = 9, FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                Foreground = new SolidColorBrush(Colors.White),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
        Grid.SetColumn(badge, 1);
        grid.Children.Add(badge);

        // Details
        var details = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
        details.Children.Add(new TextBlock
        {
            Text = file.Filename,
            Foreground = (Brush)Application.Current.Resources["TgTextPrimaryBrush"],
            FontSize = 13, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 350
        });
        details.Children.Add(new TextBlock
        {
            Text = $"{SizeFormatter.Format(file.FileSize)} • {file.UploadDate}",
            Foreground = (Brush)Application.Current.Resources["TgTextSecondaryBrush"],
            FontSize = 11
        });
        Grid.SetColumn(details, 2);
        grid.Children.Add(details);

        // Actions
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center };

        // Preview
        var prevBtn = new Button
        {
            Content = "👁️", Width = 34, Height = 28, CornerRadius = new CornerRadius(6),
            Background = (Brush)Application.Current.Resources["TgBgInputBrush"],
            Foreground = new SolidColorBrush(Colors.White),
            BorderThickness = new Thickness(0), FontSize = 12
        };
        prevBtn.Click += (s, e) => PreviewFile(file);
        actions.Children.Add(prevBtn);

        // Rename
        var renBtn = new Button
        {
            Content = "✏️", Width = 34, Height = 28, CornerRadius = new CornerRadius(6),
            Background = (Brush)Application.Current.Resources["TgBgInputBrush"],
            Foreground = new SolidColorBrush(Colors.White),
            BorderThickness = new Thickness(0), FontSize = 12
        };
        renBtn.Click += async (s, e) => await RenameFile(file);
        actions.Children.Add(renBtn);

        // Download
        var dlBtn = new Button
        {
            Content = LocalizationHelper.CurrentLanguage == "ar" ? "تحميل" : "Download", Width = 80, Height = 28, CornerRadius = new CornerRadius(6),
            Background = (Brush)Application.Current.Resources["TgPrimaryBrush"],
            Foreground = new SolidColorBrush(Colors.White),
            BorderThickness = new Thickness(0), FontSize = 11, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        };
        dlBtn.Click += (s, e) => DownloadFile(file);
        actions.Children.Add(dlBtn);

        // Delete
        var delBtn = new Button
        {
            Content = LocalizationHelper.CurrentLanguage == "ar" ? "حذف" : "Delete", Width = 65, Height = 28, CornerRadius = new CornerRadius(6),
            Background = new SolidColorBrush(Colors.Transparent),
            Foreground = (Brush)Application.Current.Resources["TgErrorBrush"],
            BorderThickness = new Thickness(0), FontSize = 11
        };
        delBtn.Click += async (s, e) => await DeleteFile(file);
        actions.Children.Add(delBtn);

        Grid.SetColumn(actions, 3);
        grid.Children.Add(actions);

        // Timestamp
        string time = ExtractTime(file.UploadDate);
        string check = _activeBackend == "cloud" ? " ✓✓" : " ✓";
        var checkColor = _activeBackend == "cloud"
            ? (Brush)Application.Current.Resources["TgTextAccentBrush"]
            : (Brush)Application.Current.Resources["TgTextSecondaryBrush"];
        var timeLbl = new TextBlock
        {
            Text = $"{time}{check}", Foreground = checkColor,
            FontSize = 9, FontStyle = Windows.UI.Text.FontStyle.Italic,
            VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(10, 0, 0, 0)
        };
        Grid.SetColumn(timeLbl, 4);
        grid.Children.Add(timeLbl);

        card.Child = grid;

        // Context menu
        var flyout = new MenuFlyout();
        flyout.Items.Add(new MenuFlyoutItem { Text = LocalizationHelper.CurrentLanguage == "ar" ? "👁️ معاينة" : "👁️ Preview", Icon = new FontIcon { Glyph = "\uE7B3" } });
        ((MenuFlyoutItem)flyout.Items[0]).Click += (s, e) => PreviewFile(file);
        flyout.Items.Add(new MenuFlyoutItem { Text = LocalizationHelper.CurrentLanguage == "ar" ? "📥 تحميل" : "📥 Download" });
        ((MenuFlyoutItem)flyout.Items[1]).Click += (s, e) => DownloadFile(file);
        flyout.Items.Add(new MenuFlyoutSeparator());
        flyout.Items.Add(new MenuFlyoutItem { Text = LocalizationHelper.CurrentLanguage == "ar" ? "✏️ إعادة تسمية" : "✏️ Rename" });
        ((MenuFlyoutItem)flyout.Items[3]).Click += async (s, e) => await RenameFile(file);
        flyout.Items.Add(new MenuFlyoutSeparator());
        flyout.Items.Add(new MenuFlyoutItem { Text = LocalizationHelper.CurrentLanguage == "ar" ? "🗑️ حذف" : "🗑️ Delete" });
        ((MenuFlyoutItem)flyout.Items[5]).Click += async (s, e) => await DeleteFile(file);
        card.ContextFlyout = flyout;

        // Hover
        card.PointerEntered += (s, e) =>
        {
            if (!_selectedFiles.ContainsKey(file.Filename))
                card.Background = new SolidColorBrush(ColorHelper.FromArgb(255, 32, 48, 64));
        };
        card.PointerExited += (s, e) =>
        {
            card.Background = _selectedFiles.ContainsKey(file.Filename)
                ? (Brush)Application.Current.Resources["TgBgSelectedBrush"]
                : (Brush)Application.Current.Resources["TgBgCardBrush"];
        };

        // Double-click to download
        card.DoubleTapped += (s, e) => DownloadFile(file);

        FileListPanel.Children.Add(card);
    }

    // ── File Operations ────────────────────────────────────────
    private async void PreviewFile(FileItem file)
    {
        string? tempPath = null;

        // If the file is on the cloud, we must download it to a temp previews directory first
        if (_activeBackend == "cloud" && file.LocalPath == null)
        {
            try
            {
                string tempDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".previews");
                Directory.CreateDirectory(tempDir);
                tempPath = Path.Combine(tempDir, file.Filename);

                // If already downloaded in previews folder, bypass downloading!
                if (!File.Exists(tempPath))
                {
                    // Create a custom dialog showing a download progress indicator
                    var progressRing = new ProgressRing
                    {
                        IsActive = true,
                        Width = 40,
                        Height = 40,
                        Margin = new Thickness(0, 0, 0, 10),
                        HorizontalAlignment = HorizontalAlignment.Center
                    };

                    var progressText = new TextBlock
                    {
                        Text = "Downloading preview (0%)...",
                        HorizontalAlignment = HorizontalAlignment.Center,
                        Foreground = (Brush)Application.Current.Resources["TgTextSecondaryBrush"],
                        FontSize = 12
                    };

                    var panel = new StackPanel
                    {
                        Spacing = 10,
                        Padding = new Thickness(20),
                        HorizontalAlignment = HorizontalAlignment.Center
                    };
                    panel.Children.Add(progressRing);
                    panel.Children.Add(progressText);

                    var downloadDialog = new ContentDialog
                    {
                        Title = LocalizationHelper.CurrentLanguage == "ar" ? "جاري تحميل المعاينة" : "Downloading Preview",
                        Content = panel,
                        XamlRoot = this.XamlRoot,
                        RequestedTheme = ElementTheme.Dark
                    };

                    // Run the download in background
                    var cts = new CancellationTokenSource();
                    downloadDialog.Closed += (s, e) => cts.Cancel();

                    var downloadTask = App.Telegram.DownloadFileAsync(file.TelegramMessageId, tempPath, (current, total) =>
                    {
                        double pct = total > 0 ? (double)current / total : 0;
                        DispatcherQueue.TryEnqueue(() =>
                        {
                            progressText.Text = LocalizationHelper.CurrentLanguage == "ar" 
                                ? $"جاري تحميل المعاينة ({(int)(pct * 100)}%)..."
                                : $"Downloading preview ({(int)(pct * 100)}%)...";
                        });
                    }, cts.Token);

                    // Show dialog and wait for it
                    var showDialogTask = downloadDialog.ShowAsync();
                    
                    try
                    {
                        await downloadTask;
                        downloadDialog.Hide();
                    }
                    catch (Exception ex)
                    {
                        downloadDialog.Hide();
                        if (File.Exists(tempPath)) try { File.Delete(tempPath); } catch { }
                        ShowInfoBar($"Failed to download preview: {ex.Message}", "error");
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                ShowInfoBar($"Preview initialization failed: {ex.Message}", "error");
                return;
            }
        }
        else
        {
            tempPath = file.LocalPath;
        }

        if (tempPath == null || !File.Exists(tempPath))
        {
            ShowInfoBar("File not found for preview.", "error");
            return;
        }

        // Now show the preview!
        if (FileTypeHelper.CanPreviewAsText(file.Filename))
        {
            try
            {
                string content = await File.ReadAllTextAsync(tempPath);
                if (content.Length > 50000) content = content[..50000] + "\n... (truncated)";
                var dialog = new ContentDialog
                {
                    Title = file.Filename, CloseButtonText = "Close",
                    XamlRoot = this.XamlRoot, RequestedTheme = ElementTheme.Dark,
                    Content = new ScrollViewer
                    {
                        Content = new TextBlock
                        {
                            Text = content, TextWrapping = TextWrapping.Wrap,
                            Foreground = (Brush)Application.Current.Resources["TgTextPrimaryBrush"],
                            FontFamily = new FontFamily("Consolas"), FontSize = 12,
                            IsTextSelectionEnabled = true
                        },
                        MaxHeight = 500
                    }
                };
                await dialog.ShowAsync();
            }
            catch (Exception ex)
            {
                ShowInfoBar($"Failed to read file: {ex.Message}", "error");
            }
        }
        else
        {
            // Try to open with system default
            try
            {
                var processInfo = new System.Diagnostics.ProcessStartInfo(tempPath) { UseShellExecute = true };
                System.Diagnostics.Process.Start(processInfo);
            }
            catch 
            { 
                ShowInfoBar(LocalizationHelper.CurrentLanguage == "ar" ? "فشل فتح الملف باستخدام عارض النظام." : "Failed to open file with system viewer.", "error"); 
            }
        }
    }

    private async Task RenameFile(FileItem file)
    {
        var input = new TextBox { Text = file.Filename, Style = (Style)Application.Current.Resources["TgTextBoxStyle"] };
        var dialog = new ContentDialog
        {
            Title = LocalizationHelper.CurrentLanguage == "ar" ? "إعادة تسمية الملف" : "Rename File", Content = input,
            PrimaryButtonText = LocalizationHelper.CurrentLanguage == "ar" ? "تعديل الاسم" : "Rename", CloseButtonText = LocalizationHelper.Get("cancel_btn"),
            XamlRoot = this.XamlRoot, RequestedTheme = ElementTheme.Dark
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            string newName = input.Text.Trim();
            if (!string.IsNullOrEmpty(newName) && newName != file.Filename)
            {
                try
                {
                    if (_activeBackend == "cloud")
                        App.Database.RenameFile(file.Id, newName);
                    else if (file.LocalPath != null)
                        App.LocalStorage.RenameFile(file.LocalPath, newName);

                    ShowInfoBar(LocalizationHelper.CurrentLanguage == "ar" ? $"تم تغيير الاسم إلى '{newName}'" : $"Renamed to '{newName}'", "success");
                    RefreshFiles();
                }
                catch (Exception ex) { ShowInfoBar(LocalizationHelper.CurrentLanguage == "ar" ? $"فشل تغيير الاسم: {ex.Message}" : $"Rename failed: {ex.Message}", "error"); }
            }
        }
    }

    private void DownloadFile(FileItem file)
    {
        if (_activeBackend == "cloud")
        {
            App.TransferQueue.AddJob(TransferType.Download,
                filename: file.Filename, messageId: file.TelegramMessageId,
                fileSize: file.FileSize, destDir: _downloadsDir);
        }
        else if (file.LocalPath != null)
        {
            App.TransferQueue.AddJob(TransferType.Download,
                filename: file.Filename, sourcePath: file.LocalPath,
                fileSize: file.FileSize, destDir: _downloadsDir);
        }
    }

    private async Task DeleteFile(FileItem file)
    {
        var dialog = new ContentDialog
        {
            Title = LocalizationHelper.Get("confirm_delete_title"),
            Content = LocalizationHelper.CurrentLanguage == "ar"
                ? $"هل أنت متأكد من حذف '{file.Filename}'؟\nسيتم إزالته نهائياً."
                : $"Are you sure you want to delete '{file.Filename}'?\nThis will remove it permanently.",
            PrimaryButtonText = LocalizationHelper.Get("delete_btn"), CloseButtonText = LocalizationHelper.Get("cancel_btn"),
            XamlRoot = this.XamlRoot, RequestedTheme = ElementTheme.Dark
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            try
            {
                if (_activeBackend == "cloud")
                {
                    await App.Telegram.DeleteMessagesAsync(new List<int> { file.TelegramMessageId });
                    App.Database.DeleteFile(file.Id);
                }
                else if (file.LocalPath != null)
                {
                    App.LocalStorage.DeleteFile(file.LocalPath);
                }
                ShowInfoBar($"Deleted '{file.Filename}' successfully.", "success");
                RefreshFiles();
            }
            catch (Exception ex) { ShowInfoBar($"Delete failed: {ex.Message}", "error"); }
        }
    }

    // ── Transfer Queue Execution ───────────────────────────────
    private async void ExecuteActiveJob()
    {
        var job = App.TransferQueue.ActiveJob;
        if (job == null) return;

        var ct = App.TransferQueue.GetActiveCancellationToken();

        try
        {
            if (job.Type == TransferType.Upload)
            {
                if (_activeBackend == "cloud" && job.FilePath != null)
                {
                    int msgId = await App.Telegram.UploadFileAsync(job.FilePath,
                        (current, total) =>
                        {
                            double pct = total > 0 ? (double)current / total : 0;
                            DispatcherQueue.TryEnqueue(() => App.TransferQueue.ReportProgress(current, total, pct));
                        }, ct);

                    string uploadDate = DateTime.Now.ToString("HH:mm");
                    App.Database.AddFile(job.Filename, job.FileSize, msgId, uploadDate, job.FolderId);
                    DispatcherQueue.TryEnqueue(() => App.TransferQueue.MarkCompleted($"Uploaded '{job.Filename}' to Saved Messages!"));
                }
                else if (job.FilePath != null)
                {
                    App.LocalStorage.CopyFile(job.FilePath, _localRelPath, job.Filename);
                    DispatcherQueue.TryEnqueue(() => App.TransferQueue.MarkCompleted($"Copied '{job.Filename}' to Local storage!"));
                }
            }
            else if (job.Type == TransferType.Download)
            {
                if (job.MessageId.HasValue)
                {
                    // Always save to Local Disk (local_storage_drive) so it appears in the Local tab
                    string localDestPath = Path.Combine(App.LocalStorage.RootPath, job.Filename);
                    await App.Telegram.DownloadFileAsync(job.MessageId.Value, localDestPath,
                        (current, total) =>
                        {
                            double pct = total > 0 ? (double)current / total : 0;
                            DispatcherQueue.TryEnqueue(() => App.TransferQueue.ReportProgress(current, total, pct));
                        }, ct);

                    // Also copy to the configured downloads folder if different from local_storage_drive
                    if (!string.IsNullOrEmpty(job.DestDir) &&
                        !string.Equals(job.DestDir, App.LocalStorage.RootPath, StringComparison.OrdinalIgnoreCase))
                    {
                        try
                        {
                            Directory.CreateDirectory(job.DestDir);
                            File.Copy(localDestPath, Path.Combine(job.DestDir, job.Filename), overwrite: true);
                        }
                        catch { /* Best-effort copy to downloads folder */ }
                    }

                    DispatcherQueue.TryEnqueue(() =>
                    {
                        App.TransferQueue.MarkCompleted($"Downloaded '{job.Filename}' → Local Disk");
                        // Refresh local badge count
                        var localCount = App.LocalStorage.GetTotalItemCount();
                        LocalCountText.Text = localCount.ToString();
                        LocalCountBadge.Visibility = localCount > 0 ? Visibility.Visible : Visibility.Collapsed;
                    });
                }
                else if (job.SourcePath != null && job.DestDir != null)
                {
                    string destPath = Path.Combine(job.DestDir, job.Filename);
                    File.Copy(job.SourcePath, destPath, true);
                    DispatcherQueue.TryEnqueue(() => App.TransferQueue.MarkCompleted($"Saved to: {destPath}"));
                }
            }
        }
        catch (OperationCanceledException)
        {
            DispatcherQueue.TryEnqueue(() => App.TransferQueue.MarkCancelled($"Transfer of '{job.Filename}' was cancelled."));
        }
        catch (Exception ex)
        {
            DispatcherQueue.TryEnqueue(() => App.TransferQueue.MarkFailed($"Transfer failed: {ex.Message}"));
        }
    }

    // ── Transfer Queue Event Handlers ──────────────────────────
    private void OnJobStarted(TransferJob job)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            string type = job.Type == TransferType.Upload ? "Uploading" : "Downloading";
            TransferLabel.Text = $"{type}: {job.Filename}";
            CancelTransferBtn.Visibility = Visibility.Visible;
            ExecuteActiveJob();
        });
    }

    private void OnJobProgress(TransferJob job, long current, long total, double percent)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            TransferBar.Value = percent * 100;
            TransferPercent.Text = $"{(int)(percent * 100)}%";
            TransferSize.Text = $"{SizeFormatter.Format(current)} / {SizeFormatter.Format(total)}";
        });
    }

    private void OnJobCompleted(TransferJob job, string message)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            ResetTransferUI();
            ShowInfoBar(message, "success");
            RefreshFiles();

            try
            {
                SystemTrayHelper.ShowNotification(
                    "Sync Completed Successfully",
                    $"'{job.Filename}' was safely uploaded to Telegram Cloud!",
                    SystemTrayHelper.NIIF_INFO
                );
            }
            catch { }
        });
    }

    private void OnJobFailed(TransferJob job, string message)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            ResetTransferUI();
            ShowInfoBar(message, "error");

            try
            {
                SystemTrayHelper.ShowNotification(
                    "Sync Failed",
                    $"Failed to upload '{job.Filename}': {message}",
                    SystemTrayHelper.NIIF_ERROR
                );
            }
            catch { }
        });
    }

    private void OnJobCancelled(TransferJob job, string message)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            ResetTransferUI();
            ShowInfoBar(message, "warning");
        });
    }

    private void OnQueueChanged()
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            var stats = App.TransferQueue.GetStats();
            if (stats.Running == 0 && stats.Pending == 0)
                ResetTransferUI();
        });
    }

    private void ResetTransferUI()
    {
        TransferLabel.Text = "Transfer: Idle";
        TransferBar.Value = 0;
        TransferPercent.Text = "0%";
        TransferSize.Text = "0.0 MB / 0.0 MB";
        CancelTransferBtn.Visibility = Visibility.Collapsed;
    }

    // ── Header Button Actions ──────────────────────────────────
    private async void OnUploadClick(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker();
        picker.FileTypeFilter.Add("*");
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(App.MainWindowInstance!));
        var files = await picker.PickMultipleFilesAsync();

        if (files != null)
        {
            foreach (var file in files)
            {
                var props = await file.GetBasicPropertiesAsync();
                App.TransferQueue.AddJob(TransferType.Upload,
                    filePath: file.Path, filename: file.Name,
                    fileSize: (long)props.Size, folderId: _currentFolderId);
            }
        }
    }

    private async void OnNewFolderClick(object sender, RoutedEventArgs e)
    {
        var input = new TextBox
        {
            PlaceholderText = LocalizationHelper.CurrentLanguage == "ar" ? "أدخل اسم المجلد" : "Enter folder name",
            Style = (Style)Application.Current.Resources["TgTextBoxStyle"]
        };
        var dialog = new ContentDialog
        {
            Title = LocalizationHelper.CurrentLanguage == "ar" ? "إنشاء مجلد جديد" : "Create Folder", Content = input,
            PrimaryButtonText = LocalizationHelper.CurrentLanguage == "ar" ? "إنشاء" : "Create", CloseButtonText = LocalizationHelper.Get("cancel_btn"),
            XamlRoot = this.XamlRoot, RequestedTheme = ElementTheme.Dark
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            string name = input.Text.Trim();
            if (!string.IsNullOrEmpty(name))
            {
                if (_activeBackend == "cloud")
                    App.Database.CreateFolder(name, _currentFolderId);
                else
                    App.LocalStorage.CreateFolder(_localRelPath, name);
                RefreshFiles();
            }
        }
    }

    private async void OnSyncClick(object sender, RoutedEventArgs e)
    {
        if (_activeBackend != "cloud") return;

        var dialog = new ContentDialog
        {
            Title = LocalizationHelper.Get("sync_cloud"),
            Content = LocalizationHelper.CurrentLanguage == "ar"
                ? "هل تريد مزامنة الفهرس المحلي مع سحابة تليجرام؟\nسيقوم هذا بفحص الرسائل المحفوظة بحثاً عن الملفات والمستندات."
                : "Synchronize your local SQLite index with Telegram Cloud?\nThis will scan your Telegram Saved Messages for document files.",
            PrimaryButtonText = LocalizationHelper.CurrentLanguage == "ar" ? "مزامنة" : "Sync", CloseButtonText = LocalizationHelper.Get("cancel_btn"),
            XamlRoot = this.XamlRoot, RequestedTheme = ElementTheme.Dark
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        TransferLabel.Text = LocalizationHelper.CurrentLanguage == "ar" ? "جاري المزامنة مع سحابة تليجرام..." : "Syncing Telegram Cloud...";
        CancelTransferBtn.Content = LocalizationHelper.CurrentLanguage == "ar" ? "✕ إلغاء المزامنة" : "✕ Cancel Sync";
        CancelTransferBtn.Visibility = Visibility.Visible;

        try
        {
            var existingIds = App.Database.GetAllMessageIds();
            var newFiles = await App.Telegram.SyncSavedMessagesAsync(existingIds);

            foreach (var (msgId, filename, fileSize, uploadDate) in newFiles)
                App.Database.AddFile(filename, fileSize, msgId, uploadDate);

            ShowInfoBar(LocalizationHelper.CurrentLanguage == "ar"
                ? $"تمت المزامنة بنجاح! تم تعيين {newFiles.Count} ملفات جديدة."
                : $"Synchronized! Mapped {newFiles.Count} new files.", "success");
        }
        catch (Exception ex)
        {
            ShowInfoBar(LocalizationHelper.CurrentLanguage == "ar" ? $"فشلت المزامنة: {ex.Message}" : $"Sync failed: {ex.Message}", "error");
        }

        ResetTransferUI();
        RefreshFiles();
    }

    private void OnRefreshClick(object sender, RoutedEventArgs e) => RefreshFiles();

    // ── Selection ──────────────────────────────────────────────
    private void OnSelectAllClick(object sender, RoutedEventArgs e)
    {
        // Get current visible files
        bool selectAll = SelectAllCheck.IsChecked == true;
        _selectedFiles.Clear();

        if (selectAll)
        {
            List<FileItem> visibleFiles;
            if (_activeBackend == "cloud")
            {
                visibleFiles = string.IsNullOrEmpty(SearchBox.Text.Trim())
                    ? App.Database.GetFilesInFolder(_currentFolderId)
                    : App.Database.SearchFiles(SearchBox.Text.Trim());
            }
            else
            {
                var (_, localFiles) = App.LocalStorage.ScanDirectory(_localRelPath, SearchBox.Text.Trim());
                visibleFiles = localFiles.Select(f => new FileItem
                {
                    Filename = f.Name, LocalPath = f.FullPath, FileSize = f.Size, UploadDate = f.Date
                }).ToList();
            }

            if (_activeFilter != "all")
                visibleFiles = visibleFiles.Where(f => FileTypeHelper.MatchesFilter(f.Filename, _activeFilter)).ToList();

            foreach (var f in visibleFiles)
                _selectedFiles[f.Filename] = f;
        }

        UpdateSelectionBar();
        RefreshFiles();
    }

    private void UpdateSelectionBar()
    {
        if (_selectedFiles.Count > 0)
        {
            SelectionCount.Text = LocalizationHelper.CurrentLanguage == "ar"
                ? $"المحدد: {_selectedFiles.Count} عناصر"
                : $"Selected: {_selectedFiles.Count} items";
            SelectionBar.Visibility = Visibility.Visible;
        }
        else
        {
            SelectionBar.Visibility = Visibility.Collapsed;
        }
    }

    private void OnClearSelectionClick(object sender, RoutedEventArgs e)
    {
        _selectedFiles.Clear();
        UpdateSelectionBar();
        RefreshFiles();
    }

    private void OnMultiDownloadClick(object sender, RoutedEventArgs e)
    {
        foreach (var (_, file) in _selectedFiles)
            DownloadFile(file);
        _selectedFiles.Clear();
        UpdateSelectionBar();
        RefreshFiles();
    }

    private async void OnMultiDeleteClick(object sender, RoutedEventArgs e)
    {
        int count = _selectedFiles.Count;
        var dialog = new ContentDialog
        {
            Title = LocalizationHelper.CurrentLanguage == "ar" ? "تأكيد الحذف المتعدد" : "Confirm Multi-Delete",
            Content = LocalizationHelper.CurrentLanguage == "ar"
                ? $"هل أنت متأكد من حذف {count} من الملفات المحددة نهائياً؟"
                : $"Delete {count} selected files permanently?",
            PrimaryButtonText = LocalizationHelper.CurrentLanguage == "ar" ? "حذف الكل" : "Delete All", CloseButtonText = LocalizationHelper.Get("cancel_btn"),
            XamlRoot = this.XamlRoot, RequestedTheme = ElementTheme.Dark
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            try
            {
                if (_activeBackend == "cloud")
                {
                    var msgIds = _selectedFiles.Values.Select(f => f.TelegramMessageId).ToList();
                    await App.Telegram.DeleteMessagesAsync(msgIds);
                    foreach (var f in _selectedFiles.Values)
                        App.Database.DeleteFile(f.Id);
                }
                else
                {
                    foreach (var f in _selectedFiles.Values)
                        if (f.LocalPath != null) App.LocalStorage.DeleteFile(f.LocalPath);
                }
                ShowInfoBar(LocalizationHelper.CurrentLanguage == "ar"
                    ? $"تم حذف {count} ملفات بنجاح!"
                    : $"Deleted {count} files successfully!", "success");
            }
            catch (Exception ex) { ShowInfoBar(LocalizationHelper.CurrentLanguage == "ar" ? $"فشل الحذف المتعدد: {ex.Message}" : $"Multi-delete failed: {ex.Message}", "error"); }

            _selectedFiles.Clear();
            UpdateSelectionBar();
            RefreshFiles();
        }
    }

    // ── Cancel Transfer ────────────────────────────────────────
    private void OnCancelTransferClick(object sender, RoutedEventArgs e)
    {
        App.TransferQueue.CancelActiveTransfer();
    }

    // ── Sidebar Actions ────────────────────────────────────────
    private async void OnBackupClick(object sender, RoutedEventArgs e)
    {
        var picker = new FolderPicker();
        picker.FileTypeFilter.Add("*");
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(App.MainWindowInstance!));
        var folder = await picker.PickSingleFolderAsync();
        if (folder != null)
        {
            App.Database.SetStatusVal("auto_backup_dir", folder.Path);
            App.BackupWatcher.NewFileDetected += OnBackupFileDetected;
            App.BackupWatcher.Start(folder.Path);
            ShowInfoBar(LocalizationHelper.CurrentLanguage == "ar"
                ? $"تم ربط مجلد النسخ الاحتياطي التلقائي: {folder.Path}"
                : $"Auto-Backup mapped to: {folder.Path}", "success");
        }
    }

    private async void OnDownloadsClick(object sender, RoutedEventArgs e)
    {
        var picker = new FolderPicker();
        picker.FileTypeFilter.Add("*");
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(App.MainWindowInstance!));
        var folder = await picker.PickSingleFolderAsync();
        if (folder != null)
        {
            App.Database.SetStatusVal("downloads_dir", folder.Path);
            _downloadsDir = folder.Path;
            ShowInfoBar(LocalizationHelper.CurrentLanguage == "ar"
                ? $"مجلد التنزيلات الافتراضي: {folder.Path}"
                : $"Default downloads folder: {folder.Path}", "success");
        }
    }

    private async void OnLogoutClick(object sender, RoutedEventArgs e)
    {
        var dialog = new ContentDialog
        {
            Title = LocalizationHelper.CurrentLanguage == "ar" ? "فصل الحساب" : "Disconnect",
            Content = LocalizationHelper.Get("confirm_logout"),
            PrimaryButtonText = LocalizationHelper.CurrentLanguage == "ar" ? "فصل الحساب" : "Disconnect",
            CloseButtonText = LocalizationHelper.Get("cancel_btn"),
            XamlRoot = this.XamlRoot, RequestedTheme = ElementTheme.Dark
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            // ── 1. Stop timers so no callbacks fire after navigation ──
            _pulseTimer?.Stop();
            _searchTimer?.Stop();

            // ── 2. Unsubscribe all event handlers ──
            try
            {
                App.TransferQueue.JobCompleted  -= OnJobCompleted;
                App.TransferQueue.JobFailed     -= OnJobFailed;
                App.TransferQueue.JobCancelled  -= OnJobCancelled;
                App.TransferQueue.JobProgress   -= OnJobProgress;
                App.TransferQueue.JobStarted    -= OnJobStarted;
                App.TransferQueue.QueueChanged  -= OnQueueChanged;
            }
            catch { }
            try { App.BackupWatcher.NewFileDetected -= OnBackupFileDetected; } catch { }

            // ── 3. Remove tray icon ──
            try { SystemTrayHelper.RemoveTrayIcon(); } catch { }

            // ── 4. Navigate to login IMMEDIATELY — instant response ──
            string msg = LocalizationHelper.CurrentLanguage == "ar"
                ? "تم فصل الحساب بنجاح."
                : "Account disconnected successfully.";
            ((MainWindow)App.MainWindowInstance!).NavigateToLogin(msg);

            // ── 5. Cleanup runs in the background without blocking UI ──
            _ = Task.Run(() =>
            {
                try { App.Telegram.Dispose(); } catch { }
                Thread.Sleep(200);
                try
                {
                    var globalDb = new Services.DatabaseService();
                    globalDb.SetStatusVal("last_active_phone", "");
                }
                catch { }
                try { App.ResetToDefaultDatabase(); } catch { }
            });
        }
    }

    // ── Backup Watcher ─────────────────────────────────────────
    private void OnBackupFileDetected(string name, string path, long size)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            App.TransferQueue.AddJob(TransferType.Upload,
                filePath: path, filename: name, fileSize: size);
        });
    }

    // ── Keyboard Shortcuts ─────────────────────────────────────
    private void Page_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        bool ctrl = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Control)
            .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

        if (ctrl && e.Key == Windows.System.VirtualKey.A)
        {
            SelectAllCheck.IsChecked = true;
            OnSelectAllClick(SelectAllCheck, new RoutedEventArgs());
            e.Handled = true;
        }
        else if (e.Key == Windows.System.VirtualKey.Delete && _selectedFiles.Count > 0)
        {
            OnMultiDeleteClick(sender, new RoutedEventArgs());
            e.Handled = true;
        }
        else if (e.Key == Windows.System.VirtualKey.Escape && _selectedFiles.Count > 0)
        {
            OnClearSelectionClick(sender, new RoutedEventArgs());
            e.Handled = true;
        }
        else if (e.Key == Windows.System.VirtualKey.F5)
        {
            RefreshFiles();
            e.Handled = true;
        }
        else if (ctrl && e.Key == Windows.System.VirtualKey.U)
        {
            OnUploadClick(sender, new RoutedEventArgs());
            e.Handled = true;
        }
    }

    // ── InfoBar Toast ──────────────────────────────────────────
    private void ShowInfoBar(string message, string kind)
    {
        // Use a TeachingTip or simple flyout for now
        var infoBar = new InfoBar
        {
            Title = kind switch
            {
                "success" => (LocalizationHelper.CurrentLanguage == "ar" ? "نجاح" : "Success"),
                "error" => (LocalizationHelper.CurrentLanguage == "ar" ? "خطأ" : "Error"),
                "warning" => (LocalizationHelper.CurrentLanguage == "ar" ? "تحذير" : "Warning"),
                _ => (LocalizationHelper.CurrentLanguage == "ar" ? "معلومات" : "Info")
            },
            Message = message,
            Severity = kind switch
            {
                "success" => InfoBarSeverity.Success,
                "error" => InfoBarSeverity.Error,
                "warning" => InfoBarSeverity.Warning,
                _ => InfoBarSeverity.Informational
            },
            IsOpen = true,
            IsClosable = true,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 20, 20)
        };

        // Auto-dismiss after 4 seconds
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        timer.Tick += (s, e) =>
        {
            timer.Stop();
            infoBar.IsOpen = false;
            if (FileListPanel.Parent is Panel parent && parent.Children.Contains(infoBar))
                parent.Children.Remove(infoBar);
        };
        timer.Start();

        // Add to the page's visual tree (overlay style)
        if (this.Content is Grid mainGrid)
        {
            // Place at top level
            mainGrid.Children.Add(infoBar);
            Grid.SetColumn(infoBar, 1);
        }
    }

    // ── Utility ────────────────────────────────────────────────
    private static string ExtractTime(string dateStr)
    {
        if (string.IsNullOrEmpty(dateStr)) return DateTime.Now.ToString("HH:mm");
        if (dateStr.Contains(' '))
        {
            var parts = dateStr.Split(' ');
            if (parts.Length >= 2) return parts[1][..Math.Min(5, parts[1].Length)];
        }
        else if (dateStr.Contains(':'))
        {
            return dateStr[..Math.Min(5, dateStr.Length)];
        }
        return DateTime.Now.ToString("HH:mm");
    }

    private void OnHeaderLangClick(object sender, RoutedEventArgs e)
    {
        LocalizationHelper.ToggleLanguage();
        ApplyLocalization();
        SwitchBackend(_activeBackend);
    }

    private void ApplyLocalization()
    {
        this.FlowDirection = LocalizationHelper.CurrentLanguage == "ar" ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;

        LogoText.Text = LocalizationHelper.Get("db_title");
        OnlineLabel.Text = LocalizationHelper.Get("online");
        CloudBtnTitle.Text = LocalizationHelper.Get("saved_messages");
        CloudBtnSubtitle.Text = LocalizationHelper.Get("cloud_backend");
        LocalBtnTitle.Text = LocalizationHelper.Get("local_disk");
        LocalBtnSubtitle.Text = LocalizationHelper.Get("local_backend");
        
        AutoBackupBtn.Content = LocalizationHelper.Get("set_backup");
        DownloadsBtn.Content = LocalizationHelper.Get("set_downloads");
        DisconnectBtn.Content = LocalizationHelper.Get("disconnect");
        
        UploadBtn.Content = LocalizationHelper.Get("upload_file");
        NewFolderBtn.Content = LocalizationHelper.Get("new_folder");
        SyncBtn.Content = LocalizationHelper.Get("sync_cloud");
        RefreshBtn.Content = LocalizationHelper.Get("refresh");
        SearchBox.PlaceholderText = LocalizationHelper.Get("search_placeholder");

        FilterAll.Content = (LocalizationHelper.CurrentLanguage == "ar" ? "🌐 " : "🌐 ") + LocalizationHelper.Get("filter_all");
        FilterDocs.Content = (LocalizationHelper.CurrentLanguage == "ar" ? "📄 " : "📄 ") + LocalizationHelper.Get("filter_docs");
        FilterMedia.Content = (LocalizationHelper.CurrentLanguage == "ar" ? "🎬 " : "🎬 ") + LocalizationHelper.Get("filter_media");
        FilterZips.Content = (LocalizationHelper.CurrentLanguage == "ar" ? "📦 " : "📦 ") + LocalizationHelper.Get("filter_zips");
        FilterOthers.Content = (LocalizationHelper.CurrentLanguage == "ar" ? "⚙️ " : "⚙️ ") + LocalizationHelper.Get("filter_others");

        ColHeaderName.Text = LocalizationHelper.CurrentLanguage == "ar" ? "الاسم" : "Name";
        ColHeaderSize.Text = LocalizationHelper.CurrentLanguage == "ar" ? "الحجم" : "Size";
        ColHeaderDate.Text = LocalizationHelper.CurrentLanguage == "ar" ? "التاريخ" : "Date";
        ColHeaderActions.Text = LocalizationHelper.CurrentLanguage == "ar" ? "الإجراءات" : "Actions";

        MultiDownloadBtn.Content = LocalizationHelper.CurrentLanguage == "ar" ? "📥 تحميل المحدد" : "📥 Download Selected";
        MultiDeleteBtn.Content = LocalizationHelper.CurrentLanguage == "ar" ? "🗑️ حذف المحدد" : "🗑️ Delete Selected";

        HeaderLangBtn.Content = LocalizationHelper.CurrentLanguage == "ar" ? "🌐 English" : "🌐 العربية";
    }
}
