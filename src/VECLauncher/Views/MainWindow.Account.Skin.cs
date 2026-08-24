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
    private async Task LoadSkinImagesAsync(MinecraftAccount acc)
    {
        try
        {
            ImageSource? localAvatar = null;
            byte[]? skinRawBytes = null;
            byte[]? capeRawBytes = null;
            var isSlim = _currentSkinModel.Equals("slim", StringComparison.OrdinalIgnoreCase);

            if (acc.IsOffline || acc.IsVec)
            {
                lock (_skinFileLock)
                {
                    var localFile = _offlineSkins.FindAccountSkin(acc.Username);
                    if (localFile != null && File.Exists(localFile))
                    {
                        try { skinRawBytes = File.ReadAllBytes(localFile); }
                        catch (Exception ex) { Log.Warn(ex.Message); }
                    }

                    var localCape = _offlineSkins.FindAccountCape(acc.Username);
                    if (localCape != null && File.Exists(localCape))
                    {
                        try { capeRawBytes = File.ReadAllBytes(localCape); }
                        catch (Exception ex) { Log.Warn(ex.Message); }
                    }
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
                        _currentSkinModel = mojang.SkinModel;
                        isSlim = _currentSkinModel.Equals("slim", StringComparison.OrdinalIgnoreCase);
                        Dispatcher.Invoke(() =>
                        {
                            if (TxtSkinModelBadge != null)
                                TxtSkinModelBadge.Text = isSlim ? "Slim" : "Classic";
                            if (TxtToggleModelBadge != null)
                                TxtToggleModelBadge.Text = isSlim ? "Slim" : "Classic";
                            if (TxtSkinModel != null)
                                TxtSkinModel.Text = isSlim ? "Slim" : "Classic";
                        });
                    }
                    Vm.Accounts.Save(acc);
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
            {
                skinRawBytes = DefaultSkinService.GetDefaultSkin(acc.Username, isSlim);
            }

            if (skinRawBytes != null && skinRawBytes.Length > 0)
            {
                localAvatar = SkinService.CreateCrispHeadAvatar(skinRawBytes, 64);
            }

            byte[]? avatarBytes = null;
            if (localAvatar == null && !acc.IsOffline && !acc.IsVec)
            {
                avatarBytes = await _skins.GetAvatarAsync(acc, 72);
            }

            _currentSkinRawBytes = skinRawBytes;
            _currentCapeRawBytes = capeRawBytes;

            if (skinRawBytes != null && skinRawBytes.Length > 0 && !acc.IsOffline)
            {
                try
                {
                    var skinPath = _offlineSkins.AccountSkinPath(acc.Username);
                    var dir = System.IO.Path.GetDirectoryName(skinPath);
                    if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                    File.WriteAllBytes(skinPath, skinRawBytes);
                }
                catch (Exception ex) { Log.Warn(ex.Message); }
            }

            Dispatcher.Invoke(() =>
            {
                TxtSkinPlaceholder.Visibility = Visibility.Collapsed;
                var finalAvatar = localAvatar ?? (avatarBytes != null ? ToImage(avatarBytes) : null);
                ImgAvatar.Source = finalAvatar;
                if (ImgAvatarLarge != null) ImgAvatarLarge.Source = finalAvatar;
            });

            ReloadSkin3DModel();
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

    private void BtnToggleModel_Click(object sender, RoutedEventArgs e)
    {
        _currentSkinModel = _currentSkinModel.Equals("slim", StringComparison.OrdinalIgnoreCase) ? "classic" : "slim";
        var isSlim = _currentSkinModel.Equals("slim", StringComparison.OrdinalIgnoreCase);
        var badge = isSlim ? "Slim" : "Classic";

        if (TxtToggleModelBadge != null) TxtToggleModelBadge.Text = badge;
        if (TxtSkinModelBadge != null) TxtSkinModelBadge.Text = badge;
        if (TxtSkinModel != null) TxtSkinModel.Text = badge;

        if (_account != null)
        {
            _account.SkinModel = _currentSkinModel;
            Vm.Accounts.Save(_account);
            if (_account.IsVec)
            {
                VecAccountDatabase.UpdateSkinModel(_account.Username, _currentSkinModel);
            }

            var skinFile = _offlineSkins.FindAccountSkin(_account.Username);
            bool hasCustomSkin = skinFile != null && File.Exists(skinFile);

            if (hasCustomSkin)
            {
                var modelEnum = isSlim ? SkinService.SkinModel.Slim : SkinService.SkinModel.Classic;
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var srvUrl = !string.IsNullOrEmpty(_account.ServerUrl) ? _account.ServerUrl : await VecAuthService.GetActiveServerUrlAsync();
                        var skinBytes = await File.ReadAllBytesAsync(skinFile!);
                        Log.Info($"[Slim/Classic] Uploading skin to VEC: model={modelEnum}, user={_account.Username}, server={srvUrl}, size={skinBytes.Length}");
                        var uploadOk = await _skins.UploadSkinToVecServerAsync(srvUrl, _account.Username, skinBytes, modelEnum, _account.Uuid);
                        if (uploadOk)
                            Log.Info($"[Slim/Classic] Skin upload OK for {_account.Username}");
                        else
                            Log.Warn($"[Slim/Classic] Skin upload FAILED for {_account.Username}");
                    }
                    catch (Exception ex)
                    {
                        Log.Warn($"Не удалось обновить модель скина на сервере VEC: {ex.Message}");
                    }
                });

                var capeFile = _offlineSkins.FindAccountCape(_account.Username);
                foreach (var inst in _instances.Where(_offlineSkins.IsCslSupported))
                {
                    _offlineSkins.SyncToInstance(inst, _account.Username, skinFile, capeFile, isSlim);
                }
            }
            else
            {
                var username = _account.Username;
                var defaultSkinBytes = DefaultSkinService.GetDefaultSkin(username, isSlim);
                _currentSkinRawBytes = defaultSkinBytes;

                if (defaultSkinBytes != null && defaultSkinBytes.Length > 0)
                {
                    try
                    {
                        var slotPath = _offlineSkins.AccountSkinPath(username);
                        var dir = System.IO.Path.GetDirectoryName(slotPath);
                        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                        File.WriteAllBytes(slotPath, defaultSkinBytes);

                        var capeFileToggle = _offlineSkins.FindAccountCape(username);
                        foreach (var inst in _instances.Where(_offlineSkins.IsCslSupported))
                        {
                            _offlineSkins.SyncToInstance(inst, username, slotPath, capeFileToggle, isSlim);
                        }
                    }
                    catch (Exception ex) { Log.Warn(ex.Message); }

                    var avatar = SkinService.CreateCrispHeadAvatar(defaultSkinBytes, 64);
                    ImgAvatar.Source = avatar;
                    if (ImgAvatarLarge != null) ImgAvatarLarge.Source = avatar;
                }

                if (_account.IsVec)
                {
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            var srvUrl = !string.IsNullOrEmpty(_account.ServerUrl) ? _account.ServerUrl : await VecAuthService.GetActiveServerUrlAsync();
                            await _skins.ResetSkinOnVecServerAsync(srvUrl, username, defaultSkinBytes, _currentSkinModel);
                        }
                        catch (Exception ex)
                        {
                            Log.Warn($"Не удалось сбросить скин на сервере VEC: {ex.Message}");
                        }
                    });
                }
            }
        }

        ReloadSkin3DModel();

        if (_isCapeMode && _capeCarouselIndex >= 0 && _capeCarouselIndex < _capeCarouselCapes.Count)
        {
            _gltfSkinModel.UpdateCapeTexture(_capeCarouselCapes[_capeCarouselIndex].RawTextureBytes);
        }
    }
    private async void BtnBrowseSkin_Click(object sender, RoutedEventArgs e)
    {
        if (_account is null)
        {
            _toast.Show("Авторизация", "Сначала войдите в аккаунт, чтобы сменить скин.", NotificationType.Warning);
            TxtSkinStatus.Text = "Сначала войдите в аккаунт.";
            TxtSkinStatus.Foreground = (Brush)(TryFindResource("Danger") ?? Brushes.Red);
            return;
        }

        var dlg = new OpenFileDialog
        {
            Title = "Выберите PNG-файл скина (64x64 или 64x32)",
            Filter = "PNG изображения (*.png)|*.png|Все файлы (*.*)|*.*",
            CheckFileExists = true
        };
        if (dlg.ShowDialog(this) != true) return;

        TxtSkinStatus.Text = "Применяю скин...";
        TxtSkinStatus.Foreground = (Brush)(TryFindResource("FgMuted") ?? Brushes.Gray);

        try
        {
            var bytes = await File.ReadAllBytesAsync(dlg.FileName);
            SkinService.ValidateSkinPng(bytes);

            _currentSkinRawBytes = bytes;
            ReloadSkin3DModel();

            var model = _currentSkinModel.Equals("slim", StringComparison.OrdinalIgnoreCase) ? SkinService.SkinModel.Slim : SkinService.SkinModel.Classic;

            if (_account != null)
            {
                _account.SkinModel = _currentSkinModel;
                Vm.Accounts.Save(_account);
            }

            if (_account.IsOffline || _account.IsVec)
            {
                var skinFile = _offlineSkins.AccountSkinPath(_account.Username);
                var capeFile = _offlineSkins.FindAccountCape(_account.Username);
                var isSlim = _currentSkinModel.Equals("slim", StringComparison.OrdinalIgnoreCase);

                var skinDir = IOPath.GetDirectoryName(skinFile);
                if (!string.IsNullOrEmpty(skinDir)) Directory.CreateDirectory(skinDir);
                lock (_skinFileLock) { File.WriteAllBytes(skinFile, bytes); }

                foreach (var inst in _instances.Where(_offlineSkins.IsCslSupported))
                {
                    await _offlineSkins.EnsureCslModAsync(inst);
                    _offlineSkins.SyncToInstance(inst, _account.Username, skinFile, capeFile, isSlim);
                }

                if (_account.IsVec)
                {
                    var serverUrl = !string.IsNullOrEmpty(_account.ServerUrl) ? _account.ServerUrl : VecAuthService.DefaultVecServerUrl;
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            var uploadOk = await _skins.UploadSkinToVecServerAsync(serverUrl, _account.Username, bytes, model, _account.Uuid);
                            if (!uploadOk)
                                Log.Warn($"[BtnBrowseSkin] Skin upload FAILED for {_account.Username} on {serverUrl}");
                        }
                        catch (Exception ex)
                        {
                            Log.Warn($"Не удалось отправить скин на сервер VEC: {ex.Message}");
                        }
                    });
                }

                TxtSkinStatus.Text = "";
                _toast.Show("Скин обновлён", "Новый скин успешно установлен!", NotificationType.Success);
                await LoadSkinImagesAsync(_account);
            }
            else
            {
                TxtSkinStatus.Text = "Отправляю скин на серверы Mojang...";
                if (_account.IsExpired && !string.IsNullOrEmpty(_account.MicrosoftRefreshToken))
                {
                    try
                    {
                        _account = await _auth.RefreshOrReloginAsync(_account.MicrosoftRefreshToken!);
                    }
                    catch (MicrosoftAuthService.TokenExpiredException)
                    {
                        _account = await ReloginMicrosoftAsync();
                    }
                    Vm.Accounts.Save(_account);
                    SetAccount(_account, refreshSkin: false);
                }

                await _skins.UploadSkinAsync(_account.AccessToken, dlg.FileName, model);
                TxtSkinStatus.Text = "";
                _toast.Show("Mojang Скин", "Скин успешно обновлён на серверах Mojang!", NotificationType.Success);
                await Task.Delay(1500);
                await LoadSkinImagesAsync(_account);
            }
        }
        catch (Exception ex)
        {
            Log.Error("Ошибка смены скина", ex);
            TxtSkinStatus.Text = ex.Message;
            TxtSkinStatus.Foreground = (Brush)(TryFindResource("Danger") ?? Brushes.Red);
            _toast.Show("Ошибка", ex.Message, NotificationType.Error);
        }
    }

    private async void BtnResetSkin_Click(object sender, RoutedEventArgs e)
    {
        if (_account is null) return;

        if (!await _dialog.ConfirmAsync("Подтверждение", "Сбросить скин на стандартный?")) return;

        BtnResetSkin.IsEnabled = false;
        try
        {
            if (_account.IsOffline || _account.IsVec)
            {
                var skinFile = _offlineSkins.AccountSkinPath(_account.Username);
                if (File.Exists(skinFile)) File.Delete(skinFile);

                var isSlim = _currentSkinModel.Equals("slim", StringComparison.OrdinalIgnoreCase);
                var defaultSkinBytes = DefaultSkinService.GetDefaultSkin(_account.Username, isSlim);

                if (defaultSkinBytes != null && defaultSkinBytes.Length > 0)
                {
                    var dir = System.IO.Path.GetDirectoryName(skinFile);
                    if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                    File.WriteAllBytes(skinFile, defaultSkinBytes);
                }

                _currentSkinRawBytes = defaultSkinBytes;

                var currentCape = _offlineSkins.FindAccountCape(_account.Username);
                foreach (var inst in _instances.Where(_offlineSkins.IsCslSupported))
                {
                    await _offlineSkins.EnsureCslModAsync(inst);
                    _offlineSkins.SyncToInstance(inst, _account.Username, skinFile, currentCape, isSlim);
                    _offlineSkins.ClearCslCache(_instancesService.InstanceDir(inst));
                }


                if (_account.IsVec)
                {
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            var srvUrl = await VecAuthService.GetActiveServerUrlAsync();
                            await _skins.ResetSkinOnVecServerAsync(srvUrl, _account.Username, defaultSkinBytes, _currentSkinModel);
                        }
                        catch (Exception ex) { Log.Warn(ex.Message); }
                    });
                }

                TxtSkinPath.Text = "Файл не выбран";
                TxtSkinStatus.Text = "";
                await LoadSkinImagesAsync(_account);
            }
            else
            {
                await _skins.ResetSkinAsync(_account.AccessToken);
                _account.SkinUrl = null;
                Vm.Accounts.Save(_account);

                _currentSkinRawBytes = null;
                TxtSkinPath.Text = "Файл не выбран";
                TxtSkinStatus.Text = "";
                await Task.Delay(1000);
                await LoadSkinImagesAsync(_account);
            }
        }
        catch (Exception ex)
        {
            TxtSkinStatus.Text = ex.Message;
            TxtSkinStatus.Foreground = (Brush)FindResource("Danger");
        }
        finally
        {
            BtnResetSkin.IsEnabled = true;
        }
    }

    private void UpdateSkinTabHeader()
    {
        var acc = _account ?? Vm.Accounts.GetActive();

        if (acc == null)
        {
            TxtSkinTabStatus.Text = "Вход не выполнен — скин можно применить после входа в аккаунт.";
            ImgSkinsPreview.Source = null;
            return;
        }

        TxtSkinTabStatus.Text = acc.IsOffline
            ? "Оффлайн-профиль: применённый скин показывается в игре через CustomSkinLoader (сборки с Fabric/Forge)."
            : acc.IsExpired
                ? "Сессия Microsoft истекла — скин будет применён после повторного входа."
                : "Аккаунт Microsoft: скин загружается в ваш профиль Mojang.";

        if (acc.IsOffline)
        {
            var local = _offlineSkins.FindAccountSkin(acc.Username);
            if (local != null)
            {
                LoadLocalSkinImageAsync(ImgSkinsPreview, local);
                return;
            }
        }

        var previewUrl = acc.IsOffline
            ? SkinService.AvatarByNameUrl(acc.Username, 96)
            : SkinService.AvatarUrl(acc.Uuid, 96);
        LoadSkinImageAsync(ImgSkinsPreview, previewUrl);
    }

    private async void LoadSkinImageAsync(Image image, string url)
    {
        try
        {
            var data = await App.Http.GetByteArrayAsync(url);
            var bitmap = new System.Windows.Media.Imaging.BitmapImage();
            using var ms = new MemoryStream(data);
            bitmap.BeginInit();
            bitmap.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
            bitmap.StreamSource = ms;
            bitmap.EndInit();
            bitmap.Freeze();
            image.Source = bitmap;
        }
        catch (Exception ex) { Log.Warn(ex.Message); }
    }

    private void LoadLocalSkins()
    {
        LocalSkinsPanel.Children.Clear();
        var skinsDir = System.IO.Path.Combine(LauncherPaths.Root, "skins");
        if (!System.IO.Directory.Exists(skinsDir)) return;

        foreach (var file in System.IO.Directory.GetFiles(skinsDir, "*.png"))
        {
            var border = new Border
            {
                Width = 80, Height = 80, CornerRadius = new CornerRadius(8),
                Margin = new Thickness(4), Cursor = Cursors.Hand,
                Background = FindResource("Panel") as Brush,
                BorderBrush = FindResource("Border") as Brush,
                BorderThickness = new Thickness(1),
                Tag = file
            };

            var image = new Image { Stretch = Stretch.UniformToFill, Margin = new Thickness(4) };
            LoadLocalSkinImageAsync(image, file);
            border.Child = image;
            border.MouseLeftButtonDown += (_, _) =>
            {
                var skin = new SkinInfo { Name = System.IO.Path.GetFileNameWithoutExtension(file), Url = file };
                SelectLocalSkin(skin);
            };
            LocalSkinsPanel.Children.Add(border);
        }
    }

    private void LoadLocalSkinImageAsync(Image image, string path)
    {
        try
        {
            var bitmap = new System.Windows.Media.Imaging.BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
            bitmap.UriSource = new Uri(path);
            bitmap.EndInit();
            bitmap.Freeze();
            image.Source = bitmap;
        }
        catch (Exception ex) { Log.Warn(ex.Message); }
    }

    private void SelectLocalSkin(SkinInfo skin)
    {
        _selectedSkin = new SkinInfo { Name = skin.Name, Url = skin.Url };
        TxtNewSkinName.Text = skin.Name;
        ShowSkinPreviewPanel();
        LoadBodyPreviewAsync(skin.Url);
    }

    private void ShowSkinPreviewPanel()
    {
        SkinPreviewPanelNew.Visibility = Visibility.Visible;
        BtnApplySkinNew.IsEnabled = true;
        SetApplyButtonIdle();

        var acc = _account ?? Vm.Accounts.GetActive();
        TxtNewSkinInfo.Text = acc == null
            ? "Войдите в аккаунт, чтобы применить скин."
            : acc.IsOffline
                ? $"Оффлайн-аккаунт «{acc.Username}» — скин будет показываться в игре."
                : acc.IsExpired
                    ? "Сессия истекла — скин применится после повторного входа."
                    : "Скин будет загружен в ваш профиль Mojang.";
    }

    private async void LoadBodyPreviewAsync(string urlOrPath)
    {
        try
        {
            byte[] data;
            if (urlOrPath.StartsWith("http"))
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(25) };
                http.DefaultRequestHeaders.Add("User-Agent", "VEC Launcher/1.0");
                data = await http.GetByteArrayAsync(urlOrPath);
            }
            else
            {
                data = File.ReadAllBytes(urlOrPath);
            }

            var slim = _selectedSkin?.Slim ?? false;
            var selected = _selectedSkin;
            var render = await Task.Run(() => SkinBodyRenderer.Render(data, slim));
            if (render != null && ReferenceEquals(_selectedSkin, selected))
                ImgNewSkinPreview.Source = render;
        }
        catch (Exception ex) { Log.Warn(ex.Message); }
    }

    private async void BtnApplySkin_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedSkin == null) return;

        BtnApplySkinNew.IsEnabled = false;
        BtnApplySkinNew.Content = "Устанавливаю…";
        try
        {
            var account = _account ?? Vm.Accounts.GetActive();
            if (account == null)
            {
                _toast.Show("Вход не выполнен", "Сначала войдите в аккаунт.", NotificationType.Error);
                SetApplyButtonIdle();
                return;
            }

            _toast.Show("Установка скина",
                $"Начинаю установку «{_selectedSkin.Name}»…", NotificationType.Info);

            var path = _selectedSkin.Url;

            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                _toast.Show("Не удалось надеть скин", "Файл скина не найден.", NotificationType.Error);
                SetApplyButtonIdle();
                return;
            }

            if (account.IsOffline)
            {
                var slot = _offlineSkins.AccountSkinPath(account.Username);
                Directory.CreateDirectory(IOPath.GetDirectoryName(slot)!);

                if (string.Equals(path, slot, StringComparison.OrdinalIgnoreCase))
                {
                    AppendLog("Файл скина уже в слоте аккаунта: " + slot);
                }
                else
                {
                    try
                    {
                        File.Copy(path, slot, overwrite: true);
                    }
                    catch (IOException)
                    {
                        File.WriteAllBytes(slot, File.ReadAllBytes(path));
                    }
                }

                var synced = 0;
                foreach (var inst in _instances.Where(_offlineSkins.IsCslSupported))
                {
                    var cslOk = await _offlineSkins.EnsureCslModAsync(inst);
                    AppendLog($"CSL для «{inst.Name}» ({inst.Loader}): " + (cslOk ? "мод готов" : "не удалось установить"));
                    if (!cslOk) continue;
                    _offlineSkins.SyncToInstance(inst, account.Username, slot);
                    synced++;
                    AppendLog($"Оффлайн-скин «{account.Username}» скопирован в «{inst.Name}».");
                }
                if (_instances.Count == 0 && _selectedInstance != null && _offlineSkins.IsCslSupported(_selectedInstance))
                {
                    var cslOk = await _offlineSkins.EnsureCslModAsync(_selectedInstance);
                    if (cslOk)
                    {
                        _offlineSkins.SyncToInstance(_selectedInstance, account.Username, slot);
                        synced++;
                        AppendLog($"Оффлайн-скин «{account.Username}» скопирован в «{_selectedInstance.Name}».");
                    }
                }
                AppendLog(synced > 0
                    ? $"Скин «{_selectedSkin.Name}» надет на «{account.Username}» (сборок: {synced})."
                    : "Скин сохранён, но подходящих сборок с модлоадером не найдено.");

                LoadLocalSkins();

                _toast.Show("Скин надет", synced > 0
                    ? $"«{_selectedSkin.Name}» установлен для оффлайн-аккаунта «{account.Username}» и будет показываться в игре."
                    : "Скин сохранён. Для показа в игре оффлайн-аккаунту нужна сборка с модлоадером Fabric/Forge.",
                    NotificationType.Success);
                UpdateSkinTabHeader();
                _ = LoadSkinImagesAsync(account);
                SetApplyButtonApplied();
                return;
            }

            if (account.IsExpired && !string.IsNullOrEmpty(account.MicrosoftRefreshToken))
            {
                try
                {
                    account = await _auth.RefreshOrReloginAsync(account.MicrosoftRefreshToken!);
                }
                catch (MicrosoftAuthService.TokenExpiredException)
                {
                    account = await ReloginMicrosoftAsync();
                }
                Vm.Accounts.Save(account);
                SetAccount(account, refreshSkin: false);
            }

            var model = _selectedSkin.Slim ? SkinService.SkinModel.Slim : SkinService.SkinModel.Classic;
            await _skins.UploadSkinAsync(account.AccessToken, path, model);
            _toast.Show("Скин надет",
                $"«{_selectedSkin.Name}» загружен в ваш профиль Mojang.", NotificationType.Success);
            UpdateSkinTabHeader();
            _ = LoadSkinImagesAsync(account);
            SetApplyButtonApplied();
        }
        catch (InvalidDataException ex)
        {
            Log.Warn("Скин повреждён: " + ex.Message);
            _toast.Show("Не удалось надеть скин", "Скин повреждён — " + ex.Message, NotificationType.Error);
            SetApplyButtonIdle();
        }
        catch (Exception ex)
        {
            Log.Warn("Не удалось применить скин: " + ex.Message);
            _toast.Show("Не удалось надеть скин", ex.Message, NotificationType.Error);
            SetApplyButtonIdle();
        }
    }

    private void SetApplyButtonIdle()
    {
        BtnApplySkinNew.IsEnabled = true;
        BtnApplySkinNew.Content = "Надеть скин";
        BtnApplySkinNew.Background = null;
        BtnResetSkinNew.Visibility = Visibility.Collapsed;
    }

    private void SetApplyButtonApplied()
    {
        BtnApplySkinNew.IsEnabled = true;
        BtnApplySkinNew.Content = "Надет ✓";
        BtnApplySkinNew.Background = new SolidColorBrush(Color.FromRgb(0x2E, 0xA0, 0x43));
        BtnResetSkinNew.Visibility = Visibility.Visible;
    }

    private async void BtnResetSkinNew_Click(object sender, RoutedEventArgs e)
    {
        var account = _account ?? Vm.Accounts.GetActive();
        if (account == null)
        {
            _toast.Show("Вход не выполнен", "Сначала войдите в аккаунт.", NotificationType.Error);
            return;
        }

        BtnResetSkinNew.IsEnabled = false;
        try
        {
            if (account.IsOffline)
            {
                var slot = _offlineSkins.AccountSkinPath(account.Username);
                if (File.Exists(slot)) File.Delete(slot);

                foreach (var inst in _instances.Where(_offlineSkins.IsCslSupported))
                {
                    _offlineSkins.RemoveFromInstance(inst, account.Username);
                }

                if (_selectedSkin != null && _selectedSkin.Url.StartsWith("http")) LoadLocalSkins();
                _toast.Show("Скин по умолчанию",
                    $"Для оффлайн-аккаунта «{account.Username}» возвращён стандартный скин.",
                    NotificationType.Success);
            }
            else
            {
                if (account.IsExpired && !string.IsNullOrEmpty(account.MicrosoftRefreshToken))
                {
                    try
                    {
                        account = await _auth.RefreshOrReloginAsync(account.MicrosoftRefreshToken!);
                    }
                    catch (MicrosoftAuthService.TokenExpiredException)
                    {
                        account = await ReloginMicrosoftAsync();
                    }
                    Vm.Accounts.Save(account);
                    SetAccount(account, refreshSkin: false);
                }

                await _skins.ResetSkinAsync(account.AccessToken, CancellationToken.None);
                _toast.Show("Скин по умолчанию",
                    "Стандартный скин восстановлен в профиле Mojang.", NotificationType.Success);
            }

            if (_selectedSkin != null) LoadBodyPreviewAsync(_selectedSkin.Url);
            UpdateSkinTabHeader();
            _ = LoadSkinImagesAsync(account);
            SetApplyButtonIdle();
        }
        catch (Exception ex)
        {
            Log.Warn("Не удалось вернуть скин по умолчанию: " + ex.Message);
            _toast.Show("Ошибка", "Не удалось вернуть скин по умолчанию: " + ex.Message, NotificationType.Error);
        }
        finally { BtnResetSkinNew.IsEnabled = true; }
    }

    private void BtnImportSkin_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "PNG files (*.png)|*.png",
            Title = "Выберите файл скина"
        };

if (dialog.ShowDialog() == true)
        {
            try
            {
                var src = dialog.FileName;
                SkinService.ValidateSkinPng(File.ReadAllBytes(src));

                var skinsDir = IOPath.Combine(LauncherPaths.Root, "skins");
                Directory.CreateDirectory(skinsDir);
                var dest = IOPath.Combine(skinsDir, IOPath.GetFileName(src));
                File.Copy(src, dest, true);
                LoadLocalSkins();
                _toast.Show("Скин импортирован", IOPath.GetFileName(dest), NotificationType.Success);
            }
            catch (Exception ex)
            {
                _toast.Show("Ошибка", ex.Message, NotificationType.Error);
            }
        }
    }

    private void BtnRefreshLocalSkins_Click(object sender, RoutedEventArgs e)
    {
        LoadLocalSkins();
    }

}
