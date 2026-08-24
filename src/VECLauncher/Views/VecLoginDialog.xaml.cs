using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using VECLauncher.Models;
using VECLauncher.Services;

namespace VECLauncher.Views;

public partial class VecLoginDialog : Window
{
    private readonly VecAuthService _auth = new();
    public MinecraftAccount? ResultAccount { get; private set; }

    public VecLoginDialog()
    {
        InitializeComponent();
        Loaded += (s, e) => TxtUsername.Focus();
    }

    private void Header_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left) DragMove();
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void Mode_Changed(object sender, RoutedEventArgs e)
    {
        if (BtnSubmit == null) return;
        var isReg = RbModeRegister.IsChecked == true;
        BtnSubmit.Content = isReg ? "Зарегистрироваться" : "Войти";
        TxtStatus.Text = "";
    }

    private void Input_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            BtnSubmit_Click(sender, e);
        }
    }

    private async void BtnSubmit_Click(object sender, RoutedEventArgs e)
    {
        var username = TxtUsername.Text.Trim();
        var password = TxtPassword.Password;

        if (string.IsNullOrWhiteSpace(username))
        {
            ShowError("Введите имя пользователя.");
            TxtUsername.Focus();
            return;
        }

        if (string.IsNullOrEmpty(password))
        {
            ShowError("Введите пароль.");
            TxtPassword.Focus();
            return;
        }

        var isReg = RbModeRegister.IsChecked == true;
        BtnSubmit.IsEnabled = false;
        TxtStatus.Foreground = (Brush)new BrushConverter().ConvertFromString("#9CA3AF");
        TxtStatus.Text = isReg ? "Создаю аккаунт VEC ID..." : "Проверяю данные...";

        try
        {
            MinecraftAccount acc;
            if (isReg)
            {
                acc = await _auth.RegisterAsync(username, password);
            }
            else
            {
                acc = await _auth.LoginAsync(username, password);
            }

            ResultAccount = acc;
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
        finally
        {
            BtnSubmit.IsEnabled = true;
        }
    }

    private void ShowError(string msg)
    {
        TxtStatus.Foreground = (Brush)new BrushConverter().ConvertFromString("#EF4444");
        TxtStatus.Text = msg;
    }
}
