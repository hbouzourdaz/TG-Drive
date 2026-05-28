using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.System;
using TelegramDrive.Helpers;
using TelegramDrive.Services;

namespace TelegramDrive.Views;

public sealed partial class LoginPage : Page
{
    public LoginPage()
    {
        this.InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        // Pre-fill saved credentials
        var savedId = App.Database.GetStatusVal("api_id", "");
        var savedHash = App.Database.GetStatusVal("api_hash", "");
        var savedPhone = App.Database.GetStatusVal("phone_number", "");
        if (!string.IsNullOrEmpty(savedId)) ApiIdBox.Text = savedId;
        if (!string.IsNullOrEmpty(savedHash)) ApiHashBox.Text = savedHash;
        if (!string.IsNullOrEmpty(savedPhone)) PhoneBox.Text = savedPhone;

        AccountManager.LoadAccounts();
        var accounts = AccountManager.GetAccounts();
        if (accounts.Count > 0)
        {
            AccountsList.ItemsSource = accounts;
            AccountsSection.Visibility = Visibility.Visible;
            LoginFormSection.Visibility = Visibility.Collapsed;
            BackToAccountsBtn.Visibility = Visibility.Visible;
        }
        else
        {
            AccountsSection.Visibility = Visibility.Collapsed;
            LoginFormSection.Visibility = Visibility.Visible;
            BackToAccountsBtn.Visibility = Visibility.Collapsed;
        }

        ApplyLocalization();

        if (e.Parameter is string errorMsg && !string.IsNullOrEmpty(errorMsg))
        {
            StatusLabel.Text = errorMsg;
            StatusLabel.Foreground = errorMsg.Contains("error", StringComparison.OrdinalIgnoreCase) ||
                                    errorMsg.Contains("fail", StringComparison.OrdinalIgnoreCase)
                 ? (Microsoft.UI.Xaml.Media.Brush)Resources["TgErrorBrush"]
                 : (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TgPrimaryBrush"];
        }
    }

    private async void OnSendCodeClick(object sender, RoutedEventArgs e)
    {
        // Validate inputs
        string apiIdStr = ApiIdBox.Text.Trim();
        string apiHash = ApiHashBox.Text.Trim();
        string phoneInput = PhoneBox.Text.Trim();

        if (string.IsNullOrEmpty(apiIdStr) || string.IsNullOrEmpty(apiHash))
        {
            StatusLabel.Text = LocalizationHelper.Get("api_id_required");
            StatusLabel.Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TgErrorBrush"];
            return;
        }

        if (!int.TryParse(apiIdStr, out int apiId))
        {
            StatusLabel.Text = LocalizationHelper.Get("api_id_numeric");
            StatusLabel.Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TgErrorBrush"];
            return;
        }

        // Clean and sanitize phone number (handles RTL issues where '+' gets pushed to the end)
        string digits = new string(phoneInput.Where(char.IsDigit).ToArray());
        string phone = string.IsNullOrEmpty(digits) ? "" : "+" + digits;

        if (string.IsNullOrEmpty(phone))
        {
            StatusLabel.Text = LocalizationHelper.Get("phone_required");
            StatusLabel.Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TgErrorBrush"];
            return;
        }

        SendCodeBtn.IsEnabled = false;
        StatusLabel.Text = LocalizationHelper.Get("requesting_otp");
        StatusLabel.Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TgPrimaryBrush"];

        try
        {
            // Configure TelegramService for this phone session right away!
            string targetSession = $"wt_session_{phone}.session";
            App.Telegram.SetSessionFile(targetSession);

            // Save credentials (using sanitized phone) to global base database
            var globalDb = new DatabaseService();
            globalDb.SetStatusVal("api_id", apiIdStr);
            globalDb.SetStatusVal("api_hash", apiHash);
            globalDb.SetStatusVal("phone_number", phone);

            bool sent = await App.Telegram.SendCodeRequestAsync(apiId, apiHash, phone);
            if (sent)
            {
                var mainWindow = (MainWindow)App.MainWindowInstance!;
                mainWindow.NavigateToOtp(phone);
            }
            else
            {
                StatusLabel.Text = LocalizationHelper.Get("failed_send_code");
                StatusLabel.Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TgErrorBrush"];
                SendCodeBtn.IsEnabled = true;
            }
        }
        catch (Exception ex)
        {
            StatusLabel.Text = $"{LocalizationHelper.Get("verification_failed")}{ex.Message}";
            StatusLabel.Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TgErrorBrush"];
            SendCodeBtn.IsEnabled = true;
        }
    }

    private async void OnHelpClick(object sender, RoutedEventArgs e)
    {
        var dialog = new ContentDialog
        {
            Title = LocalizationHelper.Get("getting_started"),
            CloseButtonText = LocalizationHelper.Get("getting_started_close"),
            XamlRoot = this.XamlRoot,
            RequestedTheme = ElementTheme.Dark,
            Content = CreateHelpContent()
        };
        dialog.PrimaryButtonText = LocalizationHelper.Get("open_telegram_org");
        dialog.PrimaryButtonClick += async (s, args) =>
        {
            await Launcher.LaunchUriAsync(new Uri("https://my.telegram.org"));
        };
        await dialog.ShowAsync();
    }

    private StackPanel CreateHelpContent()
    {
        var panel = new StackPanel { Spacing = 12 };

        // Warning card
        var warningBorder = new Border
        {
            Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 43, 34, 20)),
            BorderBrush = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TgWarningBrush"],
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(15, 12, 15, 12)
        };
        warningBorder.Child = new TextBlock
        {
            Text = LocalizationHelper.CurrentLanguage == "ar" 
                ? "يستخدم Telegram Drive حساب تليجرام الخاص بك كمساحة تخزين سحابية آمنة.\nستحتاج إلى حساب تليجرام وبيانات API للبدء."
                : "Telegram Drive uses your Telegram account as secure cloud storage.\nYou'll need a Telegram account and API credentials to get started.",
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TgWarningBrush"],
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap
        };
        panel.Children.Add(warningBorder);

