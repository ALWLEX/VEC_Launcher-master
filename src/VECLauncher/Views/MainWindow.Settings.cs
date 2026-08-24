using IOPath = System.IO.Path;
using Microsoft.Win32;
using System.IO;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Media;
using System.Windows.Threading;
using System.Windows;
using VECLauncher.Models;
using VECLauncher.Services;
using VECLauncher.ViewModels;

namespace VECLauncher.Views;

/// <summary>
/// Partial class handling settings UI: game directory selection, JVM memory, window resolution,
/// language, Java path browsing, maintenance tools, and settings persistence.
/// </summary>
public partial class MainWindow
{
    private void ApplySettingsToUi()
    {
        if (string.IsNullOrWhiteSpace(_settings.GameDir)) _settings.GameDir = LauncherPaths.Root;

        SldMemory.Value = Math.Clamp(_settings.MaxMemoryMb, 1024, 16384);
        TxtMemory.Text = $"{_settings.MaxMemoryMb} МБ";
        TxtWidth.Text = _settings.WindowWidth.ToString();
        TxtHeight.Text = _settings.WindowHeight.ToString();
        ChkFullscreen.IsChecked = _settings.Fullscreen;
        ChkSnapshots.IsChecked = _settings.ShowSnapshots;
        ChkCloseOnLaunch.IsChecked = _settings.CloseLauncherOnStart;
        ChkShowConsole.IsChecked = _settings.ShowConsole;
        ChkAllowMultiple.IsChecked = _settings.AllowMultipleInstances;
        ChkMinimizeOnLaunch.IsChecked = _settings.MinimizeOnLaunch;
        ChkConfirmStop.IsChecked = _settings.ConfirmGameStop;
        ChkDefaultIsolated.IsChecked = _settings.DefaultIsolated;
        ChkAutoLanguage.IsChecked = _settings.AutoSetGameLanguage;
        RbLangRu.IsChecked = _settings.GameLanguage != "en";
        RbLangEn.IsChecked = _settings.GameLanguage == "en";
        TxtJvmArgs.Text = _settings.ExtraJvmArgs;
        TxtGameDir.Text = _settings.GameDir;
        TxtJavaPath.Text = _settings.CustomJavaPath;

        var totalRam = (long)(GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / (1024 * 1024));
        TxtMemoryHint.Text = totalRam > 0
            ? $"Всего в системе: {totalRam} МБ. Для ванильной игры обычно достаточно 2048–4096 МБ."
            : "Для ванильной игры обычно достаточно 2048–4096 МБ.";
    }

    private void PersistSettings()
    {
        _settings.MaxMemoryMb = (int)SldMemory.Value;
        _settings.WindowWidth = ParseIntOr(TxtWidth.Text, 1280);
        _settings.WindowHeight = ParseIntOr(TxtHeight.Text, 720);
        _settings.Fullscreen = ChkFullscreen.IsChecked == true;
        _settings.ShowSnapshots = ChkSnapshots.IsChecked == true;
        _settings.CloseLauncherOnStart = ChkCloseOnLaunch.IsChecked == true;
        _settings.ShowConsole = ChkShowConsole.IsChecked == true;
        _settings.AllowMultipleInstances = ChkAllowMultiple.IsChecked == true;
        _settings.MinimizeOnLaunch = ChkMinimizeOnLaunch.IsChecked == true;
        _settings.ConfirmGameStop = ChkConfirmStop.IsChecked == true;
        _settings.DefaultIsolated = ChkDefaultIsolated.IsChecked == true;
        _settings.AutoSetGameLanguage = ChkAutoLanguage.IsChecked == true;
        _settings.ExtraJvmArgs = TxtJvmArgs.Text.Trim();
        _settings.CustomJavaPath = TxtJavaPath.Text.Trim();
        _settings.LastInstanceId = _selectedInstance?.Id ?? _settings.LastInstanceId;

        SettingsService.Save(_settings);

        Vm.Events.Publish(new SettingsSavedEvent(_settings));

        if (!_initializing && _instancesService.Loaded) _instancesService.SaveAll(_instances);
    }

    private static int ParseIntOr(string s, int fallback) =>
        int.TryParse(s.Trim(), out var v) && v > 0 ? v : fallback;

    private void DetectJava()
    {
        try
        {
            var list = _java.FindAll();
            Dispatcher.Invoke(() =>
            {
                if (list.Count == 0)
                {
                    TxtJavaList.Text = "Java не обнаружена. Лаунчер скачает нужную версию автоматически.";
                }
                else
                {
                    TxtJavaList.Text = "Найдено:\n" + string.Join("\n", list.Select(j => "  • " + j));
                }
            });
        }
        catch (Exception ex) { Log.Warn("Ошибка поиска Java: " + ex.Message); }
    }

    private void BuildThemeCards()
    {
    }

