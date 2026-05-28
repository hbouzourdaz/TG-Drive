using System;
using System.IO;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Windowing;
using Microsoft.UI;
using WinRT.Interop;
using TelegramDrive.Views;
using TelegramDrive.Helpers;

namespace TelegramDrive;

/// <summary>
/// Main application window. Manages page navigation and window chrome.
/// </summary>
public sealed partial class MainWindow : Window
{
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    private bool _isExiting = false;

    public MainWindow()
    {
        try
        {
            this.SystemBackdrop = new Microsoft.UI.Xaml.Media.MicaBackdrop();
            this.InitializeComponent();

            this.Closed += MainWindow_Closed;

            // Configure window
            this.Title = "Telegram Unlimited Cloud Storage";
            SetWindowSize(1450, 900);
            CustomizeTitleBar();

            // Set window icon programmatically
            try
            {
                var windowHandle = WindowNative.GetWindowHandle(this);
                var windowId = Win32Interop.GetWindowIdFromWindow(windowHandle);
                var appWindow = AppWindow.GetFromWindowId(windowId);
                string iconPath = Path.Combine(AppContext.BaseDirectory, "logo.ico");
                if (File.Exists(iconPath))
                {
                    appWindow.SetIcon(iconPath);
                }
            }
            catch { }

            // Initialize System Tray (NotifyIcon) helper
            var hwnd = WindowNative.GetWindowHandle(this);
            SystemTrayHelper.Initialize(hwnd, () =>
            {
                this.Activate();
            });
            SystemTrayHelper.AddTrayIcon("Telegram Unlimited Cloud Storage");

            // Start with loading, then check auth
            NavigateToLoading("Initializing Telegram Client...");
            _ = CheckAuthOnStartup();
        }
        catch (Exception ex)
        {
            var crashPath = Path.Combine(AppContext.BaseDirectory, "mainwindow_crash.log");
            System.IO.File.WriteAllText(crashPath, ex.ToString());
            throw;
        }
    }

    private void SetWindowSize(int width, int height)
    {
        var hwnd = WindowNative.GetWindowHandle(this);
        var windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
        var appWindow = AppWindow.GetFromWindowId(windowId);
        
        try
        {
            var displayArea = DisplayArea.GetFromWindowId(windowId, DisplayAreaFallback.Nearest);
            int centerX = (displayArea.WorkArea.Width - width) / 2;
            int centerY = (displayArea.WorkArea.Height - height) / 2;
            
            centerX = Math.Max(displayArea.WorkArea.X, displayArea.WorkArea.X + centerX);
            centerY = Math.Max(displayArea.WorkArea.Y, displayArea.WorkArea.Y + centerY);
            
            appWindow.MoveAndResize(new Windows.Graphics.RectInt32(centerX, centerY, width, height));
        }
        catch
        {
            appWindow.Resize(new Windows.Graphics.SizeInt32(width, height));
        }
    }

    private void CustomizeTitleBar()
    {
        var hwnd = WindowNative.GetWindowHandle(this);

        // Force immersive dark mode using DWM (attribute 20 and 19)
        try
        {
            int useImmersiveDarkMode = 1;
            DwmSetWindowAttribute(hwnd, 20, ref useImmersiveDarkMode, 4);
            DwmSetWindowAttribute(hwnd, 19, ref useImmersiveDarkMode, 4);
        }
        catch { }

        // Remove the native white title bar completely by extending window content into it
        this.ExtendsContentIntoTitleBar = true;
        this.SetTitleBar(AppTitleBar);

        var windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
        var appWindow = AppWindow.GetFromWindowId(windowId);

        if (AppWindowTitleBar.IsCustomizationSupported())
        {
            var titleBar = appWindow.TitleBar;

            // Make native caption buttons (minimize, maximize, close) transparent and white
            titleBar.ButtonBackgroundColor = Colors.Transparent;
            titleBar.ButtonForegroundColor = Colors.White;
            titleBar.ButtonHoverBackgroundColor = ColorHelper.FromArgb(255, 32, 43, 54);
            titleBar.ButtonHoverForegroundColor = Colors.White;
            titleBar.ButtonPressedBackgroundColor = ColorHelper.FromArgb(255, 45, 61, 77);
            titleBar.ButtonPressedForegroundColor = Colors.White;

            titleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
            titleBar.ButtonInactiveForegroundColor = ColorHelper.FromArgb(255, 127, 145, 164);
        }
    }

    public void ForceClose()
    {
        _isExiting = true;
        this.Close();
    }

    private void MainWindow_Closed(object sender, WindowEventArgs args)
    {
        bool isDashboardShowing = ContentFrame.Content is DashboardPage;

        if (!_isExiting && isDashboardShowing)
        {
            args.Handled = true;
            SystemTrayHelper.MinimizeToTray();
            SystemTrayHelper.ShowNotification(
                "Telegram Storage",
                "Toggled to background mode. Click the tray icon to restore.",
                SystemTrayHelper.NIIF_INFO
            );
        }
        else
        {
            try
            {
                SystemTrayHelper.RemoveTrayIcon();
                App.Telegram?.Dispose();
            }
            catch { }
        }
    }

    private async Task CheckAuthOnStartup()
    {
        try
        {
            TelegramDrive.Helpers.AccountManager.LoadAccounts();
            var accounts = TelegramDrive.Helpers.AccountManager.GetAccounts();

            var lastActivePhone = App.Database.GetStatusVal("last_active_phone");
            if (!string.IsNullOrEmpty(lastActivePhone))
            {
                var account = accounts.Find(a => a.Phone == lastActivePhone);
                if (account != null)
                {
                    App.SwitchActiveAccount(account);
                    if (int.TryParse(account.ApiId, out int apiId))
                    {
                        bool authorized = await App.Telegram.ConnectAndCheckAuthAsync(apiId, account.ApiHash, account.Phone);
                        if (authorized)
                        {
                            DispatcherQueue.TryEnqueue(() => NavigateAfterAuth());
                            return;
                        }
                    }
                }
            }

            // Fallback to base DB if not authorized
            App.ResetToDefaultDatabase();
            DispatcherQueue.TryEnqueue(() => NavigateToLogin());
        }
        catch
        {
            try { App.ResetToDefaultDatabase(); } catch { }
            DispatcherQueue.TryEnqueue(() => NavigateToLogin());
        }
    }

    // ── Navigation Methods ─────────────────────────────────────

    public void NavigateToLoading(string message = "Loading...")
    {
        ContentFrame.Navigate(typeof(LoadingPage), message);
    }

    public void NavigateToLogin(string errorMsg = "")
    {
        ContentFrame.Navigate(typeof(LoginPage), errorMsg);
    }

    public void NavigateToOtp(string phone)
    {
        ContentFrame.Navigate(typeof(OtpPage), phone);
    }

    public void NavigateToDashboard()
    {
        ContentFrame.Navigate(typeof(DashboardPage));
    }

    public void NavigateToSetup()
    {
        ContentFrame.Navigate(typeof(SetupPage));
    }

    /// <summary>
    /// After successful authentication, show the setup wizard on first run;
    /// on subsequent runs go straight to the dashboard.
    /// </summary>
    public void NavigateAfterAuth()
    {
        bool setupDone = App.Database.GetStatusVal("setup_completed", "") == "1";
        if (setupDone)
            NavigateToDashboard();
        else
            NavigateToSetup();
    }
}
