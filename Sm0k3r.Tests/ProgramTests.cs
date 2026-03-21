using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Xunit;
using Sm0k3r;

namespace Sm0k3r.Tests;

public class ParseSourceLinesTests
{
    [Fact]
    public void ParsesValidUrls()
    {
        var input = "https://example.com/file1.zip\nhttps://example.com/file2.zip\n";
        var result = Program.ParseSourceLines(input);
        Assert.Equal(2, result.Count);
        Assert.Equal("https://example.com/file1.zip", result[0]);
        Assert.Equal("https://example.com/file2.zip", result[1]);
    }

    [Fact]
    public void SkipsEmptyLines()
    {
        var input = "https://a.com/1\n\n\nhttps://a.com/2\n";
        var result = Program.ParseSourceLines(input);
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void SkipsCommentLines()
    {
        var input = "# this is a comment\nhttps://a.com/1\n# another comment\nhttps://a.com/2\n";
        var result = Program.ParseSourceLines(input);
        Assert.Equal(2, result.Count);
        Assert.Equal("https://a.com/1", result[0]);
        Assert.Equal("https://a.com/2", result[1]);
    }

    [Fact]
    public void TrimsWhitespace()
    {
        var input = "  https://a.com/1  \n  https://a.com/2  \n";
        var result = Program.ParseSourceLines(input);
        Assert.Equal(2, result.Count);
        Assert.Equal("https://a.com/1", result[0]);
        Assert.Equal("https://a.com/2", result[1]);
    }

    [Fact]
    public void HandlesWindowsLineEndings()
    {
        var input = "https://a.com/1\r\nhttps://a.com/2\r\n";
        var result = Program.ParseSourceLines(input);
        Assert.Equal(2, result.Count);
        Assert.Equal("https://a.com/1", result[0]);
        Assert.Equal("https://a.com/2", result[1]);
    }

    [Fact]
    public void ReturnsEmptyForEmptyInput()
    {
        var result = Program.ParseSourceLines("");
        Assert.Empty(result);
    }

    [Fact]
    public void ReturnsEmptyForOnlyComments()
    {
        var input = "# comment 1\n# comment 2\n";
        var result = Program.ParseSourceLines(input);
        Assert.Empty(result);
    }
}

public class ExtractVersionFromManifestTests
{
    [Fact]
    public void ExtractsVersionFromVdf()
    {
        var content = """
        "steam_client_win64"
        {
            "version"		"1773426488"
            "win64"
            {
            }
        }
        """;
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, content);
            var result = Program.ExtractVersionFromManifest(path);
            Assert.Equal("1773426488", result);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ReturnsNullWhenNoVersionField()
    {
        var content = """
        "steam_client_win64"
        {
            "win64"
            {
            }
        }
        """;
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, content);
            var result = Program.ExtractVersionFromManifest(path);
            Assert.Null(result);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ReturnsNullForNonexistentFile()
    {
        var result = Program.ExtractVersionFromManifest(@"C:\nonexistent\path\file.manifest");
        Assert.Null(result);
    }

    [Fact]
    public void HandlesTabSeparatedVdf()
    {
        var content = "\"steam_client_win64\"\n{\n\t\"version\"\t\t\"9999999999\"\n}\n";
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, content);
            var result = Program.ExtractVersionFromManifest(path);
            Assert.Equal("9999999999", result);
        }
        finally
        {
            File.Delete(path);
        }
    }
}

public class ComputeSha256Tests
{
    [Fact]
    public void ComputesCorrectHash()
    {
        var content = Encoding.UTF8.GetBytes("hello world");
        var expected = Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();

        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(path, content);
            var result = Program.ComputeSha256(path);
            Assert.Equal(expected, result);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void EmptyFileHash()
    {
        var expected = Convert.ToHexString(SHA256.HashData([])).ToLowerInvariant();

        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(path, []);
            var result = Program.ComputeSha256(path);
            Assert.Equal(expected, result);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ReturnsLowercaseHex()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(path, [0xFF, 0xAB, 0xCD]);
            var result = Program.ComputeSha256(path);
            Assert.Equal(result, result.ToLowerInvariant());
        }
        finally
        {
            File.Delete(path);
        }
    }
}

public class ReadEmbeddedSourcesTests
{
    [Fact]
    public void ReturnsNonEmptyList()
    {
        var result = Program.ReadEmbeddedSources();
        Assert.NotEmpty(result);
    }

    [Fact]
    public void AllEntriesAreUrls()
    {
        var result = Program.ReadEmbeddedSources();
        foreach (var url in result)
        {
            Assert.StartsWith("https://", url);
        }
    }

    [Fact]
    public void NoCommentLines()
    {
        var result = Program.ReadEmbeddedSources();
        foreach (var line in result)
        {
            Assert.False(line.StartsWith('#'), $"Found comment in results: {line}");
        }
    }