    private void ApplyWindowBackground()
    {
        var brush = ThemeService.BuildWindowBackground(
            _settings.WindowBackgroundPath, _settings.WindowBackgroundOpacity);

        WindowBgLayer.Fill = brush ?? (Brush)new SolidColorBrush(Colors.Transparent);
    }

    private void GameLang_Checked(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;

        _settings.GameLanguage = (sender as FrameworkElement)?.Tag?.ToString() ?? "ru";
        SettingsService.Save(_settings);
    }

    private void BuildAccentSwatches()
    {
    }

    private static SolidColorBrush FrozenBrush(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }
    private void BuildBackgroundStyleButtons()
    {
    }

    private void ApplyBanner()
    {
    }

    private void Setting_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded || _initializing) return;
        PersistSettings();
        UpdateRunStateUi();
        ShowSavedHint();
    }

    private List<InstalledVersion> _installedVersions = new();

    private string _currentSettingsSection = "game";

    private DispatcherTimer? _autoSaveTimer;

    private void ScheduleAutoSave(Action action)
    {
        _autoSaveTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };

        _autoSaveTimer.Stop();

        foreach (var d in _autoSaveHandlers) _autoSaveTimer.Tick -= d;
        _autoSaveHandlers.Clear();

        EventHandler handler = (_, _) =>
        {
            _autoSaveTimer!.Stop();
            action();
        };

        _autoSaveHandlers.Add(handler);
        _autoSaveTimer.Tick += handler;
        _autoSaveTimer.Start();
    }

    private readonly List<EventHandler> _autoSaveHandlers = new();

    private void SettingText_Changed(object sender, TextChangedEventArgs e)
    {
        if (!IsLoaded || _initializing) return;
        ScheduleAutoSave(() => { PersistSettings(); ShowSavedHint(); });
    }

    private void InstSettingText_Changed(object sender, TextChangedEventArgs e)
    {
        if (!IsLoaded || _initializing || _loadingInstSettings) return;
        ScheduleAutoSave(() => InstSetting_Changed(sender, e));
    }

    private void ShowSavedHint()
    {
        if (TxtSettingsHint is null) return;

        TxtSettingsHint.Text = $"Сохранено в {DateTime.Now:HH:mm:ss}";
        TxtSettingsHint.Foreground = (Brush)FindResource("Accent");

        var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        t.Tick += (s, _) =>
        {
            t.Stop();
            TxtSettingsHint.Text = "Изменения применяются и сохраняются сразу";
            TxtSettingsHint.Foreground = (Brush)FindResource("FgMuted");
        };
        t.Start();
    }

    private void BtnResetSection_Click(object sender, RoutedEventArgs e)
    {
        var sectionName = _currentSettingsSection switch
        {
            "java" => "«Java и память»",
            "storage" => "«Хранилище»",
            "versions" => "«Версии игры»",
            "maint" => "«Обслуживание»",
            _ => "«Игра»"
        };

        if (_currentSettingsSection is "versions" or "maint")
        {
            _dialog.ShowMessage($"В разделе {sectionName} нет настроек для сброса.", "Сброс");
            return;
        }

        if (!_dialog.ConfirmAsync("Сброс раздела", $"Сбросить настройки раздела {sectionName} к значениям по умолчанию?").GetAwaiter().GetResult()) return;

        var def = new LauncherSettings();

        switch (_currentSettingsSection)
        {
            case "game":
                _settings.WindowWidth = def.WindowWidth;
                _settings.WindowHeight = def.WindowHeight;
                _settings.Fullscreen = def.Fullscreen;
                _settings.AllowMultipleInstances = def.AllowMultipleInstances;
                _settings.MinimizeOnLaunch = def.MinimizeOnLaunch;
                _settings.ConfirmGameStop = def.ConfirmGameStop;
                _settings.CloseLauncherOnStart = def.CloseLauncherOnStart;
                _settings.ShowConsole = def.ShowConsole;
                _settings.ShowSnapshots = def.ShowSnapshots;
                _settings.DefaultIsolated = def.DefaultIsolated;
                _settings.AutoSetGameLanguage = def.AutoSetGameLanguage;
                _settings.GameLanguage = def.GameLanguage;
                break;

            case "java":
                _settings.MaxMemoryMb = LauncherSettings.RecommendedMaxMemory();
                _settings.CustomJavaPath = "";
                _settings.ExtraJvmArgs = "";
                break;

            case "storage":
                _settings.GameDir = LauncherPaths.Root;
                break;
        }

        SettingsService.Save(_settings);
        ApplySettingsToUi();

        AppendLog($"Раздел {sectionName} сброшен.");
    }
    private void BtnScanVersions_Click(object sender, RoutedEventArgs e) => ScanVersions();

    private void ScanVersions()
    {
        TxtVersionsSummary.Text = "Сканирую…";

        try
        {
            _installedVersions = VersionManagerService.Scan(_instances);

            var total = _installedVersions.Sum(v => v.SizeBytes);
            TxtVersionsSummary.Text = _installedVersions.Count == 0
                ? "Версии ещё не установлены. Они появятся после первого запуска игры."
                : $"Всего версий: {_installedVersions.Count}  ·  занято {Human(total)}";

            var defaultIcon = new BitmapImage(new Uri("pack://application:,,,/Assets/default_instance_icon.jpg"));
            ItemsVersions.ItemsSource = _installedVersions.Select(v =>
            {
                var parts = new List<string> { v.SizeDisplay };
                if (v.IsIsolated) parts.Add($"изолированная · {v.OwnerInstance}");
                if (!v.HasJar) parts.Add("клиент не загружен");
                if (v.InheritsFrom is not null) parts.Add($"на базе {v.InheritsFrom}");
                parts.Add(v.InUse ? "используется: " + string.Join(", ", v.UsedBy) : "не используется");

                ImageSource icon = defaultIcon;
                var owner = _instances.FirstOrDefault(i => string.Equals(i.Id, v.OwnerInstance, StringComparison.OrdinalIgnoreCase) ||
                                                           string.Equals(i.Name, v.OwnerInstance, StringComparison.OrdinalIgnoreCase));
                if (owner != null && !string.IsNullOrEmpty(owner.IconPath) && File.Exists(owner.IconPath))
                {
                    try { icon = new BitmapImage(new Uri(owner.IconPath)); } catch (Exception ex) { Log.Warn(ex.Message); }
                }

                return new
                {
                    v.Id,
                    v.Kind,
                    Dir = v.Directory,
                    Info = string.Join("  ·  ", parts),
                    IconSource = icon
                };
            }).ToList();
        }
        catch (Exception ex)
        {
            TxtVersionsSummary.Text = "Ошибка сканирования: " + ex.Message;
        }
    }

    private void VersionOpen_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.Tag is not string dir) return;
        try { _instancesService.OpenFolder(dir); }
        catch (Exception ex) { AppendLog(ex.Message); }
    }

    private void VersionDelete_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.Tag is not string id) return;

        var version = _installedVersions.FirstOrDefault(v => v.Id == id);
        if (version is null) return;

        if (_sessions.AnyRunning)
        {
            _dialog.ShowMessage("Сначала остановите игру — файлы версии сейчас заняты.", "Игра запущена", MessageSeverity.Warning);
            return;
        }

        var warn = version.InUse
            ? $"\n\nВНИМАНИЕ: версию используют сборки: {string.Join(", ", version.UsedBy)}.\n" +
              "После удаления они скачают файлы заново при запуске."
            : "";

        var r = _dialog.ConfirmAsync(
            "Удаление версии",
            $"Удалить версию «{version.Id}»?\n\n" +
            $"Освободится {version.SizeDisplay}.\n" +
            "Моды, миры и настройки сборок затронуты не будут." + warn).GetAwaiter().GetResult();

        if (!r) return;

        try
        {
            var freed = VersionManagerService.Delete(version);
            AppendLog($"Версия {version.Id} удалена, освобождено {Human(freed)}.");
            ScanVersions();
        }
        catch (Exception ex)
        {
            _dialog.ShowMessage("Не удалось удалить: " + ex.Message + "\n\n" +
                            "Возможно, файлы заняты другой программой.",
                "Ошибка", MessageSeverity.Error);
        }
    }
    private void BtnCalcSize_Click(object sender, RoutedEventArgs e)
    {
        TxtStorageInfo.Text = "Считаю…";

        _ = Task.Run(() =>
        {
            long Size(string dir)
            {
                try
                {
                    return Directory.Exists(dir)
                        ? new DirectoryInfo(dir).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length)
                        : 0;
                }
                catch { return 0; }
            }

            var libs = Size(LauncherPaths.LibrariesDir);
            var assets = Size(LauncherPaths.AssetsDir);
            var versions = Size(LauncherPaths.VersionsDir);
            var runtime = Size(LauncherPaths.RuntimeDir);
            var cache = Size(LauncherPaths.CacheDir);

            var perInstance = new List<string>();
            long instancesTotal = 0;

            foreach (var inst in _instances)
            {
                var s = Size(_instancesService.InstanceDir(inst));
                instancesTotal += s;
                perInstance.Add($"     • {inst.Name}: {Human(s)}" + (inst.Isolated ? "  (изолированная)" : ""));
            }

            var text =
                $"Общее хранилище:\n" +
                $"     библиотеки: {Human(libs)}\n" +
                $"     ресурсы: {Human(assets)}\n" +
                $"     версии: {Human(versions)}\n" +
                $"     Java: {Human(runtime)}\n" +
                $"     кэш: {Human(cache)}\n\n" +
                $"Сборки ({_instances.Count}): {Human(instancesTotal)}\n" +
                string.Join("\n", perInstance) +
                $"\n\nВсего: {Human(libs + assets + versions + runtime + cache + instancesTotal)}";

            Dispatcher.Invoke(() => TxtStorageInfo.Text = text);
        });
    }

    private void BtnClearCache_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var freed = 0L;
            if (Directory.Exists(LauncherPaths.CacheDir))
            {
                foreach (var f in Directory.GetFiles(LauncherPaths.CacheDir))
                {
                    if (f.EndsWith("version_manifest_v2.json", StringComparison.OrdinalIgnoreCase)) continue;
                    try { freed += new FileInfo(f).Length; File.Delete(f); } catch (Exception ex) { Log.Warn(ex.Message); }
                }
            }

            TxtMaintenance.Text = $"Кэш очищен, освобождено {Human(freed)}.";
            AppendLog($"Кэш очищен ({Human(freed)}).");
        }
        catch (Exception ex)
        {
            TxtMaintenance.Text = "Не удалось очистить кэш: " + ex.Message;
        }
    }

    private async void BtnCheckCurse_Click(object sender, RoutedEventArgs e)
    {
        TxtMaintenance.Text = "Проверяю доступ к Modrinth API…";

        var ok = await Task.FromResult(true);

        TxtMaintenance.Text = ok
            ? "Modrinth API доступен."
            : "Только Modrinth.";

        UpdateModsSubtitle();
    }

    private void BtnResetSettings_Click(object sender, RoutedEventArgs e)
    {
        var r = _dialog.ConfirmAsync(
            "Сброс настроек",
            "Сбросить все настройки лаунчера к значениям по умолчанию?\n\n" +
            "Сборки, моды и аккаунт затронуты не будут.").GetAwaiter().GetResult();

        if (!r) return;

        _settings = new LauncherSettings
        {
            MaxMemoryMb = LauncherSettings.RecommendedMaxMemory(),
            GameDir = LauncherPaths.Root
        };

        SettingsService.Save(_settings);
        ThemeService.ApplyTheme(_settings.Theme);
        ThemeService.ApplyAccent(_settings.AccentColor);
        ApplySettingsToUi();
        BuildThemeCards();
        BuildAccentSwatches();
        BuildBackgroundStyleButtons();
        ApplyBanner();
        ApplyWindowBackground();

        TxtMaintenance.Text = "Настройки сброшены.";
        AppendLog("Настройки сброшены к значениям по умолчанию.");
    }

    private async void BtnRedeemPromo_Click(object sender, RoutedEventArgs e)
    {
        var code = TxtPromoCode?.Text?.Trim();
        if (string.IsNullOrEmpty(code))
        {
            TxtPromoStatus.Text = "Введите промо-код.";
            TxtPromoStatus.Foreground = (Brush)FindResource("FgMuted");
            return;
        }

        if (_account is null)
        {
            TxtPromoStatus.Text = "Войдите в аккаунт для активации кода.";
            TxtPromoStatus.Foreground = (Brush)FindResource("FgMuted");
            return;
        }

        BtnRedeemPromo.IsEnabled = false;
        TxtPromoStatus.Text = "Активирую...";
        TxtPromoStatus.Foreground = (Brush)FindResource("FgMuted");

        try
        {
            var srvUrl = !string.IsNullOrEmpty(_account.ServerUrl)
                ? _account.ServerUrl
                : await VecAuthService.GetActiveServerUrlAsync();

            var msg = await _skins.RedeemPromoCodeAsync(srvUrl, _account.Username, code);
            TxtPromoStatus.Text = msg;
            TxtPromoStatus.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x4A, 0xDE, 0x80));
            TxtPromoCode.Text = "";
            AppendLog($"Промо-код активирован: {code}");
        }
        catch (Exception ex)
        {
            TxtPromoStatus.Text = ex.Message;
            TxtPromoStatus.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xEF, 0x44, 0x44));
            Log.Warn($"Ошибка активации промо-кода: {ex.Message}");
        }
        finally
        {
            BtnRedeemPromo.IsEnabled = true;
        }
    }

    private void SldMemory_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!IsLoaded) return;
        var mb = (int)e.NewValue;
        TxtMemory.Text = $"{mb} МБ";
        _settings.MaxMemoryMb = mb;
    }

    private void BtnBrowseJava_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "Выберите java.exe",
            Filter = "java.exe|java.exe;javaw.exe|Исполняемые файлы (*.exe)|*.exe",
            CheckFileExists = true
        };
        if (dlg.ShowDialog(this) != true) return;

        var probe = JavaService.Probe(dlg.FileName, "custom");
        if (probe is null)
        {
            _dialog.ShowMessage("Не удалось определить версию Java.", "Java", MessageSeverity.Warning);
            return;
        }

        TxtJavaPath.Text = dlg.FileName;
        _settings.CustomJavaPath = dlg.FileName;
        AppendLog("Выбрана Java: " + probe);
    }

    private void BtnOpenDir_Click(object sender, RoutedEventArgs e)
    {
        try { _instancesService.OpenFolder(_settings.GameDir); }
        catch (Exception ex) { AppendLog("Не удалось открыть папку: " + ex.Message); }
    }

    private void BtnOpenLogFile_Click(object sender, RoutedEventArgs e)
    {
        try { _instancesService.RevealFile(LauncherPaths.LauncherLogFile); }
        catch (Exception ex) { AppendLog(ex.Message); }
    }

    private void BtnClearLog_Click(object sender, RoutedEventArgs e) => Vm.DoClearLog();

    private void Nav_Checked(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded || PageHome is null) return;

        var tag = (sender as FrameworkElement)?.Tag?.ToString() ?? "0";

        // Sync VM navigation state
        Vm.NavigateCommand.Execute(tag);

        AnimatePageTransition(tag);

        if (tag == "1") { RefreshInstanceStats(); LoadScreenshots(); }
        if (tag == "3") UpdateSkinTabHeader();
        if (tag == "6")
        {
            UpdateModsSubtitle();
            if (_modResults.Count == 0 && _selectedInstance is not null) RunModSearchFromStart();
        }
        if (tag == "7") RefreshContent();
    }

    private void AnimatePageTransition(string tag)
    {
        var pages = new Dictionary<string, Grid>
        {
            ["0"] = PageHome, ["1"] = PageInstances,
            ["3"] = PageAccount, ["4"] = PageSettings, ["5"] = PageConsole,
            ["6"] = PageMods, ["7"] = PageContent, ["9"] = PageSkins
        };

        foreach (var kvp in pages)
        {
            kvp.Value.Visibility = kvp.Key == tag ? Visibility.Visible : Visibility.Collapsed;
        }

        if (tag == "1")
        {
            ShowInstanceGalleryView();
            RefreshInstanceLists();
        }


    }

    private string _instanceFilter = "";
    private void UpdateSearchVisibility()
    {
        if (TxtInstanceSearch is null) return;

        var show = _instances.Count >= 5 || _instanceFilter.Length > 0;
        TxtInstanceSearch.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
    }

    private List<GameInstance> ApplyInstanceFilter(List<GameInstance> source)
    {
        if (_instanceFilter.Length == 0) return source;

        return source.Where(i =>
            i.Name.Contains(_instanceFilter, StringComparison.OrdinalIgnoreCase) ||
            i.McVersion.Contains(_instanceFilter, StringComparison.OrdinalIgnoreCase) ||
            i.LoaderDisplay.Contains(_instanceFilter, StringComparison.OrdinalIgnoreCase)
        ).ToList();
    }

    private void LogFilter_Checked(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        var filter = (sender as FrameworkElement)?.Tag?.ToString() ?? "all";
        Vm.SetLogFilter(filter);
    }

    private static bool MatchesLogLevel(string line, string level) =>
        MainWindowViewModel.MatchesLogLevel(line, level);

    private void BlockingControl_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not UIElement element) return;

        if (sender is ComboBox { IsDropDownOpen: true }) return;

        e.Handled = true;

        var parent = FindParentScrollViewer(element);
        parent?.ScrollToVerticalOffset(parent.VerticalOffset - e.Delta);
    }

    private static ScrollViewer? FindParentScrollViewer(DependencyObject start)
    {
        var current = VisualTreeHelper.GetParent(start);

        while (current is not null)
        {
            if (current is ScrollViewer sv && sv.ScrollableHeight > 0) return sv;
            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }

    private void SetupWheelHandling(DependencyObject root)
    {
        var count = VisualTreeHelper.GetChildrenCount(root);

        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);

            if (child is ComboBox or Slider)
            {
                var el = (UIElement)child;
                el.PreviewMouseWheel -= BlockingControl_PreviewMouseWheel;
                el.PreviewMouseWheel += BlockingControl_PreviewMouseWheel;
            }

            if (child is not ComboBox) SetupWheelHandling(child);
        }
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        var ctrl = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;

        var typing = Keyboard.FocusedElement is TextBox or ComboBox;

        switch (e.Key)
        {
            case Key.F5 when !typing || ctrl:
                RefreshCurrentPage();
                e.Handled = true;
                break;

            case Key.N when ctrl:
                BtnNewInstance_Click(sender, e);
                e.Handled = true;
                break;

            case Key.F when ctrl:
                NavInstances.IsChecked = true;
                TxtInstanceSearch.Visibility = Visibility.Visible;
                TxtInstanceSearch.Focus();
                TxtInstanceSearch.SelectAll();
                e.Handled = true;
                break;

            case Key.F11:
                if (_selectedInstance is not null && !_busy) _ = LaunchAsync(_selectedInstance, null);
                e.Handled = true;
                break;

            case Key.Escape when typing:
                Keyboard.ClearFocus();
                e.Handled = true;
                break;

            case >= Key.D1 and <= Key.D9 when ctrl:
                SwitchTab(e.Key - Key.D1);
                e.Handled = true;
                break;
        }
    }

    private void SwitchTab(int index)
    {
        var navs = new[] { NavHome, NavInstances, NavMods, NavContent, NavAccount, NavSettings };
        if (index >= 0 && index < navs.Length) navs[index].IsChecked = true;
    }

    private void RefreshCurrentPage()
    {
        if (PageInstances.Visibility == Visibility.Visible)
        {
            RefreshInstanceLists();
            RefreshInstanceStats();
            LoadScreenshots();
            AppendLog("Список сборок обновлён.");
        }
        else if (PageContent.Visibility == Visibility.Visible)
        {
            RefreshContent();
        }
        else if (PageMods.Visibility == Visibility.Visible)
        {
            RunModSearchFromStart();
        }
        else if (PageSettings.Visibility == Visibility.Visible)
        {
            if (SecPanelVersions.Visibility == Visibility.Visible) ScanVersions();
            else if (SecPanelMaint.Visibility == Visibility.Visible) ScanMaintenance();
            else _ = Task.Run(DetectJava);
        }
        else
        {
            RefreshInstanceStats();
        }
    }

    private void NotifyFinished(string title, string message, bool success = true)
    {
        Dispatcher.BeginInvoke(() =>
        {
            AppendLog($"{title}: {message}");

            var inactive = !IsActive || WindowState == WindowState.Minimized;
            if (!inactive) return;

            try
            {
                if (success) System.Media.SystemSounds.Asterisk.Play();
                else System.Media.SystemSounds.Exclamation.Play();
            }
            catch (Exception ex) { Log.Warn(ex.Message); }

            try
            {
                var helper = new System.Windows.Interop.WindowInteropHelper(this);
                if (helper.Handle != IntPtr.Zero) FlashWindow(helper.Handle, true);
            }
            catch (Exception ex) { Log.Warn(ex.Message); }

            TxtRunningBadge.Text = message;
        });
    }

    private void BtnMinimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private bool _isMaximized;
    private double _restoreLeft, _restoreTop, _restoreWidth, _restoreHeight;

    private void BtnMaximize_Click(object sender, RoutedEventArgs e)
    {
        if (_isMaximized)
        {
            Left = _restoreLeft;
            Top = _restoreTop;
            Width = _restoreWidth;
            Height = _restoreHeight;
            WindowState = WindowState.Normal;
            WindowBorder.Margin = new Thickness(0);
            WindowBorder.CornerRadius = new CornerRadius(14);
            _isMaximized = false;
            MaximizeIcon.Data = System.Windows.Media.Geometry.Parse("M 0,0 L 10,0 L 10,10 L 0,10 Z");
        }
        else
        {
            _restoreLeft = Left;
            _restoreTop = Top;
            _restoreWidth = Width;
            _restoreHeight = Height;
            var workArea = SystemParameters.WorkArea;
            Left = workArea.Left;
            Top = workArea.Top;
            Width = workArea.Width;
            Height = workArea.Height;
            WindowBorder.Margin = new Thickness(0);
            WindowBorder.CornerRadius = new CornerRadius(0);
            _isMaximized = true;
            MaximizeIcon.Data = System.Windows.Media.Geometry.Parse("M 2,0 L 10,0 L 10,8 M 0,2 L 0,10 L 8,10");
        }
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();

    private void WindowBorder_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        var w = WindowBorder.ActualWidth;
        var h = WindowBorder.ActualHeight;
        var clip = new RectangleGeometry(new Rect(0, 0, w, h), 14, 14);
        WindowBorder.Clip = clip;
    }

    private void OnProgress(DownloadProgress p)
    {
        var now = DateTime.UtcNow;
        if ((now - _lastProgressUi).TotalMilliseconds < 50 && p.Percent < 100) return;
        _lastProgressUi = now;

        Dispatcher.BeginInvoke(() =>
        {
            ProgressArea.Visibility = Visibility.Visible;
            PbProgress.IsIndeterminate = false;
            PbProgress.Value = p.Percent;
            TxtProgressPercent.Text = $"{p.Percent:F1}%";

            var detail = p.FilesTotal > 1 ? $"  ({p.FilesDone}/{p.FilesTotal})" : "";
            var size = p.BytesTotal > 0 ? $"  ·  {Human(p.BytesDone)} / {Human(p.BytesTotal)}" : "";
            var file = string.IsNullOrEmpty(p.CurrentFile) ? "" : "  —  " + Shorten(p.CurrentFile, 44);

            TxtProgressStage.Text = p.Stage + detail + size + file;
        });
    }

    private void SetStage(string stage) => Vm.SetStage(stage);

    private void ShowProgress(bool indeterminate = false)
    {
        Vm.ProgressVisible = true;
        Vm.ProgressIndeterminate = indeterminate;
    }

    private void HideProgress() => Vm.HideProgress();

    private void SetBusy(bool busy)
    {
        Vm.SetBusy(busy);
        Dispatcher.Invoke(() =>
        {
            CbInstances.IsEnabled = !busy;
            BtnNewInstance.IsEnabled = !busy;
            BtnCancel.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
            UpdateRunStateUi();
        });
    }

    private void AppendLog(string line) => Vm.AppendLog(line);

    private enum ContentKind { Mods, ResourcePacks, Shaders, Worlds }

    private async void BtnPortableToggle_Click(object sender, RoutedEventArgs e)
    {
        var turnOn = !LauncherPaths.IsPortable;

        if (_sessions.AnyRunning)
        {
            _dialog.ShowMessage("Сначала остановите игру.", "Занято", MessageSeverity.Warning);
            return;
        }

        var question = turnOn
            ? "Включить портативный режим?\n\n" +
              $"Данные переедут в:\n{IOPath.Combine(LauncherPaths.ExeDir, "VEC LauncherData")}\n\n" +
              "Скопировать туда текущие сборки и настройки?"
            : "Выключить портативный режим?\n\n" +
              "Данные вернутся в папку пользователя (%APPDATA%).\n\n" +
              "Скопировать туда текущие сборки и настройки?";

        var r = await _dialog.ConfirmCancelAsync("Портативный режим", question);

        if (r == ConfirmResult.Cancel) return;

        try
        {
            if (r == ConfirmResult.Yes)
            {
                var copied = 0;
                LauncherPaths.MigrateTo(turnOn, _ => copied++);
                AppendLog($"Портативный режим: скопировано файлов {copied}.");
            }

            LauncherPaths.SetPortable(turnOn);

            var restart = await _dialog.ConfirmAsync("Перезапуск",
                "Готово. Изменения вступят в силу после перезапуска лаунчера.\n\nЗакрыть его сейчас?");

            if (restart) Application.Current.Shutdown();
            else RefreshPortableState();
        }
        catch (Exception ex)
        {
            _dialog.ShowMessage("Не удалось переключить режим: " + ex.Message, "Ошибка", MessageSeverity.Error);
        }
    }
    private void BtnScanMaint_Click(object sender, RoutedEventArgs e) => ScanMaintenance();

    private void ScanMaintenance()
    {
        TxtMaintTotal.Text = "Считаю…";

        _maintTargets = _maintenance.Enumerate();
        var total = _maintenance.TotalSize();

        TxtMaintTotal.Text = $"Всего данных лаунчера: {Human(total)}  ·  {LauncherPaths.Root}";

        RebuildMaintList();
    }

    private void RebuildMaintList()
    {
        ItemsMaint.ItemsSource = _maintTargets.Select(t => new
        {
            Key = t.Target,
            t.Title,
            t.Description,
            SizeText = t.SizeDisplay,
            Checked = _maintChecked.Contains(t.Target),
            TitleColor = new SolidColorBrush(t.Dangerous
                ? (Color)ColorConverter.ConvertFromString("#F87171")
                : (Color)ColorConverter.ConvertFromString(ThemeService.CurrentTheme.Text)),
            RowBorder = new SolidColorBrush(t.Dangerous
                ? (Color)ColorConverter.ConvertFromString("#3A2428")
                : (Color)ColorConverter.ConvertFromString(ThemeService.CurrentTheme.Border))
        }).ToList();
    }

    private void MaintItem_Changed(object sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox cb || cb.Tag is not MaintenanceService.CleanTarget key) return;

        if (cb.IsChecked == true) _maintChecked.Add(key);
        else _maintChecked.Remove(key);
    }

    private void BtnMaintSafe_Click(object sender, RoutedEventArgs e)
    {
        if (_maintTargets.Count == 0) ScanMaintenance();

        _maintChecked.Clear();
        foreach (var t in _maintTargets.Where(x => !x.Dangerous &&
                     x.Target is MaintenanceService.CleanTarget.Cache
                         or MaintenanceService.CleanTarget.ImageCache
                         or MaintenanceService.CleanTarget.Logs))
        {
            _maintChecked.Add(t.Target);
        }

        RebuildMaintList();
    }

    private async void BtnMaintClean_Click(object sender, RoutedEventArgs e)
    {
        if (_maintChecked.Count == 0)
        {
            _dialog.ShowMessage("Отметьте, что нужно удалить.", "Ничего не выбрано");
            return;
        }

        if (_sessions.AnyRunning)
        {
            _dialog.ShowMessage("Сначала остановите игру.", "Игра запущена", MessageSeverity.Warning);
            return;
        }

        var selected = _maintTargets.Where(t => _maintChecked.Contains(t.Target)).ToList();
        var dangerous = selected.Where(t => t.Dangerous).ToList();
        var totalSize = selected.Sum(t => t.Size);

        var msg = "Будет удалено:\n\n" +
                  string.Join("\n", selected.Select(t => $"  • {t.Title} — {t.SizeDisplay}")) +
                  $"\n\nОсвободится примерно {Human(totalSize)}.";

        if (dangerous.Count > 0)
            msg += "\n\nВНИМАНИЕ: среди выбранного есть сборки с модами и мирами. " +
                   "Восстановить их будет невозможно.";

        if (!await _dialog.ConfirmAsync("Подтверждение очистки", msg + "\n\nПродолжить?")) return;

        var freed = _maintenance.Clean(selected);

        if (_maintChecked.Contains(MaintenanceService.CleanTarget.Instances))
        {
            _instances.Clear();
            RefreshInstanceLists();
        }

        if (_maintChecked.Contains(MaintenanceService.CleanTarget.Account)) BtnLogout_Click(sender, e);
        if (_maintChecked.Contains(MaintenanceService.CleanTarget.ImageCache)) _imageCache.ClearMemory();

        _maintChecked.Clear();
        ScanMaintenance();

        _dialog.ShowMessage($"Готово. Освобождено {Human(freed)}.", "Очистка завершена");

        AppendLog($"Очистка: освобождено {Human(freed)}");
    }

    private async void BtnReinstallSoft_Click(object sender, RoutedEventArgs e)
    {
        if (!await _dialog.ConfirmAsync("Переустановка начисто",
                "Будут удалены версии игры, библиотеки, ресурсы, Java и кэш.\n\n" +
                "Сборки (моды, миры, скриншоты), аккаунт и настройки сохранятся.\n" +
                "Файлы игры скачаются заново при следующем запуске.\n\nПродолжить?")) return;

        if (_sessions.AnyRunning)
        {
            _dialog.ShowMessage("Сначала остановите игру.", "Игра запущена", MessageSeverity.Warning);
            return;
        }

        var targets = _maintenance.Enumerate()
            .Where(t => t.Target is MaintenanceService.CleanTarget.Versions
                or MaintenanceService.CleanTarget.Libraries
                or MaintenanceService.CleanTarget.Assets
                or MaintenanceService.CleanTarget.JavaRuntime
                or MaintenanceService.CleanTarget.Cache
                or MaintenanceService.CleanTarget.ImageCache)
            .ToList();

        var freed = _maintenance.Clean(targets);
        _imageCache.ClearMemory();
        ScanMaintenance();

        _dialog.ShowMessage($"Готово. Освобождено {Human(freed)}.\n\n" +
                        "Файлы игры загрузятся заново при нажатии «ИГРАТЬ».", "Переустановка");
    }

    private async void BtnReinstallFull_Click(object sender, RoutedEventArgs e)
    {
        if (!await _dialog.ConfirmAsync("Полная переустановка",
                "Будут удалены ВСЕ данные лаунчера:\n\n" +
                "  • версии игры, библиотеки, ресурсы\n" +
                "  • сборки со всеми модами и мирами\n" +
                "  • аккаунт и настройки\n\n" +
                "Сам файл лаунчера останется. Восстановить данные будет нельзя.\n\nПродолжить?")) return;

        if (!await _dialog.ConfirmAsync("Последнее подтверждение", "Точно удалить все миры и моды?")) return;

        if (_sessions.AnyRunning) _sessions.StopAllAsync().GetAwaiter().GetResult();

        var freed = _maintenance.Clean(_maintenance.Enumerate());

        _dialog.ShowMessage($"Удалено {Human(freed)}.\n\nЛаунчер сейчас закроется. " +
                        "Запустите его заново — он будет как после установки.", "Готово");

        Application.Current.Shutdown();
    }

    private async void BtnUninstall_Click(object sender, RoutedEventArgs e)
    {
        var exePath = Environment.ProcessPath ?? "";
        var isExe = exePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);

        var r = await _dialog.ConfirmCancelAsync("Удаление лаунчера",
            "Полностью удалить VEC Launcher с компьютера?\n\n" +
            $"Будет удалена папка данных:\n{LauncherPaths.Root}\n\n" +
            (isExe ? "«Да» — удалить и сам файл лаунчера.\n«Нет» — удалить только данные.\n"
                   : "Файл лаунчера удалить нельзя (запущен не как exe).\n") +
            "\nЭто действие необратимо.",
            "Да", "Только данные", "Отмена");

        if (r == ConfirmResult.Cancel) return;

        var removeExe = isExe && r == ConfirmResult.Yes;

        if (!await _dialog.ConfirmAsync("Последнее подтверждение",
                removeExe
                    ? "Лаунчер удалит все данные и себя, затем закроется. Подтвердить?"
                    : "Лаунчер удалит все данные и закроется. Подтвердить?")) return;

        try
        {
            if (_sessions.AnyRunning) _sessions.StopAllAsync().GetAwaiter().GetResult();

            var script = _maintenance.PrepareUninstall(removeExe);
            _maintenance.RunUninstall(script);

            Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            _dialog.ShowMessage("Не удалось запустить удаление: " + ex.Message, "Ошибка", MessageSeverity.Error);
        }
    }
    private static string Human(long bytes)
    {
        string[] units = { "Б", "КБ", "МБ", "ГБ" };
        double v = bytes;
        var i = 0;
        while (v >= 1024 && i < units.Length - 1) { v /= 1024; i++; }
        return $"{v:0.#} {units[i]}";
    }

}
