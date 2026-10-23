using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;

namespace OrbitDrop.Infrastructure.Updates;

/// <summary>Semantic version with an optional pre-release tag ("0.1.2-alpha"). Pre-releases sort below the release.</summary>
public readonly record struct SemVersion(int Major, int Minor, int Patch, string? Pre) : IComparable<SemVersion>
{
    public static bool TryParse(string? text, out SemVersion v)
    {
        v = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        string s = text.Trim().TrimStart('v', 'V');
        int plus = s.IndexOf('+');
        if (plus >= 0) s = s[..plus]; // build metadata is ignored
        string? pre = null;
        int dash = s.IndexOf('-');
        if (dash >= 0) { pre = s[(dash + 1)..]; s = s[..dash]; }
        var parts = s.Split('.');
        if (parts.Length is < 2 or > 3) return false;
        if (!int.TryParse(parts[0], out int a) || !int.TryParse(parts[1], out int b)) return false;
        int c = 0;
        if (parts.Length == 3 && !int.TryParse(parts[2], out c)) return false;
        v = new SemVersion(a, b, c, string.IsNullOrEmpty(pre) ? null : pre);
        return true;
    }

    public int CompareTo(SemVersion o)
    {
        int c = Major.CompareTo(o.Major);
        if (c == 0) c = Minor.CompareTo(o.Minor);
        if (c == 0) c = Patch.CompareTo(o.Patch);
        if (c != 0) return c;
        if (Pre is null && o.Pre is null) return 0;
        if (Pre is null) return 1;   // 1.0.0 > 1.0.0-beta
        if (o.Pre is null) return -1;
        return ComparePre(Pre, o.Pre);
    }

    // SemVer §11: dot-separated identifiers; numeric ones compare numerically and sort below alphanumeric ones.
    private static int ComparePre(string a, string b)
    {
        var x = a.Split('.'); var y = b.Split('.');
        for (int i = 0; i < Math.Max(x.Length, y.Length); i++)
        {
            if (i >= x.Length) return -1;
            if (i >= y.Length) return 1;
            bool nx = int.TryParse(x[i], out int ix), ny = int.TryParse(y[i], out int iy);
            int c = nx && ny ? ix.CompareTo(iy) : nx ? -1 : ny ? 1 : string.CompareOrdinal(x[i], y[i]);
            if (c != 0) return c;
        }
        return 0;
    }

    public static bool operator >(SemVersion a, SemVersion b) => a.CompareTo(b) > 0;
    public static bool operator <(SemVersion a, SemVersion b) => a.CompareTo(b) < 0;

    public override string ToString() => $"{Major}.{Minor}.{Patch}{(Pre is null ? "" : "-" + Pre)}";
}

public sealed record ReleaseInfo(SemVersion Version, string Tag, string SetupUrl, string? ChecksumsUrl, string PageUrl);

/// <summary>
/// Checks GitHub Releases for a newer OrbitDrop and downloads its installer with checksum verification.
/// Network use: one anonymous GET to api.github.com per check; nothing about the user or their files is sent.
/// </summary>
public sealed class UpdateService
{
    public const string SetupAsset = "OrbitDrop-Setup.exe";
    public const string ChecksumsAsset = "SHA256SUMS.txt";

    private readonly HttpClient _http;
    private readonly string _releasesApi, _releasesFeed, _downloadBase;

