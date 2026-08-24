using System.Diagnostics;
using System.IO;
using VECLauncher.Models;
using VECLauncher.Services;
using IOPath = System.IO.Path;

namespace VECLauncher.Services;

/// <summary>
/// Pure game launch logic — no UI dependencies.
/// All UI interactions (dialogs, notifications, window state) stay in the View layer.
/// </summary>
public sealed class LaunchService
{
    private readonly VersionService _versions;
    private readonly DownloadManager _downloads;
    private readonly MicrosoftAuthService _auth;
    private readonly JavaService _java;
    private readonly SkinService _skins;
    private readonly GameLauncher _game;
    private readonly ModLoaderService _loaders;

    public LaunchService(VersionService versions, DownloadManager downloads,
        MicrosoftAuthService auth, JavaService java, SkinService skins,
        GameLauncher game, ModLoaderService loaders)
    {
        _versions = versions;
        _downloads = downloads;
        _auth = auth;
        _java = java;
        _skins = skins;
        _game = game;
        _loaders = loaders;
    }

    public event Action<string>? StatusChanged;
    public event Action<string>? LogLine;

    private void SetStatus(string s) => StatusChanged?.Invoke(s);
    private void Log(string line) => LogLine?.Invoke(line);

    /// <summary>
    /// Core launch pipeline: resolve versions → Java → download → loader → VEC/skins → launch.
    /// Returns the launched Process and metadata.
    /// </summary>
    public async Task<LaunchResult> LaunchAsync(
        MinecraftAccount account,
        GameInstance inst,
        LauncherSettings settings,
        VersionManifest? manifest,
        List<GameInstance> instances,
        string? serverAddress,
        bool allowMultipleInstances,
        bool anyRunning,
        bool thisInstanceRunning,
        GameSessionManager sessions,
        CancellationToken ct)
    {
        var paths = GamePaths.ForInstance(inst);
        paths.EnsureAll();

        _versions.Paths = paths;
        _downloads.Paths = paths;
        _loaders.Paths = paths;
        _loaders.InstallRoot = paths.IsIsolated
            ? IOPath.Combine(InstanceService.InstanceDir(inst), ".minecraft")
            : LauncherPaths.Root;

        if (paths.IsIsolated) Log($"Сборка «{inst.Name}» изолирована: файлы в её папке.");

        // ── Version manifest ──
        SetStatus($"Читаю описание версии {inst.McVersion}...");
        var mvManifest = manifest ?? await _versions.GetManifestAsync(ct);
        var mv = mvManifest.Versions.FirstOrDefault(v => v.Id == inst.McVersion)
                 ?? throw new InvalidOperationException($"Версия {inst.McVersion} не найдена в манифесте.");
        var baseDetail = await _versions.GetVersionDetailAsync(mv, ct);

        // ── Java ──
        var requiredJava = baseDetail.JavaVersion?.MajorVersion ?? JavaService.RequiredJavaFor(inst.McVersion);
        SetStatus($"Проверяю Java {requiredJava}...");

        JavaInstallation java;
        var javaOverride = !string.IsNullOrWhiteSpace(inst.JavaPath) ? inst.JavaPath : settings.CustomJavaPath;

        if (!string.IsNullOrWhiteSpace(javaOverride) && File.Exists(javaOverride))
        {
            java = JavaService.Probe(javaOverride, "custom")
                   ?? throw new InvalidOperationException("Указанный java.exe не отвечает.");
            if (java.MajorVersion < requiredJava)
                Log($"ВНИМАНИЕ: выбрана Java {java.MajorVersion}, нужна {requiredJava}.");
        }
        else
        {
            java = await _java.EnsureJavaAsync(requiredJava, ct);
        }

        // ── Download base version ──
        await Task.Run(() => _downloads.InstallVersionAsync(baseDetail, ct), ct);

        var launchId = inst.EffectiveVersionId;

        // ── Loader (Fabric/Forge/NeoForge) ──
        if (inst.Loader != LoaderKind.Vanilla)
        {
            var alreadyInstalled = !string.IsNullOrEmpty(inst.LaunchVersionId) &&
                                   File.Exists(paths.VersionJson(inst.LaunchVersionId!));

            if (!alreadyInstalled)
            {
                SetStatus($"Устанавливаю {inst.Loader.Display()} {inst.LoaderVersion}...");
                launchId = await _loaders.InstallAsync(
                    inst.Loader, inst.McVersion, inst.LoaderVersion!, java, ct);

                inst.LaunchVersionId = launchId;
                InstanceService.SaveAll(instances);
            }
            else
            {
                launchId = inst.LaunchVersionId!;
            }
        }

        // ── Resolve final version ──
        SetStatus("Готовлю файлы запуска...");
        var finalDetail = await _versions.ResolveAsync(launchId, ct);
        var install = await Task.Run(() => _downloads.InstallVersionAsync(finalDetail, ct), ct);

        // ── VEC Auth / Skins ──
        bool useAuthlibInjector = false;
        string? authlibServerUrl = null;

        if (account.IsVec)
        {
            var vecResult = await PrepareVecAuthAsync(account, inst, ct);
            useAuthlibInjector = vecResult.UseAuthlib;
            authlibServerUrl = vecResult.ServerUrl;
        }

        // ── Offline skins (CSL) ──
        if (account.IsOffline || account.IsVec)
        {
            await PrepareOfflineSkinsAsync(account, inst, ct);
        }

        // ── Game language ──
        if (settings.AutoSetGameLanguage)
        {
            var created = GameOptionsService.EnsureLanguage(
                InstanceService.InstanceDir(inst), inst.McVersion, settings.GameLanguage);
            if (created)
                Log($"Язык игры установлен: " +
                    GameOptionsService.LanguageCodeFor(inst.McVersion, settings.GameLanguage));
        }

        // ── Build launch options ──
        var options = new LaunchOptions
        {
            Account = account,
            Install = install,
            Java = java,
            GameDir = InstanceService.InstanceDir(inst),
            MinMemoryMb = Math.Min(1024, EffectiveMemory(inst, settings)),
            MaxMemoryMb = EffectiveMemory(inst, settings),
            WindowWidth = inst.WindowWidth > 0 ? inst.WindowWidth : settings.WindowWidth,
            WindowHeight = inst.WindowHeight > 0 ? inst.WindowHeight : settings.WindowHeight,
            Fullscreen = settings.Fullscreen,
            ServerAddress = serverAddress ?? (string.IsNullOrWhiteSpace(inst.ServerAddress) ? null : inst.ServerAddress),
            ExtraJvmArgs = JvmPresetService.Resolve(inst.JvmPreset,
                string.IsNullOrWhiteSpace(inst.ExtraJvmArgs) ? settings.ExtraJvmArgs : inst.ExtraJvmArgs),
            ShowConsole = settings.ShowConsole,
            CloseLauncherOnStart = settings.CloseLauncherOnStart,
            UseAuthlibInjector = useAuthlibInjector,
            AuthlibServerUrl = authlibServerUrl
        };

        // ── Launch! ──
        var proc = _game.Launch(options);

        inst.LastPlayed = DateTimeOffset.Now;
        InstanceService.SaveAll(instances);

        return new LaunchResult
        {
            Process = proc,
            LaunchId = launchId,
            InstanceId = inst.Id,
            InstanceName = inst.Name,
            ServerAddress = serverAddress,
            JavaMajorVersion = java.MajorVersion
        };
    }

