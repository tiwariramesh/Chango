namespace Chango.Infrastructure;

/// <summary>
/// Chango's identity in one place: who makes it, how to reach them, and where releases are published. Colours live in
/// Themes/Tokens.*.xaml, the icon in tools/IconGen.
/// </summary>
public static class Brand
{
    public const string Name = "Chango";
    public const string Tagline = "Drag. Tap. Chango.";

    /// <summary>What Chango is, in a few words: shown under the name wherever there is room for it.</summary>
    public const string Subtitle = "Drag-and-drop file converter and manager";

    /// <summary>The colour every Chango surface is built around (Chango Teal).</summary>
    public const string AccentHex = "#0EA5A4";

    public const string Author = "Ramesh Tiwari";
    public const string AuthorLocation = "Perth, Australia";
    public const string SupportEmail = "ramestiwarii@gmail.com";
    public const string SourceUrl = "https://github.com/tiwariramesh/Chango";

    /// <summary>
    /// Where About → Website goes. The GitHub page until chango.rameshtiwari.com is live; then set this to the
    /// website.
    /// </summary>
    public const string WebsiteUrl = SourceUrl;

    /// <summary>
    /// GitHub "owner/repo" that publishes Chango releases: the GitHub version's updater checks it (see docs/PRIVACY.md,
    /// "Network access"), and the "star" prompt links to it. Null switches both off, and Chango then makes no network
    /// requests at all.
    /// </summary>
    public static readonly string? ReleasesRepo = "tiwariramesh/Chango";

    public static string? HomepageUrl => ReleasesRepo is null ? null : $"https://github.com/{ReleasesRepo}";
}
