using IOPath = System.IO.Path;
using Microsoft.Win32;
using System.Diagnostics;
using System.IO;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Media;
using System.Windows;
using VECLauncher.Models;
using VECLauncher.Services;

namespace VECLauncher.Views;

/// <summary>
/// Partial class handling instance management: instance creation/deletion, gallery and config views,
/// instance renaming, server address configuration, and instance detail display.
/// </summary>
public partial class MainWindow
{
    private async Task LoadVersionsAsync()
    {
        try
        {
            SetStage("Загружаю манифест версий Mojang...");
            ShowProgress(indeterminate: true);
            _manifest = await _versions.GetManifestAsync();

            var supported = VersionService.FilterSupported(_manifest, _settings.ShowSnapshots);
            AppendLog($"Манифест загружен: {_manifest.Versions.Count} версий, доступно {supported.Count} (≥1.16.5).");
        }
        catch (Exception ex)
        {
            AppendLog("Ошибка загрузки версий: " + ex.Message);
        }
        finally { HideProgress(); }
    }

    private void LoadInstances()
    {
        _instances = _instancesService.LoadAll();

        if (!_instancesService.Loaded)
        {
            AppendLog("ВНИМАНИЕ: список сборок не прочитан, изменения не сохраняются. " +
                      "Перезапустите лаунчер.");
            _dialog.ShowMessage(
                "Не удалось прочитать список сборок.\n\n" +
                "Чтобы не потерять данные, сохранение отключено до перезапуска.\n" +
                "Файлы сборок на диске не тронуты.",
                "Список сборок", MessageSeverity.Warning);
        }

        var orphans = _instancesService.ScanOrphans(_instances);
        if (orphans.Count > 0)
        {
            _instances.AddRange(orphans);
            _instancesService.SaveAll(_instances);
            AppendLog($"Найдено сборок на диске: {orphans.Count}.");
        }

        if (_instances.Count == 0 && _manifest is not null && _instancesService.Loaded)
        {
            var latest = VersionService.FilterSupported(_manifest, false).FirstOrDefault();
            if (latest is not null)
            {
                var inst = new GameInstance
                {
                    Name = "Minecraft " + latest.Id,
                    McVersion = latest.Id,
                    Loader = LoaderKind.Vanilla,
                    LaunchVersionId = latest.Id
                };
                _instancesService.EnsureFolders(inst);
                _instances.Add(inst);
                _instancesService.SaveAll(_instances);
                AppendLog($"Создана стартовая сборка «{inst.Name}».");
            }
        }
        else if (_instances.Count == 0 && _manifest is null)
        {
            AppendLog("Нет соединения с Mojang — список версий недоступен. " +
                      "Сборки не создаются, существующие данные сохранены.");
        }

        RefreshInstanceLists();
        VerifyInstalledVersions();
    }

    private void VerifyInstalledVersions()
    {
        var missing = new List<string>();

        foreach (var inst in _instances)
        {
            try
            {
                var paths = GamePaths.ForInstance(inst);
                if (!File.Exists(paths.VersionJar(inst.McVersion)))
                    missing.Add($"{inst.Name} ({inst.McVersion})");
            }
            catch (Exception ex) { Log.Warn(ex.Message); }
        }

        if (missing.Count > 0)
            AppendLog($"Требуют загрузки клиента: {string.Join(", ", missing)}. " +
                      "Файлы скачаются при нажатии «ИГРАТЬ».");
    }

    private void RefreshInstanceLists()
    {
        var ordered = ApplyInstanceFilter(
            _instances.OrderByDescending(i => i.LastPlayed ?? i.Created).ToList());

        UpdateSearchVisibility();

        CbInstances.ItemsSource = null;
        CbInstances.ItemsSource = ordered;
        if (CbInstancesHome is not null)
        {
            CbInstancesHome.ItemsSource = null;
            CbInstancesHome.ItemsSource = ordered;
        }
        LstInstances.ItemsSource = null;
        LstInstances.ItemsSource = ordered;

        if (PanelInstancesEmpty != null && ScrollInstancesCards != null)
        {
            if (ordered.Count == 0)
            {
                PanelInstancesEmpty.Visibility = Visibility.Visible;
                ScrollInstancesCards.Visibility = Visibility.Collapsed;
                ShowInstanceGalleryView();
            }
            else
            {
                PanelInstancesEmpty.Visibility = Visibility.Collapsed;
                ScrollInstancesCards.Visibility = Visibility.Visible;
            }
        }

        var target = ordered.FirstOrDefault(i => i.Id == _settings.LastInstanceId) ?? ordered.FirstOrDefault();
        if (target is not null)
        {
            CbInstances.SelectedItem = target;
            if (CbInstancesHome is not null) CbInstancesHome.SelectedItem = target;
            LstInstances.SelectedItem = target;
        }
        else
        {
            _selectedInstance = null;
        }
    }

