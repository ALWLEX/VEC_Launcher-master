using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VECLauncher.Models;
using VECLauncher.Services;
using VECLauncher.Views;

namespace VECLauncher.ViewModels;

/// <summary>
/// Manages account authentication (Microsoft, VEC, offline), saved accounts list,
/// skin upload/reset, and cape management.
/// </summary>
public partial class AccountViewModel : ObservableObject
{
    private readonly IAccountState _state;
    private readonly IAccountRepository _accounts;
    private readonly SkinService _skins;
    private readonly MicrosoftAuthService _auth;
    private readonly EventAggregator _events;
    private readonly object _skinFileLock = new();

    public AccountViewModel(
        IAccountState state,
        IAccountRepository accounts,
        SkinService skins,
        MicrosoftAuthService auth,
        EventAggregator events)
    {
        _state = state;
        _accounts = accounts;
        _skins = skins;
        _auth = auth;
        _events = events;
    }

    // ── Saved Accounts ──

    /// <summary>Display count of saved accounts (e.g. "3 профиля").</summary>
    [ObservableProperty]
    private string _savedAccountsCount = "0 профилей";

    public void RefreshSavedAccountsList()
    {
        var saved = _accounts.GetAllSaved();
        SavedAccountsCount = saved.Count switch
        {
            1 => "1 профиль",
            >= 2 and <= 4 => $"{saved.Count} профиля",
            _ => $"{saved.Count} профилей"
        };
    }

    public IReadOnlyList<MinecraftAccount> GetSavedAccounts() => _accounts.GetAllSaved();

    public void SelectSavedAccount(MinecraftAccount acc)
    {
        _accounts.Save(acc);
        _state.SetAccount(acc, refreshSkin: true);
        RefreshSavedAccountsList();
    }

    public void DeleteSavedAccount(MinecraftAccount acc)
    {
        if (acc.IsVec)
            VecAccountDatabase.Delete(acc.Username);
        _accounts.Remove(acc.Username, acc.Type);
    }

    public bool SwitchToNextAccount()
    {
        var next = _accounts.GetAllSaved().FirstOrDefault();
        if (next != null)
        {
            _accounts.Save(next);
            _state.SetAccount(next, refreshSkin: true);
            return true;
        }
        return false;
    }

    // ── Login ──
    public async Task<bool> LoginMicrosoftAsync(Action<string> setStage, Action<string> appendLog,
        Func<Task> hideProgress, Func<Task<string>> getAuthCode)
    {
        var auth = _auth;
        setStage("Авторизация через Microsoft...");

        try
        {
            var authUrl = auth.BuildLiveAuthorizeUrl();
            var code = await getAuthCode();
            if (string.IsNullOrEmpty(code))
            {
                appendLog("Вход через Microsoft отменён.");
                return false;
            }

            setStage("Получаю профиль Minecraft...");
            var acc = await auth.SignInWithLiveCodeAsync(code);
            _accounts.Save(acc);
            _state.SetAccount(acc, refreshSkin: true);
            appendLog($"Успешный вход в аккаунт Microsoft: {acc.Username}");
            return true;
        }
        catch (Exception ex)
        {
            Log.Warn("Ошибка входа Microsoft: " + ex.Message);
            appendLog("Ошибка входа Microsoft: " + ex.Message);
            throw;
        }
    }

    public MinecraftAccount CreateOfflineAccount(string name)
    {
        var acc = OfflineAccountService.Create(name);
        _accounts.Save(acc);
        _state.SetAccount(acc, refreshSkin: true);
        _state.AppendLog($"Создан оффлайн-аккаунт: {acc.Username} ({acc.DashedUuid})");
        return acc;
    }

    public bool ValidateOfflineName(string name, out string error)
    {
        return OfflineAccountService.TryValidateName(name, out error);
    }

    public void LoginVec(MinecraftAccount acc)
    {
        _accounts.Save(acc);
        _state.SetAccount(acc, refreshSkin: true);
        _state.AppendLog($"Вход через VEC ID: {acc.Username}");
    }