    // ── VEC Auth ──
    private async Task<(bool UseAuthlib, string? ServerUrl)> PrepareVecAuthAsync(
        MinecraftAccount account, GameInstance inst, CancellationToken ct)
    {
        var srvUrl = !string.IsNullOrEmpty(account.ServerUrl) && account.ServerUrl != VecAuthService.DefaultVecServerUrl
            ? account.ServerUrl
            : await VecAuthService.GetActiveServerUrlAsync();

        var serverOnline = await AuthlibInjectorService.IsServerAvailableAsync(srvUrl, ct);
        if (!serverOnline)
        {
            Log($"Сервер VEC ({srvUrl}) пока оффлайн — запуск в автономном режиме со скином из кэша.");
            return (false, null);
        }

        SetStatus("Подготовка VEC ID (authlib-injector)...");
        var jar = await AuthlibInjectorService.EnsureInstalledAsync(ct);
        if (jar == null) return (false, null);

        Log($"Подключен VEC Auth Server ({srvUrl}).");

        // Upload skin
        var skinF = OfflineSkinService.FindAccountSkin(account.Username);
        if (skinF != null && File.Exists(skinF))
        {
            try
            {
                var skinBytes = await File.ReadAllBytesAsync(skinF, ct);
                var model = account.SkinModel.Equals(Constants.SkinModel.Slim, StringComparison.OrdinalIgnoreCase)
                    ? SkinService.SkinModel.Slim
                    : SkinService.SkinModel.Classic;
                Log($"Загрузка скина на VEC: модель={model}, сервер={srvUrl}, размер={skinBytes.Length} байт");
                var uploadOk = await _skins.UploadSkinToVecServerAsync(srvUrl, account.Username, skinBytes, model, account.Uuid, ct);
                if (uploadOk) Log("Скин+модель загружены на VEC OK.");
                else Log("⚠ ОШИБКА: скин НЕ загружен на сервер VEC! Проверьте лог.");
            }
            catch (Exception exSkin)
            {
                Log($"⚠ Ошибка загрузки скина на VEC: {exSkin.Message}");
            }
        }

        // Upload cape
        var capeF = OfflineSkinService.FindAccountCape(account.Username);
        if (capeF != null && File.Exists(capeF))
        {
            try
            {
                var capeBytes = await File.ReadAllBytesAsync(capeF, ct);
                await _skins.UploadCapeToVecServerAsync(srvUrl, account.Username, capeBytes, ct);
            }
            catch (Exception ex) { VECLauncher.Services.Log.Warn(ex.Message); }
        }

        // Sync skin model
        try
        {
            var modelStr = account.SkinModel.Equals(Constants.SkinModel.Slim, StringComparison.OrdinalIgnoreCase) ? Constants.SkinModel.Slim : Constants.SkinModel.Classic;
            Log($"Синхронизация модели: {modelStr} для {account.Username}...");
            await _skins.UpdateSkinModelOnVecServerAsync(srvUrl, account.Username, modelStr, ct);
            Log($"Модель '{modelStr}' отправлена на сервер VEC.");
        }
        catch (Exception exModel)
        {
            Log($"Ошибка синхронизации модели: {exModel.Message}");
        }

        return (true, srvUrl);
    }

