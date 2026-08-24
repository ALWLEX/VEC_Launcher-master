using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VECLauncher.Models;
using VECLauncher.Services;

namespace VECLauncher.ViewModels;

/// <summary>
/// Main ViewModel for the launcher window. Manages navigation, account state,
/// progress/log display, instance selection, and exposes 17 RelayCommands
/// bound to XAML elements.
/// </summary>
public partial class MainWindowViewModel : ObservableObject, IAccountState
{
    // ── Services ──

    /// <summary>Fetches Minecraft version manifest and version metadata.</summary>
    public readonly VersionService Versions;

    /// <summary>Downloads game assets, libraries, and mods with progress tracking.</summary>
    public readonly DownloadManager Downloads;

    /// <summary>Handles Microsoft OAuth authentication flow.</summary>
    public readonly MicrosoftAuthService Auth;

    /// <summary>Discovers and validates local Java installations.</summary>
    public readonly JavaService Java;

    /// <summary>Downloads and caches player skin/cape textures from Mojang.</summary>
    public readonly SkinService Skins;

    /// <summary>Builds JVM arguments and launches the Minecraft process.</summary>
    public readonly GameLauncher Game;

    /// <summary>Downloads and installs mod loaders (Fabric, Forge, Quilt, NeoForge).</summary>
    public readonly ModLoaderService Loaders;

    /// <summary>Pings Minecraft servers to retrieve player count and MOTD.</summary>
    public readonly ServerPingService Ping;

    /// <summary>Searches CurseForge/Modrinth mod catalogs.</summary>
    public readonly ModService Mods;

    /// <summary>Import/export CurseForge/Modrinth modpacks.</summary>
    public readonly ModpackService Modpacks;

    /// <summary>Tracks running game processes and their lifecycle.</summary>
    public readonly GameSessionManager Sessions = new();

    /// <summary>Records per-instance play time statistics.</summary>
    public readonly GameStatistics Stats;

    /// <summary>Manages user's favorite instances list.</summary>
    public readonly FavoriteInstances Favorites;

    /// <summary>Monitors system RAM usage for display in the launcher UI.</summary>
    public readonly RamMonitor RamMonitor;

    /// <summary>Authenticates against the VEC (vec.kpvk.edu.kz) server.</summary>
    public readonly VecAuthService VecAuth = new();

    /// <summary>Pure game-launch logic: downloads, prepares, and starts Minecraft.</summary>
    public readonly LaunchService Launch;

    /// <summary>Abstract account data access.</summary>
    public readonly IAccountRepository Accounts;
    public readonly EventAggregator Events;

    /// <summary>UI abstraction for dialogs, toasts, and window manipulation.</summary>
    public IDialogService Dialogs { get; private set; } = null!;

    // ── Sub-ViewModels ──

    /// <summary>Handles account login, saved accounts list, and skin management.</summary>
    public AccountViewModel AccountVm { get; }

    /// <summary>Handles instance CRUD, gallery view, and instance filtering.</summary>
    public InstancesViewModel InstancesVm { get; }

    /// <summary>Handles mod search, content browsing, and mod import.</summary>
    public ModsViewModel ModsVm { get; }

    /// <summary>Handles settings UI binding and persistence.</summary>
    public SettingsViewModel SettingsVm { get; }

    public MainWindowViewModel(
        VersionService versions,
        DownloadManager downloads,
        MicrosoftAuthService auth,
        JavaService java,
        SkinService skins,
        GameLauncher game,
        ModLoaderService loaders,
        ServerPingService ping,
        ModService mods,
        ModpackService modpacks,
        RamMonitor ramMonitor,
        LaunchService launch,
        GameStatistics stats,
        FavoriteInstances favorites,
        IAccountRepository accounts,
        EventAggregator events)
    {
        Versions = versions;
        Downloads = downloads;
        Auth = auth;
        Java = java;
        Skins = skins;
        Game = game;
        Loaders = loaders;
        Ping = ping;
        Mods = mods;
        Modpacks = modpacks;
        Stats = stats;
        Favorites = favorites;
        Favorites.Load();
        RamMonitor = ramMonitor;
        Accounts = accounts;
        Events = events;
        Launch = launch;

        AccountVm = new AccountViewModel(this, Accounts, Skins, Auth, Events);
        InstancesVm = new InstancesViewModel(this, Events);
        ModsVm = new ModsViewModel(this, Mods, Events);
        SettingsVm = new SettingsViewModel(Java, Events);
    }

