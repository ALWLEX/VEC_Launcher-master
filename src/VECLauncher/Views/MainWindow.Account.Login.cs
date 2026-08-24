using IOPath = System.IO.Path;
using Microsoft.Win32;
using System.IO;
using System.Net.Http;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using System.Windows.Media;
using System.Windows.Threading;
using System.Windows;
using VECLauncher.Models;
using VECLauncher.Services;

namespace VECLauncher.Views;

/// <summary>
/// Partial class handling account management: login/logout, saved accounts list,
/// skin loading and display, skin upload/reset, cape wardrobe, and avatar rendering.
/// </summary>
public partial class MainWindow
{
    private void BtnOfflineLogin_Click(object sender, RoutedEventArgs e)
    {
        var name = TxtOfflineName.Text.Trim();

        if (!OfflineAccountService.TryValidateName(name, out var error))
        {
            TxtOfflineHint.Text = error;
            TxtOfflineHint.Foreground = (Brush)FindResource("Danger");
            TxtOfflineName.Focus();
            return;
        }

        try
        {
            var acc = OfflineAccountService.Create(name);
            Vm.Accounts.Save(acc);
            Vm.SetAccount(acc, refreshSkin: true);
            SetAccount(acc, refreshSkin: true);

            TxtOfflineHint.Text = "Оффлайн-профиль создан.";
            TxtOfflineHint.Foreground = (Brush)FindResource("Accent");
            AppendLog($"Создан оффлайн-аккаунт: {acc.Username} ({acc.DashedUuid})");
        }
        catch (Exception ex)
        {
            TxtOfflineHint.Text = ex.Message;
            TxtOfflineHint.Foreground = (Brush)FindResource("Danger");
        }
    }

    private async Task<MinecraftAccount> ReloginMicrosoftAsync()
    {
        var authUrl = _auth.BuildLiveAuthorizeUrl();
        var dialog = new MicrosoftLoginDialog(authUrl) { Owner = this };
        var result = dialog.ShowDialog();

        if (result != true || string.IsNullOrEmpty(dialog.AuthorizationCode))
            throw new InvalidOperationException("Повторный вход отменён пользователем.");

        var acc = await _auth.SignInWithLiveCodeAsync(dialog.AuthorizationCode);
        Vm.Accounts.Save(acc);
        SetAccount(acc, refreshSkin: false);
        AppendLog($"Сессия Microsoft обновлена: {acc.Username}");
        return acc;
    }

    private async void BtnLogin_Click(object sender, RoutedEventArgs e)
    {
        BtnLogin.IsEnabled = false;
        try
        {
            await Vm.LoginMicrosoftCommand.ExecuteAsync(null);
        }
        finally
        {
            BtnLogin.IsEnabled = true;
            if (Vm.Account != null)
                SetAccount(Vm.Account, refreshSkin: true);
            RefreshSavedAccountsListUI();
            RefreshSidebarAccounts();
        }
    }

    private async void BtnVecLogin_Click(object sender, RoutedEventArgs e)
    {
        await Vm.LoginVecCommand.ExecuteAsync(null);
        if (Vm.Account != null)
            SetAccount(Vm.Account, refreshSkin: true);
        RefreshSavedAccountsListUI();
        RefreshSidebarAccounts();
    }

