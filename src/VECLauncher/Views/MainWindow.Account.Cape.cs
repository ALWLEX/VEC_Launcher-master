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
    private void BtnWardrobe_Click(object sender, RoutedEventArgs e)
    {
        if (_isCapeMode)
        {
            _gltfSkinModel.UpdateCapeTexture(_savedOriginalCapeBytes);
            ExitCapeMode();
            return;
        }
        if (_account is null)
        {
            _toast.Show("Авторизация", "Сначала войдите в аккаунт.", NotificationType.Warning);
            return;
        }
        EnterCapeMode();
    }

    private void EnterCapeMode()
    {
        _isCapeMode = true;
        _capeCarouselCapes = CapeService.GetAllCapes(_account, _currentCapeRawBytes);
        var eqIdx = _capeCarouselCapes.FindIndex(c => c.IsEquipped);
        _capeCarouselIndex = eqIdx >= 0 ? eqIdx : 0;

        _savedCamPos = SkinCamera.Position;
        _savedCamLookDir = SkinCamera.LookDirection;
        _savedCamYaw = _rotAngleY;
        _savedCamPitch = _rotAngleX;
        _savedOriginalCapeBytes = _currentCapeRawBytes;
        _autoRotateEnabled = false;

        _rotAngleY = 205;
        _rotAngleX = 10;
        ApplySkin3DTransforms();
        SkinCamera.Position = new Point3D(0, 4, -50);
        SkinCamera.LookDirection = new Vector3D(0, -1, 5);

        ApplyCapePreview(_capeCarouselIndex);

        if (BtnWardrobe.Content is TextBlock wtb) wtb.Text = "✕";

        CapeModeOverlay.Visibility = Visibility.Visible;
        UpdateCapeCarouselUI();
    }

    private void UpdateCapeCarouselUI()
    {
        if (_capeCarouselIndex < 0 || _capeCarouselIndex >= _capeCarouselCapes.Count) return;
        var cape = _capeCarouselCapes[_capeCarouselIndex];
        if (cape.IsEquipped)
            TxtCapeConfirmLabel.Text = "Надет ✓";
        else if (cape.Id == "none")
            TxtCapeConfirmLabel.Text = "✕ Снять плащ";
        else
            TxtCapeConfirmLabel.Text = "✓ Выбрать";
    }

    private void BtnCapePrev_Click(object sender, RoutedEventArgs e)
    {
        if (_capeCarouselIndex > 0)
        {
            _capeCarouselIndex--;
            ApplyCapePreview(_capeCarouselIndex);
            UpdateCapeCarouselUI();
        }
    }

    private void BtnCapeNext_Click(object sender, RoutedEventArgs e)
    {
        if (_capeCarouselIndex < _capeCarouselCapes.Count - 1)
        {
            _capeCarouselIndex++;
            ApplyCapePreview(_capeCarouselIndex);
            UpdateCapeCarouselUI();
        }
    }
    private async void BtnCapeConfirm_Click(object sender, RoutedEventArgs e)
    {
        if (_capeCarouselIndex < 0 || _capeCarouselIndex >= _capeCarouselCapes.Count) return;
        var cape = _capeCarouselCapes[_capeCarouselIndex];
        if (cape.IsEquipped) { ExitCapeMode(); return; }

        var newCape = cape.RawTextureBytes;
        _currentCapeRawBytes = newCape;
        ExitCapeMode();

        try
        {
            var capePath = _offlineSkins.AccountCapePath(_account.Username);
            var skinPath = _offlineSkins.FindAccountSkin(_account.Username);
            var isSlim = _currentSkinModel.Equals("slim", StringComparison.OrdinalIgnoreCase);

            if (newCape != null && newCape.Length > 0)
            {
                var dir = System.IO.Path.GetDirectoryName(capePath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                lock (_skinFileLock) { File.WriteAllBytes(capePath, newCape); }

                foreach (var inst in _instances.Where(_offlineSkins.IsCslSupported))
                {
                    await _offlineSkins.EnsureCslModAsync(inst);
                    _offlineSkins.SyncToInstance(inst, _account.Username, skinPath, capePath, isSlim);
                }

                if (_account.IsVec)
                {
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            var srvUrl = await VecAuthService.GetActiveServerUrlAsync();
                            await _skins.UploadCapeToVecServerAsync(srvUrl, _account.Username, newCape);
                        }
                        catch (Exception ex) { Log.Warn($"VEC cape upload failed: {ex.Message}"); }
                    });
                }
                _toast.Show("Гардероб", "Плащ успешно надет!", NotificationType.Success);
            }
            else
            {
                if (System.IO.File.Exists(capePath)) System.IO.File.Delete(capePath);
                foreach (var inst in _instances.Where(_offlineSkins.IsCslSupported))
                    _offlineSkins.RemoveCapeFromInstance(inst, _account.Username);
                if (_account.IsVec)
                {
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            var srvUrl = await VecAuthService.GetActiveServerUrlAsync();
                            await _skins.ResetCapeOnVecServerAsync(srvUrl, _account.Username);
                        }
                        catch (Exception ex) { Log.Warn($"VEC cape reset failed: {ex.Message}"); }
                    });
                }
                _toast.Show("Гардероб", "Плащ снят.", NotificationType.Info);
            }
        }
        catch (Exception ex)
        {
            Log.Error("Cape change error", ex);
            _toast.Show("Ошибка", ex.Message, NotificationType.Error);
        }
    }

}