    /// <summary>Called by View after construction to inject UI service.</summary>
    public void SetDialogService(IDialogService dialogs)
    {
        Dialogs = dialogs;
    }

    // ── Navigation ──

    /// <summary>Available pages in the launcher sidebar.</summary>
    public enum PageId { Home, Instances, Mods, Content, Account, Settings, Console, Skins }

    [ObservableProperty]
    private PageId _activePage = PageId.Home;

    [ObservableProperty]
    private int _activeNavIndex;

    [RelayCommand]
    private void Navigate(string tag)
    {
        ActivePage = tag switch
        {
            "0" => PageId.Home,
            "1" => PageId.Instances,
            "3" => PageId.Account,
            "4" => PageId.Settings,
            "5" => PageId.Console,
            "6" => PageId.Mods,
            "7" => PageId.Content,
            "9" => PageId.Skins,
            _ => ActivePage
        };

        // Sync nav index for XAML RadioButtons
        ActiveNavIndex = tag switch
        {
            "0" => 0, "1" => 1, "6" => 2,
            "7" => 3, "3" => 4, "4" => 5,
            _ => ActiveNavIndex
        };
    }

    /// <summary>
    /// Called by View after page transition animation completes.
    /// Performs page-specific data refresh that depends on the active page.
    /// </summary>
    public void OnPageActivated(string tag)
    {
        // Page-specific refresh handled by View via callbacks
        PageActivated?.Invoke(tag);
    }

    /// <summary>Event raised when a page becomes active. View subscribes for page-specific refresh.</summary>
    public event Action<string>? PageActivated;

    // ── Account ──
    [ObservableProperty]
    private MinecraftAccount? _account;

    [ObservableProperty]
    private string _accountDisplayName = "—";

    [ObservableProperty]
    private string _accountUuid = "";

    [ObservableProperty]
    private string _accountType = "";

    [ObservableProperty]
    private string _authState = "Вы не вошли в аккаунт.";

    [ObservableProperty]
    private string _sideName = "Не выполнен вход";

    [ObservableProperty]
    private string _sideStatus = "Оффлайн";

    [ObservableProperty]
    private bool _isAccountLoggedIn;

    [ObservableProperty]
    private string _skinModel = "Classic";

    [ObservableProperty]
    private ImageSource? _avatar;

    [ObservableProperty]
    private ImageSource? _avatarLarge;

    [ObservableProperty]
    private bool _skinPlaceholderVisible = true;

    /// <summary>Sets the current account and updates all account-related display properties.</summary>
    public void SetAccount(MinecraftAccount acc, bool refreshSkin)
    {
        Account = acc;
        AccountDisplayName = acc.Username;
        AccountUuid = acc.DashedUuid;
        IsAccountLoggedIn = true;

        if (acc.IsVec)
        {
            AccountType = "VEC ID (КПВК)";
            AuthState = acc.Username;
            SideName = acc.Username;
            SideStatus = "VEC ID";
        }
        else if (acc.IsOffline)
        {
            AccountType = "Оффлайн";
            AuthState = acc.Username;
            SideName = acc.Username;
            SideStatus = "Оффлайн";
        }
        else
        {
            AccountType = "Microsoft";
            AuthState = acc.Username;
            SideName = acc.Username;
            SideStatus = "Microsoft";
        }

        Events.Publish(new AccountChangedEvent(acc, IsLogout: false));
    }

