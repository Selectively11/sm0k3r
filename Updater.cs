using System.Diagnostics;
using System.Net.Http.Json;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Sm0k3r;

class ReleaseInfo
{
    [JsonPropertyName("tag_name")]
    public string? TagName { get; set; }

    [JsonPropertyName("body")]
    public string? Body { get; set; }

    [JsonPropertyName("assets")]
    public ReleaseAsset[]? Assets { get; set; }
}

class ReleaseAsset
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("browser_download_url")]
    public string? DownloadUrl { get; set; }
}

class RemoteConfig
{
    [JsonPropertyName("version")]
    public string? Version { get; set; }

    [JsonPropertyName("steamtools_xinput_sha256")]
    public string? SteamToolsXinputSha256 { get; set; }

    [JsonPropertyName("steamtools_dwmapi_sha256")]
    public string? SteamToolsDwmapiSha256 { get; set; }
}

class VersionEntry
{
    [JsonPropertyName("version")]
    public string? Version { get; set; }

    [JsonPropertyName("label")]
    public string? Label { get; set; }

    [JsonPropertyName("sources_asset")]
    public string? SourcesAsset { get; set; }

    [JsonPropertyName("manifest_asset")]
    public string? ManifestAsset { get; set; }

    [JsonIgnore]
    public string? SourcesUrl { get; set; }

    [JsonIgnore]
    public string? ManifestUrl { get; set; }
}

[JsonSerializable(typeof(ReleaseInfo))]
[JsonSerializable(typeof(RemoteConfig))]
[JsonSerializable(typeof(VersionEntry[]))]
partial class AppJsonContext : JsonSerializerContext { }

static class Updater
{
    const string RepoOwner = "Selectively11";
    const string RepoName = "sm0k3r";

    static readonly HttpClient _http = new();

    public static string? SourcesAssetUrl { get; private set; }
    public static string? ManifestAssetUrl { get; private set; }
    public static Dictionary<string, string> ConfigAssets { get; private set; } = new(StringComparer.OrdinalIgnoreCase);

    static Updater()
    {
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("sm0k3r-updater");
    }

    public static string CurrentVersion
    {
        get
        {
            var ver = Assembly.GetExecutingAssembly()
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion;
            // Strip the +commithash suffix if present
            if (ver != null)
            {
                int plus = ver.IndexOf('+');
                if (plus >= 0) ver = ver[..plus];
            }
            return ver ?? "0.0.0";
        }
    }

    public static async Task CheckAndApply()
    {
        // Clean up leftover .old from previous update
        try
        {
            var exePath = Environment.ProcessPath;
            if (exePath != null)
            {
                var oldPath = exePath + ".old";
                if (File.Exists(oldPath)) File.Delete(oldPath);
            }
        }
        catch { }

        try
        {
            var release = await CheckForUpdate(CurrentVersion);
            if (release == null) return;

            string remote = release.TagName!.TrimStart('v');
            Console.WriteLine($"Update available: v{remote} (current: v{CurrentVersion})");
            Console.Write("Download and install? [y/N] ");

            string? input = Console.ReadLine();
            if (input == null || !input.Trim().Equals("y", StringComparison.OrdinalIgnoreCase))
                return;

            await ApplyUpdate(release);
            Environment.Exit(0);
        }
        catch (HttpRequestException)
        {
            // No repo / no releases / no internet — silently skip
        }
        catch (Exception)
        {
        }
    }

    // Returns true when remoteTag represents a newer version than currentVersion.
    // Returns false for equal versions, older versions, or non-parseable versions.
    internal static bool IsNewerVersion(string remoteTag, string currentVersion)
    {
        var remote = remoteTag.TrimStart('v');
        var local = currentVersion.TrimStart('v');

        if (remote == local)
            return false;

        if (!Version.TryParse(remote, out var remoteVer) ||
            !Version.TryParse(local, out var localVer))
            return false;

        return remoteVer > localVer;
    }

    static async Task<ReleaseInfo?> CheckForUpdate(string currentVersion)
    {
        var url = $"https://api.github.com/repos/{RepoOwner}/{RepoName}/releases/latest";
        var release = await _http.GetFromJsonAsync(url, AppJsonContext.Default.ReleaseInfo);

        if (release == null || string.IsNullOrEmpty(release.TagName))
            return null;

        if (!IsNewerVersion(release.TagName, currentVersion))
            return null;

        return release;
    }