    [Fact]
    public void NoEmptyEntries()
    {
        var result = Program.ReadEmbeddedSources();
        foreach (var line in result)
        {
            Assert.False(string.IsNullOrWhiteSpace(line), "Found empty entry in results");
        }
    }
}

public class VersionComparisonTests
{
    [Fact]
    public void NewerVersionDetectedAsTooNew()
    {
        string current = "1773426499";
        string target = "1773426488";

        bool tooNew = long.TryParse(current, out var curVer)
            && long.TryParse(target, out var tgtVer)
            && curVer > tgtVer;

        Assert.True(tooNew);
    }

    [Fact]
    public void OlderVersionNotTooNew()
    {
        string current = "1773426400";
        string target = "1773426488";

        bool tooNew = long.TryParse(current, out var curVer)
            && long.TryParse(target, out var tgtVer)
            && curVer > tgtVer;

        Assert.False(tooNew);
    }

    [Fact]
    public void SameVersionNotTooNew()
    {
        string current = "1773426488";
        string target = "1773426488";

        bool tooNew = long.TryParse(current, out var curVer)
            && long.TryParse(target, out var tgtVer)
            && curVer > tgtVer;

        Assert.False(tooNew);
    }

    [Fact]
    public void NonNumericVersionHandledGracefully()
    {
        string current = "notanumber";
        string target = "1773426488";

        bool tooNew = long.TryParse(current, out var curVer)
            && long.TryParse(target, out var tgtVer)
            && curVer > tgtVer;

        Assert.False(tooNew);
    }
}

public class RemoteConfigDeserializationTests
{
    [Fact]
    public void DeserializesValidJson()
    {
        var json = """
        {
            "version": "1773426488",
            "steamtools_xinput_sha256": "abc123",
            "steamtools_dwmapi_sha256": "def456"
        }
        """;

        var config = JsonSerializer.Deserialize(json, AppJsonContext.Default.RemoteConfig);

        Assert.NotNull(config);
        Assert.Equal("1773426488", config.Version);
        Assert.Equal("abc123", config.SteamToolsXinputSha256);
        Assert.Equal("def456", config.SteamToolsDwmapiSha256);
    }

    [Fact]
    public void HandlesNullFields()
    {
        var json = """{"version": "1773426488"}""";
        var config = JsonSerializer.Deserialize(json, AppJsonContext.Default.RemoteConfig);

        Assert.NotNull(config);
        Assert.Equal("1773426488", config.Version);
        Assert.Null(config.SteamToolsXinputSha256);
        Assert.Null(config.SteamToolsDwmapiSha256);
    }

    [Fact]
    public void HandlesEmptyObject()
    {
        var json = "{}";
        var config = JsonSerializer.Deserialize(json, AppJsonContext.Default.RemoteConfig);

        Assert.NotNull(config);
        Assert.Null(config.Version);
    }
}

public class ReleaseInfoDeserializationTests
{
    [Fact]
    public void DeserializesReleaseWithAssets()
    {
        var json = """
        {
            "tag_name": "v0.2.0",
            "body": "Release notes",
            "assets": [
                {
                    "name": "sm0k3r.exe",
                    "browser_download_url": "https://github.com/example/sm0k3r/releases/download/v0.2.0/sm0k3r.exe"
                },
                {
                    "name": "version.json",
                    "browser_download_url": "https://github.com/example/sm0k3r/releases/download/config/version.json"
                }
            ]
        }
        """;

        var release = JsonSerializer.Deserialize(json, AppJsonContext.Default.ReleaseInfo);

        Assert.NotNull(release);
        Assert.Equal("v0.2.0", release.TagName);
        Assert.Equal("Release notes", release.Body);
        Assert.NotNull(release.Assets);
        Assert.Equal(2, release.Assets.Length);
        Assert.Equal("sm0k3r.exe", release.Assets[0].Name);
        Assert.Contains("sm0k3r.exe", release.Assets[0].DownloadUrl);
    }

    [Fact]
    public void DeserializesReleaseWithNoAssets()
    {
        var json = """{"tag_name": "v0.1.0", "assets": []}""";
        var release = JsonSerializer.Deserialize(json, AppJsonContext.Default.ReleaseInfo);

        Assert.NotNull(release);
        Assert.Equal("v0.1.0", release.TagName);
        Assert.Empty(release.Assets!);
    }
}

// --- Audit fix tests ---

public class ParseHashFromBodyTests
{
    [Fact]
    public void ExtractsValidHash()
    {
        var body = "sha256: 9cca13f2bf62e068de5d93f1e7ba4295d8bfd70f86965f6fa3d821ed9bfe96b0";
        var result = Updater.ParseHashFromBody(body);
        Assert.Equal("9cca13f2bf62e068de5d93f1e7ba4295d8bfd70f86965f6fa3d821ed9bfe96b0", result);
    }