    /// <summary>Clears account state and resets all account-related display properties to defaults.</summary>
    public void ClearAccount()
    {
        Account = null;
        AccountDisplayName = "—";
        AccountUuid = "";
        AuthState = "Вы не вошли в аккаунт.";
        SideName = "Не выполнен вход";
        SideStatus = "Оффлайн";
        IsAccountLoggedIn = false;
        SkinPlaceholderVisible = true;
        Avatar = null;
        AvatarLarge = null;

        Events.Publish(new AccountChangedEvent(null, IsLogout: true));
    }

    // ── Skin Raw Data (shared between VMs and View) ──
    public byte[]? CurrentSkinRawBytes { get; set; }
    public byte[]? CurrentCapeRawBytes { get; set; }
    public string CurrentSkinModel { get; set; } = "classic";

    // ── Settings ──

    /// <summary>Global launcher settings (memory, window, language, etc.).</summary>
    [ObservableProperty]
    private LauncherSettings _settings = new();

    /// <summary>Loads launcher settings from disk.</summary>
    public void LoadSettings()
    {
        Settings = SettingsService.Load();
    }

    /// <summary>Persists current launcher settings to disk.</summary>
    public void SaveSettings()
    {
        SettingsService.Save(Settings);
    }

    // ── Manifest ──
    public VersionManifest? Manifest { get; set; }

    // ── Instances ──
    public List<GameInstance> Instances { get; set; } = new();

    [ObservableProperty]
    private GameInstance? _selectedInstance;

    [ObservableProperty]
    private string _instanceFilter = "";

    public void LoadInstances()
    {
        Instances = InstanceService.LoadAll();
    }

    public List<GameInstance> GetFilteredInstances()
    {
        var source = Instances
            .OrderByDescending(i => i.LastPlayed ?? i.Created)
            .ToList();

        if (string.IsNullOrEmpty(InstanceFilter)) return source;

        return source.Where(i =>
            i.Name.Contains(InstanceFilter, StringComparison.OrdinalIgnoreCase) ||
            i.McVersion.Contains(InstanceFilter, StringComparison.OrdinalIgnoreCase) ||
            i.LoaderDisplay.Contains(InstanceFilter, StringComparison.OrdinalIgnoreCase)
        ).ToList();
    }

    public void SelectInstance(GameInstance inst)
    {
        SelectedInstance = inst;
        Settings.LastInstanceId = inst.Id;
    }

    // ── Busy / Progress ──

    /// <summary>Whether a long-running operation is in progress (disables launch button).</summary>
    [ObservableProperty]
    private bool _busy;

    /// <summary>Whether the progress bar area is visible.</summary>
    [ObservableProperty]
    private bool _progressVisible;

    [ObservableProperty]
    private bool _progressIndeterminate;

    [ObservableProperty]
    private double _progressValue;

    [ObservableProperty]
    private string _progressStage = "";

    [ObservableProperty]
    private string _progressPercent = "";

    [ObservableProperty]
    private string _progressDetail = "";

    [ObservableProperty]
    private bool _cancelVisible;

    public CancellationTokenSource? Cts { get; set; }

    /// <summary>Sets busy state. When busy, hides progress unless keepProgress is true.</summary>
    public void SetBusy(bool busy, bool keepProgress = false)
    {
        Busy = busy;
        CancelVisible = busy;
        if (!busy && !keepProgress)
        {
            ProgressIndeterminate = false;
        }
    }

    /// <summary>Shows the progress bar and sets the current operation description.</summary>
    public void SetStage(string stage)
    {
        ProgressVisible = true;
        ProgressStage = stage;
    }

    /// <summary>Hides the progress bar area.</summary>
    public void HideProgress()
    {
        ProgressIndeterminate = false;
        ProgressVisible = false;
    }

