using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using TelegramDrive.Helpers;

namespace TelegramDrive.Views;

public sealed partial class OtpPage : Page
{
    private string _phone = "";

    public OtpPage()
    {
        this.InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is string phone)
        {
            _phone = phone;
        }
        ApplyLocalization();
    }

    private async void OnVerifyClick(object sender, RoutedEventArgs e)
    {
        string code = OtpBox.Text.Trim();
        string password = PasswordBox.Password.Trim();

        if (string.IsNullOrEmpty(code) && string.IsNullOrEmpty(password))
        {
            StatusLabel.Text = LocalizationHelper.CurrentLanguage == "ar" ? "يرجى تزويد رمز التحقق." : "Please provide the verification code.";
            StatusLabel.Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TgErrorBrush"];
            return;
        }

        VerifyBtn.IsEnabled = false;
        StatusLabel.Text = LocalizationHelper.Get("verifying_auth");
        StatusLabel.Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TgPrimaryBrush"];

        try
        {
            bool verified = await App.Telegram.VerifyCodeAsync(code, string.IsNullOrEmpty(password) ? null : password);

            if (verified)
            {
                // Retrieve user info and save account
                var info = App.Telegram.GetUserInfoDetailed();
                string phoneOrId = string.IsNullOrEmpty(info.Phone) ? info.Id.ToString() : info.Phone;
                if (!phoneOrId.StartsWith("+") && !string.IsNullOrEmpty(info.Phone)) phoneOrId = "+" + phoneOrId;

                // Load API credentials from global base database
                var globalDb = new Services.DatabaseService();
                var savedId = globalDb.GetStatusVal("api_id", "") ?? "";
                var savedHash = globalDb.GetStatusVal("api_hash", "") ?? "";

                var newAccount = new UserAccount
                {
                    Phone = phoneOrId,
                    Name = info.Name,
                    Username = info.Username,
                    ApiId = savedId,
                    ApiHash = savedHash,
                    SessionFile = $"wt_session_{phoneOrId}.session",
                    DbFile = $"telegram_drive_{phoneOrId}.db"
                };

                // Save to remembered accounts and switch active
                AccountManager.SaveAccount(newAccount);
                App.SwitchActiveAccount(newAccount);

                // Write credentials into user-specific database
                App.Database.SetStatusVal("api_id", savedId);
                App.Database.SetStatusVal("api_hash", savedHash);
                App.Database.SetStatusVal("phone_number", phoneOrId);

                // Save last active phone globally
                globalDb.SetStatusVal("last_active_phone", phoneOrId);

                var mainWindow = (MainWindow)App.MainWindowInstance!;
                mainWindow.NavigateAfterAuth();
            }
            else
            {
                // Might need 2FA
                PasswordLabel.Visibility = Visibility.Visible;
                PasswordBox.Visibility = Visibility.Visible;
                VerifyBtn.Content = LocalizationHelper.CurrentLanguage == "ar" ? "التحقق بكلمة المرور الإضافية" : "Verify with 2FA Password";
                StatusLabel.Text = LocalizationHelper.Get("pwd_required_msg");
                StatusLabel.Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TgWarningBrush"];
                VerifyBtn.IsEnabled = true;
            }
        }
        catch (Exception ex)
        {
            StatusLabel.Text = $"{LocalizationHelper.Get("verification_failed")}{ex.Message}";
            StatusLabel.Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TgErrorBrush"];
            VerifyBtn.IsEnabled = true;
        }
    }

    private void OnBackClick(object sender, RoutedEventArgs e)
    {
        var mainWindow = (MainWindow)App.MainWindowInstance!;
        mainWindow.NavigateToLogin();
    }

    private void ApplyLocalization()
    {
        this.FlowDirection = LocalizationHelper.CurrentLanguage == "ar" ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;

        TitleText.Text = LocalizationHelper.CurrentLanguage == "ar" ? "التحقق من الأمان" : "Security Verification";
        SubtitleText.Text = $"{LocalizationHelper.Get("otp_subtitle")} {_phone}";
        OtpLabel.Text = LocalizationHelper.Get("otp_label");
        OtpBox.PlaceholderText = LocalizationHelper.Get("otp_placeholder");
        PasswordLabel.Text = LocalizationHelper.Get("pwd_label");
        PasswordBox.PlaceholderText = LocalizationHelper.Get("pwd_placeholder");
        VerifyBtn.Content = LocalizationHelper.Get("verify_login");
        BackBtn.Content = LocalizationHelper.Get("back_to_login");
    }
}
