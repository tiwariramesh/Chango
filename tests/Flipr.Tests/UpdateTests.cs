using System.Text.Json;
using Flipr.Infrastructure.Updates;
using Xunit;

namespace Flipr.Tests;

public class UpdateTests
{
    private static SemVersion V(string s) => SemVersion.TryParse(s, out var v) ? v : throw new ArgumentException(s);

    [Theory]
    [InlineData("0.1.2-alpha", "0.1.1-alpha")]
    [InlineData("0.1.0", "0.1.0-alpha")]
    [InlineData("0.2.0-alpha", "0.1.9")]
    [InlineData("1.0.0-beta", "1.0.0-alpha")]
    [InlineData("1.0.0-alpha.10", "1.0.0-alpha.2")]
    [InlineData("v0.1.10", "0.1.9")]
    public void Newer_versions_sort_higher(string newer, string older) => Assert.True(V(newer) > V(older));

    [Fact]
    public void Build_metadata_is_ignored() => Assert.Equal(0, V("0.1.2-alpha+abc123").CompareTo(V("0.1.2-alpha")));

    [Theory]
    [InlineData("")]
    [InlineData("latest")]
    [InlineData("1")]
    [InlineData("1.x.0")]
    public void Garbage_does_not_parse(string s) => Assert.False(SemVersion.TryParse(s, out _));

    private const string Releases = """
    [
      { "tag_name": "v0.1.3-alpha", "draft": true, "prerelease": true, "html_url": "d",
        "assets": [ { "name": "Flipr-Setup.exe", "browser_download_url": "draft" } ] },
      { "tag_name": "v0.1.2-alpha", "draft": false, "prerelease": true, "html_url": "page-012",
        "assets": [ { "name": "Flipr-Setup.exe", "browser_download_url": "setup-012" },
                    { "name": "SHA256SUMS.txt", "browser_download_url": "sums-012" } ] },
      { "tag_name": "v0.1.1", "draft": false, "prerelease": false, "html_url": "page-011",
        "assets": [ { "name": "Flipr-Setup.exe", "browser_download_url": "setup-011" } ] },
      { "tag_name": "v0.1.5-alpha", "draft": false, "prerelease": true, "html_url": "no-installer",
        "assets": [ { "name": "Flipr-Portable.zip", "browser_download_url": "zip" } ] }
    ]
    """;

    [Fact]
    public void Picks_the_newest_published_release_with_an_installer()
    {
        using var doc = JsonDocument.Parse(Releases);
        var r = UpdateService.PickLatest(doc.RootElement)!;
        Assert.Equal("v0.1.2-alpha", r.Tag);
        Assert.Equal("setup-012", r.SetupUrl);
        Assert.Equal("sums-012", r.ChecksumsUrl);
        Assert.Equal("page-012", r.PageUrl);
    }

    [Fact]
    public void Stable_users_are_not_offered_prereleases()
    {
        using var doc = JsonDocument.Parse(Releases);
        Assert.Equal("v0.1.1", UpdateService.PickLatest(doc.RootElement, includePrereleases: false)!.Tag);
    }

    [Fact]
    public void Reads_hashes_from_a_sha256sum_listing()
    {
        string hash = new('a', 64), other = new('b', 64);
        string listing = $"﻿{other}  Flipr-Portable.zip\r\n{hash}  Flipr-Setup.exe\r\n";
        Assert.Equal(hash, UpdateService.ExpectedHash(listing, "Flipr-Setup.exe"));
        Assert.Equal(other, UpdateService.ExpectedHash(listing, "Flipr-Portable.zip"));
        Assert.Null(UpdateService.ExpectedHash(listing, "missing.exe"));
    }
}

public class UpdateFeedTests
{
    private const string Feed = """
    <feed xmlns="http://www.w3.org/2005/Atom">
      <entry><title>Flipr v0.2.3-alpha</title><link rel="alternate" type="text/html" href="https://github.com/i3sam/Flipr/releases/tag/v0.2.3-alpha"/></entry>
      <entry><title>Flipr v0.2.2-alpha</title><link rel="alternate" type="text/html" href="https://github.com/i3sam/Flipr/releases/tag/v0.2.2-alpha"/></entry>
      <entry><title>Flipr v0.1.0</title><link rel="alternate" type="text/html" href="https://github.com/i3sam/Flipr/releases/tag/v0.1.0"/></entry>
    </feed>
    """;

    [Fact]
    public void Feed_fallback_finds_the_newest_release_and_its_standard_assets()
    {
        var r = UpdateService.PickLatestFromFeed(Feed, "https://github.com/i3sam/Flipr/releases/download")!;
        Assert.Equal("v0.2.3-alpha", r.Tag);
        Assert.Equal("https://github.com/i3sam/Flipr/releases/download/v0.2.3-alpha/Flipr-Setup.exe", r.SetupUrl);
        Assert.Equal("https://github.com/i3sam/Flipr/releases/download/v0.2.3-alpha/SHA256SUMS.txt", r.ChecksumsUrl);
        Assert.Equal("v0.1.0", UpdateService.PickLatestFromFeed(Feed, "x", includePrereleases: false)!.Tag);
    }
}