    // ── Log ──
    private readonly StringBuilder _logBuffer = new();
    private string _logFilter = "all";

    [ObservableProperty]
    private string _logText = "";

    [ObservableProperty]
    private string _logInfo = "Журнал лаунчера и вывод игры";

    /// <summary>Appends a line to the log buffer and updates the UI log display.
    /// Truncates buffer at 400K chars. Applies log-level filter if active.</summary>
    public void AppendLog(string line)
    {
        lock (_logBuffer)
        {
            _logBuffer.AppendLine(line);
            if (_logBuffer.Length > 400_000) _logBuffer.Remove(0, 200_000);
        }

        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            if (_logFilter != "all")
            {
                if (MatchesLogLevel(line, _logFilter)) ApplyLogFilterInternal();
                return;
            }

            string text;
            lock (_logBuffer) text = _logBuffer.ToString();
            LogText = text;
        });
    }

    /// <summary>Filters the log display by level: "all", "warn", or "error".</summary>
    public void SetLogFilter(string filter)
    {
        _logFilter = filter;
        ApplyLogFilterInternal();
    }

    private void ApplyLogFilterInternal()
    {
        string all;
        lock (_logBuffer) all = _logBuffer.ToString();

        if (_logFilter == "all")
        {
            LogText = all;
            LogInfo = "Журнал лаунчера и вывод игры";
            return;
        }

        var lines = all.Split('\n');
        var filtered = lines.Where(l => MatchesLogLevel(l, _logFilter)).ToList();

        LogText = filtered.Count > 0
            ? string.Join("\n", filtered)
            : (_logFilter == "error" ? "Ошибок нет." : "Предупреждений нет.");

        LogInfo = $"Показано {filtered.Count} из {lines.Length} строк";
    }


    /// <summary>Returns true if the log line matches the specified level filter.</summary>
    internal static bool MatchesLogLevel(string line, string level)
    {
        var lower = line.ToLowerInvariant();
        var isError = lower.Contains("[error]") || lower.Contains("error]") ||
                      lower.Contains("exception") || lower.Contains("ошибка") ||
                      lower.Contains("не удалось") || lower.Contains("severe") ||
                      lower.Contains("fatal") || lower.Contains("!!!");
        if (level == "error") return isError;
        return isError || lower.Contains("[warn]") || lower.Contains("warn]") ||
               lower.Contains("внимание") || lower.Contains("предупрежд");
    }

    // ── Running State ──

    /// <summary>Whether at least one game instance is currently running.</summary>
    [ObservableProperty]
    private bool _isRunning;

    [ObservableProperty]
    private string _playButtonText = "ИГРАТЬ";

    [ObservableProperty]
    private bool _playButtonVisible = true;

    [ObservableProperty]
    private bool _stopButtonVisible;

    [ObservableProperty]
    private string _stopButtonText = "ОСТАНОВИТЬ";

    [ObservableProperty]
    private string _runningBadge = "";

    /// <summary>Recalculates play/stop button visibility and text based on active sessions.</summary>
    public void UpdateRunState()
    {
        Sessions.Prune();
        var anyRunning = Sessions.AnyRunning;
        var thisRunning = SelectedInstance is not null && Sessions.IsInstanceRunning(SelectedInstance.Id);

        var hidePlay = !Busy && anyRunning && (!Settings.AllowMultipleInstances || thisRunning);

        PlayButtonVisible = !hidePlay;
        StopButtonVisible = anyRunning;
        IsRunning = anyRunning;

        PlayButtonText = Busy ? "ПОДГОТОВКА…"
            : SelectedInstance is not null && !File.Exists(GamePaths.ForInstance(SelectedInstance).VersionJar(SelectedInstance.McVersion))
                ? "УСТАНОВИТЬ И ИГРАТЬ"
                : "ИГРАТЬ";

        var running = Sessions.Sessions.Where(s => s.IsRunning).ToList();
        if (running.Count > 0)
        {
            RunningBadge = running.Count == 1
                ? $"{running[0].InstanceName} · {running[0].UptimeDisplay}"
                : $"Запущено игр: {running.Count}";
            StopButtonText = running.Count > 1 ? $"ОСТАНОВИТЬ ({running.Count})" : "ОСТАНОВИТЬ";
        }
    }

    // ── Instance Info Display ──

    /// <summary>Selected instance's name for display in the info panel.</summary>
    [ObservableProperty]
    private string _instName = "";

    [ObservableProperty]
    private string _instVersion = "";

    [ObservableProperty]
    private string _instLoader = "";

    [ObservableProperty]
    private string _instPlaytime = "";

    [ObservableProperty]
    private string _instStatTotal = "";

    [ObservableProperty]
    private string _instStatSessions = "";

    [ObservableProperty]
    private string _instStatAvg = "";

    [ObservableProperty]
    private string _instCountMods = "";

    [ObservableProperty]
    private string _instCountRp = "";

    [ObservableProperty]
    private string _instCountShaders = "";

    [ObservableProperty]
    private string _instCountWorlds = "";

    [ObservableProperty]
    private string _instSize = "";

    [ObservableProperty]
    private string _instHealth = "";

    /// <summary>Updates instance name, version, loader, and playtime display.</summary>
    public void UpdateInstanceInfo(GameInstance inst)
    {
        InstName = inst.Name;
        InstVersion = "Minecraft " + inst.McVersion;
        InstLoader = inst.LoaderDisplay;
        InstPlaytime = inst.TotalPlaySeconds > 0 ? "В игре: " + inst.PlayTimeDisplay : "Ещё не запускалась";
    }

    /// <summary>Updates instance file counts (mods, resource packs, shaders, worlds) and size.</summary>
    public void UpdateInstanceStats(GameInstance inst)
    {
        var st = InstanceService.GetStats(inst);
        InstCountMods = Plural(st.Mods, "файл", "файла", "файлов");
        InstCountRp = Plural(st.ResourcePacks, "пак", "пака", "паков");
        InstCountShaders = Plural(st.ShaderPacks, "пак", "пака", "паков");
        InstCountWorlds = Plural(st.Worlds, "мир", "мира", "миров");
        InstSize = st.SizeDisplay;
    }

    public void UpdateInstancePlaytime(GameInstance inst)
    {
        InstPlaytime = "В игре: " + inst.PlayTimeDisplay;
    }

    // ── Settings Hint ──
    [ObservableProperty]
    private string _settingsHint = "Изменения применяются и сохраняются сразу";

    [ObservableProperty]
    private string _settingsHintColor = "FgMuted";

    // ── Java Info ──
    [ObservableProperty]
    private string _javaList = "";

    // ── Server Ping ──
    [ObservableProperty]
    private string _serverPlayers = "—";

    // ── Statistics ──
    [ObservableProperty]
    private string _totalTimeDisplay = "";

    public void UpdateStatistics()
    {
        TotalTimeDisplay = Stats.GetFormattedTotalTime();
    }

    // ── Instance Config View ──
    [ObservableProperty]
    private bool _isGalleryView = true;

    public void ShowGallery() => IsGalleryView = true;
    public void ShowConfig() => IsGalleryView = false;

    // ── Helpers ──

    /// <summary>Formats bytes as human-readable string (e.g. "1.5 ГБ").</summary>
    public static string Human(long bytes)
    {
        string[] units = { "Б", "КБ", "МБ", "ГБ" };
        double v = bytes;
        var i = 0;
        while (v >= 1024 && i < units.Length - 1) { v /= 1024; i++; }
        return $"{v:0.#} {units[i]}";
    }

    /// <summary>Returns Russian pluralized string (e.g. "3 файла").</summary>
    public static string Plural(int n, string one, string few, string many)
    {
        var mod10 = n % 10;
        var mod100 = n % 100;
        var word = (mod10 == 1 && mod100 != 11) ? one
            : (mod10 >= 2 && mod10 <= 4 && (mod100 < 12 || mod100 > 14)) ? few
            : many;
        return $"{n} {word}";
    }

    /// <summary>Formats seconds into a human-readable time string.</summary>
    public static string FormatMinutes(long seconds)
    {
        if (seconds < 60) return $"{seconds} с";
        var ts = TimeSpan.FromSeconds(seconds);
        return ts.TotalHours >= 1 ? $"{(int)ts.TotalHours} ч {ts.Minutes} мин" : $"{ts.Minutes} мин";
    }

    /// <summary>Returns the effective RAM for an instance (instance override or global default).</summary>
    public int EffectiveMemory(GameInstance inst) =>
        inst.MaxMemoryMb > 0 ? inst.MaxMemoryMb : Settings.MaxMemoryMb;

    // ── Commands ──

    /// <summary>Cancels the current download or launch operation.</summary>
    [RelayCommand]
    private void Cancel()
    {
        Cts?.Cancel();
        SetStage("Отмена...");
    }

    /// <summary>Stops all running game instances.</summary>
    [RelayCommand]
    private async Task StopGameAsync()
    {
        Sessions.Prune();
        var running = Sessions.Sessions.Where(s => s.IsRunning).ToList();
        if (running.Count == 0)
        {
            UpdateRunState();
            return;
        }

        if (Settings.ConfirmGameStop)
        {
            // Caller must handle dialog
            return;
        }

        foreach (var s in running)
        {
            AppendLog($"Останавливаю «{s.InstanceName}» (PID {s.Pid})...");
            await Sessions.StopAsync(s);
        }
        UpdateRunState();
    }

    /// <summary>Logs out the current account, removes it from saved list, and resets display properties.</summary>
    [RelayCommand]
    private void Logout()
    {
        // Remove current account from saved accounts list
        if (Account is not null)
            Accounts.Remove(Account.Username, Account.Type);

        Accounts.ClearActiveSession();
        ClearAccount();
        AppendLog("Выполнен выход из аккаунта.");
    }

    /// <summary>Creates and logs into an offline account with the given name.</summary>
    [RelayCommand]
    private void LoginOffline(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return;
        var acc = OfflineAccountService.Create(name);
        Accounts.Save(acc);
        SetAccount(acc, refreshSkin: true);
        AppendLog($"Создан оффлайн-аккаунт: {acc.Username} ({acc.DashedUuid})");
    }

    /// <summary>Initiates Microsoft OAuth login via dialog service.</summary>
    [RelayCommand]
    private async Task LoginMicrosoftAsync()
    {
        if (Dialogs is null) return;

        SetBusy(true);
        SetStage("Авторизация через Microsoft...");

        try
        {
            var authUrl = Auth.BuildLiveAuthorizeUrl();
            var (code, error) = await Dialogs.ShowMicrosoftLoginAsync(authUrl);

            if (error is not null)
                throw new Exception(error);

            if (string.IsNullOrEmpty(code))
            {
                AppendLog("Вход через Microsoft отменён.");
                return;
            }

            SetStage("Получаю профиль Minecraft...");
            var acc = await Auth.SignInWithLiveCodeAsync(code);

            Accounts.Save(acc);
            SetAccount(acc, refreshSkin: true);
            AppendLog($"Вход выполнен: {acc.Username} ({acc.Type})");
            Dialogs.ShowToast("Успешный вход", $"Добро пожаловать, {acc.Username}!", ToastType.Success);
        }
        catch (Exception ex)
        {
            AppendLog("Ошибка авторизации Microsoft: " + ex.Message);
            Dialogs.ShowToast("Ошибка", ex.Message, ToastType.Error);
        }
        finally
        {
            SetBusy(false);
            HideProgress();
        }
    }

    /// <summary>Initiates VEC ID login via dialog service.</summary>
    [RelayCommand]
    private async Task LoginVecAsync()
    {
        if (Dialogs is null) return;

        SetBusy(true);
        SetStage("Авторизация через VEC ID...");

        try
        {
            var vecAcc = await Dialogs.ShowVecLoginAsync();
            if (vecAcc is null)
            {
                AppendLog("Вход через VEC ID отменён.");
                return;
            }

            // The dialog already authenticated and returned a full account
            // Save it (adds to saved list if new, updates if existing)
            Accounts.Save(vecAcc);
            SetAccount(vecAcc, refreshSkin: true);
            Dialogs.ShowToast("VEC ID", $"Добро пожаловать, {vecAcc.Username}!", ToastType.Success);
        }
        catch (Exception ex)
        {
            AppendLog("Ошибка авторизации VEC: " + ex.Message);
            Dialogs.ShowToast("Ошибка", ex.Message, ToastType.Error);
        }
        finally
        {
            SetBusy(false);
            HideProgress();
        }
    }

    /// <summary>Opens the VEC website in the default browser.</summary>
    [RelayCommand]
    private void OpenWebsite()
    {
        try
        {
            Process.Start(new ProcessStartInfo("https://vec.kpvk.edu.kz/") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            AppendLog("Не удалось открыть сайт: " + ex.Message);
        }
    }

    /// <summary>Clears the log buffer and displayed log text.</summary>
    [RelayCommand]
    public void DoClearLog()
    {
        lock (_logBuffer) _logBuffer.Clear();
        LogText = "";
    }

    /// <summary>Opens the launcher log file in the default text editor.</summary>
    [RelayCommand]
    private void OpenLogFile()
    {
        try { InstanceService.RevealFile(LauncherPaths.LauncherLogFile); }
        catch (Exception ex) { AppendLog(ex.Message); }
    }

    /// <summary>Deletes the current account from the launcher permanently.</summary>
    [RelayCommand]
    private void DeleteAccount()
    {
        if (Account == null) return;
        var name = Account.Username;
        var type = Account.Type;
        if (Account.IsVec)
            VecAccountDatabase.Delete(name);
        Accounts.Remove(name, type);
        Logout();
        AppendLog($"Аккаунт «{name}» удалён.");
    }

    /// <summary>Opens the game directory in Explorer.</summary>
    [RelayCommand]
    private void OpenGameDir()
    {
        try { InstanceService.OpenFolder(Settings.GameDir); }
        catch (Exception ex) { AppendLog("Не удалось открыть папку: " + ex.Message); }
    }

    /// <summary>Searches the system for Java installations and updates the Java list display.</summary>
    [RelayCommand]
    private void RescanJava()
    {
        JavaList = "Поиск…";
        _ = Task.Run(() =>
        {
            var list = Java.FindAll();
            Application.Current?.Dispatcher.Invoke(() =>
            {
                JavaList = list.Count == 0
                    ? "Java не обнаружена. Лаунчер скачает нужную версию автоматически."
                    : "Найдено:\n" + string.Join("\n", list.Select(j => "  • " + j));
            });
        });
    }

    /// <summary>Resets custom Java path to empty and triggers a rescan.</summary>
    [RelayCommand]
    private void ClearJavaPath()
    {
        Settings.CustomJavaPath = "";
        SaveSettings();
        RescanJava();
    }

    /// <summary>Sets the game window resolution from a "WxH" string.</summary>
    [RelayCommand]
    private void SetResolution(string wh)
    {
        var parts = wh.Split('x');
        if (parts.Length == 2 && int.TryParse(parts[0], out var w) && int.TryParse(parts[1], out var h))
        {
            Settings.WindowWidth = w;
            Settings.WindowHeight = h;
            Settings.Fullscreen = false;
            SaveSettings();
        }
    }

    /// <summary>Sets global max memory from a megabytes string.</summary>
    [RelayCommand]
    private void SetMemory(string mb)
    {
        if (int.TryParse(mb, out var val))
        {
            Settings.MaxMemoryMb = Math.Clamp(val, 1024, 16384);
            SaveSettings();
        }
    }

    /// <summary>Sets global memory to the system-recommended value.</summary>
    [RelayCommand]
    private void SetMemoryAuto()
    {
        Settings.MaxMemoryMb = LauncherSettings.RecommendedMaxMemory();
        SaveSettings();
    }

    /// <summary>Sets the selected instance's memory override.</summary>
    [RelayCommand]
    private void SetInstanceMemory(string mb)
    {
        if (SelectedInstance != null && int.TryParse(mb, out var val))
        {
            SelectedInstance.MaxMemoryMb = val;
            InstancesVm.SaveAll(Instances);
        }
    }

    /// <summary>Sets the selected instance's window size override.</summary>
    [RelayCommand]
    private void SetInstanceWindow(string wh)
    {
        if (SelectedInstance == null) return;
        var parts = wh.Split('x');
        if (parts.Length == 2 && int.TryParse(parts[0], out var w) && int.TryParse(parts[1], out var h))
        {
            SelectedInstance.WindowWidth = w;
            SelectedInstance.WindowHeight = h;
            InstancesVm.SaveAll(Instances);
        }
    }

    /// <summary>Resets the selected instance's custom settings to global defaults.</summary>
    [RelayCommand]
    private void ResetInstanceSettings()
    {
        if (SelectedInstance == null) return;
        InstancesVm.ResetInstanceSettings(SelectedInstance);
        InstancesVm.SaveAll(Instances);
        AppendLog($"Настройки сборки «{SelectedInstance.Name}» сброшены.");
    }

    /// <summary>Opens a file dialog to manually select a Java executable.</summary>
    [RelayCommand]
    private void BrowseJava()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Выберите java.exe",
            Filter = "java.exe|java.exe;javaw.exe|Исполняемые файлы (*.exe)|*.exe",
            CheckFileExists = true
        };
        if (dlg.ShowDialog() != true) return;
        var probe = JavaService.Probe(dlg.FileName, "custom");
        if (probe == null) return;
        Settings.CustomJavaPath = dlg.FileName;
        SaveSettings();
        AppendLog("Выбрана Java: " + probe);
    }

    /// <summary>Updates the game language setting.</summary>
    [RelayCommand]
    private void GameLangChanged(string lang)
    {
        Settings.GameLanguage = lang;
        SaveSettings();
    }

    // ── Launch Game ──

    /// <summary>Callback for the actual launch — set by View after construction.
    /// Signature: async Task(GameInstance inst, string? serverAddress)</summary>
    public Func<GameInstance, string?, Task>? LaunchAsyncCallback { get; set; }

    /// <summary>Main play button command. Handles pre-flight checks, then delegates
    /// the actual launch pipeline to the View via LaunchAsyncCallback.</summary>
    [RelayCommand]
    private async Task LaunchGameAsync()
    {
        if (Busy)
        {
            Cts?.Cancel();
            SetStage("Отмена...");
            return;
        }

        var inst = SelectedInstance ?? Instances.FirstOrDefault();
        if (inst is null)
        {
            Dialogs?.ShowToast("Нет сборки", "Сначала выберите или создайте сборку Minecraft.", ToastType.Warning);
            return;
        }

        if (Account is null)
        {
            Dialogs?.ShowToast("Нет аккаунта", "Сначала войдите в аккаунт.", ToastType.Warning);
            return;
        }

        var targetIp = string.IsNullOrWhiteSpace(inst.ServerAddress)
            ? "95.59.233.227:25565"
            : inst.ServerAddress;

        if (LaunchAsyncCallback is not null)
        {
            await LaunchAsyncCallback(inst, targetIp);
        }
    }
}
