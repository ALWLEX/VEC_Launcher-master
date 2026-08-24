using VECLauncher.Models;

namespace VECLauncher.Services;

/// <summary>
/// Abstracts instance data access. Default implementation wraps existing InstanceService.
/// </summary>
public interface IInstanceRepository
{
    /// <summary>Returns all game instances.</summary>
    IReadOnlyList<GameInstance> GetAll();

    /// <summary>Saves all instances to disk.</summary>
    void SaveAll(IReadOnlyList<GameInstance> instances);

    /// <summary>Ensures required folders exist for an instance.</summary>
    void EnsureFolders(GameInstance instance);

    /// <summary>Returns the instance directory path.</summary>
    string GetInstanceDir(GameInstance instance);
}

/// <summary>
/// Default implementation wrapping the existing static <see cref="InstanceService"/>.
/// </summary>
public sealed class InstanceRepository : IInstanceRepository
{
    public IReadOnlyList<GameInstance> GetAll() => InstanceService.LoadAll();
    public void SaveAll(IReadOnlyList<GameInstance> instances) => InstanceService.SaveAll(instances);
    public void EnsureFolders(GameInstance instance) => InstanceService.EnsureFolders(instance);
    public string GetInstanceDir(GameInstance instance) => InstanceService.InstanceDir(instance);
}
