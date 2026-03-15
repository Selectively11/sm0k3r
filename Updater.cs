using System.Diagnostics;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json.Serialization;

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

    [JsonPropertyName("manifest_url")]
    public string? ManifestUrl { get; set; }

    [JsonPropertyName("sources_url")]
    public string? SourcesUrl { get; set; }

    [JsonPropertyName("steamtools_xinput_sha256")]
    public string? SteamToolsXinputSha256 { get; set; }

    [JsonPropertyName("steamtools_dwmapi_sha256")]
    public string? SteamToolsDwmapiSha256 { get; set; }
}

[JsonSerializable(typeof(ReleaseInfo))]
[JsonSerializable(typeof(RemoteConfig))]
partial class AppJsonContext : JsonSerializerContext { }

static class Updater
{
    const string RepoOwner = "Selectively11";
    const string RepoName = "sm0k3r";

    static readonly HttpClient _http = new();

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
            var oldPath = Environment.ProcessPath + ".old";
            if (File.Exists(oldPath)) File.Delete(oldPath);
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

    static async Task<ReleaseInfo?> CheckForUpdate(string currentVersion)
    {
        var url = $"https://api.github.com/repos/{RepoOwner}/{RepoName}/releases/latest";
        var release = await _http.GetFromJsonAsync(url, AppJsonContext.Default.ReleaseInfo);

        if (release == null || string.IsNullOrEmpty(release.TagName))
            return null;

        var remote = release.TagName.TrimStart('v');
        var local = currentVersion.TrimStart('v');

        if (remote == local)
            return null;

        if (Version.TryParse(remote, out var remoteVer) &&
            Version.TryParse(local, out var localVer))
        {
            if (remoteVer <= localVer)
                return null;
        }

        return release;
    }

    public static async Task<RemoteConfig?> FetchRemoteConfig()
    {
        try
        {
            var url = $"https://raw.githubusercontent.com/{RepoOwner}/{RepoName}/main/version.json";
            return await _http.GetFromJsonAsync(url, AppJsonContext.Default.RemoteConfig);
        }
        catch
        {
            return null;
        }
    }

    static async Task ApplyUpdate(ReleaseInfo release)
    {
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

        var tempPath = Path.GetTempFileName() + ".exe";
        using (var stream = await _http.GetStreamAsync(exeAsset.DownloadUrl))
        using (var fs = File.Create(tempPath))
        {
            await stream.CopyToAsync(fs);
        }

        var currentExe = Environment.ProcessPath!;
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
            if (!File.Exists(currentExe) && File.Exists(backupPath))
                File.Move(backupPath, currentExe);
            return;
        }

        Console.WriteLine("Updated. Relaunching...");
        Process.Start(new ProcessStartInfo(currentExe) { UseShellExecute = true });
    }
}
