using System.Diagnostics;
using System.Text;
using System.Text.Json;
using VECLauncher.Models;

namespace VECLauncher.Services;

public sealed class LaunchOptions
{
    public required MinecraftAccount Account { get; init; }
    public required DownloadManager.InstallResult Install { get; init; }
    public required JavaInstallation Java { get; init; }

    public string GameDir { get; init; } = LauncherPaths.Root;
    public int MinMemoryMb { get; init; } = 1024;
    public int MaxMemoryMb { get; init; } = 4096;
    public int WindowWidth { get; init; } = 1280;
    public int WindowHeight { get; init; } = 720;
    public bool Fullscreen { get; init; }
    public string? ServerAddress { get; init; }
    public string ExtraJvmArgs { get; init; } = "";
    public bool ShowConsole { get; init; }
    public bool CloseLauncherOnStart { get; init; }
    public bool UseAuthlibInjector { get; init; }
    public string? AuthlibServerUrl { get; init; }
}

public sealed class GameLauncher
{
    public const string LauncherName = "VEC Launcher";
    public const string LauncherVersion = "1.0.0";

    public event Action<string>? GameOutput;
    public event Action<int>? GameExited;

    public Process Launch(LaunchOptions o)
    {
        Directory.CreateDirectory(o.GameDir);

        var detail = o.Install.Detail;
        var args = BuildArguments(o);

        var psi = new ProcessStartInfo
        {
            FileName = o.ShowConsole ? o.Java.JavaConsoleExe : o.Java.JavaExe,
            WorkingDirectory = o.GameDir,
            UseShellExecute = false,
            CreateNoWindow = !o.ShowConsole,
            RedirectStandardOutput = !o.ShowConsole,
            RedirectStandardError = !o.ShowConsole,
            StandardOutputEncoding = o.ShowConsole ? null : Encoding.UTF8,
            StandardErrorEncoding = o.ShowConsole ? null : Encoding.UTF8
        };

        foreach (var a in args) psi.ArgumentList.Add(a);

        psi.Environment.Remove("JAVA_TOOL_OPTIONS");
        psi.Environment["APPDATA"] = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

        Log.Info($"GameLauncher: launching {detail.Id} via {psi.FileName}");
        Log.Info($"GameLauncher: arguments: {string.Join(' ', args.Select(Quote))}");

        var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };

        if (!o.ShowConsole)
        {
            proc.OutputDataReceived += (_, e) => { if (e.Data is not null) GameOutput?.Invoke(e.Data); };
            proc.ErrorDataReceived += (_, e) => { if (e.Data is not null) GameOutput?.Invoke(e.Data); };
        }

        proc.Exited += (_, _) =>
        {
            try { GameExited?.Invoke(proc.ExitCode); } catch (Exception ex) { Log.Warn(ex.Message); }
        };

        if (!proc.Start())
        {
            Log.Error($"GameLauncher: failed to start Java process for {detail.Id}");
            throw new InvalidOperationException("Failed to start Java process.");
        }

        Log.Info($"GameLauncher: process started (PID: {proc.Id})");

        if (!o.ShowConsole)
        {
            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();
        }

