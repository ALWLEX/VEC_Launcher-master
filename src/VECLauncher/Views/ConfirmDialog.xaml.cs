using System;
using System.Windows;
using System.Windows.Input;
using VECLauncher.Services;

namespace VECLauncher.Views;

public partial class ConfirmDialog : Window
{
    public bool Confirmed { get; private set; }

    public ConfirmDialog(string title, string message, string confirmText = "Запустить", string cancelText = "Отмена")
    {
        InitializeComponent();
        TxtTitle.Text = title;
        TxtMessage.Text = message;
        BtnOk.Content = confirmText;
        BtnCancel.Content = cancelText;
    }

    public static bool Show(Window? owner, string title, string message, string confirmText = "Запустить", string cancelText = "Отмена")
    {
        var dlg = new ConfirmDialog(title, message, confirmText, cancelText);
        if (owner != null && owner.IsLoaded && owner.IsVisible)
        {
            dlg.Owner = owner;
        }
        else
        {
            dlg.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }
        dlg.ShowDialog();
        return dlg.Confirmed;
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            try { DragMove(); } catch (Exception ex) { Log.Warn(ex.Message); }
        }
    }

    private void BtnOk_Click(object sender, RoutedEventArgs e)
    {
        Confirmed = true;
        DialogResult = true;
        Close();
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        Confirmed = false;
        DialogResult = false;
        Close();
    }
}
