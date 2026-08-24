using IOPath = System.IO.Path;
using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using System.Windows.Media;
using System.Windows.Threading;
using System.Windows;
using VECLauncher.Models;
using VECLauncher.Services;
using VECLauncher.ViewModels;

namespace VECLauncher.Views;

public partial class MainWindow : Window
{
    public MainWindowViewModel Vm { get; }

    // Services (thin shims — delegate to Vm)
    private VersionService _versions;
    private DownloadManager _downloads;
    private MicrosoftAuthService _auth;
    private JavaService _java;
    private SkinService _skins;
    private GameLauncher _game;
    private ModLoaderService _loaders;
    private ServerPingService _ping;
    private ModService _mods;
    private ModpackService _modpacks;
    private GameSessionManager _sessions => Vm.Sessions;
    private GameStatistics _stats => Vm.Stats;
    private RamMonitor _ramMonitor => Vm.RamMonitor;
    private readonly IToastService _toast;
    private readonly IDialogService _dialog;
    private readonly IInstanceService _instancesService;
    private readonly IOfflineSkinService _offlineSkins;
    private readonly IMaintenanceService _maintenance;
    private readonly IImageCacheService _imageCache;
    private readonly object _skinFileLock = new();
    private SkinInfo? _selectedSkin;

    // ── Compatibility shims: old field names → VM properties ──
    private LauncherSettings _settings { get => Vm.Settings; set => Vm.Settings = value; }
    private MinecraftAccount? _account { get => Vm.Account; set => Vm.Account = value; }
    private VersionManifest? _manifest { get => Vm.Manifest; set => Vm.Manifest = value; }
    private List<GameInstance> _instances { get => Vm.Instances; set => Vm.Instances = value; }
    private GameInstance? _selectedInstance { get => Vm.SelectedInstance; set => Vm.SelectedInstance = value; }
    private CancellationTokenSource? _cts { get => Vm.Cts; set => Vm.Cts = value; }
    private bool _busy { get => Vm.Busy; set => Vm.Busy = value; }
    private bool _initializing = true;
    private DateTime _lastProgressUi = DateTime.MinValue;
    private DispatcherTimer? _uptimeTimer;

    public MainWindow(MainWindowViewModel vm, IToastService toast,
        IInstanceService instancesService, IOfflineSkinService offlineSkins,
        IMaintenanceService maintenance, IImageCacheService imageCache)
    {
        Vm = vm;
        _toast = toast;
        _instancesService = instancesService;
        _offlineSkins = offlineSkins;
        _maintenance = maintenance;
        _imageCache = imageCache;
        DataContext = Vm;

        InitializeComponent();

        // Inject UI service for VM dialogs
        _dialog = new DialogService(this);
        Vm.SetDialogService(_dialog);

        // Inject launch callback — VM handles pre-flight, View handles UI
        Vm.LaunchAsyncCallback = LaunchAsync;

        // Wire VM services to local shims for backward compat
        _versions = Vm.Versions;
        _downloads = Vm.Downloads;
        _auth = Vm.Auth;
        _java = Vm.Java;
        _skins = Vm.Skins;
        _game = Vm.Game;
        _loaders = Vm.Loaders;
        _ping = Vm.Ping;
        _mods = Vm.Mods;
        _modpacks = Vm.Modpacks;


        Vm.RamMonitor.OnUpdate += RamMonitor_OnUpdate;

        _capeTransitionTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(16)
        };
        _capeTransitionTimer.Tick += CapeTransitionTimer_Tick;

        ToastNotification.Initialize(this);

        _downloads.Progress += OnProgress;
        _java.Progress += OnProgress;
        _auth.Status += s => Dispatcher.Invoke(() => SetStage(s));
        _loaders.Status += s => Dispatcher.Invoke(() => SetStage(s));
        _mods.Status += s => Dispatcher.Invoke(() => SetStage(s));
        _modpacks.Status += s => Dispatcher.Invoke(() => SetStage(s));
        _modpacks.Progress += OnProgress;
        _game.GameOutput += line => Vm.AppendLog(line);
        Log.LineWritten += line => Vm.AppendLog(line);

