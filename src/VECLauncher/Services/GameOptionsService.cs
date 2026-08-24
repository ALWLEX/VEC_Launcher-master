using System.Text;

namespace VECLauncher.Services;

public static class GameOptionsService
{
    public static string LanguageCodeFor(string mcVersion, string lang = "ru")
    {
        var v = VersionService.ParseMcVersion(mcVersion);
        var old = v is not null && v < new Version(1, 11, 0);

        return lang switch
        {
            "ru" => old ? "ru_RU" : "ru_ru",
            "uk" => old ? "uk_UA" : "uk_ua",
            "en" => old ? "en_US" : "en_us",
            _ => old ? "ru_RU" : "ru_ru"
        };
    }

    public static bool EnsureLanguage(string gameDir, string mcVersion, string lang = "ru")
    {
        try
        {
            Directory.CreateDirectory(gameDir);
            var path = Path.Combine(gameDir, "options.txt");
            var code = LanguageCodeFor(mcVersion, lang);

            if (File.Exists(path))
            {
                return false;
            }

            var sb = new StringBuilder();
            sb.AppendLine($"lang:{code}");
            sb.AppendLine("skipMultiplayerWarning:true");
            sb.AppendLine("tutorialStep:none");

            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
            Log.Info($"GameOptionsService: created options.txt with language {code} for {mcVersion}");
            return true;
        }
        catch (Exception ex)
        {
            Log.Warn($"GameOptionsService: failed to write options.txt: {ex.Message}");
            return false;
        }
    }

    public static bool SetLanguage(string gameDir, string mcVersion, string lang = "ru")
    {
        try
        {
            var path = Path.Combine(gameDir, "options.txt");
            var code = LanguageCodeFor(mcVersion, lang);

            if (!File.Exists(path)) return EnsureLanguage(gameDir, mcVersion, lang);

            var lines = File.ReadAllLines(path).ToList();
            var found = false;

            for (var i = 0; i < lines.Count; i++)
            {
                if (!lines[i].StartsWith("lang:", StringComparison.OrdinalIgnoreCase)) continue;
                lines[i] = $"lang:{code}";
                found = true;
                break;
            }

            if (!found) lines.Insert(0, $"lang:{code}");

            File.WriteAllLines(path, lines, new UTF8Encoding(false));
            Log.Info($"GameOptionsService: language updated to {code}");
            return true;
        }
        catch (Exception ex)
        {
            Log.Warn($"GameOptionsService: failed to update language: {ex.Message}");
            return false;
        }
    }

    public static string? GetLanguage(string gameDir)
    {
        try
        {
            var path = Path.Combine(gameDir, "options.txt");
            if (!File.Exists(path)) return null;

            foreach (var line in File.ReadLines(path))
            {
                if (line.StartsWith("lang:", StringComparison.OrdinalIgnoreCase))
                    return line[5..].Trim();
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"GameOptionsService: failed to read language: {ex.Message}");
        }

        return null;
    }
}