    // ── Logout ──
    public void Logout()
    {
        _accounts.ClearActiveSession();
        _state.ClearAccount();
        _state.AppendLog("Выполнен выход из аккаунта.");
    }

    // ── Delete Account ──
    public void DeleteAccount(MinecraftAccount acc)
    {
        if (acc.IsVec)
            VecAccountDatabase.Delete(acc.Username);
        _accounts.Remove(acc.Username, acc.Type);
        Logout();
    }

    // ── Skin Management ──
    public string CurrentSkinModel
    {
        get => _state.CurrentSkinModel;
        set => _state.CurrentSkinModel = value;
    }

    public async Task<MinecraftAccount?> RefreshMicrosoftSessionAsync()
    {
        var account = _state.Account;
        if (account == null || account.IsOffline) return account;

        if (!account.IsExpired || string.IsNullOrEmpty(account.MicrosoftRefreshToken))
            return account;

        try
        {
            _state.SetStage("Обновляю сессию Microsoft...");
            var refreshed = await _auth.RefreshOrReloginAsync(account.MicrosoftRefreshToken!);
            _accounts.Save(refreshed);
            _state.SetAccount(refreshed, refreshSkin: false);
            return refreshed;
        }
        catch (MicrosoftAuthService.TokenExpiredException)
        {
            _state.AppendLog("Токен истёк — требуется повторный вход.");
            return null;
        }
    }

    public async Task UploadSkinToVecAsync(byte[] skinBytes, SkinService.SkinModel model)
    {
        var acc = _state.Account;
        if (acc == null || !acc.IsVec) return;

        var srvUrl = !string.IsNullOrEmpty(acc.ServerUrl) && acc.ServerUrl != VecAuthService.DefaultVecServerUrl
            ? acc.ServerUrl
            : await VecAuthService.GetActiveServerUrlAsync();

        await _skins.UploadSkinToVecServerAsync(srvUrl, acc.Username, skinBytes, model, acc.Uuid);
    }

    public async Task ResetSkinOnVecAsync(byte[] defaultSkinBytes, string model)
    {
        var acc = _state.Account;
        if (acc == null || !acc.IsVec) return;

        var srvUrl = !string.IsNullOrEmpty(acc.ServerUrl) ? acc.ServerUrl : await VecAuthService.GetActiveServerUrlAsync();
        await _skins.ResetSkinOnVecServerAsync(srvUrl, acc.Username, defaultSkinBytes, model);
    }

