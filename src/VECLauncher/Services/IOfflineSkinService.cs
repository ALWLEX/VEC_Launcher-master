using VECLauncher.Models;

namespace VECLauncher.Services;

/// <summary>
/// Abstracts offline skin operations. Default implementation wraps existing OfflineSkinService.
/// </summary>
public interface IOfflineSkinService
{
    string AccountSkinPath(string username);
    string AccountCapePath(string username);
    string? FindAccountSkin(string username);
    string? FindAccountCape(string username);
    bool IsCslSupported(GameInstance inst);
    bool IsCslInstalled(GameInstance inst);
    void SyncToInstance(GameInstance inst, string username, string? skinFile, string? capeFile = null, bool isSlim = false);
    void RemoveFromInstance(GameInstance inst, string username);
    void RemoveCapeFromInstance(GameInstance inst, string username);
    void ClearCslCache(string instanceDir);
    Task<bool> EnsureCslModAsync(GameInstance inst, CancellationToken ct = default);
}

/// <summary>
/// Default implementation wrapping the existing static OfflineSkinService.
/// </summary>
public sealed class OfflineSkinServiceAdapter : IOfflineSkinService
{
    public string AccountSkinPath(string username) => OfflineSkinService.AccountSkinPath(username);
    public string AccountCapePath(string username) => OfflineSkinService.AccountCapePath(username);
    public string? FindAccountSkin(string username) => OfflineSkinService.FindAccountSkin(username);
    public string? FindAccountCape(string username) => OfflineSkinService.FindAccountCape(username);
    public bool IsCslSupported(GameInstance inst) => OfflineSkinService.IsCslSupported(inst);
    public bool IsCslInstalled(GameInstance inst) => OfflineSkinService.IsCslInstalled(inst);
    public void SyncToInstance(GameInstance inst, string username, string? skinFile, string? capeFile = null, bool isSlim = false)
        => OfflineSkinService.SyncToInstance(inst, username, skinFile, capeFile, isSlim);
    public void RemoveFromInstance(GameInstance inst, string username) => OfflineSkinService.RemoveFromInstance(inst, username);
    public void RemoveCapeFromInstance(GameInstance inst, string username) => OfflineSkinService.RemoveCapeFromInstance(inst, username);
    public void ClearCslCache(string instanceDir) => OfflineSkinService.ClearCslCache(instanceDir);
    public Task<bool> EnsureCslModAsync(GameInstance inst, CancellationToken ct = default) => OfflineSkinService.EnsureCslModAsync(inst, ct);
}