    // ── Offline skins ──
    private async Task PrepareOfflineSkinsAsync(MinecraftAccount account, GameInstance inst, CancellationToken ct)
    {
        var skinFile = OfflineSkinService.FindAccountSkin(account.Username);
        var capeFile = OfflineSkinService.FindAccountCape(account.Username);
        var isSlim = account.SkinModel.Equals(Constants.SkinModel.Slim, StringComparison.OrdinalIgnoreCase);

        if (skinFile == null && capeFile == null) return;
        if (!OfflineSkinService.IsCslSupported(inst))
        {
            if (!account.IsVec)
                Log("Внимание: чистая Vanilla без модов. Для оффлайн-скинов рекомендуется Fabric/Forge.");
            return;
        }

        SetStatus("Подготавливаю скин и плащ (CustomSkinLoader)...");
        var cslOk = await OfflineSkinService.EnsureCslModAsync(inst, ct);
        if (cslOk)
        {
            OfflineSkinService.SyncToInstance(inst, account.Username, skinFile, capeFile, isSlim);
            Log($"Скин и плащ «{account.Username}» синхронизированы в CustomSkinLoader.");
        }
        else
        {
            Log("Не удалось подготовить CustomSkinLoader.");
        }
    }

    private static int EffectiveMemory(GameInstance inst, LauncherSettings settings) =>
        inst.MaxMemoryMb > 0 ? inst.MaxMemoryMb : settings.MaxMemoryMb;
}

// ── Result models ──

/// <summary>Result of a successful game launch, containing the process handle and metadata.</summary>
public sealed class LaunchResult
{
    public required Process Process { get; init; }
    public required string LaunchId { get; init; }
    public required string InstanceId { get; init; }
    public required string InstanceName { get; init; }
    public string? ServerAddress { get; init; }
    public int JavaMajorVersion { get; init; }
}