    private ImageSource? GetAccountAvatarImage(MinecraftAccount acc, int size = 32)
    {
        try
        {
            var localFile = _offlineSkins.FindAccountSkin(acc.Username);
            if (localFile != null && File.Exists(localFile))
            {
                var bytes = File.ReadAllBytes(localFile);
                var head = SkinService.CreateCrispHeadAvatar(bytes, size);
                if (head != null) return head;
            }

            if (_account != null && _account.Username.Equals(acc.Username, StringComparison.OrdinalIgnoreCase) && _currentSkinRawBytes != null && _currentSkinRawBytes.Length > 0)
            {
                var head = SkinService.CreateCrispHeadAvatar(_currentSkinRawBytes, size);
                if (head != null) return head;
            }

            if (!acc.IsOffline && !string.IsNullOrEmpty(acc.Uuid))
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var skinUrl = !string.IsNullOrEmpty(acc.SkinUrl) ? acc.SkinUrl : SkinService.RawSkinUrl(acc.Uuid);
                        if (!string.IsNullOrEmpty(skinUrl))
                        {
                            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(6) };
                            using var resp = await http.GetAsync(skinUrl);
                            if (resp.IsSuccessStatusCode)
                            {
                                var downloaded = await resp.Content.ReadAsByteArrayAsync();
                                var path = _offlineSkins.AccountSkinPath(acc.Username);
                                var dir = System.IO.Path.GetDirectoryName(path);
                                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                                await File.WriteAllBytesAsync(path, downloaded);
                            }
                        }
                    }
                    catch (Exception ex) { Log.Warn(ex.Message); }
                });
            }

            var isSlim = _currentSkinModel.Equals("slim", StringComparison.OrdinalIgnoreCase);
            var defBytes = DefaultSkinService.GetDefaultSkin(acc.Username, isSlim);
            if (defBytes != null && defBytes.Length > 0)
            {
                var head = SkinService.CreateCrispHeadAvatar(defBytes, size);
                if (head != null) return head;
            }
        }
        catch (Exception ex) { Log.Warn(ex.Message); }

        return null;
    }

    private void RefreshSidebarAccounts()
    {
        if (StackSidebarAccounts == null) return;

        StackSidebarAccounts.Children.Clear();
        var saved = Vm.Accounts.GetAllSaved();

        if (saved.Count == 0)
        {
            var tbEmpty = new TextBlock
            {
                Text = "Нет сохранённых аккаунтов",
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(120, 120, 130)),
                Margin = new Thickness(6, 4, 6, 6)
            };
            StackSidebarAccounts.Children.Add(tbEmpty);
        }
        else
        {
            foreach (var acc in saved)
            {
                var isCurrent = _account != null &&
                                _account.Username.Equals(acc.Username, StringComparison.OrdinalIgnoreCase) &&
                                _account.Type == acc.Type;

                var btn = new Button
                {
                    Background = Brushes.Transparent,
                    BorderThickness = new Thickness(0),
                    Cursor = Cursors.Hand,
                    Padding = new Thickness(0),
                    Margin = new Thickness(0, 0, 0, 2)
                };

                var border = new Border
                {
                    CornerRadius = new CornerRadius(5),
                    Padding = new Thickness(6, 4, 6, 4),
                    Background = isCurrent ? new SolidColorBrush(Color.FromRgb(22, 22, 30)) : Brushes.Transparent
                };

                var grid = new Grid();
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var imgBox = new Border
                {
                    Width = 22,
                    Height = 22,
                    CornerRadius = new CornerRadius(4),
                    Background = new SolidColorBrush(Color.FromRgb(24, 24, 32)),
                    ClipToBounds = true
                };

                var avatarHead = GetAccountAvatarImage(acc, 32);
                if (avatarHead != null)
                {
                    var img = new Image
                    {
                        Source = avatarHead,
                        Stretch = Stretch.UniformToFill
                    };
                    RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.NearestNeighbor);
                    RenderOptions.SetEdgeMode(img, EdgeMode.Aliased);
                    imgBox.Child = img;
                }
                else
                {
                    var tbIcon = new TextBlock
                    {
                        Text = acc.IsVec ? "V" : (acc.IsOffline ? "O" : "M"),
                        FontSize = 10.5,
                        FontWeight = FontWeights.Bold,
                        Foreground = Brushes.White,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    imgBox.Child = tbIcon;
                }

                Grid.SetColumn(imgBox, 0);
                grid.Children.Add(imgBox);

                var stack = new StackPanel { Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
                var tbName = new TextBlock
                {
                    Text = acc.Username,
                    FontSize = 11,
                    FontWeight = isCurrent ? FontWeights.Bold : FontWeights.Normal,
                    Foreground = isCurrent ? Brushes.White : new SolidColorBrush(Color.FromRgb(180, 180, 190)),
                    TextTrimming = TextTrimming.CharacterEllipsis
                };
                var tbType = new TextBlock
                {
                    Text = acc.IsVec ? "VEC ID" : (acc.IsOffline ? "Оффлайн" : "Microsoft"),
                    FontSize = 9,
                    Foreground = new SolidColorBrush(Color.FromRgb(90, 90, 100))
                };
                stack.Children.Add(tbName);
                stack.Children.Add(tbType);
                Grid.SetColumn(stack, 1);
                grid.Children.Add(stack);

                if (isCurrent)
                {
                    var tbCheck = new TextBlock
                    {
                        Text = "✓",
                        FontSize = 11,
                        FontWeight = FontWeights.Bold,
                        Foreground = Brushes.White,
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(4, 0, 0, 0)
                    };
                    Grid.SetColumn(tbCheck, 2);
                    grid.Children.Add(tbCheck);
                }

                border.Child = grid;
                btn.Content = border;

                var targetAcc = acc;
                btn.Click += (s, ev) =>
                {
                    PopupSavedAccounts.IsOpen = false;
                    Vm.Accounts.Save(targetAcc);
                    SetAccount(targetAcc, refreshSkin: true);
                    _toast.Show("Профиль переключен", $"Активен аккаунт: {targetAcc.Username}", NotificationType.Info);
                };

                StackSidebarAccounts.Children.Add(btn);
            }
        }

    }

    private void BorderSideUser_Click(object sender, MouseButtonEventArgs e)
    {
        RefreshSidebarAccounts();
        PopupSavedAccounts.IsOpen = true;
    }

    private void BtnSidebarAddAccount_Click(object sender, RoutedEventArgs e)
    {
        if (PopupSavedAccounts != null) PopupSavedAccounts.IsOpen = false;
        if (NavAccount != null) NavAccount.IsChecked = true;
    }

    private void BtnSidebarLogout_Click(object sender, RoutedEventArgs e)
    {
        if (PopupSavedAccounts != null) PopupSavedAccounts.IsOpen = false;
        BtnLogout_Click(sender, e);
    }

    private void BtnLogout_Click(object sender, RoutedEventArgs e)
    {
        // Delegate data logic to VM
        Vm.LogoutCommand.Execute(null);

        // UI cleanup (only things VM can't touch)
        _currentSkinRawBytes = null;
        TxtAccName.Text = "—";
        TxtAccUuid.Text = "";
        TxtAccType.Text = "";
        TxtAuthState.Text = "Вы не вошли в аккаунт.";
        TxtSideName.SetCurrentValue(TextBlock.TextProperty, "Не выполнен вход");
        TxtSideStatus.SetCurrentValue(TextBlock.TextProperty, "Оффлайн");
        ImgSkinPreview.Source = null;
        ImgAvatar.Source = null;
        ImgAvatarLarge.Source = null;
        ReloadSkin3DModel();
        TxtSkinPlaceholder.Visibility = Visibility.Visible;

        BtnLogout.IsEnabled = false;
        BtnUploadSkin.IsEnabled = false;
        BtnResetSkin.IsEnabled = false;

        TxtOfflineName.Clear();
        TxtOfflineHint.Text = "Введите никнейм (3-16 символов).";
        TxtOfflineHint.Foreground = (Brush)FindResource("FgMuted");
        TxtSkinStatus.Text = "";

        RefreshSavedAccountsListUI();
        RefreshSidebarAccounts();
    }

}
