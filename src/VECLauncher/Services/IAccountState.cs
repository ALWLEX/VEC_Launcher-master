using System.Windows.Media;
using VECLauncher.Models;

namespace VECLauncher.Services;

/// <summary>
/// Read/write access to account state shared across VMs.
/// MainWindowViewModel implements this — child VMs depend on the interface, not the concrete class.
/// </summary>
public interface IAccountState
{
    /// <summary>Current logged-in account (null if not logged in).</summary>
    MinecraftAccount? Account { get; set; }

    /// <summary>Whether an account is logged in.</summary>
    bool IsAccountLoggedIn { get; }

    /// <summary>All game instances.</summary>
    List<GameInstance> Instances { get; }

    /// <summary>Raw skin texture bytes for the current account.</summary>
    byte[]? CurrentSkinRawBytes { get; set; }

    /// <summary>Raw cape texture bytes for the current account.</summary>
    byte[]? CurrentCapeRawBytes { get; set; }

    /// <summary>Skin model ("classic" or "slim").</summary>
    string CurrentSkinModel { get; set; }

    /// <summary>Whether the skin placeholder is visible (no skin loaded).</summary>
    bool SkinPlaceholderVisible { get; set; }

    /// <summary>Large avatar image for the account page.</summary>
    ImageSource? AvatarLarge { get; set; }

    /// <summary>Small avatar image for the sidebar.</summary>
    ImageSource? Avatar { get; set; }

    /// <summary>Sets the active account and updates all related state.</summary>
    void SetAccount(MinecraftAccount acc, bool refreshSkin);

    /// <summary>Clears all account state (logout).</summary>
    void ClearAccount();

    /// <summary>Appends a line to the log buffer.</summary>
    void AppendLog(string line);

    /// <summary>Updates the progress stage text in the UI.</summary>
    void SetStage(string stage);
}