        // Steps
        if (LocalizationHelper.CurrentLanguage == "ar")
        {
            AddStep(panel, "١", "انتقل إلى بوابة مطوري تليجرام", "قم بزيارة my.telegram.org وسجل الدخول باستخدام رقم هاتفك.");
            AddStep(panel, "٢", "إنشاء تطبيق جديد", "انقر فوق \"API development tools\" وأنشئ تطبيقاً جديداً.\nاستخدم أي اسم ووصف تريده.");
            AddStep(panel, "٣", "نسخ بيانات الاعتماد الخاصة بك", "بعد إنشاء التطبيق، سترى API ID (رقم) و API Hash\n(نص). انسخ كلاهما وألصقهما في الحقول المخصصة في شاشة تسجيل الدخول.");
        }
        else
        {
            AddStep(panel, "1", "Go to Telegram's Developer Portal", "Visit my.telegram.org and log in with your phone number.");
            AddStep(panel, "2", "Create a New Application", "Click on \"API development tools\" and create a new application.\nUse any name and description you like.");
            AddStep(panel, "3", "Copy Your Credentials", "After creating the app, you'll see your API ID (a number) and API Hash\n(a string). Copy both and paste them into the fields on the login screen.");
        }

        // Privacy notice
        var privacyBorder = new Border
        {
            Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 24, 24, 24)),
            BorderBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 43, 43, 43)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(15, 10, 15, 10)
        };
        privacyBorder.Child = new TextBlock
        {
            Text = LocalizationHelper.CurrentLanguage == "ar"
                ? "🔒 الخصوصية: يتم تخزين بيانات الاعتماد الخاصة بك محلياً على جهازك ولا يتم إرسالها أبداً إلى أي خوادم خارجية."
                : "🔒 Privacy: Your credentials are stored locally on your device and are never sent to any third-party servers.",
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TgTextMutedBrush"],
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap
        };
        panel.Children.Add(privacyBorder);

        return panel;
    }

    private void AddStep(StackPanel parent, string number, string title, string body)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };

        var badge = new Border
        {
            Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TgWarningBrush"],
            CornerRadius = new CornerRadius(12),
            Width = 24, Height = 24,
            Child = new TextBlock
            {
                Text = number,
                Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 18, 18, 18)),
                FontSize = 12, FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
        row.Children.Add(badge);

        var textCol = new StackPanel { Spacing = 2 };
        textCol.Children.Add(new TextBlock
        {
            Text = title,
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TgTextPrimaryBrush"],
            FontSize = 13, FontWeight = Microsoft.UI.Text.FontWeights.Bold
        });
        textCol.Children.Add(new TextBlock
        {
            Text = body,
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TgTextMutedBrush"],
            FontSize = 11, TextWrapping = TextWrapping.Wrap
        });
        row.Children.Add(textCol);

        parent.Children.Add(row);
    }

    private async void OnQrClick(object sender, RoutedEventArgs e)
    {
        string apiIdStr = ApiIdBox.Text.Trim();
        string apiHash = ApiHashBox.Text.Trim();

        if (string.IsNullOrEmpty(apiIdStr) || string.IsNullOrEmpty(apiHash))
        {
            StatusLabel.Text = LocalizationHelper.Get("api_id_required");
            StatusLabel.Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TgErrorBrush"];
            return;
        }

        if (!int.TryParse(apiIdStr, out int apiId))
        {
            StatusLabel.Text = LocalizationHelper.Get("api_id_numeric");
            StatusLabel.Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TgErrorBrush"];
            return;
        }

        // Setup dialog container
        var container = new StackPanel 
        { 
            Spacing = 15, 
            Width = 300, 
            HorizontalAlignment = HorizontalAlignment.Center 
        };

        var qrImage = new Image
        {
            Width = 220, 
            Height = 220,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Visibility = Visibility.Collapsed
        };

        var progressRing = new ProgressRing
        {
            IsActive = true,
            Width = 50, 
            Height = 50,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 30, 0, 30)
        };

        var subtext = new TextBlock
        {
            Text = LocalizationHelper.CurrentLanguage == "ar"
                ? "جاري الاتصال وإنشاء رمز الـ QR... يرجى الانتظار."
                : "Connecting & generating QR Code... Please wait.",
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TgTextSecondaryBrush"],
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center
        };

        container.Children.Add(progressRing);
        container.Children.Add(qrImage);
        container.Children.Add(subtext);

        var dialog = new ContentDialog
        {
            Title = LocalizationHelper.CurrentLanguage == "ar" ? "مسح رمز QR للدخول السريع" : "Scan QR Code to Login",
            CloseButtonText = LocalizationHelper.Get("cancel_btn"),
            XamlRoot = this.XamlRoot,
            RequestedTheme = ElementTheme.Dark,
            Content = container
        };

        bool qrActive = true;
        dialog.Closed += (s, args) =>
        {
            qrActive = false;
            App.Telegram.Dispose();
        };

        _ = Task.Run(async () =>
        {
            try
            {
                // Setup temporary session file for QR Login
                App.Telegram.SetSessionFile("wt_session_temp.session");

                var user = await App.Telegram.LoginWithQRCodeAsync(apiId, apiHash, (qrUrl) =>
                {
                    DispatcherQueue.TryEnqueue(() =>
                    {
                        if (qrActive)
                        {
                            progressRing.Visibility = Visibility.Collapsed;
                            progressRing.IsActive = false;
                            
                            var bitmap = new BitmapImage(
                                new Uri($"https://api.qrserver.com/v1/create-qr-code/?size=300x300&data={Uri.EscapeDataString(qrUrl)}")
                            );
                            qrImage.Source = bitmap;
                            qrImage.Visibility = Visibility.Visible;

                            subtext.Text = LocalizationHelper.CurrentLanguage == "ar"
                                ? "افتح تطبيق تليجرام على هاتفك، ثم اذهب إلى الإعدادات > الأجهزة > ربط جهاز الكمبيوتر وامسح الرمز."
                                : "Open Telegram on your phone, go to Settings > Devices > Link Desktop Device and scan this code.";
                        }
                    });
                });

                if (user != null && qrActive)
                {
                    string phoneOrId = string.IsNullOrEmpty(user.phone) ? user.id.ToString() : user.phone;
                    if (!phoneOrId.StartsWith("+") && !string.IsNullOrEmpty(user.phone)) phoneOrId = "+" + phoneOrId;

                    // Dispose client so session file is unlocked and can be renamed
                    App.Telegram.Dispose();
                    await Task.Delay(500); // give it a moment to unlock the file

                    // Rename session file
                    string tempPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "wt_session_temp.session");
                    string targetSessionName = $"wt_session_{phoneOrId}.session";
                    string targetPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, targetSessionName);
                    
                    if (System.IO.File.Exists(tempPath))
                    {
                        if (System.IO.File.Exists(targetPath)) System.IO.File.Delete(targetPath);
                        System.IO.File.Move(tempPath, targetPath);
                    }

                    // Save and switch active account
                    var newAccount = new UserAccount
                    {
                        Phone = phoneOrId,
                        Name = $"{user.first_name} {user.last_name}".Trim(),
                        Username = string.IsNullOrEmpty(user.username) ? "Saved Messages" : $"@{user.username}",
                        ApiId = apiIdStr,
                        ApiHash = apiHash,
                        SessionFile = targetSessionName,
                        DbFile = $"telegram_drive_{phoneOrId}.db"
                    };

                    AccountManager.SaveAccount(newAccount);
                    App.SwitchActiveAccount(newAccount);

                    // Re-initialize dynamic settings inside user-specific DB
                    App.Database.SetStatusVal("api_id", apiIdStr);
                    App.Database.SetStatusVal("api_hash", apiHash);
                    App.Database.SetStatusVal("phone_number", phoneOrId);

                    // Set last active phone in global database
                    var globalDb = new DatabaseService();
                    globalDb.SetStatusVal("last_active_phone", phoneOrId);

                    // Reconnect dynamic WTelegram using uniquely saved session file
                    await App.Telegram.ConnectAndCheckAuthAsync(apiId, apiHash, phoneOrId);

                    DispatcherQueue.TryEnqueue(() =>
                    {
                        dialog.Hide();
                        var mainWindow = (MainWindow)App.MainWindowInstance!;
                        mainWindow.NavigateAfterAuth();
                    });
                }
            }
            catch (Exception ex)
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    if (qrActive)
                    {
                        dialog.Hide();
                        StatusLabel.Text = $"{LocalizationHelper.Get("verification_failed")}{ex.Message}";
                        StatusLabel.Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TgErrorBrush"];
                    }
                });
            }
        });

        await dialog.ShowAsync();
    }

    private void OnLangClick(object sender, RoutedEventArgs e)
    {
        LocalizationHelper.ToggleLanguage();
        ApplyLocalization();
    }

    private void ApplyLocalization()
    {
        this.FlowDirection = LocalizationHelper.CurrentLanguage == "ar" ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;

        TitleText.Text = LocalizationHelper.Get("login_title");
        SubtitleText.Text = LocalizationHelper.Get("login_subtitle");
        ApiIdLabel.Text = LocalizationHelper.Get("api_id");
        ApiHashLabel.Text = LocalizationHelper.Get("api_hash");
        PhoneLabel.Text = LocalizationHelper.Get("phone_number");
        SendCodeBtn.Content = LocalizationHelper.Get("send_code");
        HelpBtn.Content = LocalizationHelper.Get("how_to_get");
        QrBtn.Content = LocalizationHelper.Get("scan_qr");

        CopyrightLabel.Text = LocalizationHelper.CurrentLanguage == "ar" ? "تم التصميم والتطوير بواسطة" : "Designed & Developed by";
        LangBtn.Content = LocalizationHelper.CurrentLanguage == "ar" ? "🌐 English" : "🌐 العربية";

        // Localize dynamic remembered accounts view controls
        WelcomeBackTitle.Text = LocalizationHelper.Get("welcome_back");
        WelcomeBackSubtitle.Text = LocalizationHelper.Get("select_account");
        UseAnotherBtn.Content = LocalizationHelper.Get("use_another_account");
        BackToAccountsBtn.Content = LocalizationHelper.Get("back_to_accounts");
    }

    private async void OnInstantLoginClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string phone)
        {
            btn.IsEnabled = false;
            
            WelcomeBackSubtitle.Text = LocalizationHelper.CurrentLanguage == "ar"
                ? "جاري الاتصال بالحساب... يرجى الانتظار"
                : "Connecting to account... Please wait";
            WelcomeBackSubtitle.Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TgPrimaryBrush"];

            try
            {
                AccountManager.LoadAccounts();
                var account = AccountManager.GetAccounts().Find(a => a.Phone == phone);
                if (account != null)
                {
                    App.SwitchActiveAccount(account);
                    if (int.TryParse(account.ApiId, out int apiId))
                    {
                        bool authorized = await App.Telegram.ConnectAndCheckAuthAsync(apiId, account.ApiHash, account.Phone);
                        if (authorized)
                        {
                            var globalDb = new DatabaseService();
                            globalDb.SetStatusVal("last_active_phone", phone);

                            var mainWindow = (MainWindow)App.MainWindowInstance!;
                            mainWindow.NavigateAfterAuth();
                            return;
                        }
                    }
                }

                WelcomeBackSubtitle.Text = LocalizationHelper.CurrentLanguage == "ar"
                    ? "فشل الاتصال التلقائي. يرجى تسجيل الدخول يدوياً."
                    : "Instant login failed. Please sign in manually.";
                WelcomeBackSubtitle.Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TgErrorBrush"];
                
                AccountsSection.Visibility = Visibility.Collapsed;
                LoginFormSection.Visibility = Visibility.Visible;
                StatusLabel.Text = WelcomeBackSubtitle.Text;
                StatusLabel.Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TgErrorBrush"];
            }
            catch (Exception ex)
            {
                WelcomeBackSubtitle.Text = $"{LocalizationHelper.Get("verification_failed")}{ex.Message}";
                WelcomeBackSubtitle.Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TgErrorBrush"];
            }
            finally
            {
                btn.IsEnabled = true;
            }
        }
    }

    private async void OnDeleteAccountClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string phone)
        {
            var dialog = new ContentDialog
            {
                Title = LocalizationHelper.CurrentLanguage == "ar" ? "مسح معلومات الدخول" : "Delete Login Information",
                Content = LocalizationHelper.Get("confirm_remove_account"),
                PrimaryButtonText = LocalizationHelper.CurrentLanguage == "ar" ? "مسح نهائي" : "Delete Permanently",
                CloseButtonText = LocalizationHelper.Get("cancel_btn"),
                XamlRoot = this.XamlRoot,
                RequestedTheme = ElementTheme.Dark
            };

            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            {
                AccountManager.RemoveAccount(phone);

                var globalDb = new DatabaseService();
                if (globalDb.GetStatusVal("last_active_phone") == phone)
                {
                    globalDb.SetStatusVal("last_active_phone", "");
                }

                var accounts = AccountManager.GetAccounts();
                if (accounts.Count > 0)
                {
                    AccountsList.ItemsSource = null;
                    AccountsList.ItemsSource = accounts;
                }
                else
                {
                    AccountsSection.Visibility = Visibility.Collapsed;
                    LoginFormSection.Visibility = Visibility.Visible;
                    BackToAccountsBtn.Visibility = Visibility.Collapsed;
                }
            }
        }
    }

    private void OnUseAnotherClick(object sender, RoutedEventArgs e)
    {
        AccountsSection.Visibility = Visibility.Collapsed;
        LoginFormSection.Visibility = Visibility.Visible;
    }

    private void OnBackToAccountsClick(object sender, RoutedEventArgs e)
    {
        AccountsSection.Visibility = Visibility.Visible;
        LoginFormSection.Visibility = Visibility.Collapsed;
    }
}