    [Fact]
    public void ReturnsNullForNullBody()
    {
        Assert.Null(Updater.ParseHashFromBody(null));
    }

    [Fact]
    public void ReturnsNullForEmptyBody()
    {
        Assert.Null(Updater.ParseHashFromBody(""));
    }

    [Fact]
    public void ReturnsNullWhenNoHash()
    {
        Assert.Null(Updater.ParseHashFromBody("Just some release notes, no hash here."));
    }

    [Fact]
    public void ExtractsHashFromMultilineBody()
    {
        var body = "## Release v0.1.1\n\nBug fixes and improvements.\n\nsha256: abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789\n\nEnjoy!";
        var result = Updater.ParseHashFromBody(body);
        Assert.Equal("abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789", result);
    }

    [Fact]
    public void NormalizesToLowercase()
    {
        var body = "sha256: ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789";
        var result = Updater.ParseHashFromBody(body);
        Assert.Equal("abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789", result);
    }

    [Fact]
    public void ToleratesExtraSpaces()
    {
        var body = "sha256:   9cca13f2bf62e068de5d93f1e7ba4295d8bfd70f86965f6fa3d821ed9bfe96b0";
        var result = Updater.ParseHashFromBody(body);
        Assert.Equal("9cca13f2bf62e068de5d93f1e7ba4295d8bfd70f86965f6fa3d821ed9bfe96b0", result);
    }

    [Fact]
    public void RejectsShortHash()
    {
        // Only 32 hex chars, not 64
        var body = "sha256: abcdef0123456789abcdef0123456789";
        Assert.Null(Updater.ParseHashFromBody(body));
    }

    [Fact]
    public void RejectsNonHexCharacters()
    {
        // 'g' is not a valid hex character
        var body = "sha256: gggggggggggggggggggggggggggggggggggggggggggggggggggggggggggggggg";
        Assert.Null(Updater.ParseHashFromBody(body));
    }
}

public class PathTraversalTests
{
    [Fact]
    public void AllowsFileInRoot()
    {
        var root = Path.GetTempPath();
        var file = Path.Combine(root, "somefile.txt");
        Assert.True(Program.IsPathWithinRoot(file, root));
    }

    [Fact]
    public void AllowsFileInSubdirectory()
    {
        var root = Path.GetTempPath();
        var file = Path.Combine(root, "subdir", "somefile.txt");
        Assert.True(Program.IsPathWithinRoot(file, root));
    }

    [Fact]
    public void BlocksDotDotTraversal()
    {
        var root = Path.Combine(Path.GetTempPath(), "fakepkg");
        var file = Path.Combine(root, "..", "etc", "passwd");
        Assert.False(Program.IsPathWithinRoot(file, root));
    }

    [Fact]
    public void BlocksAbsolutePathOutsideRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "fakepkg");
        Assert.False(Program.IsPathWithinRoot(@"C:\Windows\System32\cmd.exe", root));
    }

    [Fact]
    public void RootItselfIsNotWithinRoot()
    {
        // The root directory itself shouldn't match — we serve files IN the root, not the root dir
        var root = Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar);
        Assert.False(Program.IsPathWithinRoot(root, root));
    }
}

public class IsNewerVersionTests
{
    [Fact]
    public void NewerVersionReturnsTrue()
    {
        Assert.True(Updater.IsNewerVersion("v0.2.0", "0.1.0"));
    }

    [Fact]
    public void SameVersionReturnsFalse()
    {
        Assert.False(Updater.IsNewerVersion("v0.1.0", "0.1.0"));
    }

    [Fact]
    public void OlderVersionReturnsFalse()
    {
        Assert.False(Updater.IsNewerVersion("v0.1.0", "0.2.0"));
    }

    [Fact]
    public void BothWithVPrefix()
    {
        Assert.True(Updater.IsNewerVersion("v1.0.0", "v0.9.0"));
    }

    [Fact]
    public void NonParseableRemoteReturnsFalse()
    {
        // This is the #11 fix — non-parseable versions must not trigger an update
        Assert.False(Updater.IsNewerVersion("notaversion", "0.1.0"));
    }

    [Fact]
    public void NonParseableLocalReturnsFalse()
    {
        Assert.False(Updater.IsNewerVersion("v0.2.0", "notaversion"));
    }

    [Fact]
    public void BothNonParseableReturnsFalse()
    {
        Assert.False(Updater.IsNewerVersion("garbage", "trash"));
    }

    [Fact]
    public void PatchVersionBump()
    {
        Assert.True(Updater.IsNewerVersion("v0.1.2", "0.1.1"));
    }

    [Fact]
    public void MajorVersionBump()
    {
        Assert.True(Updater.IsNewerVersion("v2.0.0", "1.9.9"));
    }
}