        Vm.Sessions.Changed += () => Dispatcher.BeginInvoke(UpdateRunStateUi);
        _sessions.SessionExited += OnSessionExited;

        Loaded += OnLoadedAsync;
        Closing += OnClosing;
        KeyDown += Window_KeyDown;
    }

    private async void OnLoadedAsync(object sender, RoutedEventArgs e)
    {
        _initializing = true;
        _settings = SettingsService.Load();

        ThemeService.ApplyTheme(_settings.Theme);
        ThemeService.ApplyAccent(_settings.AccentColor);
        ApplySettingsToUi();
        BuildThemeCards();
        BuildAccentSwatches();
        BuildBackgroundStyleButtons();
        ApplyBanner();
        ApplyWindowBackground();

        AppendLog("VEC Launcher запущен. Папка: " + _settings.GameDir);

        _uptimeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _uptimeTimer.Tick += (_, _) => UpdateUptimeBadge();
        _uptimeTimer.Start();

        _ = Task.Run(DetectJava);

        var saved = Vm.Accounts.GetActive();
        if (saved is not null)
        {
            SetAccount(saved, refreshSkin: true);

            if (!saved.IsOffline && saved.IsExpired && !string.IsNullOrEmpty(saved.MicrosoftRefreshToken))
            {
                try
                {
                    SetStage("Обновляю сессию Microsoft...");
                    var refreshed = await _auth.RefreshOrReloginAsync(saved.MicrosoftRefreshToken!);
                    Vm.Accounts.Save(refreshed);
                    SetAccount(refreshed, refreshSkin: true);
                }
                catch (MicrosoftAuthService.TokenExpiredException)
                {
                    try
                    {
                        AppendLog("Токен истёк — открываю окно входа Microsoft...");
                        var relogged = await ReloginMicrosoftAsync();
                        SetAccount(relogged, refreshSkin: true);
                    }
                    catch (Exception rex)
                    {
                        AppendLog("Повторный вход не удался: " + rex.Message);
                        TxtAuthState.Text = "Сессия истекла — войдите заново.";
                    }
                }
                catch (Exception ex)
                {
                    AppendLog("Не удалось обновить сессию: " + ex.Message);
                    TxtAuthState.Text = "Сессия истекла — войдите заново.";
                }
                finally { HideProgress(); }
            }
        }
        else
        {
            RefreshSavedAccountsListUI();
        }

        await LoadVersionsAsync();
        LoadInstances();
        _initializing = false;
        UpdateRunStateUi();

        Dispatcher.BeginInvoke(new Action(() => SetupWheelHandling(this)),
            System.Windows.Threading.DispatcherPriority.Loaded);

        _ramMonitor.Start();
        UpdateStatisticsDisplay();
        StartServerPingTimer();

        InitNative3DSkinViewer();
        if (_account != null) _ = LoadSkinImagesAsync(_account);
    }

    private string _currentSkinModel = "classic";
    private byte[]? _currentSkinRawBytes = null;
    private byte[]? _currentCapeRawBytes = null;
    private bool _autoRotateEnabled = false;
    private double _rotAngleY = 205;
    private double _rotAngleX = 10;
    private Point _lastMousePos;
    private bool _isDraggingSkin = false;
    private DispatcherTimer? _skinRotateTimer;    private readonly RotateTransform3D _skinRotY = new(new AxisAngleRotation3D(new Vector3D(0, 1, 0), 205));
    private readonly RotateTransform3D _skinRotX = new(new AxisAngleRotation3D(new Vector3D(1, 0, 0), 10));

    private bool _isCapeMode = false;
    private List<CapeItem> _capeCarouselCapes = new();
    private int _capeCarouselIndex = 0;
    private double _savedCamYaw, _savedCamPitch;
    private Point3D _savedCamPos;
    private Vector3D _savedCamLookDir;

    private ModelVisual3D? _groundVisual;

    private void InitNative3DSkinViewer()
    {
        RenderOptions.SetBitmapScalingMode(SkinViewport3D, BitmapScalingMode.NearestNeighbor);
        RenderOptions.SetEdgeMode(SkinViewport3D, EdgeMode.Aliased);
        if (SkinViewerHost != null)
        {
            RenderOptions.SetBitmapScalingMode(SkinViewerHost, BitmapScalingMode.NearestNeighbor);
            RenderOptions.SetEdgeMode(SkinViewerHost, EdgeMode.Aliased);
        }

        var group = new Transform3DGroup();
        group.Children.Add(_skinRotY);
        group.Children.Add(_skinRotX);
        SkinModelVisual.Transform = group;

        BuildGroundAndShadow();

        _skinRotateTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(16)
        };
        _skinRotateTimer.Tick += (s, e) =>
        {
            if (_autoRotateEnabled && !_isDraggingSkin && PageAccount.Visibility == Visibility.Visible)
            {
                _rotAngleY = (_rotAngleY + 0.6) % 360;
                _skinRotY.Rotation = new AxisAngleRotation3D(new Vector3D(0, 1, 0), _rotAngleY);
            }
        };
        _skinRotateTimer.Start();
    }

    private System.Windows.Point _mouseDownPos;

    private void SkinViewport_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_isCapeMode) return;
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            _isDraggingSkin = true;
            _lastMousePos = e.GetPosition(SkinViewerHost);
            _mouseDownPos = _lastMousePos;
            SkinViewerHost.CaptureMouse();
        }
    }

    private void UpdateStatisticsDisplay()
    {
        StatTotalTime.Text = _stats.GetFormattedTotalTime();
    }

    private DispatcherTimer? _serverPingTimer;

    private async void PingServerOnce()
    {
        try
        {
            var status = await _ping.PingAsync("95.59.233.227:25565");
            Dispatcher.Invoke(() =>
            {
                if (status.Online)
                    TxtServerPlayers.Text = $"{status.OnlinePlayers} / {status.MaxPlayers}";
                else
                    TxtServerPlayers.Text = "офлайн";
            });
        }
        catch
        {
            Dispatcher.Invoke(() => TxtServerPlayers.Text = "офлайн");
        }
    }

    private void StartServerPingTimer()
    {
        PingServerOnce();
        _serverPingTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _serverPingTimer.Tick += (_, _) => PingServerOnce();
        _serverPingTimer.Start();
    }

    private void RamMonitor_OnUpdate((DateTime Time, double UsedMb) point)
    {
    }

    private async void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_sessions.AnyRunning)
        {
            var r = await _dialog.ConfirmCancelAsync(
                "Игра запущена",
                $"Сейчас запущено игр: {_sessions.RunningCount}.\n\n" +
                "Закрыть лаунчер вместе с игрой?\n" +
                "«Нет» — лаунчер закроется, игра продолжит работать.",
                "Да с игрой", "Только лаунчер", "Отмена");

            if (r == ConfirmResult.Cancel) { e.Cancel = true; return; }
            if (r == ConfirmResult.Yes) await _sessions.StopAllAsync();
        }

        _uptimeTimer?.Stop();
        PersistSettings();
    }

    private static string FormatMinutes(long seconds)
    {
        if (seconds < 60) return $"{seconds} с";
        var ts = TimeSpan.FromSeconds(seconds);
        return ts.TotalHours >= 1 ? $"{(int)ts.TotalHours} ч {ts.Minutes} мин" : $"{ts.Minutes} мин";
    }

    private void InstMem2_Click(object s, RoutedEventArgs e) => SetInstMemory(2048);
    private void InstMem4_Click(object s, RoutedEventArgs e) => SetInstMemory(4096);
    private void InstMem8_Click(object s, RoutedEventArgs e) => SetInstMemory(8192);
    private void InstMemAuto_Click(object s, RoutedEventArgs e) => SetInstMemory(0);
    private void SetInstMemory(int mb)
    {
        TxtInstMemory.Text = mb.ToString();
        if (SldInstMemory != null) SldInstMemory.Value = mb > 0 ? mb : 4096;
        InstSetting_Changed(this, new RoutedEventArgs());
    }

    private void InstWin720_Click(object s, RoutedEventArgs e) => SetInstWindow(1280, 720);
    private void InstWin900_Click(object s, RoutedEventArgs e) => SetInstWindow(1600, 900);
    private void InstWin1080_Click(object s, RoutedEventArgs e) => SetInstWindow(1920, 1080);
    private void SetInstWindow(int w, int h)
    {
        TxtInstWidth.Text = w.ToString();
        TxtInstHeight.Text = h.ToString();
        InstSetting_Changed(this, new RoutedEventArgs());
    }

    private static string Plural(int n, string one, string few, string many)
    {
        var mod10 = n % 10;
        var mod100 = n % 100;
        var word = (mod10 == 1 && mod100 != 11) ? one
            : (mod10 >= 2 && mod10 <= 4 && (mod100 < 12 || mod100 > 14)) ? few
            : many;
        return $"{n} {word}";
    }

    private void BtnOpenWebsite_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo("https://vec.kpvk.edu.kz/") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _dialog.ShowMessage("Не удалось открыть сайт: " + ex.Message, "VEC Launcher", MessageSeverity.Warning);
        }
    }

    private async void BtnPlay_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedInstance is null)
        {
            _dialog.ShowMessage("Сначала выберите или создайте сборку.", "Сборка не выбрана");
            NavInstances.IsChecked = true;
            return;
        }

        await LaunchAsync(_selectedInstance, null);
    }

    private static string Dashed(string uuid)
    {
        var u = uuid.Replace("-", "");
        return u.Length != 32 ? uuid
            : $"{u[..8]}-{u.Substring(8, 4)}-{u.Substring(12, 4)}-{u.Substring(16, 4)}-{u.Substring(20)}";
    }

    private readonly System.Windows.Threading.DispatcherTimer _capeTransitionTimer;
    private byte[]? _capeTransitionTarget;
    private double _capeTransitionAlpha = 0;

    private void ApplyCapePreview(int index)
    {
        if (index < 0 || index >= _capeCarouselCapes.Count) return;
        var cape = _capeCarouselCapes[index];

        _capeTransitionTimer.Stop();
        _capeTransitionTarget = cape.RawTextureBytes;
        _capeTransitionAlpha = 0;
        _capeTransitionTimer.Start();
    }

    private void SettingsSection_Checked(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded || SecPanelGame is null) return;

        var tag = (sender as FrameworkElement)?.Tag?.ToString() ?? "game";
        _currentSettingsSection = tag;
        SecPanelGame.Visibility = tag == "game" ? Visibility.Visible : Visibility.Collapsed;
        SecPanelJava.Visibility = tag == "java" ? Visibility.Visible : Visibility.Collapsed;
        SecPanelStorage.Visibility = tag == "storage" ? Visibility.Visible : Visibility.Collapsed;
        SecPanelVersions.Visibility = tag == "versions" ? Visibility.Visible : Visibility.Collapsed;
        SecPanelMaint.Visibility = tag == "maint" ? Visibility.Visible : Visibility.Collapsed;

        if (tag == "versions" && ItemsVersions.ItemsSource is null) ScanVersions();
        if (tag == "maint" && ItemsMaint.ItemsSource is null) ScanMaintenance();
        if (tag == "storage") RefreshPortableState();
    }

    private void SetResolution(int w, int h)
    {
        TxtWidth.Text = w.ToString();
        TxtHeight.Text = h.ToString();
        ChkFullscreen.IsChecked = false;
        PersistSettings();
    }

    private void Preset720_Click(object s, RoutedEventArgs e) => SetResolution(1280, 720);
    private void Preset900_Click(object s, RoutedEventArgs e) => SetResolution(1600, 900);
    private void Preset1080_Click(object s, RoutedEventArgs e) => SetResolution(1920, 1080);

    private void SetMemory(int mb)
    {
        SldMemory.Value = Math.Clamp(mb, 1024, 16384);
        PersistSettings();
    }

    private void Mem2_Click(object s, RoutedEventArgs e) => SetMemory(2048);
    private void Mem4_Click(object s, RoutedEventArgs e) => SetMemory(4096);
    private void Mem8_Click(object s, RoutedEventArgs e) => SetMemory(8192);
    private void MemAuto_Click(object s, RoutedEventArgs e) => SetMemory(LauncherSettings.RecommendedMaxMemory());

    private void BtnRescanJava_Click(object sender, RoutedEventArgs e)
    {
        TxtJavaList.Text = "Поиск…";
        _ = Task.Run(DetectJava);
    }

    private void BtnClearJava_Click(object sender, RoutedEventArgs e)
    {
        TxtJavaPath.Clear();
        _settings.CustomJavaPath = "";
        PersistSettings();
        _ = Task.Run(DetectJava);
    }

    private System.Timers.Timer? _searchDebounceTimer;

    private void TxtModSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!IsLoaded) return;
        _searchDebounceTimer?.Stop();
        _searchDebounceTimer = new System.Timers.Timer(600);
        _searchDebounceTimer.Elapsed += (_, _) =>
        {
            _searchDebounceTimer.Stop();
            _searchDebounceTimer.Dispose();
            Dispatcher.Invoke(() =>
            {
                if (TxtModSearch.Text.Length >= 2)
                    RunModSearchFromStart();
            });
        };
        _searchDebounceTimer.AutoReset = false;
        _searchDebounceTimer.Start();
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool FlashWindow(IntPtr hwnd, bool bInvert);
    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 1) DragMove();
    }

    private static readonly Dictionary<string, string> _modNameCache = new();
    private static readonly Dictionary<string, string?> _modIdCache = new();
    private static readonly Dictionary<string, BitmapImage?> _modIconCache = new();
    private static readonly HttpClient _iconHttp = new() { Timeout = TimeSpan.FromSeconds(5) };

    private static string CleanTomlValue(string line)
    {
        var eq = line.IndexOf('=');
        if (eq < 0) return "";
        var val = line[(eq + 1)..].Trim();
        bool inQuote = false;
        int hashIdx = -1;
        for (int i = 0; i < val.Length; i++)
        {
            if (val[i] == '"') inQuote = !inQuote;
            if (!inQuote && val[i] == '#') { hashIdx = i; break; }
        }
        if (hashIdx >= 0) val = val[..hashIdx].TrimEnd();
        return val.Trim('"', '\'', ' ', '\r', '\n');
    }

    private (string modName, string? modId, BitmapImage? icon) ExtractModInfo(string jarPath)
    {
        if (_modNameCache.TryGetValue(jarPath, out var cachedName))
        {
            _modIconCache.TryGetValue(jarPath, out var cachedIcon);
            _modIdCache.TryGetValue(jarPath, out var cachedId);
            return (cachedName, cachedId, cachedIcon);
        }

        string name = IOPath.GetFileNameWithoutExtension(jarPath);
        string? modId = null;
        BitmapImage? icon = null;

        try
        {
            using var archive = ZipFile.OpenRead(jarPath);

            var fabricEntry = archive.GetEntry("fabric.mod.json");
            if (fabricEntry is not null)
            {
                using var stream = fabricEntry.Open();
                var doc = JsonNode.Parse(stream);
                name = doc?["name"]?.GetValue<string>() ?? name;
                modId = doc?["id"]?.GetValue<string>();

                var iconPath = doc?["icon"]?.GetValue<string>();
                if (!string.IsNullOrEmpty(iconPath))
                {
                    var iconEntry = archive.GetEntry(iconPath);
                    if (iconEntry is not null)
                        icon = LoadIconFromEntry(iconEntry);
                }
            }
            else
            {
                var modsToml = archive.GetEntry("META-INF/mods.toml")
                              ?? archive.GetEntry("META-INF/neoforge.mods.toml");
                if (modsToml is not null)
                {
                    using var reader = new StreamReader(modsToml.Open());
                    var tomlText = reader.ReadToEnd();
                    var lines = tomlText.Split('\n');
                    bool inMods = false;
                    foreach (var line in lines)
                    {
                        var trimmed = line.Trim();
                        var sectionCheck = trimmed.Split('#')[0].Trim();
                        if (sectionCheck == "[[mods]]" || sectionCheck.StartsWith("[[mods.")) inMods = true;
                        else if (sectionCheck.StartsWith("[")) inMods = false;

                        if (inMods && trimmed.StartsWith("displayName"))
                        {
                            name = CleanTomlValue(trimmed);
                        }
                        if (inMods && trimmed.StartsWith("modId"))
                        {
                            modId = CleanTomlValue(trimmed);
                        }
                        if (inMods && trimmed.StartsWith("logoFile"))
                        {
                            var logoPath = CleanTomlValue(trimmed);
                            var logoEntry = archive.GetEntry(logoPath);
                            if (logoEntry is not null)
                                icon = LoadIconFromEntry(logoEntry);
                        }
                    }
                }
            }
        }
        catch (Exception ex) { Log.Warn(ex.Message); }

        _modNameCache[jarPath] = name;
        _modIconCache[jarPath] = icon;
        if (modId != null) _modIdCache[jarPath] = modId;
        return (name, modId, icon);
    }

    private static readonly Dictionary<string, (string? name, BitmapImage? icon)> _modrinthProjectCache = new();

    private static readonly System.Text.RegularExpressions.Regex _rxVersionSuffix = new(
        @"[-_](mc)?(1\.|forge|fabric|neoforge|quilt|sl|babric|legacy)[^""]*",
        System.Text.RegularExpressions.RegexOptions.Compiled);
    private static readonly System.Text.RegularExpressions.Regex _rxVersionNumber = new(
        @"[-_]v?\d+\.\d+[^""]*",
        System.Text.RegularExpressions.RegexOptions.Compiled);

    private static readonly Dictionary<string, string> _knownModNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["alexsmobs"] = "Alex's Mobs",
        ["ars_nouveau"] = "Ars Nouveau",
        ["baublemounts"] = "Baubles Mounts",
        ["appleskin"] = "AppleSkin",
        ["backpacked"] = "Backpacked",
        ["balm"] = "Balm",
        ["autoreglib"] = "AutoRegLib",
        ["architectury"] = "Architectury",
        ["bbs"] = "Better Bed spawning",
        ["beylewither"] = "Beyle Wither",
        ["curios"] = "Curios API",
        ["jei"] = "Just Enough Items",
        ["crafttweaker"] = "CraftTweaker",
        ["create"] = "Create",
        ["createaddition"] = "Create Addition",
        ["mekanism"] = "Mekanism",
        ["tconstruct"] = "Tinkers Construct",
        ["mantle"] = "Mantle",
        ["thermal"] = "Thermal Expansion",
        ["botania"] = "Botania",
        ["thaumcraft"] = "Thaumcraft",
        [" appliedenergistics2"] = "Applied Energistics 2",
        ["ae2"] = "Applied Energistics 2",
        ["ironchest"] = "Iron Chests",
        ["ironfurnaces"] = "Iron Furnaces",
        ["computercraft"] = "Computer Craft",
        ["xnet"] = "XNet",
        ["rftools"] = "RFTools",
        ["rftoolsbase"] = "RFTools Base",
        ["refinedstorage"] = "Refined Storage",
        ["rabbitry"] = "Rabbitry",
        ["morpheus"] = "Morpheus",
        ["cloth_config"] = "Cloth Config",
        ["patchouli"] = "Patchouli",
        ["obookshelf"] = "O Bookshelf",
        ["bookshelf"] = "Bookshelf",
        ["reccomplex"] = "Recurrent Complex",
        ["llibrary"] = "LLibrary",
        ["obfuscate"] = "Obfuscate",
        ["placebo"] = "Placebo",
        ["blueprint"] = "Blueprint",
        ["environmental"] = "Environmental",
        ["blue_skies"] = "Blue Skies",
        ["iceandfire"] = "Ice and Fire",
        ["zawa"] = "Zawa",
        ["minecolonies"] = "MineColonies",
        ["valhelsia_structures"] = "Valhelsia Structures",
        ["titanium"] = "Titanium",
        ["silence_lib"] = "Silence Lib",
        ["sophisticated_core"] = "Sophisticated Core",
        ["sophisticated_backpacks"] = "Sophisticated Backpacks",
        ["sophisticated_storage"] = "Sophisticated Storage",
        ["geckolib"] = "GeckoLib",
        ["geckolib_forge"] = "GeckoLib",
        ["croptopia"] = "Croptopia",
        ["farmers_delight"] = "Farmer's Delight",
        ["farmersdelight"] = "Farmer's Delight",
        ["elevatorid"] = "Elevator Mod",
        ["lightOverlay"] = "Light Overlay",
        ["lightoverlay"] = "Light Overlay",
        ["worldedit"] = "WorldEdit",
        ["world_guard"] = "World Guard",
        ["journeymap"] = "JourneyMap",
        ["xaeros_minimap"] = "Xaero's Minimap",
        ["xaeros_world_map"] = "Xaero's World Map",
        ["travelers_titles"] = "Traveler's Titles",
        ["explorify"] = "Explorify",
        ["village_spawn_point"] = "Village Spawn Point",
        ["kotlinforforge"] = "Kotlin for Forge",
        ["bootstrap"] = "Bootstrap",
        ["moonlight"] = "Moonlight Lib",
        ["moonlightlib"] = "Moonlight Lib",
        ["selene"] = "Moonlight Lib",
        ["medievalgiant"] = "Medieval Giant",
        ["handcrafted"] = "Handcrafted",
        ["chipped"] = "Chipped",
        ["decoration_delight"] = "Decoration Delight",
        ["resourceful_lib"] = "Resourceful Lib",
        ["resourcefullib"] = "Resourceful Lib",
        ["resourceful_tools"] = "Resourceful Tools",
        ["chisels_and_bits"] = "Chisels and Bits",
        ["ctoverclocked"] = "CraftTweaker Overclocked",
        ["toughasnails"] = "Tough As Nails",
        ["serene_seasons"] = "Serene Seasons",
        ["comforts"] = "Comforts",
        ["carry_on"] = "Carry On",
        ["carryon"] = "Carry On",
        ["inventory_hud"] = "Inventory HUD",
        ["inventoryhud"] = "Inventory HUD",
        ["neapolitan"] = "Neapolitan",
        ["supplementaries"] = "Supplementaries",
        ["quark"] = "Quark",
        ["quark_oddities"] = "Quark Oddities",
    };

    private async Task<(string? title, BitmapImage? icon)> FetchModrinthProjectAsync(string query, string projectType = "mod")
    {
        var cacheKey = $"{projectType}:{query}";
        if (_modrinthProjectCache.TryGetValue(cacheKey, out var cached)) return cached;

        string? title = null;
        BitmapImage? icon = null;

        try
        {
            var url = $"https://api.modrinth.com/v2/project/{Uri.EscapeDataString(query)}";
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.TryAddWithoutValidation("User-Agent", "VEC Launcher/1.0");
            using var resp = await _iconHttp.SendAsync(req);

            if (resp.IsSuccessStatusCode)
            {
                var json = await resp.Content.ReadAsStringAsync();
                var doc = JsonNode.Parse(json);
                title = doc?["title"]?.GetValue<string>();
                var iconUrl = doc?["icon_url"]?.GetValue<string>();
                if (!string.IsNullOrEmpty(iconUrl))
                    icon = await DownloadIconAsync(iconUrl, cacheKey);
            }
            else
            {
                var searchUrl = $"https://api.modrinth.com/v2/search?query={Uri.EscapeDataString(query)}&limit=1&facets=[[\"project_type:{projectType}\"]]&index=downloads";
                using var req2 = new HttpRequestMessage(HttpMethod.Get, searchUrl);
                req2.Headers.TryAddWithoutValidation("User-Agent", "VEC Launcher/1.0");
                using var resp2 = await _iconHttp.SendAsync(req2);
                if (resp2.IsSuccessStatusCode)
                {
                    var json2 = await resp2.Content.ReadAsStringAsync();
                    var doc2 = JsonNode.Parse(json2);
                    var hits = doc2?["hits"]?.AsArray();
                    if (hits?.Count > 0)
                    {
                        title = hits[0]?["title"]?.GetValue<string>();
                        var iconUrl = hits[0]?["icon_url"]?.GetValue<string>();
                        if (!string.IsNullOrEmpty(iconUrl))
                            icon = await DownloadIconAsync(iconUrl, cacheKey);
                    }
                }
            }
        }
        catch (Exception ex) { Log.Warn(ex.Message); }

        _modrinthProjectCache[cacheKey] = (title, icon);
        return (title, icon);
    }

    private (string name, string? slug) ExtractPackInfo(string zipPath)
    {
        string name = IOPath.GetFileNameWithoutExtension(zipPath);
        string? slug = null;
        try
        {
            using var archive = ZipFile.OpenRead(zipPath);

            var packMeta = archive.GetEntry("pack.mcmeta");
            if (packMeta is not null)
            {
                using var reader = new StreamReader(packMeta.Open());
                var doc = JsonNode.Parse(reader.ReadToEnd());
                name = doc?["pack"]?["description"]?.GetValue<string>() ?? name;
            }

        }
        catch (Exception ex) { Log.Warn(ex.Message); }
        return (name, slug);
    }

    private List<MaintenanceService.TargetInfo> _maintTargets = new();
    private readonly HashSet<MaintenanceService.CleanTarget> _maintChecked = new();

    private void RefreshPortableState()
    {
        if (TxtPortableState is null) return;

        if (LauncherPaths.IsPortable)
        {
            TxtPortableState.Text = $"Включён. Данные: {LauncherPaths.Root}";
            TxtPortableState.Foreground = (Brush)FindResource("Accent");
            BtnPortableToggle.Content = "Выключить портативный режим";
        }
        else
        {
            var can = LauncherPaths.CanUsePortable();

            TxtPortableState.Text = can
                ? $"Выключен. Данные: {LauncherPaths.Root}"
                : "Недоступен: нет прав на запись рядом с лаунчером. " +
                  "Перенесите exe в обычную папку или на флешку.";

            TxtPortableState.Foreground = (Brush)FindResource(can ? "FgMuted" : "Danger");
            BtnPortableToggle.Content = "Включить портативный режим";
            BtnPortableToggle.IsEnabled = can;
        }
    }

    private static string Shorten(string s, int max) =>
        s.Length <= max ? s : "…" + s[^(max - 1)..];

    // ── Safe async wrapper (prevents fire-and-forget exceptions) ──
    private void RunAsync(Func<Task> action, string? errorContext = null)
    {
        _ = Task.Run(async () =>
        {
            try { await action(); }
            catch (OperationCanceledException ex) { Log.Warn(ex.Message); }
            catch (Exception ex)
            {
                Log.Error($"Async error{(errorContext != null ? $" ({errorContext})" : "")}: {ex.Message}");
                Dispatcher.Invoke(() =>
                    _toast.Show("Ошибка", ex.Message, Services.NotificationType.Error));
            }
        });
    }
}
