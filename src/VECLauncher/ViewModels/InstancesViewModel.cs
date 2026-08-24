using System.Diagnostics;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VECLauncher.Models;
using VECLauncher.Services;

namespace VECLauncher.ViewModels;

/// <summary>
/// Manages game instance CRUD operations, gallery/config view toggle,
/// instance creation, deletion, and settings reset.
/// </summary>
public partial class InstancesViewModel : ObservableObject
{
    private readonly IAccountState _state;
    private readonly EventAggregator _events;

    /// <summary>Raised when instance list needs UI refresh (e.g. after account change).</summary>
    public event Action? InstancesChanged;

    public InstancesViewModel(IAccountState state, EventAggregator events)
    {
        _state = state;
        _events = events;

        _events.Subscribe<AccountChangedEvent>(_ =>
        {
            // Account changed → refresh instance list
            InstancesChanged?.Invoke();
        });
    }

    // ── Instance List ──
    public List<GameInstance> LoadAll()
    {
        return InstanceService.LoadAll();
    }

    public void SaveAll(List<GameInstance> instances)
    {
        InstanceService.SaveAll(instances);
    }

    public List<GameInstance> ScanOrphans(List<GameInstance> instances)
    {
        return InstanceService.ScanOrphans(instances);
    }

    // ── Instance CRUD ──
    public void CreateInstance(GameInstance inst)
    {
        InstanceService.EnsureFolders(inst);
    }

    public void DeleteInstance(GameInstance inst, bool deleteFiles)
    {
        InstanceService.Delete(inst, deleteFiles);
    }

    // ── Instance Info ──
    public InstanceService.FolderStats GetStats(GameInstance inst)
    {
        return InstanceService.GetStats(inst);
    }

    public List<FileInfo> GetScreenshots(GameInstance inst, int maxCount = 12)
    {
        return InstanceService.GetScreenshots(inst, maxCount);
    }

    // ── Duplicate Instance ──
    public GameInstance DuplicateInstance(GameInstance src, bool copyContent)
    {
        var copy = new GameInstance
        {
            Name = src.Name + " (копия)",
            McVersion = src.McVersion,
            Loader = src.Loader,
            LoaderVersion = src.LoaderVersion,
            LaunchVersionId = src.LaunchVersionId,
            MaxMemoryMb = src.MaxMemoryMb,
            WindowWidth = src.WindowWidth,
            WindowHeight = src.WindowHeight,
            ServerAddress = src.ServerAddress,
            ExtraJvmArgs = src.ExtraJvmArgs,
            JavaPath = src.JavaPath,
            IconColor = src.IconColor,
            Isolated = src.Isolated
        };

        InstanceService.EnsureFolders(copy);

        if (copyContent)
        {
            try
            {
                foreach (var sub in Constants.InstanceFolders.ContentFolders)
                {
                    var from = Path.Combine(InstanceService.InstanceDir(src), sub);
                    if (Directory.Exists(from))
                        CopyDirectory(from, Path.Combine(InstanceService.InstanceDir(copy), sub));
                }
            }
            catch (Exception ex)
            {
                _state.AppendLog("Ошибка копирования содержимого: " + ex.Message);
            }
        }

        return copy;
    }

    // ── Reset Instance Settings ──
    public void ResetInstanceSettings(GameInstance inst)
    {
        inst.MaxMemoryMb = 0;
        inst.WindowWidth = 0;
        inst.WindowHeight = 0;
        inst.ServerAddress = "";
        inst.ExtraJvmArgs = "";
        inst.JavaPath = "";
    }

    // ── Mod Profiles ──
    public List<string> GetModProfiles(GameInstance inst)
    {
        return ModProfileService.List(inst);
    }

    public int CountMods(GameInstance inst, string profile)
    {
        return ModProfileService.CountMods(inst, profile);
    }

    // ── JVM Presets ──
    public (string Name, string Description)[] GetJvmPresets()
    {
        return JvmPresetService.Presets.Select(p => (p.Name, p.Description)).ToArray();
    }

    // ── Helpers ──
    public static string Plural(int n, string one, string few, string many)
    {
        var mod10 = n % 10;
        var mod100 = n % 100;
        var word = (mod10 == 1 && mod100 != 11) ? one
            : (mod10 >= 2 && mod10 <= 4 && (mod100 < 12 || mod100 > 14)) ? few
            : many;
        return $"{n} {word}";
    }

    private static void CopyDirectory(string src, string dst)
    {
        Directory.CreateDirectory(dst);
        foreach (var file in Directory.GetFiles(src))
            File.Copy(file, Path.Combine(dst, Path.GetFileName(file)), true);
        foreach (var dir in Directory.GetDirectories(src))
            CopyDirectory(dir, Path.Combine(dst, Path.GetFileName(dir)));
    }
}
