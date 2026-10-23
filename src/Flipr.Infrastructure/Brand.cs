namespace Flipr.Infrastructure;

/// <summary>
/// Flipr's identity in one place: where its releases are published and where "Star on GitHub" points. Colours live in
/// Themes/Tokens.*.xaml, the icon in tools/IconGen.
/// </summary>
public static class Brand
{
    public const string Name = "Flipr";

    /// <summary>The colour every Flipr surface is built around (Flip Teal).</summary>
    public const string AccentHex = "#0EA5A4";

    /// <summary>
    /// GitHub "owner/repo" that publishes Flipr releases. Null until that repository exists, which keeps the updater
    /// and the "star" prompt switched off: no network request is ever made to a repository that isn't Flipr's.
    /// </summary>
    public static readonly string? ReleasesRepo = null;

    public static string? HomepageUrl => ReleasesRepo is null ? null : $"https://github.com/{ReleasesRepo}";

    /// <summary>The MIT-licensed project Flipr is built from, credited in Settings → About.</summary>
    public const string UpstreamName = "OrbitDrop";
    public const string UpstreamUrl = "https://github.com/i3sam/OrbitDrop";
}