    public UpdateService(HttpClient? http = null, string owner = "i3sam", string repo = "OrbitDrop")
    {
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        if (!_http.DefaultRequestHeaders.UserAgent.Any())
            _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("OrbitDrop", Current.ToString()));
        _releasesApi = $"https://api.github.com/repos/{owner}/{repo}/releases?per_page=20";
        _releasesFeed = $"https://github.com/{owner}/{repo}/releases.atom";
        _downloadBase = $"https://github.com/{owner}/{repo}/releases/download";
    }

    public static SemVersion Current
    {
        get
        {
            string? v = Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            return SemVersion.TryParse(v, out var s) ? s : new SemVersion(0, 0, 0, null);
        }
    }

    /// <summary>The newest published release that is newer than <paramref name="current"/>, or null.</summary>
    /// <remarks>
    /// Uses the GitHub API, and falls back to the public releases feed when the API refuses: anonymous API calls are
    /// limited to 60 an hour per network, which a shared office or school connection can use up.
    /// </remarks>
    public async Task<ReleaseInfo?> CheckAsync(SemVersion current, CancellationToken ct = default)
    {
        try
        {
            return await CheckApiAsync(current, ct).ConfigureAwait(false);
        }
        catch (HttpRequestException ex) when (ex.StatusCode is System.Net.HttpStatusCode.Forbidden or System.Net.HttpStatusCode.TooManyRequests)
        {
            return await CheckFeedAsync(current, ct).ConfigureAwait(false);
        }
    }

    private async Task<ReleaseInfo?> CheckFeedAsync(SemVersion current, CancellationToken ct)
    {
        string feed = await _http.GetStringAsync(_releasesFeed, ct).ConfigureAwait(false);
        var latest = PickLatestFromFeed(feed, _downloadBase, includePrereleases: current.Pre is not null);
        return latest is not null && latest.Version > current ? latest : null;
    }

    /// <summary>
    /// Newest release in the Atom feed (github.com/…/releases.atom). The feed has no asset list, so the standard
    /// asset URLs are assumed; a missing checksum file makes the download fail safely. Pure: unit-tested.
    /// </summary>
    public static ReleaseInfo? PickLatestFromFeed(string atom, string downloadBase, bool includePrereleases = true)
    {
        ReleaseInfo? best = null;
        foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(atom, "href=\"(?<page>[^\"]*/releases/tag/(?<tag>[^\"]+))\""))
        {
            string tag = Uri.UnescapeDataString(m.Groups["tag"].Value);
            if (!SemVersion.TryParse(tag, out var v) || (!includePrereleases && v.Pre is not null)) continue;
            if (best is null || v > best.Version)
                best = new ReleaseInfo(v, tag, $"{downloadBase}/{tag}/{SetupAsset}", $"{downloadBase}/{tag}/{ChecksumsAsset}", m.Groups["page"].Value);
        }
        return best;
    }

    private async Task<ReleaseInfo?> CheckApiAsync(SemVersion current, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, _releasesApi);
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        using var res = await _http.SendAsync(req, ct).ConfigureAwait(false);
        res.EnsureSuccessStatusCode();
        using var doc = await JsonDocument.ParseAsync(await res.Content.ReadAsStreamAsync(ct).ConfigureAwait(false), cancellationToken: ct).ConfigureAwait(false);
        // Someone on a stable build is only offered stable builds; pre-release users get anything newer.
        var latest = PickLatest(doc.RootElement, includePrereleases: current.Pre is not null);
        return latest is not null && latest.Version > current ? latest : null;
    }

    /// <summary>Highest-versioned, non-draft release that ships the installer asset. Pure: unit-tested with sample JSON.</summary>
    public static ReleaseInfo? PickLatest(JsonElement releases, bool includePrereleases = true)
    {
        ReleaseInfo? best = null;
        foreach (var r in releases.EnumerateArray())
        {
            if (r.TryGetProperty("draft", out var draft) && draft.GetBoolean()) continue;
            if (!includePrereleases && r.TryGetProperty("prerelease", out var pre) && pre.GetBoolean()) continue;
            string tag = r.GetProperty("tag_name").GetString() ?? "";
            if (!SemVersion.TryParse(tag, out var v)) continue;
            string? setup = null, sums = null;
            foreach (var a in r.GetProperty("assets").EnumerateArray())
            {
                string name = a.GetProperty("name").GetString() ?? "";
                string url = a.GetProperty("browser_download_url").GetString() ?? "";
                if (name.Equals(SetupAsset, StringComparison.OrdinalIgnoreCase)) setup = url;
                else if (name.Equals(ChecksumsAsset, StringComparison.OrdinalIgnoreCase)) sums = url;
            }
            if (setup is null) continue;
            if (best is null || v > best.Version)
                best = new ReleaseInfo(v, tag, setup, sums, r.GetProperty("html_url").GetString() ?? "");
        }
        return best;
    }

    /// <summary>
    /// Downloads the installer to a fresh temp folder and verifies it against SHA256SUMS.txt.
    /// Throws if the checksum is missing or doesn't match (the partial file is deleted).
    /// </summary>
    public async Task<string> DownloadAsync(ReleaseInfo release, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        if (release.ChecksumsUrl is null) throw new InvalidOperationException("This release has no checksum file, so it can't be verified.");
        string sums = await _http.GetStringAsync(release.ChecksumsUrl, ct).ConfigureAwait(false);
        string expected = ExpectedHash(sums, SetupAsset) ?? throw new InvalidOperationException("The checksum file doesn't list the installer.");

        string dir = Path.Combine(Path.GetTempPath(), "OrbitDrop-update", release.Version.ToString());
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, SetupAsset);
        try
        {
            using var res = await _http.GetAsync(release.SetupUrl, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            res.EnsureSuccessStatusCode();
            long total = res.Content.Headers.ContentLength ?? -1;
            await using (var input = await res.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
            await using (var output = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                var buffer = new byte[81920];
                long done = 0;
                int n;
                while ((n = await input.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
                {
                    await output.WriteAsync(buffer.AsMemory(0, n), ct).ConfigureAwait(false);
                    done += n;
                    if (total > 0) progress?.Report((double)done / total);
                }
            }
            string actual;
            await using (var fs = File.OpenRead(path)) actual = Convert.ToHexString(await SHA256.HashDataAsync(fs, ct).ConfigureAwait(false));
            if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The downloaded installer didn't match its published checksum, so it was discarded.");
            return path;
        }
        catch
        {
            try { File.Delete(path); } catch { /* best effort */ }
            throw;
        }
    }

    /// <summary>Finds "&lt;sha256&gt;  &lt;file&gt;" in a sha256sum-style listing.</summary>
    public static string? ExpectedHash(string listing, string file)
    {
        foreach (var raw in listing.Split('\n'))
        {
            var line = raw.Trim().TrimStart('﻿');
            int space = line.IndexOf(' ');
            if (space <= 0) continue;
            string hash = line[..space], name = line[(space + 1)..].Trim().TrimStart('*');
            if (name.Equals(file, StringComparison.OrdinalIgnoreCase) && hash.Length == 64) return hash;
        }
        return null;
    }
}
