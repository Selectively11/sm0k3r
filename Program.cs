using System.Diagnostics;
using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace Sm0k3r;

enum SteamToolsStatus { UpToDate, Outdated, NotInstalled, Unknown }

class Program
{
    const string FallbackVersion = "1773426488";
    const int FileServerPort = 1666;
    const string FallbackManifestUrl = "https://raw.githubusercontent.com/SteamDatabase/SteamTracking/master/ClientManifest/steam_client_win64";
    const string ManifestFileName = "steam_client_win64";

    static RemoteConfig? _remoteConfig;
    static readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(5) };

    static string TargetVersion => _remoteConfig?.Version ?? FallbackVersion;
    static string TargetManifestUrl => Updater.ManifestAssetUrl ?? FallbackManifestUrl;

    // SteamTools
    const string SteamToolsDllUrl = "http://update.aaasn.com/update";
    const string SteamToolsDwmapiUrl = "http://update.aaasn.com/dwmapi";
    const string SteamToolsRegPath = @"Software\Valve\Steamtools";

    static async Task<int> Main(string[] args)
    {
        ClearScreen();
        Console.WriteLine($"=== sm0k3r v{Updater.CurrentVersion} ===");
        Console.WriteLine();

        await Updater.CheckAndApply();

        _remoteConfig = await Updater.FetchRemoteConfig();
        if (_remoteConfig != null)
            Console.WriteLine($"Remote config loaded (target version: {TargetVersion})");
        else
            Console.WriteLine($"Using built-in config (target version: {TargetVersion})");

        string? steamPath = GetSteamPath();
        if (steamPath == null)
        {
            Console.Error.WriteLine("ERROR: Could not find Steam installation path in registry.");
            Console.Error.WriteLine("Looked in: HKCU\\Software\\Valve\\Steam -> SteamPath");
            return 1;
        }
        steamPath = steamPath.Replace('/', '\\');
        Console.WriteLine($"Steam install path: {steamPath}");

        if (!Directory.Exists(steamPath))
        {
            Console.Error.WriteLine($"ERROR: Steam directory does not exist: {steamPath}");
            return 1;
        }

        string? currentVersion = GetSteamVersion(steamPath);
        long curVer = 0, tgtVer = 0;
        bool tooNew = currentVersion != null
            && long.TryParse(currentVersion, out curVer)
            && long.TryParse(TargetVersion, out tgtVer)
            && curVer > tgtVer;

        if (currentVersion != null)
        {
            Console.WriteLine($"Current Steam client version: {currentVersion}");
            if (currentVersion == TargetVersion)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("Steam is up to date.");
                Console.ResetColor();
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                if (tooNew)
                {
                    Console.WriteLine($"** Your Steam client is newer than the latest supported version ({TargetVersion}). **");
                    Console.WriteLine("** Select 'Run everything' or 'Downgrade Steam' to roll back. **");
                }
                else
                {
                    Console.WriteLine($"** Your Steam client is out of date ({TargetVersion} is latest compatible) **");
                    Console.WriteLine("** Select 'Run everything' to update **");
                }
                Console.ResetColor();
            }
        }
        else
            Console.WriteLine("Current Steam client version: unknown");

        bool steamCurrent = currentVersion == TargetVersion;

        var stStatus = CheckSteamToolsStatus(steamPath);
        switch (stStatus)
        {
            case SteamToolsStatus.UpToDate:
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("SteamTools is up to date");
                Console.ResetColor();
                break;
            case SteamToolsStatus.Outdated:
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("** SteamTools is outdated. **");
                Console.ResetColor();
                break;
            case SteamToolsStatus.NotInstalled:
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("** SteamTools is not installed. **");
                Console.ResetColor();
                break;
            default:
                Console.WriteLine("SteamTools status: unknown (no remote hashes to compare).");
                break;
        }

        bool stCurrent = stStatus == SteamToolsStatus.UpToDate;

        if (stStatus != SteamToolsStatus.NotInstalled && stStatus != SteamToolsStatus.Unknown)
        {
            if (tooNew)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("SteamTools is not compatible with this version of Steam!");
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("SteamTools is compatible with this version of Steam!");
            }
            Console.ResetColor();
        }

        string cfgPath = Path.Combine(steamPath, "steam.cfg");
        bool updatesBlocked = false;
        try
        {
            if (File.Exists(cfgPath))
            {
                string cfg = File.ReadAllText(cfgPath);
                updatesBlocked = cfg.Contains("BootStrapperInhibitAll=enable", StringComparison.OrdinalIgnoreCase)
                    && cfg.Contains("BootStrapperForceSelfUpdate=disable", StringComparison.OrdinalIgnoreCase);
            }
        }
        catch { }

        if (updatesBlocked)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("Steam updates are blocked");
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("** Steam updates are NOT blocked. **");
        }
        Console.ResetColor();

        if (!tooNew && !steamCurrent && stStatus != SteamToolsStatus.NotInstalled && stStatus != SteamToolsStatus.Unknown)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("SteamTools is compatible with your Steam version, but your version is out of date. Recommend updating.");
            Console.ResetColor();
        }

        Console.WriteLine();

        while (true)
        {
            // Recompute state so labels reflect changes from previous operations
            currentVersion = GetSteamVersion(steamPath);
            tooNew = currentVersion != null
                && long.TryParse(currentVersion, out curVer)
                && long.TryParse(TargetVersion, out tgtVer)
                && curVer > tgtVer;
            steamCurrent = currentVersion == TargetVersion;
            stStatus = CheckSteamToolsStatus(steamPath);
            stCurrent = stStatus == SteamToolsStatus.UpToDate;

            string option1Label;
            if (steamCurrent && stCurrent)
                option1Label = "Verify installation";
            else if (steamCurrent)
                option1Label = "Run everything (install SteamTools)";
            else if (stCurrent)
                option1Label = tooNew
                    ? "Run everything (downgrade Steam)"
                    : "Run everything (update Steam)";
            else
                option1Label = tooNew
                    ? "Run everything (downgrade + install SteamTools)"
                    : "Run everything (update + install SteamTools)";

            string option2Label;
            if (steamCurrent)
                option2Label = "Reinstall Steam at current version";
            else if (tooNew)
                option2Label = $"Downgrade Steam to {TargetVersion}";
            else
                option2Label = $"Update Steam to {TargetVersion}";

            Console.WriteLine("Select an option:");
            Console.WriteLine($"  1) {option1Label}");
            Console.WriteLine($"  2) {option2Label}");
            Console.WriteLine("  3) Install SteamTools");
            Console.WriteLine("  4) Pick a specific Steam version (advanced)");
            Console.WriteLine("  0) Exit");
            Console.WriteLine();
            Console.Write("> ");

            string? choice = Console.ReadLine()?.Trim();
            Console.WriteLine();

            switch (choice)
            {
                case "1":
                    ClearScreen();
                    await RunEverything(steamPath);
                    WaitForKey();
                    ClearScreen();
                    break;
                case "2":
                    ClearScreen();
                    await ApplyTargetVersion(steamPath);
                    WaitForKey();
                    ClearScreen();
                    break;
                case "3":
                    ClearScreen();
                    await InstallSteamTools(steamPath);
                    WaitForKey();
                    ClearScreen();
                    break;
                case "4":
                    ClearScreen();
                    await PickAndApplyVersion(steamPath);
                    WaitForKey();
                    ClearScreen();
                    break;
                case "0":
                    return 0;
                default:
                    Console.WriteLine("Invalid option. Please enter 0-4.");
                    Console.WriteLine();
                    break;
            }
        }
    }

    static async Task RunEverything(string steamPath)
    {
        string? currentVersion = GetSteamVersion(steamPath);
        bool needsVersionChange = currentVersion != TargetVersion;
        var stStatus = CheckSteamToolsStatus(steamPath);
        bool needsSteamTools = stStatus != SteamToolsStatus.UpToDate;

        if (!needsVersionChange && !needsSteamTools)
        {
            Console.WriteLine("=== Verify Installation ===");
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"Steam client version: {currentVersion} (target: {TargetVersion})");
            Console.WriteLine("SteamTools DLLs match expected hashes.");
            Console.ResetColor();
            Console.WriteLine();
            Console.WriteLine("Everything is up to date. Nothing to do.");
            Console.WriteLine();
            return;
        }

        Console.WriteLine("=== Run Everything ===");
        Console.WriteLine();
        Console.WriteLine("This will:");
        if (needsVersionChange)
        {
            bool isDown = currentVersion != null
                && long.TryParse(currentVersion, out var c)
                && long.TryParse(TargetVersion, out var t)
                && c > t;
            string verb = isDown ? "downgrade" : "update";

            Console.WriteLine("  - Quit Steam");
            Console.WriteLine($"  - Delete the package/ directory and download the target Steam version");
            Console.WriteLine($"  - Launch Steam in update mode to apply the {verb}");
            Console.WriteLine("  - Write steam.cfg to block future updates");
            Console.WriteLine("  - Delete the appcache/ directory to prevent automatic, unintentional update");
        }
        if (needsSteamTools)
        {
            Console.WriteLine("  - Install SteamTools DLLs without installing the unnecessary app itself");
            Console.WriteLine("  - Launch Steam");
        }
        Console.WriteLine();
        Console.WriteLine("You will NOT lose any installed games or game saves. Everything will be preserved, don't worry.");
        Console.WriteLine();
        Console.Write("Proceed? [Y/n] ");

        string? input = Console.ReadLine();
        if (input != null && input.Trim().Equals("n", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine("Aborted.");
            Console.WriteLine();
            return;
        }
        Console.WriteLine();

        if (needsVersionChange)
        {
            bool versionOk = await ApplyTargetVersion(steamPath, skipPrompt: true);
            if (!versionOk)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("Version apply failed — skipping SteamTools install to avoid incompatible state.");
                Console.ResetColor();
                Console.WriteLine();
                return;
            }
        }
        if (needsSteamTools)
            await InstallSteamTools(steamPath, skipPrompt: true);
    }

    static async Task<bool> ApplyTargetVersion(string steamPath, bool skipPrompt = false,
        string? overrideVersion = null, string? overrideSourcesUrl = null, string? overrideManifestUrl = null,
        bool skipSteamToolsInstall = false)
    {
        string version = overrideVersion ?? TargetVersion;
        string? currentVersion = GetSteamVersion(steamPath);

        bool isDowngrade = currentVersion != null
            && long.TryParse(currentVersion, out var cur)
            && long.TryParse(version, out var tgt)
            && cur > tgt;

        string action = currentVersion == version ? "reinstall"
            : isDowngrade ? "downgrade"
            : "update";

        if (currentVersion != null)
        {
            if (action == "reinstall")
                Console.WriteLine($"Will reinstall Steam at version {version}.");
            else
                Console.WriteLine($"Will {action} from {currentVersion} to {version}.");
        }
        else
            Console.WriteLine($"Will apply target version {version}.");

        if (!skipPrompt)
        {
            Console.WriteLine();
            Console.WriteLine("This will:");
            Console.WriteLine("  - Quit Steam");
            Console.WriteLine($"  - Delete the package/ directory and download the target Steam version");
            Console.WriteLine($"  - Launch Steam in update mode to apply the {(action == "reinstall" ? "reinstallation" : action)}");
            Console.WriteLine("  - Write steam.cfg to block future updates");
            Console.WriteLine("  - Delete the appcache/ directory to prevent automatic, unintentional update");
            Console.WriteLine();
            Console.WriteLine("You will NOT lose any installed games or game saves. Everything will be preserved, don't worry.");
            Console.WriteLine();
            Console.Write("Proceed? [Y/n] ");

            string? input = Console.ReadLine();
            if (input != null && input.Trim().Equals("n", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine("Aborted.");
                Console.WriteLine();
                return false;
            }
            Console.WriteLine();
        }

        KillSteam();

        // Write steam.cfg early so even a partial failure leaves update prevention in place
        WriteSteamCfg(steamPath);
        NukeAppcache(steamPath);

        string packageDir = Path.Combine(steamPath, "package");
        Console.WriteLine($"Clearing package directory: {packageDir}");
        if (Directory.Exists(packageDir))
            Directory.Delete(packageDir, true);
        Directory.CreateDirectory(packageDir);

        Console.WriteLine("Downloading client manifest...");
        bool manifestOk = await DownloadManifest(packageDir, overrideManifestUrl);
        if (!manifestOk)
        {
            Console.Error.WriteLine("ERROR: Failed to download client manifest.");
            Console.WriteLine();
            return false;
        }

        List<string> urls = await FetchSources(overrideSourcesUrl);
        if (urls.Count == 0)
        {
            Console.Error.WriteLine("ERROR: No package URLs found from remote or embedded sources.");
            Console.WriteLine();
            return false;
        }

        Console.WriteLine($"Downloading {urls.Count} package files...");
        Console.WriteLine();

        bool downloadOk = await DownloadAllPackages(urls, packageDir);
        if (!downloadOk)
        {
            Console.Error.WriteLine("ERROR: Some downloads failed.");
            Console.WriteLine();
            return false;
        }

        Console.WriteLine();
        Console.WriteLine("All packages downloaded successfully.");
        Console.WriteLine();

        Console.WriteLine($"Starting local file server on port {FileServerPort}...");
        using var cts = new CancellationTokenSource();
        var serverTask = RunFileServer(packageDir, FileServerPort, cts.Token);
        await Task.Delay(500);

        // Bail if the file server failed to start
        if (serverTask.IsFaulted || serverTask.IsCompleted)
        {
            Console.Error.WriteLine("ERROR: File server failed to start.");
            Console.WriteLine();
            return false;
        }

        string steamExe = Path.Combine(steamPath, "steam.exe");
        if (!File.Exists(steamExe))
        {
            Console.Error.WriteLine($"ERROR: steam.exe not found at {steamExe}");
            cts.Cancel();
            Console.WriteLine();
            return false;
        }

        string steamArgs = $"-textmode -forcesteamupdate -forcepackagedownload -overridepackageurl http://127.0.0.1:{FileServerPort}/ -exitsteam";
        Console.WriteLine($"Launching: {steamExe} {steamArgs}");
        Console.WriteLine();
        Console.WriteLine("Steam will download the pinned packages from the local server and then exit.");
        Console.WriteLine("Waiting for Steam to finish...");
        Console.WriteLine();

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = steamExe,
                Arguments = steamArgs,
                UseShellExecute = false,
            };
            using var proc = Process.Start(psi);
            if (proc != null)
            {
                // 10-minute timeout — update mode should finish well within this
                using var steamTimeout = new CancellationTokenSource(TimeSpan.FromMinutes(10));
                try
                {
                    await proc.WaitForExitAsync(steamTimeout.Token);
                    Console.WriteLine($"Steam exited with code {proc.ExitCode}.");
                }
                catch (OperationCanceledException)
                {
                    Console.Error.WriteLine("WARNING: Steam did not exit within 10 minutes. Killing...");
                    try { proc.Kill(true); } catch { }
                }
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"ERROR: Failed to launch Steam: {ex.Message}");
        }

        Console.WriteLine("Stopping local file server...");
        cts.Cancel();
        try { await serverTask; } catch (OperationCanceledException) { }

        // Re-write steam.cfg in case Steam overwrote it
        WriteSteamCfg(steamPath);
        NukeAppcache(steamPath);

        string? newVersion = GetSteamVersion(steamPath);
        bool versionOk = false;
        if (newVersion != null)
        {
            Console.WriteLine($"Steam client version after {action}: {newVersion}");
            if (newVersion == version)
            {
                string pastTense = action == "downgrade" ? "downgraded"
                    : action == "reinstall" ? "reinstalled"
                    : "updated";
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"Success! Steam is now {pastTense} and update blocking is in place!");
                Console.ResetColor();
                versionOk = true;
            }
            else
                Console.WriteLine($"WARNING: Version is {newVersion}, expected {version}. The {action} may not have fully applied.");
        }
        else
        {
            Console.WriteLine($"Could not verify version after {action}. Check manually.");
        }

        if (versionOk && !skipSteamToolsInstall)
        {
            Console.WriteLine();
            await InstallSteamTools(steamPath, skipPrompt: true);
        }

        // Make sure Steam is running at the end of the flow
        // InstallSteamTools launches Steam when it does work, but skips when already current
        LaunchSteamIfNotRunning(steamPath);

        Console.WriteLine();
        return versionOk;
    }

    static async Task InstallSteamTools(string steamPath, bool skipPrompt = false)
    {
        Console.WriteLine("=== Install SteamTools ===");
        Console.WriteLine();

        var stStatus = CheckSteamToolsStatus(steamPath);
        if (stStatus == SteamToolsStatus.UpToDate)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("SteamTools is already up to date.");
            Console.ResetColor();
            Console.WriteLine();
            return;
        }

        if (!skipPrompt)
        {
            Console.Write("Install SteamTools? [Y/n] ");
            string? input = Console.ReadLine();
            if (input != null && input.Trim().Equals("n", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine("Aborted.");
                Console.WriteLine();
                return;
            }
            Console.WriteLine();
        }

        KillSteam();

        // Clean up old conflicting DLLs
        string[] oldDlls = ["user32.dll", "version.dll"];
        foreach (var dll in oldDlls)
        {
            string path = Path.Combine(steamPath, dll);
            if (File.Exists(path))
            {
                try
                {
                    File.Delete(path);
                    Console.WriteLine($"Removed old DLL: {dll}");
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"WARNING: Could not remove {dll}: {ex.Message}");
                }
            }
        }

        string betaPath = Path.Combine(steamPath, "package", "beta");
        if (File.Exists(betaPath))
        {
            try { File.Delete(betaPath); } catch { }
        }

        string xinputPath = Path.Combine(steamPath, "xinput1_4.dll");
        string dwmapiPath = Path.Combine(steamPath, "dwmapi.dll");

        // Add Defender exclusions (best-effort, requires admin)
        AddDefenderExclusion(xinputPath);
        AddDefenderExclusion(dwmapiPath);

        Console.WriteLine("Downloading SteamTools DLLs...");

        bool dlOk = true;
        dlOk &= await DownloadFile(_http, SteamToolsDllUrl, xinputPath, "xinput1_4.dll");
        dlOk &= await DownloadFile(_http, SteamToolsDwmapiUrl, dwmapiPath, "dwmapi.dll");

        if (!dlOk)
        {
            Console.Error.WriteLine("ERROR: Failed to download one or more SteamTools DLLs.");
            Console.WriteLine();
            return;
        }

        // Verify downloaded DLLs against known hashes when available
        var cfg = _remoteConfig;
        if (cfg?.SteamToolsXinputSha256 != null && cfg?.SteamToolsDwmapiSha256 != null)
        {
            string xinputHash = ComputeSha256(xinputPath);
            string dwmapiHash = ComputeSha256(dwmapiPath);

            if (xinputHash != cfg.SteamToolsXinputSha256 || dwmapiHash != cfg.SteamToolsDwmapiSha256)
            {
                Console.Error.WriteLine("ERROR: Downloaded SteamTools DLLs do not match expected hashes!");
                Console.Error.WriteLine($"  xinput1_4.dll: got {xinputHash}, expected {cfg.SteamToolsXinputSha256}");
                Console.Error.WriteLine($"  dwmapi.dll:    got {dwmapiHash}, expected {cfg.SteamToolsDwmapiSha256}");
                try { File.Delete(xinputPath); } catch { }
                try { File.Delete(dwmapiPath); } catch { }
                Console.WriteLine();
                return;
            }
            Console.WriteLine("DLL hashes verified.");
        }

        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(SteamToolsRegPath);
            if (key != null)
            {
                // Clean up old properties
                try { key.DeleteValue("ActivateUnlockMode", false); } catch { }
                try { key.DeleteValue("AlwaysStayUnlocked", false); } catch { }
                try { key.DeleteValue("notUnlockDepot", false); } catch { }

                key.SetValue("iscdkey", "true", Microsoft.Win32.RegistryValueKind.String);
                Console.WriteLine("Set SteamTools registry keys.");
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"WARNING: Could not set registry keys: {ex.Message}");
        }

        // Re-apply steam.cfg to preserve update prevention
        WriteSteamCfg(steamPath);

        string steamExe = Path.Combine(steamPath, "steam.exe");
        if (File.Exists(steamExe))
        {
            Console.WriteLine("Launching Steam...");
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = steamExe,
                    UseShellExecute = true,
                });
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"WARNING: Could not launch Steam: {ex.Message}");
            }
        }

        Console.WriteLine();
        Console.WriteLine("SteamTools installed successfully. Please log in to Steam to activate.");
        Console.WriteLine();
    }

    static async Task PickAndApplyVersion(string steamPath)
    {
        Console.WriteLine("=== Pick a Specific Steam Version ===");
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("WARNING: This is an advanced option. Only use this if you know what you");
        Console.WriteLine("are doing. Not all versions are compatible with SteamTools. SteamTools");
        Console.WriteLine("will NOT be automatically installed after this operation.");
        Console.ResetColor();
        Console.WriteLine();

        Console.WriteLine("Fetching available versions...");
        var versions = await Updater.FetchVersionsList();
        if (versions.Count == 0)
        {
            Console.Error.WriteLine("ERROR: Could not fetch version list from remote config.");
            Console.WriteLine();
            return;
        }

        string? currentVersion = GetSteamVersion(steamPath);

        Console.WriteLine();
        Console.WriteLine("Available versions:");
        Console.WriteLine();
        for (int i = 0; i < versions.Count; i++)
        {
            string label = versions[i].Label ?? "";
            string recommended = versions[i].Version == TargetVersion ? " (recommended)" : "";
            string current = versions[i].Version == currentVersion ? " [installed]" : "";
            Console.WriteLine($"  {i + 1}) {versions[i].Version} - {label}{recommended}{current}");
        }
        Console.WriteLine();
        Console.WriteLine("  0) Back to main menu");
        Console.WriteLine();
        Console.Write("> ");

        string? choice = Console.ReadLine()?.Trim();
        if (choice == "0" || string.IsNullOrEmpty(choice))
        {
            Console.WriteLine();
            return;
        }

        if (!int.TryParse(choice, out int idx) || idx < 1 || idx > versions.Count)
        {
            Console.WriteLine("Invalid selection.");
            Console.WriteLine();
            return;
        }

        var selected = versions[idx - 1];
        Console.WriteLine();

        if (selected.SourcesUrl == null || selected.ManifestUrl == null)
        {
            Console.Error.WriteLine("ERROR: Missing download URLs for this version. The config release may be incomplete.");
            Console.WriteLine();
            return;
        }

        await ApplyTargetVersion(steamPath,
            overrideVersion: selected.Version,
            overrideSourcesUrl: selected.SourcesUrl,
            overrideManifestUrl: selected.ManifestUrl,
            skipSteamToolsInstall: true);
    }

    static async Task<bool> DownloadFile(HttpClient httpClient, string url, string filePath, string displayName)
    {
        try
        {
            if (File.Exists(filePath))
                File.Delete(filePath);

            using var response = await httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();

            byte[] content = await response.Content.ReadAsByteArrayAsync();
            await File.WriteAllBytesAsync(filePath, content);

            Console.WriteLine($"  Downloaded {displayName} ({content.Length / 1024.0:F1} KB)");
            return true;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"  FAILED {displayName}: {ex.Message}");
            return false;
        }
    }

    static void AddDefenderExclusion(string path)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "powershell",
                Arguments = $"-Command \"Add-MpPreference -ExclusionPath '{path}' -ErrorAction SilentlyContinue\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            using var proc = Process.Start(psi);
            proc?.WaitForExit(5000);
        }
        catch { }
    }

    static string? GetSteamPath()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
            if (key != null)
            {
                var val = key.GetValue("SteamPath");
                if (val is string s && !string.IsNullOrWhiteSpace(s))
                    return s;
            }
        }
        catch { }

        // Fallback: check common paths
        string[] fallbacks = [
            @"C:\Program Files (x86)\Steam",
            @"C:\Program Files\Steam",
            @"C:\Steam",
        ];
        foreach (var path in fallbacks)
        {
            if (Directory.Exists(path) && File.Exists(Path.Combine(path, "steam.exe")))
                return path;
        }

        return null;
    }

    static string? GetSteamVersion(string steamPath)
    {
        string manifestPath = Path.Combine(steamPath, "package", "steam_client_win64.manifest");
        if (File.Exists(manifestPath))
        {
            string? ver = ExtractVersionFromManifest(manifestPath);
            if (ver != null) return ver;
        }

        string infPath = Path.Combine(steamPath, "steam.inf");
        if (File.Exists(infPath))
        {
            try
            {
                foreach (var line in File.ReadAllLines(infPath))
                {
                    if (line.StartsWith("ClientVersion=", StringComparison.OrdinalIgnoreCase))
                    {
                        return line.Substring("ClientVersion=".Length).Trim();
                    }
                }
            }
            catch { }
        }

        return null;
    }

    internal static string? ExtractVersionFromManifest(string manifestPath)
    {
        try
        {
            string content = File.ReadAllText(manifestPath);
            // VDF-like format: "version" "1234567890"
            var match = Regex.Match(content, @"""version""\s+""(\d+)""");
            if (match.Success)
                return match.Groups[1].Value;
        }
        catch { }
        return null;
    }

    static void KillSteam()
    {
        string[] processNames = ["steam", "steamwebhelper", "steamservice"];
        bool killed = false;
        foreach (var name in processNames)
        {
            foreach (var proc in Process.GetProcessesByName(name))
            {
                try
                {
                    Console.WriteLine($"Killing process: {proc.ProcessName} (PID {proc.Id})");
                    proc.Kill(true);
                    killed = true;
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"WARNING: Could not kill {proc.ProcessName}: {ex.Message}");
                }
                finally
                {
                    proc.Dispose();
                }
            }
        }
        if (killed)
        {
            Console.WriteLine("Waiting for Steam processes to exit...");
            Thread.Sleep(3000);
        }
    }

    static void LaunchSteamIfNotRunning(string steamPath)
    {
        var existing = Process.GetProcessesByName("steam");
        bool running = existing.Length > 0;
        foreach (var p in existing) p.Dispose();

        if (running) return;

        string steamExe = Path.Combine(steamPath, "steam.exe");
        if (!File.Exists(steamExe)) return;

        Console.WriteLine("Launching Steam...");
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = steamExe,
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"WARNING: Could not launch Steam: {ex.Message}");
        }
    }

    internal static List<string> ReadEmbeddedSources()
    {
        var urls = new List<string>();
        var assembly = Assembly.GetExecutingAssembly();

        string? resourceName = null;
        foreach (var name in assembly.GetManifestResourceNames())
        {
            if (name.EndsWith("sources.txt", StringComparison.OrdinalIgnoreCase))
            {
                resourceName = name;
                break;
            }
        }

        if (resourceName == null)
        {
            Console.Error.WriteLine("ERROR: Embedded resource 'sources.txt' not found.");
            Console.Error.WriteLine("Available resources: " + string.Join(", ", assembly.GetManifestResourceNames()));
            return urls;
        }

        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream == null) return urls;

        using var reader = new StreamReader(stream);
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            line = line.Trim();
            if (line.Length > 0 && !line.StartsWith('#'))
            {
                urls.Add(line);
            }
        }

        return urls;
    }

    static async Task<List<string>> FetchSources(string? overrideSourcesUrl = null)
    {
        string? sourcesUrl = overrideSourcesUrl ?? Updater.SourcesAssetUrl;
        if (!string.IsNullOrEmpty(sourcesUrl))
        {
            try
            {
                string raw = await _http.GetStringAsync(sourcesUrl);
                var urls = ParseSourceLines(raw);
                if (urls.Count > 0)
                {
                    Console.WriteLine($"Loaded {urls.Count} package URLs from remote sources.");
                    return urls;
                }
            }
        catch
        {
            Console.WriteLine("Could not fetch remote sources, falling back to embedded.");
        }
    }

    // Embedded sources are for the default version only
    if (overrideSourcesUrl != null)
        return [];

    return ReadEmbeddedSources();
    }

    internal static List<string> ParseSourceLines(string text)
    {
        var urls = new List<string>();
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length > 0 && !line.StartsWith('#'))
                urls.Add(line);
        }
        return urls;
    }

    static async Task<bool> DownloadManifest(string packageDir, string? overrideManifestUrl = null)
    {
        try
        {
            string manifestUrl = overrideManifestUrl ?? TargetManifestUrl;
            string filePath = Path.Combine(packageDir, ManifestFileName);
            using var response = await _http.GetAsync(manifestUrl);
            response.EnsureSuccessStatusCode();

            byte[] content = await response.Content.ReadAsByteArrayAsync();
            await File.WriteAllBytesAsync(filePath, content);

            Console.WriteLine($"  Downloaded manifest: {ManifestFileName} ({content.Length / 1024.0:F1} KB)");
            return true;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"  Failed to download manifest: {ex.Message}");
            return false;
        }
    }

    static async Task<bool> DownloadAllPackages(List<string> urls, string packageDir)
    {
        using var semaphore = new SemaphoreSlim(8);
        int completed = 0;
        int total = urls.Count;
        int failCount = 0;

        var tasks = urls.Select(async url =>
        {
            await semaphore.WaitAsync();
            try
            {
                string fileName = url.Split('/').Last();
                string filePath = Path.Combine(packageDir, fileName);

                using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
                response.EnsureSuccessStatusCode();

                using var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);
                using var downloadStream = await response.Content.ReadAsStreamAsync();

                byte[] buffer = new byte[81920];
                long totalRead = 0;
                int bytesRead;

                while ((bytesRead = await downloadStream.ReadAsync(buffer)) > 0)
                {
                    await fs.WriteAsync(buffer.AsMemory(0, bytesRead));
                    totalRead += bytesRead;
                }

                int count = Interlocked.Increment(ref completed);
                Console.WriteLine($"  [{count}/{total}] {fileName} ({totalRead / 1024.0 / 1024.0:F1} MB)");
            }
            catch (Exception ex)
            {
                int count = Interlocked.Increment(ref completed);
                string fileName = url.Split('/').Last();
                Console.Error.WriteLine($"  [{count}/{total}] FAILED: {fileName} - {ex.Message}");
                Interlocked.Increment(ref failCount);
            }
            finally
            {
                semaphore.Release();
            }
        }).ToArray();

        await Task.WhenAll(tasks);
        return failCount == 0;
    }

    static async Task RunFileServer(string rootDir, int port, CancellationToken ct)
    {
        var listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        listener.Prefixes.Add($"http://localhost:{port}/");

        try
        {
            listener.Start();
        }
        catch (HttpListenerException ex)
        {
            Console.Error.WriteLine($"ERROR: Could not start HTTP listener on port {port}: {ex.Message}");
            Console.Error.WriteLine("Try running as Administrator, or check if the port is already in use.");
            return;
        }

        Console.WriteLine($"File server listening on http://127.0.0.1:{port}/");

        ct.Register(() => { try { listener.Stop(); } catch { } });

        try
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var context = await listener.GetContextAsync();
                    _ = Task.Run(() => HandleFileRequest(context, rootDir), CancellationToken.None);
                }
                catch (HttpListenerException) when (ct.IsCancellationRequested)
                {
                    break;
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"File server error: {ex.Message}");
                }
            }
        }
        finally
        {
            listener.Close();
        }
    }

    internal static bool IsPathWithinRoot(string candidatePath, string rootDir)
    {
        string fullPath = Path.GetFullPath(candidatePath);
        string rootFull = Path.GetFullPath(rootDir).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return fullPath.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase);
    }

    static void HandleFileRequest(HttpListenerContext context, string rootDir)
    {
        string requestPath = context.Request.Url?.AbsolutePath?.TrimStart('/') ?? "";
        string filePath = Path.GetFullPath(Path.Combine(rootDir, requestPath));

        try
        {
            if (!IsPathWithinRoot(filePath, rootDir))
            {
                context.Response.StatusCode = 403;
                Console.WriteLine($"  [HTTP 403] {requestPath} (path traversal blocked)");
                return;
            }

            if (File.Exists(filePath))
            {
                var fileInfo = new FileInfo(filePath);
                context.Response.StatusCode = 200;
                context.Response.ContentType = "application/octet-stream";
                context.Response.ContentLength64 = fileInfo.Length;

                using var fs = File.OpenRead(filePath);
                fs.CopyTo(context.Response.OutputStream);

                Console.WriteLine($"  [HTTP 200] {requestPath} ({fileInfo.Length / 1024.0 / 1024.0:F1} MB)");
            }
            else
            {
                context.Response.StatusCode = 404;
                Console.WriteLine($"  [HTTP 404] {requestPath}");
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"  [HTTP ERR] {requestPath}: {ex.Message}");
            try { context.Response.StatusCode = 500; } catch { }
        }
        finally
        {
            try { context.Response.Close(); } catch { }
        }
    }

    static void WriteSteamCfg(string steamPath)
    {
        string cfgPath = Path.Combine(steamPath, "steam.cfg");
        string content = "BootStrapperInhibitAll=enable\nBootStrapperForceSelfUpdate=disable\n";

        try
        {
            File.WriteAllText(cfgPath, content);
            Console.WriteLine($"Wrote update prevention config: {cfgPath}");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"ERROR: Could not write steam.cfg: {ex.Message}");
        }
    }

    static void NukeAppcache(string steamPath)
    {
        string appcachePath = Path.Combine(steamPath, "appcache");
        if (Directory.Exists(appcachePath))
        {
            try
            {
                Directory.Delete(appcachePath, true);
                Console.WriteLine($"Deleted appcache directory: {appcachePath}");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"WARNING: Could not fully delete appcache: {ex.Message}");
            }
        }
    }

    static void WaitForKey()
    {
        Console.WriteLine();
        Console.WriteLine("Press any key to continue...");
        Console.ReadKey(true);
    }

    static SteamToolsStatus CheckSteamToolsStatus(string steamPath)
    {
        string xinputPath = Path.Combine(steamPath, "xinput1_4.dll");
        string dwmapiPath = Path.Combine(steamPath, "dwmapi.dll");

        if (!File.Exists(xinputPath) || !File.Exists(dwmapiPath))
            return SteamToolsStatus.NotInstalled;

        var cfg = _remoteConfig;
        if (cfg?.SteamToolsXinputSha256 == null || cfg?.SteamToolsDwmapiSha256 == null)
            return SteamToolsStatus.Unknown;

        string xinputHash = ComputeSha256(xinputPath);
        string dwmapiHash = ComputeSha256(dwmapiPath);

        if (xinputHash == cfg.SteamToolsXinputSha256 && dwmapiHash == cfg.SteamToolsDwmapiSha256)
            return SteamToolsStatus.UpToDate;

        return SteamToolsStatus.Outdated;
    }

    internal static string ComputeSha256(string filePath)
    {
        using var sha = SHA256.Create();
        using var fs = File.OpenRead(filePath);
        byte[] hash = sha.ComputeHash(fs);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern IntPtr GetStdHandle(int nStdHandle);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool GetConsoleMode(IntPtr hConsoleHandle, out uint lpMode);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool SetConsoleMode(IntPtr hConsoleHandle, uint dwMode);

    static bool _vtEnabled;

    static void EnableVirtualTerminal()
    {
        if (_vtEnabled) return;
        try
        {
            var handle = GetStdHandle(-11); // STD_OUTPUT_HANDLE
            if (GetConsoleMode(handle, out uint mode))
                _vtEnabled = SetConsoleMode(handle, mode | 0x0004); // ENABLE_VIRTUAL_TERMINAL_PROCESSING
        }
        catch { }
    }

    static void ClearScreen()
    {
        EnableVirtualTerminal();
        Console.Write("\x1b[3J\x1b[2J\x1b[H");
    }
}
