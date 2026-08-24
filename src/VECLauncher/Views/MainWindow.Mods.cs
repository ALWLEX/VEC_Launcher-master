using IOPath = System.IO.Path;
using Microsoft.Win32;
using System.Diagnostics;
using System.IO.Compression;
using System.IO;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Media;
using System.Windows.Threading;
using System.Windows;
using VECLauncher.Models;
using VECLauncher.Services;

namespace VECLauncher.Views;

/// <summary>
/// Partial class handling mod management: mod search, CurseForge/Modrinth catalog browsing,
/// modpack import/export, local mod list display, and mod install/uninstall.
/// </summary>
public partial class MainWindow
{
    private List<ModSearchResult> _modResults = new();
    private CancellationTokenSource? _modCts;
    private const int ModPageSize = 20;
    private int _modOffset;
    private int _modTotal;

    private void TxtModSearch_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) RunModSearchFromStart();
    }

    private void ModFilter_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded) return;
        if (_modResults.Count > 0) RunModSearchFromStart();
    }

    private ModContentType SelectedContentType => (CbModType.SelectedItem as ComboBoxItem)?.Tag?.ToString() switch
    {
        "1" => ModContentType.ResourcePack,
        "2" => ModContentType.ShaderPack,
        _ => ModContentType.Mod
    };

    private ModProvider? SelectedProvider => (CbModSource.SelectedItem as ComboBoxItem)?.Tag?.ToString() switch
    {
        "modrinth" => ModProvider.Modrinth,
        "curseforge" => ModProvider.CurseForge,
        _ => null
    };

    private void UpdatePager(ModService.SearchPage page)
    {
        ModPager.Visibility = page.TotalCount > ModPageSize ? Visibility.Visible : Visibility.Collapsed;
        BtnPrevPage.IsEnabled = page.HasPrevious;
        BtnNextPage.IsEnabled = page.HasNext;
        BuildPageButtons(page.PageNumber, page.TotalPages);
    }

    private void BuildPageButtons(int current, int total)
    {
        PageButtons.Children.Clear();
        var pages = GetPageNumbers(current, total);
        foreach (var p in pages)
        {
            if (p == -1)
            {
                var tb = new TextBlock { Text = "…", FontSize = 11, Foreground = new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66)), Margin = new Thickness(4, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center };
                PageButtons.Children.Add(tb);
            }
            else
            {
                var pageNum = p;
                var btn = new Button
                {
                    Width = 32, Height = 28, Cursor = Cursors.Hand, Background = Brushes.Transparent, BorderThickness = new Thickness(0),
                    Tag = pageNum
                };
                btn.Click += PageNum_Click;
                var bd = new Border
                {
                    Background = pageNum == current ? new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A)) : Brushes.Transparent,
                    CornerRadius = new CornerRadius(5)
                };
                var txt = new TextBlock
                {
                    Text = pageNum.ToString(), FontSize = 11,
                    Foreground = pageNum == current ? Brushes.White : new SolidColorBrush(Color.FromRgb(0x99, 0x99, 0x99)),
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
                };
                bd.Child = txt;
                btn.Content = bd;
                PageButtons.Children.Add(btn);
            }
        }
    }

    private static List<int> GetPageNumbers(int current, int total)
    {
        var result = new List<int>();
        if (total <= 7)
        {
            for (int i = 1; i <= total; i++) result.Add(i);
        }
        else
        {
            result.Add(1);
            if (current > 3) result.Add(-1); // ellipsis
            for (int i = Math.Max(2, current - 1); i <= Math.Min(total - 1, current + 1); i++)
                result.Add(i);
            if (current < total - 2) result.Add(-1); // ellipsis
            result.Add(total);
        }
        return result;
    }

    private void PageNum_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is int pageNum)
        {
            _modOffset = (pageNum - 1) * ModPageSize;
            RunModSearch();
        }
    }

    private void BtnPrevPage_Click(object sender, RoutedEventArgs e)
    {
        if (_modOffset <= 0) return;
        _modOffset = Math.Max(0, _modOffset - ModPageSize);
        RunModSearch();
    }

    private void BtnNextPage_Click(object sender, RoutedEventArgs e)
    {
        if (_modOffset + ModPageSize >= _modTotal) return;
        _modOffset += ModPageSize;
        RunModSearch();
    }

    private void RunModSearchFromStart()
    {
        _modOffset = 0;
        RunModSearch();
    }

    private void RunModSearch() => BtnModSearch_Click(this, new RoutedEventArgs());

    private void BtnModSearchNew_Click(object sender, RoutedEventArgs e) => RunModSearchFromStart();

    private async void BtnModSearch_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedInstance is null)
        {
            TxtModStatus.Text = "Сначала выберите сборку — от неё зависят версия игры и загрузчик.";
            return;
        }

        _modCts?.Cancel();
        _modCts = new CancellationTokenSource();
        var ct = _modCts.Token;

        BtnModSearch.IsEnabled = false;
        var type = SelectedContentType;
        TxtModStatus.Text = "Ищу…";
        ItemsMods.ItemsSource = null;

        try
        {
            var page = await _mods.SearchAsync(
                TxtModSearch.Text.Trim(),
                _selectedInstance.McVersion,
                _selectedInstance.Loader,
                type,
                SelectedProvider,
                ModPageSize, _modOffset, ct);

            if (ct.IsCancellationRequested) return;

            _modResults = page.Items;
            _modTotal = page.TotalCount;

            if (_modResults.Count == 0)
            {
                TxtModStatus.Text = _selectedInstance.Loader == LoaderKind.Vanilla && type == ModContentType.Mod
                    ? $"Ничего не найдено. У сборки «{_selectedInstance.Name}» нет модлоадера — " +
                      "для модов создайте сборку с Fabric, Forge или NeoForge."
                    : $"Ничего не найдено для Minecraft {_selectedInstance.McVersion}.";
                UpdatePager(page);
                return;
            }

            var extra = "";
            TxtModStatus.Text = $"Найдено: {page.TotalCount}  ·  " +
                                $"{_selectedInstance.McVersion} · {_selectedInstance.Loader.Display()}{extra}";

            ItemsMods.ItemsSource = _modResults.Select((m, i) => BuildModView(m, i)).ToList();
            UpdatePager(page);
            ModScroll.ScrollToTop();

            _ = LoadModIconsAsync(_modResults.ToList(), ct);
        }
        catch (OperationCanceledException ex) { Log.Warn(ex.Message); }
        catch (Exception ex)
        {
            TxtModStatus.Text = "Ошибка поиска: " + ex.Message;
            Log.Error("Поиск модов", ex);
        }
        finally { BtnModSearch.IsEnabled = true; }
    }

    private object BuildModView(ModSearchResult m, int index)
    {
        var icon = _imageCache.TryGetCached(m.IconUrl);

        var isModrinth = m.Provider == ModProvider.Modrinth;

        return new
        {
            Index = index,
            m.Title,
            Summary = string.IsNullOrWhiteSpace(m.Summary) ? "Без описания" : m.Summary,
            Icon = icon,
            Initial = m.Title.Length > 0 ? m.Title[..1].ToUpperInvariant() : "?",
            PlaceholderVisibility = icon is null ? Visibility.Visible : Visibility.Collapsed,
            Source = m.ProviderDisplay,
            SourceBg = new SolidColorBrush((Color)ColorConverter.ConvertFromString(isModrinth ? "#3D3012" : "#33210F")),
            SourceFg = new SolidColorBrush((Color)ColorConverter.ConvertFromString(isModrinth ? "#FACC15" : "#FB923C")),
            DownloadsText = m.DownloadsDisplay + " загрузок",
            AuthorText = string.IsNullOrEmpty(m.Author) ? "" : "автор: " + m.Author,
            PageUrl = m.PageUrl ?? ""
        };
    }

    private async Task LoadModIconsAsync(List<ModSearchResult> items, CancellationToken ct)
    {
        var loadedAny = false;

        await Parallel.ForEachAsync(items,
            new ParallelOptions { MaxDegreeOfParallelism = 5, CancellationToken = ct },
            async (m, token) =>
            {
                var img = await _imageCache.GetAsync(m.IconUrl, App.Http, 108, token);
                if (img is not null) loadedAny = true;
            });

        if (!loadedAny || ct.IsCancellationRequested) return;

        await Dispatcher.InvokeAsync(() =>
        {
            if (ct.IsCancellationRequested) return;
            if (!ReferenceEquals(_modResults, items) && !_modResults.SequenceEqual(items)) return;

            ItemsMods.ItemsSource = _modResults.Select((m, i) => BuildModView(m, i)).ToList();
        });
    }
    private void ModPage_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.Tag is not string url || url.Length == 0) return;
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception ex) { AppendLog("Не удалось открыть ссылку: " + ex.Message); }
    }

    private void ModPageInApp_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not int index) return;
        if (index < 0 || index >= _modResults.Count) return;

        var project = _modResults[index];

        var dlg = new ModBrowserDialog(project) { Owner = this };
        var result = dlg.ShowDialog();

        if (result == true && dlg.InstallRequested)
            _ = InstallModAsync(project, null);
    }
    private async void ModInstall_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not int index) return;
        if (index < 0 || index >= _modResults.Count) return;

        await InstallModAsync(_modResults[index], btn);
    }

    private static void SetButtonText(Button btn, string text)
    {
        var tb = FindChild<TextBlock>(btn);
        if (tb != null) tb.Text = text;
        else btn.Content = text;
    }

    private static T? FindChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
            if (child is T found) return found;
            var deep = FindChild<T>(child);
            if (deep != null) return deep;
        }
        return null;
    }

    private async Task InstallModAsync(ModSearchResult project, Button? btn)
    {
        if (_selectedInstance is null)
        {
            _dialog.ShowMessage("Сначала выберите сборку.", "Сборка не выбрана");
            return;
        }

        var inst = _selectedInstance;

        if (btn is not null)
        {
            btn.IsEnabled = false;
            SetButtonText(btn, "…");
        }

        try
        {
            var dlg = new ModVersionDialog(_mods, project, inst.McVersion, inst.Loader) { Owner = this };
            if (dlg.ShowDialog() != true || dlg.SelectedFile is null) return;

            var chosen = dlg.SelectedFile;

            var targetDir = SelectedContentType switch
            {
                ModContentType.ResourcePack => _instancesService.ResourcePacksDir(inst),
                ModContentType.ShaderPack => _instancesService.ShaderPacksDir(inst),
                _ => _instancesService.ModsDir(inst)
            };

            var outcome = await _mods.InstallAsync(
                chosen, targetDir, inst.McVersion, inst.Loader, dlg.InstallDependencies);

            var msg = $"Установлено: {outcome.Installed.Count}";
            if (outcome.Skipped.Count > 0) msg += $"\nПропущено: {string.Join(", ", outcome.Skipped)}";
            if (outcome.Failed.Count > 0) msg += $"\nОшибки: {string.Join(", ", outcome.Failed)}";

            AppendLog($"«{project.Title}» → {msg.Replace("\n", "; ")}");

            _dialog.ShowMessage(msg, project.Title,
                outcome.Failed.Count > 0 ? MessageSeverity.Warning : MessageSeverity.Info);

            RefreshInstanceStats();
        }
        catch (Exception ex)
        {
            Log.Error("Установка мода", ex);
            _dialog.ShowMessage(ex.Message, "Ошибка установки", MessageSeverity.Error);
        }
        finally
        {
            if (btn is not null)
            {
                btn.IsEnabled = true;
                SetButtonText(btn, "Установить");
            }
        }
    }

    private void UpdateModsSubtitle()
    {
        if (TxtModsSubtitle is null) return;

        TxtModsSubtitle.Text = _mods.CurseForgeAvailable
            ? "Каталог Modrinth и CurseForge"
            : "Каталог Modrinth  ·  CurseForge недоступен с текущим ключом";
    }
    private ContentKind _contentKind = ContentKind.Mods;

    private void ContentKind_Checked(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;

        _contentKind = (sender as FrameworkElement)?.Tag?.ToString() switch
        {
            "rp" => ContentKind.ResourcePacks,
            "shaders" => ContentKind.Shaders,
            "worlds" => ContentKind.Worlds,
            _ => ContentKind.Mods
        };

        RefreshContent();
    }

    private string CurrentContentDir()
    {
        if (_selectedInstance is null) return "";

        return _contentKind switch
        {
            ContentKind.ResourcePacks => _instancesService.ResourcePacksDir(_selectedInstance),
            ContentKind.Shaders => _instancesService.ShaderPacksDir(_selectedInstance),
            ContentKind.Worlds => _instancesService.SavesDir(_selectedInstance),
            _ => _instancesService.ModsDir(_selectedInstance)
        };
    }

    private static BitmapImage? LoadIconFromEntry(ZipArchiveEntry entry)
    {
        try
        {
            using var entryStream = entry.Open();
            using var ms = new MemoryStream();
            entryStream.CopyTo(ms);
            ms.Position = 0;
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.StreamSource = ms;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch { return null; }
    }

    private static string CleanSlugFromFilename(string filename)
    {
        var slug = IOPath.GetFileNameWithoutExtension(filename).ToLower();
        slug = _rxVersionSuffix.Replace(slug, "");
        slug = _rxVersionNumber.Replace(slug, "");
        return slug.Trim('-', '_');
    }

    private static string FormatModName(string slug)
    {
        if (_knownModNames.TryGetValue(slug, out var known)) return known;
        return string.Join(" ", slug.Split('_', '-').Select(w =>
            w.Length > 0 ? char.ToUpper(w[0]) + w[1..] : w));
    }

    private async Task<BitmapImage?> DownloadIconAsync(string url, string cacheKey)
    {
        try
        {
            var data = await _iconHttp.GetByteArrayAsync(url);
            using var ms = new MemoryStream(data);
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.StreamSource = ms;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch { return null; }
    }

    private void RefreshContent()
    {
        if (_selectedInstance is null)
        {
            TxtContentStatus.Text = "Сборка не выбрана.";
            ItemsContent.ItemsSource = null;
            return;
        }

        TxtContentSubtitle.Text = $"Сборка «{_selectedInstance.Name}» · " +
                                  $"{_selectedInstance.McVersion} · {_selectedInstance.LoaderDisplay}";

        var dir = CurrentContentDir();
        Directory.CreateDirectory(dir);

        var items = new List<object>();

        try
        {
            if (_contentKind == ContentKind.Worlds)
            {
                foreach (var d in new DirectoryInfo(dir).GetDirectories().OrderByDescending(x => x.LastWriteTime))
                {
                    long size = 0;
                    try { size = d.EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length); } catch (Exception ex) { Log.Warn(ex.Message); }

                    items.Add(new
                    {
                        Name = d.Name,
                        Info = $"{Human(size)} · изменён {d.LastWriteTime:dd.MM.yyyy HH:mm}",
                        Path = d.FullName,
                        ToggleText = "",
                        ToggleVisibility = Visibility.Collapsed,
                        Dot = new SolidColorBrush(ThemeService.CurrentAccent)
                    });
                }
            }
            else
            {
                var patterns = _contentKind == ContentKind.Mods
                    ? new[] { "*.jar", "*.jar.disabled" }
                    : new[] { "*.zip", "*.zip.disabled" };

                var files = patterns
                    .SelectMany(pat => new DirectoryInfo(dir).GetFiles(pat))
                    .DistinctBy(f => f.FullName)
                    .OrderBy(f => f.Name)
                    .ToList();

                foreach (var f in files)
                {
                    var enabled = !f.Name.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase);
                    string display;
                    BitmapImage? modIcon = null;

                    if (_contentKind == ContentKind.Mods)
                    {
                        var info = ExtractModInfo(f.FullName);
                        display = enabled ? info.modName : info.modName + "  (выключен)";
                        modIcon = info.icon;
                    }
                    else
                    {
                        display = enabled
                            ? IOPath.GetFileNameWithoutExtension(f.Name)
                            : IOPath.GetFileNameWithoutExtension(IOPath.GetFileNameWithoutExtension(f.Name)) + "  (выключен)";
                    }

                    items.Add(new
                    {
                        Name = display,
                        Info = $"{Human(f.Length)} · {f.LastWriteTime:dd.MM.yyyy}",
                        Path = f.FullName,
                        ToggleText = enabled ? "Выключить" : "Включить",
                        ToggleVisibility = Visibility.Visible,
                        IconImage = modIcon,
                        DefaultIconVisibility = modIcon is null ? Visibility.Visible : Visibility.Collapsed,
                        Dot = new SolidColorBrush(enabled
                            ? ThemeService.CurrentAccent
                            : (Color)ColorConverter.ConvertFromString("#6B7280"))
                    });
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warn("Чтение содержимого сборки: " + ex.Message);
        }

        ItemsContent.ItemsSource = items;

        var kindName = _contentKind switch
        {
            ContentKind.ResourcePacks => "ресурспаков",
            ContentKind.Shaders => "шейдеров",
            ContentKind.Worlds => "миров",
            _ => "модов"
        };

        TxtContentStatus.Text = items.Count == 0
            ? $"Нет {kindName}. Перетащите файлы сюда или нажмите «Импорт»."
            : $"Всего {kindName}: {items.Count}";

        _ = LoadContentIconsAsync();
    }

    private async Task LoadContentIconsAsync()
    {
        if (ItemsContent.ItemsSource is not List<object> items || items.Count == 0) return;
        var itemsCopy = items.ToList();
        bool changed = false;

        foreach (var item in itemsCopy)

        {
            var pathProp = item.GetType().GetProperty("Path");
            var filePath = pathProp?.GetValue(item) as string;
            if (string.IsNullOrEmpty(filePath)) continue;

            var iconProp = item.GetType().GetProperty("IconImage");
            var currentIcon = iconProp?.GetValue(item) as BitmapImage;
            var nameProp = item.GetType().GetProperty("Name");
            var currentName = nameProp?.GetValue(item) as string ?? "";

            BitmapImage? remoteIcon = currentIcon;
            string? remoteName = null;

            try
            {
                if (_contentKind == ContentKind.Mods)
                {
                    _modIdCache.TryGetValue(filePath, out var modId);
                    var query = !string.IsNullOrEmpty(modId) ? modId : CleanSlugFromFilename(filePath);
                    var (title, icon) = await FetchModrinthProjectAsync(query, "mod");
                    if (icon is not null) remoteIcon = icon;
                    if (!string.IsNullOrEmpty(title)) remoteName = title;
                    else remoteName = FormatModName(CleanSlugFromFilename(filePath));
                }
                else if (_contentKind == ContentKind.ResourcePacks)
                {
                    try
                    {
                        using var archive = ZipFile.OpenRead(filePath);
                        var packPng = archive.GetEntry("pack.png");
                        if (packPng is not null) remoteIcon = LoadIconFromEntry(packPng);
                    }
                    catch (Exception ex) { Log.Warn(ex.Message); }
                    var query = CleanSlugFromFilename(filePath);
                    var (title, icon) = await FetchModrinthProjectAsync(query, "resourcepack");
                    if (icon is not null && remoteIcon is null) remoteIcon = icon;
                    if (!string.IsNullOrEmpty(title)) remoteName = title;
                }
                else if (_contentKind == ContentKind.Shaders)
                {
                    var query = CleanSlugFromFilename(filePath);
                    var (title, icon) = await FetchModrinthProjectAsync(query, "shader");
                    if (icon is not null) remoteIcon = icon;
                    if (!string.IsNullOrEmpty(title)) remoteName = title;
                }

                var nameChanged = remoteName is not null && remoteName != currentName;
                var iconChanged = remoteIcon is not null && remoteIcon != currentIcon;
                if (nameChanged || iconChanged)
                {
                    var newName = nameChanged ? remoteName : currentName;
                    if (currentName.Contains("(выключен)")) newName += "  (выключен)";

                    var newAnonymous = new
                    {
                        Name = newName,
                        Info = item.GetType().GetProperty("Info")?.GetValue(item),
                        Path = filePath,
                        ToggleText = item.GetType().GetProperty("ToggleText")?.GetValue(item),
                        ToggleVisibility = item.GetType().GetProperty("ToggleVisibility")?.GetValue(item),
                        IconImage = remoteIcon,
                        DefaultIconVisibility = remoteIcon is null ? Visibility.Visible : Visibility.Collapsed,
                        Dot = item.GetType().GetProperty("Dot")?.GetValue(item)
                    };
                    var idx = items.IndexOf(item);
                    if (idx >= 0) items[idx] = newAnonymous;
                    changed = true;
                }
            }
            catch (Exception ex) { Log.Warn(ex.Message); }
        }

        if (changed)
        {
            ItemsContent.ItemsSource = null;
            ItemsContent.ItemsSource = items;
        }
    }

    private void BtnRefreshContent_Click(object sender, RoutedEventArgs e) => RefreshContent();

    private void BtnOpenContentFolder_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedInstance is null) return;
        try { _instancesService.OpenFolder(CurrentContentDir()); }
        catch (Exception ex) { AppendLog(ex.Message); }
    }

    private void ContentToggle_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.Tag is not string path) return;

        try
        {
            var target = path.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase)
                ? path[..^".disabled".Length]
                : path + ".disabled";

            if (File.Exists(target)) File.Delete(target);
            File.Move(path, target);

            RefreshContent();
            RefreshInstanceStats();
        }
        catch (Exception ex)
        {
            _dialog.ShowMessage("Не удалось переключить: " + ex.Message, "Ошибка", MessageSeverity.Error);
        }
    }

    private void ContentReveal_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.Tag is not string path) return;

        try
        {
            if (Directory.Exists(path)) _instancesService.OpenFolder(path);
            else _instancesService.RevealFile(path);
        }
        catch (Exception ex) { AppendLog(ex.Message); }
    }

    private async void ContentDelete_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.Tag is not string path) return;

        var name = IOPath.GetFileName(path);
        var isDir = Directory.Exists(path);

        var msg = isDir
            ? $"Удалить мир «{name}»?\n\nЭто действие необратимо."
            : $"Удалить «{name}»?";

        if (!await _dialog.ConfirmAsync("Удаление", msg)) return;

        try
        {
            if (isDir) Directory.Delete(path, true);
            else File.Delete(path);

            AppendLog($"Удалено: {name}");
            RefreshContent();
            RefreshInstanceStats();
        }
        catch (Exception ex)
        {
            _dialog.ShowMessage("Не удалось удалить: " + ex.Message, "Ошибка", MessageSeverity.Error);
        }
    }

    private void BtnImportMod_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedInstance is null)
        {
            _dialog.ShowMessage("Сначала выберите сборку.", "Сборка не выбрана");
            return;
        }

        var dlg = new OpenFileDialog
        {
            Title = "Выберите моды, ресурспаки или шейдеры",
            Filter = "Все поддерживаемые|*.jar;*.zip;*.mrpack|Моды (*.jar)|*.jar|Архивы (*.zip)|*.zip|Все файлы|*.*",
            Multiselect = true
        };

        if (dlg.ShowDialog(this) != true) return;
        ImportFiles(dlg.FileNames);
    }

    private void ImportFiles(IEnumerable<string> paths)
    {
        if (_selectedInstance is null) return;

        var inst = _selectedInstance;
        _instancesService.EnsureFolders(inst);

        var ok = 0;
        var skipped = new List<string>();
        var failed = new List<string>();

        foreach (var src in paths)
        {
            try
            {
                if (Directory.Exists(src))
                {
                    var worldDst = IOPath.Combine(_instancesService.SavesDir(inst), IOPath.GetFileName(src));
                    if (Directory.Exists(worldDst)) { skipped.Add(IOPath.GetFileName(src) + " (уже есть)"); continue; }
                    CopyDirectory(src, worldDst);
                    ok++;
                    continue;
                }

                if (!File.Exists(src)) continue;

                var ext = IOPath.GetExtension(src).ToLowerInvariant();
                var name = IOPath.GetFileName(src);

                string dstDir;

                if (ext == ".jar")
                {
                    dstDir = _instancesService.ModsDir(inst);
                }
                else if (ext == ".zip")
                {
                    dstDir = LooksLikeShaderPack(src)
                        ? _instancesService.ShaderPacksDir(inst)
                        : _instancesService.ResourcePacksDir(inst);
                }
                else if (ext == ".mrpack")
                {
                    _ = InstallModpackAsync(inst, src);
                    ok++;
                    continue;
                }
                else
                {
                    skipped.Add(name + " (неизвестный тип)");
                    continue;
                }

                var dst = IOPath.Combine(dstDir, name);
                if (File.Exists(dst)) { skipped.Add(name + " (уже есть)"); continue; }

                File.Copy(src, dst);
                ok++;
            }
            catch (Exception ex)
            {
                failed.Add(IOPath.GetFileName(src) + ": " + ex.Message);
            }
        }

        var report = $"Добавлено: {ok}";
        if (skipped.Count > 0) report += $"\nПропущено: {string.Join(", ", skipped)}";
        if (failed.Count > 0) report += $"\nОшибки: {string.Join(", ", failed)}";

        AppendLog("Импорт: " + report.Replace("\n", "; "));
        RefreshContent();
        RefreshInstanceStats();

        _dialog.ShowMessage(report, "Импорт файлов",
            failed.Count > 0 ? MessageSeverity.Warning : MessageSeverity.Info);
    }

    private static bool LooksLikeShaderPack(string zipPath)
    {
        try
        {
            using var zip = System.IO.Compression.ZipFile.OpenRead(zipPath);
            return zip.Entries.Any(entry =>
                entry.FullName.StartsWith("shaders/", StringComparison.OrdinalIgnoreCase) ||
                entry.FullName.Contains("/shaders/", StringComparison.OrdinalIgnoreCase));
        }
        catch { return false; }
    }

    private static void CopyDirectory(string src, string dst)
    {
        Directory.CreateDirectory(dst);

        foreach (var file in Directory.GetFiles(src))
            File.Copy(file, IOPath.Combine(dst, IOPath.GetFileName(file)), true);

        foreach (var dir in Directory.GetDirectories(src))
            CopyDirectory(dir, IOPath.Combine(dst, IOPath.GetFileName(dir)));
    }

    private void Content_DragOver(object sender, DragEventArgs e)
    {
        var hasFiles = e.Data.GetDataPresent(DataFormats.FileDrop);
        e.Effects = hasFiles && _selectedInstance is not null ? DragDropEffects.Copy : DragDropEffects.None;

        if (hasFiles && _selectedInstance is not null)
        {
            DropHint.Visibility = Visibility.Visible;
            TxtDropTarget.Text = $"в сборку «{_selectedInstance.Name}»  ·  .jar → моды, .zip → ресурспаки или шейдеры";
        }

        e.Handled = true;
    }

    private void Content_Drop(object sender, DragEventArgs e)
    {
        DropHint.Visibility = Visibility.Collapsed;

        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        if (_selectedInstance is null)
        {
            _dialog.ShowMessage("Сначала выберите сборку.", "Сборка не выбрана");
            return;
        }

        if (e.Data.GetData(DataFormats.FileDrop) is string[] files) ImportFiles(files);
        e.Handled = true;
    }

    public static string? ExtractModrinthSlug(string input)
    {
        input = input.Trim();

        if (input.Contains("modrinth.com", StringComparison.OrdinalIgnoreCase))
        {
            var m = System.Text.RegularExpressions.Regex.Match(
                input, @"modrinth\.com/(?:mod|plugin|datapack|resourcepack|shader|modpack)/([A-Za-z0-9._-]+)",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            return m.Success ? m.Groups[1].Value : null;
        }

        return System.Text.RegularExpressions.Regex.IsMatch(input, @"^[A-Za-z0-9._-]{2,64}$")
            ? input : null;
    }

}
