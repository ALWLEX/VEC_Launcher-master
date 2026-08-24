namespace VECLauncher.Services;

/// <summary>
/// Centralized constants for magic strings used across the project.
/// </summary>
public static class Constants
{
    /// <summary>Skin model types.</summary>
    public static class SkinModel
    {
        public const string Classic = "classic";
        public const string Slim = "slim";
    }

    /// <summary>Instance subdirectory names.</summary>
    public static class InstanceFolders
    {
        public const string Mods = "mods";
        public const string ResourcePacks = "resourcepacks";
        public const string ShaderPacks = "shaderpacks";
        public const string Config = "config";
        public const string Saves = "saves";
        public const string Screenshots = "screenshots";

        /// <summary>All content folders that can be duplicated between instances.</summary>
        public static readonly string[] ContentFolders = { Mods, ResourcePacks, ShaderPacks, Config };
    }

    /// <summary>Version directory names.</summary>
    public static class VersionFolders
    {
        public const string Versions = "versions";
        public const string Libraries = "libraries";
        public const string Assets = "assets";
    }
}