    public static async Task<RemoteConfig?> FetchRemoteConfig()
    {
        try
        {
            var url = $"https://api.github.com/repos/{RepoOwner}/{RepoName}/releases/tags/config";
            var release = await _http.GetFromJsonAsync(url, AppJsonContext.Default.ReleaseInfo);
            if (release?.Assets == null) return null;

            string? configUrl = null;
            foreach (var asset in release.Assets)
            {
                if (asset.Name != null && asset.DownloadUrl != null)
                    ConfigAssets[asset.Name] = asset.DownloadUrl;

                switch (asset.Name)
                {
                    case "version.json":
                        configUrl = asset.DownloadUrl;
                        break;
                    case "sources.txt":
                        SourcesAssetUrl = asset.DownloadUrl;
                        break;
                    case "steam_client_win64":
                        ManifestAssetUrl = asset.DownloadUrl;
                        break;
                }
            }

            if (configUrl == null) return null;

            return await _http.GetFromJsonAsync(configUrl, AppJsonContext.Default.RemoteConfig);
        }
        catch
        {
            return null;
        }
    }

    static async Task ApplyUpdate(ReleaseInfo release)
    {
        var currentExe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(currentExe))
        {
            Console.Error.WriteLine("Could not determine current executable path.");
            return;
        }

        ReleaseAsset? exeAsset = null;
        if (release.Assets != null)
        {
            foreach (var asset in release.Assets)
            {
                if (asset.Name != null && asset.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                {
                    exeAsset = asset;
                    break;
                }
            }
        }

        if (exeAsset?.DownloadUrl == null)
        {
            Console.Error.WriteLine("No .exe asset found in release.");
            return;
        }

        Console.WriteLine($"Downloading {exeAsset.Name}...");

        // GetTempFileName creates a 0-byte file; we append .exe so the actual download goes to a different path.
        // Delete the orphan immediately to avoid accumulating temp files.
        var orphan = Path.GetTempFileName();
        try { File.Delete(orphan); } catch { }
        var tempPath = orphan + ".exe";
        try
        {
            using (var stream = await _http.GetStreamAsync(exeAsset.DownloadUrl))
            using (var fs = File.Create(tempPath))
            {
                await stream.CopyToAsync(fs);
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Download failed: {ex.Message}");
            try { File.Delete(tempPath); } catch { }
            return;
        }

        // Verify hash if the release body contains a sha256 line
        string? expectedHash = ParseHashFromBody(release.Body);
        if (expectedHash != null)
        {
            string actualHash;
            using (var sha = SHA256.Create())
            using (var fs = File.OpenRead(tempPath))
            {
                actualHash = Convert.ToHexString(sha.ComputeHash(fs)).ToLowerInvariant();
            }

            if (actualHash != expectedHash)
            {
                Console.Error.WriteLine("ERROR: Downloaded update does not match expected hash!");
                Console.Error.WriteLine($"  Expected: {expectedHash}");
                Console.Error.WriteLine($"  Got:      {actualHash}");
                try { File.Delete(tempPath); } catch { }
                return;
            }
            Console.WriteLine("Hash verified.");
        }

        var backupPath = currentExe + ".old";

        Console.WriteLine("Installing update...");
        try
        {
            if (File.Exists(backupPath))
                File.Delete(backupPath);
            File.Move(currentExe, backupPath);
            File.Move(tempPath, currentExe);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Could not replace exe: {ex.Message}");
            try { File.Delete(tempPath); } catch { }
            if (!File.Exists(currentExe) && File.Exists(backupPath))
                File.Move(backupPath, currentExe);
            return;
        }

        Console.WriteLine("Updated. Relaunching...");
        Process.Start(new ProcessStartInfo(currentExe) { UseShellExecute = true });
    }

    // Looks for "sha256: <hex>" in the release body
    internal static string? ParseHashFromBody(string? body)
    {
        if (string.IsNullOrEmpty(body)) return null;
        var match = Regex.Match(body, @"sha256:\s*([0-9a-fA-F]{64})");
        return match.Success ? match.Groups[1].Value.ToLowerInvariant() : null;
    }

    public static async Task<List<VersionEntry>> FetchVersionsList()
    {
        if (!ConfigAssets.TryGetValue("versions.json", out var url))
            return [];

        try
        {
            var entries = await _http.GetFromJsonAsync(url, AppJsonContext.Default.VersionEntryArray);
            if (entries == null) return [];

            var result = new List<VersionEntry>();
            foreach (var entry in entries)
            {
                if (entry.SourcesAsset != null && ConfigAssets.TryGetValue(entry.SourcesAsset, out var sourcesUrl))
                    entry.SourcesUrl = sourcesUrl;
                if (entry.ManifestAsset != null && ConfigAssets.TryGetValue(entry.ManifestAsset, out var manifestUrl))
                    entry.ManifestUrl = manifestUrl;
                result.Add(entry);
            }
            return result;
        }
        catch
        {
            return [];
        }
    }
}