    public async Task ToggleSkinModelAsync()
    {
        var acc = _state.Account;
        if (acc == null) return;

        var newModel = CurrentSkinModel.Equals(Constants.SkinModel.Slim, StringComparison.OrdinalIgnoreCase) ? Constants.SkinModel.Classic : Constants.SkinModel.Slim;
        CurrentSkinModel = newModel;

        acc.SkinModel = newModel;
        _accounts.Save(acc);
        if (acc.IsVec)
            VecAccountDatabase.UpdateSkinModel(acc.Username, newModel);

        var skinFile = OfflineSkinService.FindAccountSkin(acc.Username);
        bool hasCustomSkin = skinFile != null && File.Exists(skinFile);
        bool isSlim = newModel.Equals(Constants.SkinModel.Slim, StringComparison.OrdinalIgnoreCase);

        if (hasCustomSkin)
        {
            var modelEnum = isSlim ? SkinService.SkinModel.Slim : SkinService.SkinModel.Classic;
            _ = Task.Run(async () =>
            {
                try
                {
                    var srvUrl = !string.IsNullOrEmpty(acc.ServerUrl) ? acc.ServerUrl : await VecAuthService.GetActiveServerUrlAsync();
                    var bytes = await File.ReadAllBytesAsync(skinFile!);
                    await _skins.UploadSkinToVecServerAsync(srvUrl, acc.Username, bytes, modelEnum, acc.Uuid);
                }
                catch (Exception ex)
                {
                    Log.Warn($"Не удалось обновить модель скина на сервере VEC: {ex.Message}");
                }
            });

            var capeFile = OfflineSkinService.FindAccountCape(acc.Username);
            foreach (var inst in _state.Instances.Where(OfflineSkinService.IsCslSupported))
            {
                OfflineSkinService.SyncToInstance(inst, acc.Username, skinFile, capeFile, isSlim);
            }
        }
        else
        {
            var defaultSkinBytes = DefaultSkinService.GetDefaultSkin(acc.Username, isSlim);
            _state.CurrentSkinRawBytes = defaultSkinBytes;

            if (defaultSkinBytes != null && defaultSkinBytes.Length > 0)
            {
                try
                {
                    var slotPath = OfflineSkinService.AccountSkinPath(acc.Username);
                    var dir = Path.GetDirectoryName(slotPath);
                    if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                    File.WriteAllBytes(slotPath, defaultSkinBytes);

                    var capeFile = OfflineSkinService.FindAccountCape(acc.Username);
                    foreach (var inst in _state.Instances.Where(OfflineSkinService.IsCslSupported))
                    {
                        OfflineSkinService.SyncToInstance(inst, acc.Username, slotPath, capeFile, isSlim);
                    }
                }
                catch (Exception ex) { Log.Warn(ex.Message); }

                if (acc.IsVec)
                {
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            var srvUrl = !string.IsNullOrEmpty(acc.ServerUrl) ? acc.ServerUrl : await VecAuthService.GetActiveServerUrlAsync();
                            await _skins.ResetSkinOnVecServerAsync(srvUrl, acc.Username, defaultSkinBytes, newModel);
                        }
                        catch (Exception ex)
                        {
                            Log.Warn($"Не удалось сбросить скин на сервере VEC: {ex.Message}");
                        }
                    });
                }
            }
        }
    }

    public async Task ApplySkinFileAsync(string filePath)
    {
        var acc = _state.Account;
        if (acc == null) return;

        var bytes = await File.ReadAllBytesAsync(filePath);
        SkinService.ValidateSkinPng(bytes);

        _state.CurrentSkinRawBytes = bytes;
        var model = CurrentSkinModel.Equals(Constants.SkinModel.Slim, StringComparison.OrdinalIgnoreCase)
            ? SkinService.SkinModel.Slim
            : SkinService.SkinModel.Classic;

        acc.SkinModel = CurrentSkinModel;
        _accounts.Save(acc);

        var skinFile = OfflineSkinService.AccountSkinPath(acc.Username);
        var skinDir = Path.GetDirectoryName(skinFile);
        if (!string.IsNullOrEmpty(skinDir)) Directory.CreateDirectory(skinDir);
        lock (_skinFileLock) { File.WriteAllBytes(skinFile, bytes); }

        var capeFile = OfflineSkinService.FindAccountCape(acc.Username);
        bool isSlim = CurrentSkinModel.Equals(Constants.SkinModel.Slim, StringComparison.OrdinalIgnoreCase);

        foreach (var inst in _state.Instances.Where(OfflineSkinService.IsCslSupported))
        {
            await OfflineSkinService.EnsureCslModAsync(inst);
            OfflineSkinService.SyncToInstance(inst, acc.Username, skinFile, capeFile, isSlim);
        }

        if (acc.IsVec)
        {
            var serverUrl = !string.IsNullOrEmpty(acc.ServerUrl) ? acc.ServerUrl : VecAuthService.DefaultVecServerUrl;
            _ = Task.Run(async () =>
            {
                try
                {
                    await _skins.UploadSkinToVecServerAsync(serverUrl, acc.Username, bytes, model, acc.Uuid);
                }
                catch (Exception ex) { Log.Warn($"Не удалось отправить скин на сервер VEC: {ex.Message}"); }
            });
        }
        else if (!acc.IsOffline)
        {
            if (acc.IsExpired && !string.IsNullOrEmpty(acc.MicrosoftRefreshToken))
            {
                try
                {
                    acc = await _auth.RefreshOrReloginAsync(acc.MicrosoftRefreshToken!);
                }
                catch (MicrosoftAuthService.TokenExpiredException)
                {
                    // Caller should handle re-login
                    throw new InvalidOperationException("Требуется повторный вход в Microsoft.");
                }
                _accounts.Save(acc);
                _state.SetAccount(acc, refreshSkin: false);
            }
            await _skins.UploadSkinAsync(acc.AccessToken, filePath, model);
        }
    }

    public async Task ResetSkinAsync()
    {
        var acc = _state.Account;
        if (acc == null) return;

        if (acc.IsOffline || acc.IsVec)
        {
            var skinFile = OfflineSkinService.AccountSkinPath(acc.Username);
            if (File.Exists(skinFile)) File.Delete(skinFile);

            bool isSlim = CurrentSkinModel.Equals(Constants.SkinModel.Slim, StringComparison.OrdinalIgnoreCase);
            var defaultSkinBytes = DefaultSkinService.GetDefaultSkin(acc.Username, isSlim);

            if (defaultSkinBytes != null && defaultSkinBytes.Length > 0)
            {
                var dir = Path.GetDirectoryName(skinFile);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.WriteAllBytes(skinFile, defaultSkinBytes);
            }

            _state.CurrentSkinRawBytes = defaultSkinBytes;

            var currentCape = OfflineSkinService.FindAccountCape(acc.Username);
            foreach (var inst in _state.Instances.Where(OfflineSkinService.IsCslSupported))
            {
                await OfflineSkinService.EnsureCslModAsync(inst);
                OfflineSkinService.SyncToInstance(inst, acc.Username, skinFile, currentCape, isSlim);
                OfflineSkinService.ClearCslCache(InstanceService.InstanceDir(inst));
            }

            if (acc.IsVec)
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var srvUrl = await VecAuthService.GetActiveServerUrlAsync();
                        await _skins.ResetSkinOnVecServerAsync(srvUrl, acc.Username, defaultSkinBytes, CurrentSkinModel);
                    }
                    catch (Exception ex) { Log.Warn(ex.Message); }
                });
            }
        }
        else
        {
            await _skins.ResetSkinAsync(acc.AccessToken);
            acc.SkinUrl = null;
            _accounts.Save(acc);
            _state.CurrentSkinRawBytes = null;
        }
    }

    public async Task LoadSkinImagesAsync(MinecraftAccount acc)
    {
        try
        {
            ImageSource? localAvatar = null;
            byte[]? skinRawBytes = null;
            byte[]? capeRawBytes = null;
            var isSlim = CurrentSkinModel.Equals(Constants.SkinModel.Slim, StringComparison.OrdinalIgnoreCase);

            if (acc.IsOffline || acc.IsVec)
            {
                lock (_skinFileLock)
                {
                    var localFile = OfflineSkinService.FindAccountSkin(acc.Username);
                    if (localFile != null && File.Exists(localFile))
                        try { skinRawBytes = File.ReadAllBytes(localFile); } catch (Exception ex) { Log.Warn(ex.Message); }

                    var localCape = OfflineSkinService.FindAccountCape(acc.Username);
                    if (localCape != null && File.Exists(localCape))
                        try { capeRawBytes = File.ReadAllBytes(localCape); } catch (Exception ex) { Log.Warn(ex.Message); }
                }
            }

            if (!acc.IsOffline && !acc.IsVec)
            {
                var mojang = await _skins.FetchMojangTexturesAsync(acc.Uuid);
                if (mojang != null)
                {
                    if (!string.IsNullOrEmpty(mojang.SkinUrl)) acc.SkinUrl = mojang.SkinUrl;
                    if (!string.IsNullOrEmpty(mojang.CapeUrl)) acc.CapeUrl = mojang.CapeUrl;
                    if (!string.IsNullOrEmpty(mojang.SkinModel))
                    {
                        acc.SkinModel = mojang.SkinModel;
                        CurrentSkinModel = mojang.SkinModel;
                    }
                    _accounts.Save(acc);
                }
            }

            if (skinRawBytes == null && !acc.IsOffline)
            {
                try
                {
                    var skinUrl = !string.IsNullOrEmpty(acc.SkinUrl) ? acc.SkinUrl : SkinService.RawSkinUrl(acc.Uuid);
                    if (!string.IsNullOrEmpty(skinUrl))
                    {
                        using var resp = await App.Http.GetAsync(skinUrl);
                        if (resp.IsSuccessStatusCode) skinRawBytes = await resp.Content.ReadAsByteArrayAsync();
                    }
                }
                catch (Exception ex) { Log.Warn(ex.Message); }
            }

            if (!string.IsNullOrEmpty(acc.CapeUrl))
            {
                try
                {
                    using var resp = await App.Http.GetAsync(acc.CapeUrl);
                    if (resp.IsSuccessStatusCode) capeRawBytes = await resp.Content.ReadAsByteArrayAsync();
                }
                catch (Exception ex) { Log.Warn(ex.Message); }
            }

            if (skinRawBytes == null || skinRawBytes.Length == 0)
                skinRawBytes = DefaultSkinService.GetDefaultSkin(acc.Username, isSlim);

            if (skinRawBytes != null && skinRawBytes.Length > 0)
                localAvatar = SkinService.CreateCrispHeadAvatar(skinRawBytes, 64);

            byte[]? avatarBytes = null;
            if (localAvatar == null && !acc.IsOffline && !acc.IsVec)
                avatarBytes = await _skins.GetAvatarAsync(acc, 72);

            _state.CurrentSkinRawBytes = skinRawBytes;
            _state.CurrentCapeRawBytes = capeRawBytes;

            if (skinRawBytes != null && skinRawBytes.Length > 0 && !acc.IsOffline)
            {
                try
                {
                    var skinPath = OfflineSkinService.AccountSkinPath(acc.Username);
                    var dir = Path.GetDirectoryName(skinPath);
                    if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                    File.WriteAllBytes(skinPath, skinRawBytes);
                }
                catch (Exception ex) { Log.Warn(ex.Message); }
            }

            var finalAvatar = localAvatar ?? (avatarBytes != null ? ToImage(avatarBytes) : null);
            _state.Avatar = finalAvatar;
            _state.AvatarLarge = finalAvatar;
            _state.SkinPlaceholderVisible = skinRawBytes == null || skinRawBytes.Length == 0;
        }
        catch (Exception ex)
        {
            Log.Warn("Не удалось загрузить скин: " + ex.Message);
        }
    }

    private static BitmapImage ToImage(byte[] data)
    {
        var bmp = new BitmapImage();
        using var ms = new MemoryStream(data);
        bmp.BeginInit();
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.StreamSource = ms;
        bmp.EndInit();
        bmp.Freeze();
        return bmp;
    }

    public ImageSource? GetAccountAvatarImage(MinecraftAccount acc, int size = 32)
    {
        try
        {
            var localFile = OfflineSkinService.FindAccountSkin(acc.Username);
            if (localFile != null && File.Exists(localFile))
            {
                var bytes = File.ReadAllBytes(localFile);
                var head = SkinService.CreateCrispHeadAvatar(bytes, size);
                if (head != null) return head;
            }

            if (_state.Account?.Username.Equals(acc.Username, StringComparison.OrdinalIgnoreCase) == true
                && _state.CurrentSkinRawBytes != null && _state.CurrentSkinRawBytes.Length > 0)
            {
                var head = SkinService.CreateCrispHeadAvatar(_state.CurrentSkinRawBytes, size);
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
                                var path = OfflineSkinService.AccountSkinPath(acc.Username);
                                var dir = Path.GetDirectoryName(path);
                                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                                await File.WriteAllBytesAsync(path, downloaded);
                            }
                        }
                    }
                    catch (Exception ex) { Log.Warn(ex.Message); }
                });
            }

            bool isSlim = CurrentSkinModel.Equals(Constants.SkinModel.Slim, StringComparison.OrdinalIgnoreCase);
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
}
