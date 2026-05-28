using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace TelegramDrive.Views;

public sealed partial class LoadingPage : Page
{
    public LoadingPage()
    {
        this.InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is string message)
            StatusText.Text = message;
    }
}
