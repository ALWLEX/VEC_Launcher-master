using System;
using System.IO;
using System.Web;
using System.Windows;
using System.Windows.Input;
using Microsoft.Web.WebView2.Core;
using VECLauncher.Services;

namespace VECLauncher.Views;

public partial class MicrosoftLoginDialog : Window
{
    private readonly string _authUrl;
    public string? AuthorizationCode { get; private set; }
    public string? ErrorDescription { get; private set; }

    public MicrosoftLoginDialog(string authUrl)
    {
        InitializeComponent();
        _authUrl = authUrl;
        Loaded += MicrosoftLoginDialog_Loaded;
    }

    private async void MicrosoftLoginDialog_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            var userDataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "VECLauncher", "webview2_auth");

            var env = await CoreWebView2Environment.CreateAsync(null, userDataFolder);
            await Browser.EnsureCoreWebView2Async(env);

            Browser.CoreWebView2.NavigationStarting += CoreWebView2_NavigationStarting;
            Browser.CoreWebView2.Navigate(_authUrl);
        }
        catch (Exception ex)
        {
            Log.Warn($"MicrosoftLoginDialog: WebView2 initialization error: {ex.Message}");
            MessageBox.Show($"Failed to initialize Microsoft Edge WebView2: {ex.Message}");
            DialogResult = false;
            Close();
        }
    }

    private void Header_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void CoreWebView2_NavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        if (string.IsNullOrEmpty(e.Uri)) return;

        var url = e.Uri;

        if (url.StartsWith("https://login.live.com/oauth20_desktop.srf", StringComparison.OrdinalIgnoreCase))
        {
            e.Cancel = true;

            var uri = new Uri(url);
            var query = HttpUtility.ParseQueryString(uri.Query);

            var code = query["code"];
            var error = query["error"];
            var errorDesc = query["error_description"];

            if (!string.IsNullOrEmpty(code))
            {
                AuthorizationCode = code;
                DialogResult = true;
                Close();
            }
            else if (!string.IsNullOrEmpty(error))
            {
                ErrorDescription = errorDesc ?? error;
                DialogResult = false;
                Close();
            }
        }
    }
}