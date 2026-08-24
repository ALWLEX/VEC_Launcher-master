namespace VECLauncher.Services;

/// <summary>
/// Abstracts maintenance/cleanup operations. Default implementation wraps existing MaintenanceService.
/// </summary>
public interface IMaintenanceService
{
    List<MaintenanceService.TargetInfo> Enumerate();
    long TotalSize();
    long Clean(IEnumerable<MaintenanceService.TargetInfo> targets);
    string PrepareUninstall(bool removeExe);
    void RunUninstall(string script);
}

/// <summary>
/// Default implementation wrapping the existing static MaintenanceService.
/// </summary>
public sealed class MaintenanceServiceAdapter : IMaintenanceService
{
    public List<MaintenanceService.TargetInfo> Enumerate() => MaintenanceService.Enumerate();
    public long TotalSize() => MaintenanceService.TotalSize();
    public long Clean(IEnumerable<MaintenanceService.TargetInfo> targets) => MaintenanceService.Clean(targets);
    public string PrepareUninstall(bool removeExe) => MaintenanceService.PrepareUninstall(removeExe);
    public void RunUninstall(string script) => MaintenanceService.RunUninstall(script);
}
