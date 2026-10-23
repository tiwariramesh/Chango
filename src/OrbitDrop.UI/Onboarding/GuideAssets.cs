using System.Windows.Media.Imaging;
using OrbitDrop.Core;

namespace OrbitDrop.UI.Onboarding;

/// <summary>A wheel state to show in the guide: mode, files, highlighted segment, hub text, optional open group.</summary>
public sealed record WheelShot(OperationMode Mode, string[] Files, int Highlight, string Value, string Detail, string? Group = null, string[]? Hidden = null, string[]? Order = null);

/// <summary>A rendered wheel plus its segment labels (so the guide can point at "JPG" wherever it is).</summary>
public sealed record WheelImage(BitmapSource Image, IReadOnlyList<string> Labels);

/// <summary>
/// Real OrbitDrop visuals for the guide's demos, rendered by the app from the live registry, wheel and toast code,
/// so the guide always matches what the user will actually see (theme, options, wording).
/// </summary>
public interface IGuideAssets
{
    WheelImage Wheel(WheelShot shot);
    BitmapSource Toast(string title, string detail);
}

/// <summary>
/// Lets a drag that starts inside OrbitDrop (the guide's practice photo, the Float shelf) open the wheel too.
/// Drags from our own windows have no shell drag image, which is what normally arms the trigger key.
/// </summary>
public static class InternalDrag
{
    public static Action? Starting { get; set; }
}