    public void ShowInstanceConfigView()
    {
        if (PanelInstancesGallery != null) PanelInstancesGallery.Visibility = Visibility.Collapsed;
        if (PanelInstanceConfig != null) PanelInstanceConfig.Visibility = Visibility.Visible;
    }

    public void ShowInstanceGalleryView()
    {
        if (PanelInstancesGallery != null) PanelInstancesGallery.Visibility = Visibility.Visible;
        if (PanelInstanceConfig != null) PanelInstanceConfig.Visibility = Visibility.Collapsed;
    }

    private void BtnBackToGallery_Click(object sender, RoutedEventArgs e)
    {
        ShowInstanceGalleryView();
        RefreshInstanceLists();
    }

    private void BtnOpenAllInstancesFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(_instancesService.InstancesRoot);
            Process.Start(new ProcessStartInfo(_instancesService.InstancesRoot) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _dialog.ShowMessage("Не удалось открыть папку сборок: " + ex.Message, "Ошибка", MessageSeverity.Warning);
        }
    }

    private void InstanceCard_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is GameInstance inst)
        {
            SelectInstance(inst);
            ShowInstanceConfigView();
        }
    }

    private async void CardPlay_Click(object sender, RoutedEventArgs e)
    {
        var inst = (sender as FrameworkElement)?.Tag as GameInstance
                   ?? (sender as FrameworkElement)?.DataContext as GameInstance
                   ?? (sender as MenuItem)?.DataContext as GameInstance
                   ?? _selectedInstance;
        if (inst is null) return;
        SelectInstance(inst);
        Vm.SelectedInstance = inst;
        var targetIp = string.IsNullOrWhiteSpace(inst.ServerAddress) ? null : inst.ServerAddress;
        await LaunchAsync(inst, targetIp);
    }

    private async void InstConfigPlay_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedInstance is null) return;
        var targetIp = string.IsNullOrWhiteSpace(_selectedInstance.ServerAddress) ? null : _selectedInstance.ServerAddress;
        await LaunchAsync(_selectedInstance, targetIp);
    }

    private void CardSettings_Click(object sender, RoutedEventArgs e)
    {
        var inst = (sender as FrameworkElement)?.Tag as GameInstance
                   ?? (sender as FrameworkElement)?.DataContext as GameInstance
                   ?? (sender as MenuItem)?.DataContext as GameInstance
                   ?? _selectedInstance;
        if (inst is null) return;
        SelectInstance(inst);
        ShowInstanceConfigView();
    }

    private void CardContent_Click(object sender, RoutedEventArgs e)
    {
        var inst = (sender as FrameworkElement)?.Tag as GameInstance
                   ?? (sender as FrameworkElement)?.DataContext as GameInstance
                   ?? (sender as MenuItem)?.DataContext as GameInstance
                   ?? _selectedInstance;
        if (inst is null) return;
        SelectInstance(inst);
        NavContent.IsChecked = true;
    }

    private void CardOpenMods_Click(object sender, RoutedEventArgs e)
    {
        var inst = (sender as FrameworkElement)?.Tag as GameInstance
                   ?? (sender as FrameworkElement)?.DataContext as GameInstance
                   ?? (sender as MenuItem)?.DataContext as GameInstance
                   ?? _selectedInstance;
        if (inst is null) return;
        var path = _instancesService.ModsDir(inst);
        Directory.CreateDirectory(path);
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); } catch (Exception ex) { Log.Warn(ex.Message); }
    }

    private void CardOpenRoot_Click(object sender, RoutedEventArgs e)
    {
        var inst = (sender as FrameworkElement)?.Tag as GameInstance
                   ?? (sender as FrameworkElement)?.DataContext as GameInstance
                   ?? (sender as MenuItem)?.DataContext as GameInstance
                   ?? _selectedInstance;
        if (inst is null) return;
        var path = _instancesService.InstanceDir(inst);
        Directory.CreateDirectory(path);
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); } catch (Exception ex) { Log.Warn(ex.Message); }
    }

    private void CardDuplicate_Click(object sender, RoutedEventArgs e)
    {
        var inst = (sender as FrameworkElement)?.Tag as GameInstance
                   ?? (sender as FrameworkElement)?.DataContext as GameInstance
                   ?? (sender as MenuItem)?.DataContext as GameInstance
                   ?? _selectedInstance;
        if (inst is null) return;
        SelectInstance(inst);
        BtnDuplicateInstance_Click(sender, e);
    }

    private void CardDelete_Click(object sender, RoutedEventArgs e)
    {
        var inst = (sender as FrameworkElement)?.Tag as GameInstance
                   ?? (sender as FrameworkElement)?.DataContext as GameInstance
                   ?? (sender as MenuItem)?.DataContext as GameInstance
                   ?? _selectedInstance;
        if (inst is null) return;
        SelectInstance(inst);
        BtnDeleteInstance_Click(sender, e);
    }

    private void CbInstances_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CbInstances.SelectedItem is not GameInstance inst) return;
        SelectInstance(inst);
        if (CbInstancesHome is not null && !ReferenceEquals(CbInstancesHome.SelectedItem, inst))
            CbInstancesHome.SelectedItem = inst;
        if (!ReferenceEquals(LstInstances.SelectedItem, inst)) LstInstances.SelectedItem = inst;
    }

    private void CbInstancesHome_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CbInstancesHome?.SelectedItem is not GameInstance inst) return;
        SelectInstance(inst);
        if (!ReferenceEquals(CbInstances.SelectedItem, inst)) CbInstances.SelectedItem = inst;
        if (!ReferenceEquals(LstInstances.SelectedItem, inst)) LstInstances.SelectedItem = inst;
    }

    private void LstInstances_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LstInstances.SelectedItem is not GameInstance inst) return;
        SelectInstance(inst);
        if (!ReferenceEquals(CbInstances.SelectedItem, inst)) CbInstances.SelectedItem = inst;
    }

    private void SelectInstance(GameInstance inst)
    {
        _selectedInstance = inst;
        _settings.LastInstanceId = inst.Id;

        TxtInstName.Text = inst.Name;
        TxtInstVersion.Text = "Minecraft " + inst.McVersion;
        TxtInstLoader.Text = inst.LoaderDisplay;
        TxtInstPlaytime.Text = inst.TotalPlaySeconds > 0 ? "В игре: " + inst.PlayTimeDisplay : "Ещё не запускалась";

        RefreshInstanceStats();
        LoadScreenshots();
        FillInstanceSettings(inst);
        RefreshModProfiles();
        RefreshJvmPresets();
        RefreshInstanceIcon();
        RefreshStatistics();
        TxtInstHealth.Text = "";
        UpdateRunStateUi();
    }

    private bool _loadingInstSettings;

    private void FillInstanceSettings(GameInstance inst)
    {
        _loadingInstSettings = true;
        try
        {
            TxtInstEditName.Text = inst.Name;
            TxtInstMemory.Text = inst.MaxMemoryMb > 0 ? inst.MaxMemoryMb.ToString() : "";
            TxtInstWidth.Text = inst.WindowWidth > 0 ? inst.WindowWidth.ToString() : "";
            TxtInstHeight.Text = inst.WindowHeight > 0 ? inst.WindowHeight.ToString() : "";
            TxtInstServer.Text = inst.ServerAddress;
            TxtInstJava.Text = inst.JavaPath;
            TxtInstJvm.Text = inst.ExtraJvmArgs;
        }
        finally { _loadingInstSettings = false; }
    }

    private void RefreshModProfiles()
    {
        if (_selectedInstance is null) return;

        _loadingInstSettings = true;
        try
        {
            var profiles = ModProfileService.List(_selectedInstance);
            CbModProfile.ItemsSource = profiles;
            CbModProfile.SelectedItem = profiles.Contains(_selectedInstance.ActiveModProfile)
                ? _selectedInstance.ActiveModProfile
                : profiles[0];

            var counts = profiles.Select(p =>
                $"{p} — {ModProfileService.CountMods(_selectedInstance, p)}");
            TxtProfileInfo.Text = "Модов: " + string.Join("  ·  ", counts);
        }
        finally { _loadingInstSettings = false; }
    }

    private void RefreshStatistics()
    {
        if (_selectedInstance is null) return;

        var inst = _selectedInstance;

        TxtStatTotal.Text = inst.TotalPlaySeconds > 0 ? inst.PlayTimeDisplay : "—";
        TxtStatSessions.Text = inst.Sessions.Count.ToString();

        TxtStatAvg.Text = inst.Sessions.Count > 0
            ? FormatMinutes((long)inst.Sessions.Average(s => s.Seconds))
            : "—";

        var today = DateTime.Today;
        var days = Enumerable.Range(0, 14).Select(i => today.AddDays(-13 + i)).ToList();

        var byDay = days.Select(d => new
        {
            Day = d,
            Seconds = inst.Sessions.Where(s => s.Date.Date == d).Sum(s => s.Seconds)
        }).ToList();

        var max = Math.Max(1, byDay.Max(x => x.Seconds));

        ItemsChart.ItemsSource = byDay.Select(x =>
        {
            var height = x.Seconds == 0 ? 2.0 : Math.Max(4, x.Seconds * 62.0 / max);

            return new
            {
                BarHeight = height,
                Label = x.Day.ToString("dd"),
                Bar = new SolidColorBrush(x.Seconds > 0
                    ? ThemeService.CurrentAccent
                    : (Color)ColorConverter.ConvertFromString("#2A2F3A")),
                Tip = x.Seconds > 0
                    ? $"{x.Day:dd.MM}: {FormatMinutes(x.Seconds)}"
                    : $"{x.Day:dd.MM}: не играли"
            };
        }).ToList();
    }

    private void RefreshJvmPresets()
    {
        if (_selectedInstance is null) return;

        _loadingInstSettings = true;
        try
        {
            if (CbJvmPreset.ItemsSource is null)
                CbJvmPreset.ItemsSource = JvmPresetService.Presets.Select(p => p.Name).ToList();

            CbJvmPreset.SelectedItem = JvmPresetService.Get(_selectedInstance.JvmPreset).Name;
            UpdateJvmPresetInfo();
        }
        finally { _loadingInstSettings = false; }
    }

    private void UpdateJvmPresetInfo()
    {
        if (_selectedInstance is null || CbJvmPreset.SelectedItem is not string name) return;

        var preset = JvmPresetService.Get(name);
        var memory = _selectedInstance.MaxMemoryMb > 0
            ? _selectedInstance.MaxMemoryMb : _settings.MaxMemoryMb;

        var javaMajor = 0;
        try
        {
            var path = !string.IsNullOrWhiteSpace(_selectedInstance.JavaPath)
                ? _selectedInstance.JavaPath : _settings.CustomJavaPath;

            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                javaMajor = JavaService.Probe(path, "check")?.MajorVersion ?? 0;
        }
        catch (Exception ex) { Log.Warn(ex.Message); }

        var warning = JvmPresetService.Validate(name, memory, javaMajor);

        TxtJvmPresetInfo.Text = warning ?? preset.Description;
        TxtJvmPresetInfo.Foreground = (Brush)FindResource(warning is null ? "FgMuted" : "Danger");
    }

    private void CbJvmPreset_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || _loadingInstSettings || _selectedInstance is null) return;
        if (CbJvmPreset.SelectedItem is not string name) return;

        _selectedInstance.JvmPreset = name;
        _instancesService.SaveAll(_instances);
        UpdateJvmPresetInfo();

        AppendLog($"Сборка «{_selectedInstance.Name}»: пресет JVM «{name}».");
    }

    private void RefreshInstanceIcon()
    {
        if (_selectedInstance is null) return;

        var color = (Color)ColorConverter.ConvertFromString(_selectedInstance.IconColor);
        InstIconDot.Background = new SolidColorBrush(color);

        if (!string.IsNullOrWhiteSpace(_selectedInstance.IconPath) &&
            File.Exists(_selectedInstance.IconPath))
        {
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.DecodePixelWidth = 96;
                bmp.UriSource = new Uri(_selectedInstance.IconPath);
                bmp.EndInit();
                bmp.Freeze();

                ImgInstIcon.Source = bmp;
                InstIconDot.Visibility = Visibility.Collapsed;
                return;
            }
            catch (Exception ex) { Log.Warn("Иконка сборки: " + ex.Message); }
        }

        try
        {
            var defBmp = new BitmapImage();
            defBmp.BeginInit();
            defBmp.CacheOption = BitmapCacheOption.OnLoad;
            defBmp.DecodePixelWidth = 96;
            defBmp.UriSource = new Uri("pack://application:,,,/Assets/default_instance_icon.jpg");
            defBmp.EndInit();
            defBmp.Freeze();
            ImgInstIcon.Source = defBmp;
        }
        catch { ImgInstIcon.Source = null; }
        InstIconDot.Visibility = Visibility.Collapsed;
    }

    private void BtnInstIcon_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedInstance is null) return;

        var dlg = new OpenFileDialog
        {
            Title = "Иконка сборки",
            Filter = "Изображения|*.png;*.jpg;*.jpeg;*.bmp;*.ico|Все файлы|*.*"
        };

        if (dlg.ShowDialog(this) != true) return;

        try
        {
            var dst = IOPath.Combine(_instancesService.InstanceDir(_selectedInstance),
                "icon" + IOPath.GetExtension(dlg.FileName));

            File.Copy(dlg.FileName, dst, true);

            _selectedInstance.IconPath = dst;
            _instancesService.SaveAll(_instances);

            RefreshInstanceIcon();
            RefreshInstanceLists();
            AppendLog($"Иконка сборки «{_selectedInstance.Name}» обновлена.");
        }
        catch (Exception ex)
        {
            _dialog.ShowMessage("Не удалось установить иконку: " + ex.Message, "Ошибка", MessageSeverity.Error);
        }
    }

    private void BtnInstIconClear_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedInstance is null) return;

        _selectedInstance.IconPath = "";
        _instancesService.SaveAll(_instances);

        RefreshInstanceIcon();
        RefreshInstanceLists();
    }
    private void InstSetting_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded || _loadingInstSettings || _selectedInstance is null) return;

        var inst = _selectedInstance;

        var newName = TxtInstEditName.Text.Trim();
        if (newName.Length > 0 && newName != inst.Name)
        {
            inst.Name = newName;
            RefreshInstanceLists();
            CbInstances.SelectedItem = _instances.FirstOrDefault(i => i.Id == inst.Id);
        }

        inst.MaxMemoryMb = int.TryParse(TxtInstMemory.Text.Trim(), out var mem) && mem > 0 ? mem : 0;
        inst.WindowWidth = int.TryParse(TxtInstWidth.Text.Trim(), out var w) && w > 0 ? w : 0;
        inst.WindowHeight = int.TryParse(TxtInstHeight.Text.Trim(), out var h) && h > 0 ? h : 0;
        inst.ServerAddress = TxtInstServer.Text.Trim();
        inst.ExtraJvmArgs = TxtInstJvm.Text.Trim();

        _instancesService.SaveAll(_instances);
        TxtInstName.Text = inst.Name;
    }

    private void SldInstMemory_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!IsLoaded || _loadingInstSettings) return;
        var mb = (int)e.NewValue;
        TxtInstMemory.Text = mb.ToString();
    }
    private void BtnInstJava_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedInstance is null) return;

        var dlg = new OpenFileDialog
        {
            Title = "java.exe для этой сборки",
            Filter = "java.exe|java.exe;javaw.exe|Исполняемые файлы (*.exe)|*.exe"
        };
        if (dlg.ShowDialog(this) != true) return;

        var probe = JavaService.Probe(dlg.FileName, "instance");
        if (probe is null)
        {
            _dialog.ShowMessage("Не удалось определить версию Java по этому пути.", "Java", MessageSeverity.Warning);
            return;
        }

        _selectedInstance.JavaPath = dlg.FileName;
        TxtInstJava.Text = dlg.FileName;
        _instancesService.SaveAll(_instances);
        AppendLog($"Для «{_selectedInstance.Name}» выбрана {probe}");
    }
    private async void BtnDuplicateInstance_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedInstance is null) return;

        var src = _selectedInstance;

        var copy = new GameInstance
        {
            Name = src.Name + " (копия)",
            McVersion = src.McVersion,
            Loader = src.Loader,
            LoaderVersion = src.LoaderVersion,
            LaunchVersionId = src.LaunchVersionId,
            MaxMemoryMb = src.MaxMemoryMb,
            WindowWidth = src.WindowWidth,
            WindowHeight = src.WindowHeight,
            ServerAddress = src.ServerAddress,
            ExtraJvmArgs = src.ExtraJvmArgs,
            JavaPath = src.JavaPath,
            IconColor = src.IconColor,
            Isolated = src.Isolated
        };

        _instancesService.EnsureFolders(copy);

        var r = await _dialog.ConfirmCancelAsync(
            "Дублирование",
            "Скопировать моды, ресурспаки и шейдеры в новую сборку?\n\n" +
            "«Нет» — создать пустую сборку с теми же настройками.");

        if (r == ConfirmResult.Cancel) return;

        if (r == ConfirmResult.Yes)
        {
            try
            {
                foreach (var sub in Constants.InstanceFolders.ContentFolders)
                {
                    var from = IOPath.Combine(_instancesService.InstanceDir(src), sub);
                    if (Directory.Exists(from))
                        CopyDirectory(from, IOPath.Combine(_instancesService.InstanceDir(copy), sub));
                }
            }
            catch (Exception ex)
            {
                AppendLog("Ошибка копирования содержимого: " + ex.Message);
            }
        }

        _instances.Add(copy);
        _instancesService.SaveAll(_instances);
        RefreshInstanceLists();
        CbInstances.SelectedItem = _instances.FirstOrDefault(i => i.Id == copy.Id);

        AppendLog($"Создана копия сборки: «{copy.Name}»");
    }

    private async void BtnResetInstance_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedInstance is null) return;

        if (!await _dialog.ConfirmAsync("Сброс настроек сборки",
                "Сбросить индивидуальные настройки этой сборки?\n\n" +
                "Память, размер окна, Java и аргументы вернутся к общим значениям.\n" +
                "Моды и миры не пострадают.")) return;

        var inst = _selectedInstance;
        inst.MaxMemoryMb = 0;
        inst.WindowWidth = 0;
        inst.WindowHeight = 0;
        inst.ServerAddress = "";
        inst.ExtraJvmArgs = "";
        inst.JavaPath = "";

        _instancesService.SaveAll(_instances);
        FillInstanceSettings(inst);
        AppendLog($"Настройки сборки «{inst.Name}» сброшены.");
    }
    private void RefreshInstanceStats()
    {
        if (_selectedInstance is null) return;

        var st = _instancesService.GetStats(_selectedInstance);

        TxtCountMods.Text = Plural(st.Mods, "файл", "файла", "файлов");
        TxtCountRp.Text = Plural(st.ResourcePacks, "пак", "пака", "паков");
        TxtCountShaders.Text = Plural(st.ShaderPacks, "пак", "пака", "паков");
        TxtCountWorlds.Text = Plural(st.Worlds, "мир", "мира", "миров");
        TxtInstSize.Text = st.SizeDisplay;

    }

    private void LoadScreenshots()
    {
        if (_selectedInstance is null) return;

        var files = _instancesService.GetScreenshots(_selectedInstance, 12);

        if (files.Count == 0)
        {
            ItemsScreenshots.ItemsSource = null;
            TxtNoShots.Visibility = Visibility.Visible;
            return;
        }

        TxtNoShots.Visibility = Visibility.Collapsed;

        var items = new List<object>();
        foreach (var f in files)
        {
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.DecodePixelWidth = 264;
                bmp.UriSource = new Uri(f.FullName);
                bmp.EndInit();
                bmp.Freeze();

                items.Add(new { Thumb = bmp, Path = f.FullName, Name = f.Name });
            }
            catch (Exception ex) { Log.Warn(ex.Message); }
        }

        ItemsScreenshots.ItemsSource = items;
    }
    private void ChkSnapshots_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        _settings.ShowSnapshots = ChkSnapshots.IsChecked == true;
    }

    private void BtnNewInstance_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new InstanceDialog(_versions, _loaders, _settings.ShowSnapshots, _settings.DefaultIsolated) { Owner = this };
        if (dlg.ShowDialog() != true || dlg.Result is null) return;

        var inst = dlg.Result;
        _instancesService.EnsureFolders(inst);
        _instances.Add(inst);
        _instancesService.SaveAll(_instances);

        _settings.LastInstanceId = inst.Id;
        RefreshInstanceLists();
        AppendLog($"Создана сборка «{inst.Name}» ({inst.McVersion}, {inst.LoaderDisplay}).");

        NavInstances.IsChecked = true;

        if (!string.IsNullOrEmpty(dlg.ModpackPath))
            _ = InstallModpackAsync(inst, dlg.ModpackPath!);
    }

    private async Task InstallModpackAsync(GameInstance inst, string packPath)
    {
        SetBusy(true);

        try
        {
            SetStage("Устанавливаю модпак...");
            var info = await _modpacks.InstallAsync(packPath, inst);

            RefreshInstanceStats();
            RefreshContent();

            _dialog.ShowMessage(
                $"Модпак «{info.Name}» установлен в сборку «{inst.Name}».\n\n" +
                $"Версия: {info.McVersion} {info.Loader.Display()}\n" +
                $"Файлов: {info.FileCount}\n\n" +
                "Загрузчик установится при первом запуске.", "Модпак готов");
        }
        catch (Exception ex)
        {
            Log.Error("Установка модпака", ex);
            _dialog.ShowMessage("Не удалось установить модпак:\n\n" + ex.Message, "Ошибка", MessageSeverity.Error);
        }
        finally
        {
            SetBusy(false);
            HideProgress();
        }
    }
    private async void BtnDeleteInstance_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedInstance is null) return;

        if (_sessions.IsInstanceRunning(_selectedInstance.Id))
        {
            _dialog.ShowMessage("Нельзя удалить сборку, пока она запущена. Сначала остановите игру.",
                "Сборка занята", MessageSeverity.Warning);
            return;
        }

        var inst = _selectedInstance;
        var r = await _dialog.ConfirmCancelAsync(
            "Удаление сборки",
            $"Удалить сборку «{inst.Name}»?\n\n" +
            "«Да» — удалить вместе с модами, мирами и скриншотами.\n" +
            "«Нет» — убрать из списка, файлы оставить.",
            "Да", "Без файлов", "Отмена");

        if (r == ConfirmResult.Cancel) return;

        try
        {
            if (r == ConfirmResult.Yes) _instancesService.Delete(inst, true);

            _instances.Remove(inst);
            _instancesService.SaveAll(_instances);
            _selectedInstance = null;
            RefreshInstanceLists();
            AppendLog($"Сборка «{inst.Name}» удалена.");
        }
        catch (Exception ex)
        {
            _dialog.ShowMessage(ex.Message, "Ошибка удаления", MessageSeverity.Error);
        }
    }

    private void OpenInstanceFolder(Func<GameInstance, string> selector)
    {
        if (_selectedInstance is null)
        {
            _dialog.ShowMessage("Сначала выберите сборку.", "Сборка не выбрана");
            return;
        }

        try
        {
            _instancesService.OpenFolder(selector(_selectedInstance));
            RefreshInstanceStats();
        }
        catch (Exception ex)
        {
            _dialog.ShowMessage(ex.Message, "Ошибка", MessageSeverity.Error);
        }
    }

    private void OpenMods_Click(object s, RoutedEventArgs e) => OpenInstanceFolder(_instancesService.ModsDir);
    private void OpenResourcePacks_Click(object s, RoutedEventArgs e) => OpenInstanceFolder(_instancesService.ResourcePacksDir);
    private void OpenShaders_Click(object s, RoutedEventArgs e) => OpenInstanceFolder(_instancesService.ShaderPacksDir);
    private void OpenInstanceRoot_Click(object s, RoutedEventArgs e) => OpenInstanceFolder(_instancesService.InstanceDir);

    private enum HomeButtonState { Idle, Busy, Running }
}