        return proc;
    }

    public List<string> BuildArguments(LaunchOptions o)
    {
        var detail = o.Install.Detail;
        var result = new List<string>();

        var classpath = string.Join(Path.PathSeparator.ToString(), o.Install.ClasspathJars);

        var serverAddr = o.ServerAddress?.Trim();
        var serverHost = serverAddr;
        var serverPort = "25565";
        if (!string.IsNullOrWhiteSpace(serverAddr))
        {
            var idx = serverAddr.LastIndexOf(':');
            if (idx > 0 && idx < serverAddr.Length - 1 && int.TryParse(serverAddr[(idx + 1)..], out _))
            {
                serverPort = serverAddr[(idx + 1)..];
                serverHost = serverAddr[..idx];
            }
        }
        var fullServer = string.IsNullOrWhiteSpace(serverAddr) ? "" : $"{serverHost}:{serverPort}";

        var vars = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["auth_player_name"] = o.Account.Username,
            ["version_name"] = detail.Id,
            ["game_directory"] = o.GameDir,
            ["assets_root"] = LauncherPaths.AssetsDir,
            ["game_assets"] = o.Install.AssetsDir,
            ["assets_index_name"] = o.Install.AssetIndexId,
            ["auth_uuid"] = o.Account.Uuid.Replace("-", ""),
            ["auth_access_token"] = o.Account.AccessToken,
            ["auth_session"] = "token:" + o.Account.AccessToken + ":" + o.Account.Uuid.Replace("-", ""),
            ["auth_xuid"] = o.Account.Xuid ?? "",
            ["clientid"] = MakeClientId(),
            ["user_type"] = o.Account.UserType,
            ["version_type"] = detail.Type,
            ["user_properties"] = "{}",
            ["natives_directory"] = o.Install.NativesDir,
            ["launcher_name"] = LauncherName,
            ["launcher_version"] = LauncherVersion,
            ["classpath"] = classpath,
            ["classpath_separator"] = Path.PathSeparator.ToString(),
            ["library_directory"] = LauncherPaths.LibrariesDir,
            ["resolution_width"] = o.WindowWidth.ToString(),
            ["resolution_height"] = o.WindowHeight.ToString(),
            ["quickPlayMultiplayer"] = fullServer,
            ["quick_play_multiplayer"] = fullServer,
            ["quickPlayPath"] = Path.Combine(o.GameDir, "quickPlay", "log.json"),
            ["quickPlaySingleplayer"] = "",
            ["quickPlayRealms"] = "",
            ["server"] = serverHost ?? "",
            ["port"] = serverPort
        };

        var features = new Dictionary<string, bool>
        {
            ["is_demo_user"] = false,
            ["has_custom_resolution"] = !o.Fullscreen,
            ["has_quick_plays_support"] = false,
            ["is_quick_play_singleplayer"] = false,
            ["is_quick_play_multiplayer"] = !string.IsNullOrWhiteSpace(o.ServerAddress),
            ["is_quick_play_realms"] = false
        };

        var jvmFromJson = new List<string>();

        if (detail.Arguments is { } argsEl &&
            argsEl.ValueKind == JsonValueKind.Object &&
            argsEl.TryGetProperty("jvm", out var jvmEl))
        {
            CollectArguments(jvmEl, features, jvmFromJson);
        }
        else
        {
            jvmFromJson.AddRange(new[]
            {
                "-Djava.library.path=${natives_directory}",
                "-cp", "${classpath}"
            });
        }

        result.Add($"-Xms{o.MinMemoryMb}M");
        result.Add($"-Xmx{o.MaxMemoryMb}M");
        result.Add("-XX:+UnlockExperimentalVMOptions");
        result.Add("-XX:+UseG1GC");
        result.Add("-XX:G1NewSizePercent=20");
        result.Add("-XX:G1ReservePercent=20");
        result.Add("-XX:MaxGCPauseMillis=50");
        result.Add("-XX:G1HeapRegionSize=32M");
        result.Add("-Dfile.encoding=UTF-8");
        result.Add("-Dstdout.encoding=UTF-8");
        result.Add("-Dstderr.encoding=UTF-8");
        result.Add("-Djava.rmi.server.useCodebaseOnly=true");
        result.Add("-Dcom.sun.jndi.rmi.object.trustURLCodebase=false");
        result.Add("-Dcom.sun.jndi.cosnaming.object.trustURLCodebase=false");
        result.Add("-Dlog4j2.formatMsgNoLookups=true");
        result.Add($"-Dminecraft.launcher.brand={LauncherName}");
        result.Add($"-Dminecraft.launcher.version={LauncherVersion}");

        if (OperatingSystem.IsWindows())
            result.Add("-XX:HeapDumpPath=MojangTricksIntelDriversForPerformance_javaw.exe_minecraft.exe.heapdump");

        var logCfgId = detail.Logging?.Client?.File?.Id;
        var logArgTemplate = detail.Logging?.Client?.Argument;
        if (!string.IsNullOrEmpty(logCfgId) && !string.IsNullOrEmpty(logArgTemplate))
        {
            var cfgPath = Path.Combine(LauncherPaths.LogConfigsDir, logCfgId!);
            if (File.Exists(cfgPath))
                result.Add(logArgTemplate!.Replace("${path}", cfgPath));
        }

        foreach (var raw in jvmFromJson)
            result.Add(Substitute(raw, vars));

        if (!result.Contains("-cp") && !result.Contains("-classpath"))
        {
            result.Add("-cp");
            result.Add(classpath);
        }
        if (!result.Any(a => a.StartsWith("-Djava.library.path=", StringComparison.Ordinal)))
            result.Add("-Djava.library.path=" + o.Install.NativesExtractDir);

        foreach (var extra in SplitArgs(o.ExtraJvmArgs))
            result.Add(extra);

        if (o.UseAuthlibInjector && !string.IsNullOrEmpty(o.AuthlibServerUrl) && AuthlibInjectorService.IsInstalled)
        {
            result.Add(AuthlibInjectorService.BuildJvmArg(AuthlibInjectorService.JarPath, o.AuthlibServerUrl));
            result.Add("-Dauthlibinjector.side=client");
            Log.Info($"GameLauncher: authlib-injector enabled for {o.AuthlibServerUrl}");
        }

        result.Add(detail.MainClass);

        var gameArgs = new List<string>();

        if (detail.Arguments is { } argsEl2 &&
            argsEl2.ValueKind == JsonValueKind.Object &&
            argsEl2.TryGetProperty("game", out var gameEl))
        {
            CollectArguments(gameEl, features, gameArgs);
        }
        else if (!string.IsNullOrEmpty(detail.MinecraftArguments))
        {
            gameArgs.AddRange(SplitArgs(detail.MinecraftArguments!));
        }
        else
        {
            gameArgs.AddRange(new[]
            {
                "--username", "${auth_player_name}",
                "--version", "${version_name}",
                "--gameDir", "${game_directory}",
                "--assetsDir", "${assets_root}",
                "--assetIndex", "${assets_index_name}",
                "--uuid", "${auth_uuid}",
                "--accessToken", "${auth_access_token}",
                "--userType", "${user_type}",
                "--versionType", "${version_type}"
            });
        }

        foreach (var raw in gameArgs)
            result.Add(Substitute(raw, vars));

        if (o.Fullscreen)
        {
            if (!result.Contains("--fullscreen")) result.Add("--fullscreen");
        }
        else if (!result.Contains("--width"))
        {
            result.Add("--width"); result.Add(o.WindowWidth.ToString());
            result.Add("--height"); result.Add(o.WindowHeight.ToString());
        }

        if (!string.IsNullOrWhiteSpace(o.ServerAddress))
        {
            var mcVer = VersionService.ParseMcVersion(detail.InheritsFrom ?? detail.Id);
            if (mcVer is not null && mcVer >= new Version(1, 20, 0))
            {
                if (!result.Contains("--quickPlayMultiplayer"))
                {
                    result.Add("--quickPlayMultiplayer");
                    result.Add(fullServer);
                }
            }
            else
            {
                if (!result.Contains("--server"))
                {
                    result.Add("--server");
                    result.Add(serverHost!);
                    result.Add("--port");
                    result.Add(serverPort);
                }
            }
        }

        return result;
    }

    private static void CollectArguments(JsonElement element, Dictionary<string, bool> features, List<string> output)
    {
        if (element.ValueKind != JsonValueKind.Array) return;

        foreach (var item in element.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String)
            {
                var s = item.GetString();
                if (!string.IsNullOrEmpty(s)) output.Add(s!);
                continue;
            }

            if (item.ValueKind != JsonValueKind.Object) continue;

            List<Rule>? rules = null;
            if (item.TryGetProperty("rules", out var rulesEl))
            {
                try { rules = JsonSerializer.Deserialize<List<Rule>>(rulesEl.GetRawText()); }
                catch { rules = null; }
            }

            if (!RuleEvaluator.Allows(rules, features)) continue;

            if (!item.TryGetProperty("value", out var valueEl)) continue;

            if (valueEl.ValueKind == JsonValueKind.String)
            {
                var s = valueEl.GetString();
                if (!string.IsNullOrEmpty(s)) output.Add(s!);
            }
            else if (valueEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var v in valueEl.EnumerateArray())
                {
                    var s = v.GetString();
                    if (!string.IsNullOrEmpty(s)) output.Add(s!);
                }
            }
        }
    }

    private static string Substitute(string input, Dictionary<string, string> vars)
    {
        if (!input.Contains("${", StringComparison.Ordinal)) return input;

        var sb = new StringBuilder(input);
        foreach (var (key, value) in vars)
            sb.Replace("${" + key + "}", value);

        return sb.ToString();
    }

    public static IEnumerable<string> SplitArgs(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) yield break;

        var sb = new StringBuilder();
        var inQuotes = false;

        foreach (var c in input)
        {
            if (c == '"') { inQuotes = !inQuotes; continue; }
            if (char.IsWhiteSpace(c) && !inQuotes)
            {
                if (sb.Length > 0) { yield return sb.ToString(); sb.Clear(); }
                continue;
            }
            sb.Append(c);
        }

        if (sb.Length > 0) yield return sb.ToString();
    }

    private static string Quote(string s) => s.Contains(' ') ? "\"" + s + "\"" : s;

    private static string MakeClientId()
    {
        var raw = Environment.MachineName + "|" + Environment.UserName + "|" + LauncherName;
        var bytes = System.Security.Cryptography.MD5.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToBase64String(bytes).TrimEnd('=');
    }
}