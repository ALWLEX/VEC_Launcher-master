using VECLauncher.Models;

namespace VECLauncher.Services;

/// <summary>
/// Abstracts account data access. Default implementation wraps existing AccountStorage.
/// Swap for SQLite, HTTP API, or mock in tests.
/// </summary>
public interface IAccountRepository
{
    /// <summary>Loads the active session account (or null if not logged in).</summary>
    MinecraftAccount? GetActive();

    /// <summary>Returns all saved accounts.</summary>
    IReadOnlyList<MinecraftAccount> GetAllSaved();

    /// <summary>Saves an account (updates if exists, inserts if new).</summary>
    void Save(MinecraftAccount account);

    /// <summary>Removes an account from the saved list by username and type.</summary>
    void Remove(string username, AccountType type);

    /// <summary>Clears the active session (does not remove from saved list).</summary>
    void ClearActiveSession();
}

/// <summary>
/// Default implementation wrapping the existing static <see cref="AccountStorage"/>.
/// </summary>
public sealed class AccountRepository : IAccountRepository
{
    public MinecraftAccount? GetActive() => AccountStorage.Load();
    public IReadOnlyList<MinecraftAccount> GetAllSaved() => AccountStorage.GetAllSaved();
    public void Save(MinecraftAccount account) => AccountStorage.Save(account);
    public void Remove(string username, AccountType type) => AccountStorage.RemoveSaved(username, type);
    public void ClearActiveSession() => AccountStorage.Clear();
}
