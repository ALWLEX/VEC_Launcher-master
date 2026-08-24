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
    private void SetAccount(MinecraftAccount acc, bool refreshSkin)
    {
        _account = acc;

        TxtAccName.Text = acc.Username;
        TxtAccUuid.Text = acc.DashedUuid;

        TxtAccUuidShort.Text = acc.DashedUuid;

        if (acc.IsVec)
        {
            var vecUsers = VecAccountDatabase.GetAllUsers();
            var matched = vecUsers.FirstOrDefault(u => string.Equals(u.Username, acc.Username, StringComparison.OrdinalIgnoreCase));
            if (matched != null && !string.IsNullOrEmpty(matched.SkinModel))
            {
                acc.SkinModel = matched.SkinModel;
            }
        }

        _currentSkinModel = !string.IsNullOrEmpty(acc.SkinModel) ? acc.SkinModel : "classic";
        var isSlim = _currentSkinModel.Equals("slim", StringComparison.OrdinalIgnoreCase);
        var badge = isSlim ? "Slim" : "Classic";
        if (TxtSkinModel != null) TxtSkinModel.Text = badge;
        if (TxtSkinModelBadge != null) TxtSkinModelBadge.Text = badge;
        if (TxtToggleModelBadge != null) TxtToggleModelBadge.Text = badge;

        var accountType = acc.IsVec ? "VEC ID (КПВК)" : (acc.IsOffline ? "Оффлайн" : "Microsoft");
        var sideStatus = acc.IsVec ? "VEC ID" : (acc.IsOffline ? "Оффлайн" : "Microsoft");

        // Use SetCurrentValue for bound elements to preserve WPF bindings
        TxtAuthState.SetCurrentValue(TextBlock.TextProperty, acc.Username);
        TxtSideName.SetCurrentValue(TextBlock.TextProperty, acc.Username);
        TxtSideStatus.SetCurrentValue(TextBlock.TextProperty, sideStatus);

        // Hidden compatibility elements (not bound)
        TxtAccType.Text = accountType;

        BtnLogout.IsEnabled = true;
        BtnUploadSkin.IsEnabled = true;
        BtnResetSkin.IsEnabled = true;

        RefreshSavedAccountsListUI();
        RefreshSidebarAccounts();
        if (refreshSkin) _ = LoadSkinImagesAsync(acc);
    }

    private void RefreshSavedAccountsListUI()
    {
        if (StackSavedAccountsList == null) return;
        StackSavedAccountsList.Children.Clear();

        var saved = Vm.Accounts.GetAllSaved();
        if (TxtSavedAccountsCount != null)
            TxtSavedAccountsCount.Text = $"{saved.Count} профил{(saved.Count == 1 ? "ь" : (saved.Count >= 2 && saved.Count <= 4 ? "я" : "ей"))}";

        if (saved.Count == 0)
        {
            StackSavedAccountsList.Children.Add(new TextBlock
            {
                Text = "Нет сохранённых аккаунтов. Войдите или зарегистрируйтесь выше.",
                Foreground = (Brush)(TryFindResource("FgMuted") ?? Brushes.Gray),
                FontSize = 11,
                Margin = new Thickness(0, 4, 0, 4)
            });
            return;
        }

        foreach (var acc in saved)
        {
            var isCurrent = _account != null &&
                            _account.Username.Equals(acc.Username, StringComparison.OrdinalIgnoreCase) &&
                            _account.Type == acc.Type;

            var border = new Border
            {
                Background = new SolidColorBrush(isCurrent ? Color.FromRgb(26, 26, 30) : Color.FromRgb(14, 14, 16)),
                BorderBrush = new SolidColorBrush(isCurrent ? Color.FromRgb(80, 80, 90) : Color.FromRgb(34, 34, 38)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(10, 8, 10, 8),
                Margin = new Thickness(0, 0, 0, 6)
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // Badge
            var badgeText = acc.IsVec ? "V" : (acc.IsOffline ? "O" : "M");
            var badgeBg = acc.IsVec ? "#222" : (acc.IsOffline ? "#181818" : "#1E2A38");
            var badgeBorder = new Border
            {
                Width = 28, Height = 28,
                CornerRadius = new CornerRadius(6),
                Background = (Brush)new BrushConverter().ConvertFromString(badgeBg),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock
                {
                    Text = badgeText,
                    FontWeight = FontWeights.Bold,
                    FontSize = 13,
                    Foreground = Brushes.White,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
            Grid.SetColumn(badgeBorder, 0);
            grid.Children.Add(badgeBorder);

            var infoStack = new StackPanel { Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            var nameBlock = new TextBlock
            {
                Text = acc.Username + (isCurrent ? " (Активен)" : ""),
                FontWeight = isCurrent ? FontWeights.Bold : FontWeights.SemiBold,
                FontSize = 12,
                Foreground = isCurrent ? Brushes.White : (Brush)(TryFindResource("Fg") ?? Brushes.LightGray)
            };
            var typeBlock = new TextBlock
            {
                Text = acc.IsVec ? "VEC ID (КПВК)" : (acc.IsOffline ? "Оффлайн-профиль" : "Microsoft"),
                FontSize = 10,
                Foreground = (Brush)(TryFindResource("FgMuted") ?? Brushes.Gray)
            };
            infoStack.Children.Add(nameBlock);
            infoStack.Children.Add(typeBlock);
            Grid.SetColumn(infoStack, 1);
            grid.Children.Add(infoStack);

            var btnStack = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

            if (!isCurrent)
            {
                var btnSelect = new Button
                {
                    Content = "Выбрать",
                    Padding = new Thickness(10, 4, 10, 4),
                    FontSize = 11,
                    FontWeight = FontWeights.SemiBold,
                    Background = Brushes.White,
                    Foreground = Brushes.Black,
                    Cursor = Cursors.Hand,
                    Margin = new Thickness(0, 0, 6, 0)
                };
                btnSelect.Click += (s, e) =>
                {
                    Vm.Accounts.Save(acc);
                    SetAccount(acc, refreshSkin: true);
                    RefreshSavedAccountsListUI();
                    RefreshSidebarAccounts();
                    _toast.Show("Профиль переключен", $"Активен аккаунт: {acc.Username}", NotificationType.Info);
                };
                btnStack.Children.Add(btnSelect);
            }

            var btnDelete = new Button
            {
                Content = "✕",
                Padding = new Thickness(6, 4, 6, 4),
                FontSize = 11,
                Background = Brushes.Transparent,
                Foreground = (Brush)(TryFindResource("FgMuted") ?? Brushes.Gray),
                BorderBrush = Brushes.Transparent,
                Cursor = Cursors.Hand,
                ToolTip = "Удалить из списка"
            };
            btnDelete.Click += (s, e) =>
            {
                Vm.Accounts.Remove(acc.Username, acc.Type);
                if (isCurrent)
                {
                    var next = Vm.Accounts.GetAllSaved().FirstOrDefault();
                    if (next != null)
                    {
                        Vm.Accounts.Save(next);
                        SetAccount(next, refreshSkin: true);
                    }
                    else
                    {
                        BtnLogout_Click(s, e);
                    }
                }
                RefreshSavedAccountsListUI();
                RefreshSidebarAccounts();
            };
            btnStack.Children.Add(btnDelete);

            Grid.SetColumn(btnStack, 2);
            grid.Children.Add(btnStack);

            border.Child = grid;
            StackSavedAccountsList.Children.Add(border);
        }
    }

}
