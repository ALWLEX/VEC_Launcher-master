namespace VECLauncher.Services;

public sealed class SkinInfo
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Url { get; set; } = "";
    public string FilePath { get; set; } = "";
    public string? PreviewUrl { get; set; }
    public string? Source { get; set; }
    public bool Slim { get; set; }
}
