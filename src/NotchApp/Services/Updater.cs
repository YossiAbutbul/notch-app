using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;

namespace NotchApp.Services;

/// <summary>The newest release on GitHub, when it's newer than this copy.</summary>
public sealed record UpdateInfo(Version Version, string Notes, string PageUrl, string DownloadUrl, long Size, string? Sha256);

/// <summary>
/// Updates come from the GitHub releases of Notch's own repository: the latest release's Notch.exe is downloaded,
/// checked against the SHA-256 GitHub publishes for it, and run with --update, which replaces the installed copy
/// (keeping settings, shortcuts and the start-up choice) and opens the new version.
/// </summary>
public static class Updater
{
    public const string Repo = "YossiAbutbul/notch-app";
    private const string AssetName = "Notch.exe";

    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd($"Notch-Updater/{Installer.Current.ToString(3)}");
        c.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return c;
    }

    /// <summary>Asks GitHub for the latest release. Null means this copy is up to date.</summary>
    public static async Task<UpdateInfo?> CheckAsync(CancellationToken ct = default)
    {
        using var res = await Http.GetAsync($"https://api.github.com/repos/{Repo}/releases/latest", ct);
        if (res.StatusCode == System.Net.HttpStatusCode.NotFound)
            throw new UpdateException("Couldn't find Notch's releases. The repository may be private, or has no releases yet.");
        if ((int)res.StatusCode == 403 || (int)res.StatusCode == 429)
            throw new UpdateException("GitHub is limiting requests right now. Try again in a few minutes.");
        res.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));
        var root = doc.RootElement;
        string tag = root.GetProperty("tag_name").GetString() ?? "";
        if (!Version.TryParse(tag.TrimStart('v', 'V'), out var v)) throw new UpdateException($"The latest release has an unexpected version: {tag}");
        v = new Version(v.Major, v.Minor, Math.Max(0, v.Build));
        var current = new Version(Installer.Current.Major, Installer.Current.Minor, Math.Max(0, Installer.Current.Build));
        if (v <= current) return null;

        foreach (var a in root.GetProperty("assets").EnumerateArray())
        {
            if (!string.Equals(a.GetProperty("name").GetString(), AssetName, StringComparison.OrdinalIgnoreCase)) continue;
            string? sha = a.TryGetProperty("digest", out var d) && d.GetString() is { } ds && ds.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)
                ? ds["sha256:".Length..].ToLowerInvariant() : null;
            return new UpdateInfo(v,
                root.TryGetProperty("body", out var b) ? b.GetString() ?? "" : "",
                root.TryGetProperty("html_url", out var h) ? h.GetString() ?? "" : "",
                a.GetProperty("browser_download_url").GetString() ?? throw new UpdateException("The release has no download link."),
                a.GetProperty("size").GetInt64(), sha);
        }
        throw new UpdateException($"Notch {v.ToString(3)} is out, but its release has no {AssetName} to download.");
    }

    /// <summary>
    /// Downloads the update into its own temp folder, verifies it, and starts it. The new copy closes this one while
    /// it installs, so after this returns the app is about to quit.
    /// </summary>
    public static async Task DownloadAndInstallAsync(UpdateInfo u, IProgress<double> progress, CancellationToken ct = default)
    {
        // only ever run a file downloaded over https from GitHub
        if (!Uri.TryCreate(u.DownloadUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps || !uri.Host.EndsWith("github.com", StringComparison.OrdinalIgnoreCase))
            throw new UpdateException("The download link isn't a GitHub address, so it wasn't used.");

        var dir = Path.Combine(Path.GetTempPath(), $"Notch-update-{u.Version.ToString(3)}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, AssetName);

        using (var res = await Http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct))
        {
            res.EnsureSuccessStatusCode();
            long total = res.Content.Headers.ContentLength ?? u.Size;
            await using var src = await res.Content.ReadAsStreamAsync(ct);
            await using var dst = File.Create(file);
            var buf = new byte[1 << 16];
            long done = 0;
            int n;
            while ((n = await src.ReadAsync(buf, ct)) > 0)
            {
                await dst.WriteAsync(buf.AsMemory(0, n), ct);
                done += n;
                if (total > 0) progress.Report(Math.Min(1, (double)done / total));
            }
        }

        var info = new FileInfo(file);
        if (u.Size > 0 && info.Length != u.Size) { TryDelete(dir); throw new UpdateException("The download was incomplete. Try again."); }
        if (u.Sha256 != null)
        {
            await using var fs = File.OpenRead(file);
            var hash = Convert.ToHexString(await SHA256.HashDataAsync(fs, ct)).ToLowerInvariant();
            if (hash != u.Sha256) { TryDelete(dir); throw new UpdateException("The download didn't match the release's checksum, so it wasn't installed."); }
        }

        Process.Start(new ProcessStartInfo(file, "--update") { UseShellExecute = true, WorkingDirectory = dir });
    }

    private static void TryDelete(string dir) { try { Directory.Delete(dir, recursive: true); } catch { } }
}

public sealed class UpdateException(string message) : Exception(message);
