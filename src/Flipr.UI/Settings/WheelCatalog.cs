using Flipr.Core;

namespace Flipr.UI.Settings;

/// <summary>One thing the user can turn off: an option, a whole group, or one choice inside a group.</summary>
public sealed record WheelChoice(string Key, string Label, string? Glyph);

/// <summary>A wheel segment as listed in Settings. <see cref="Members"/> is non-empty for groups.</summary>
public sealed record WheelEntry(WheelChoice Choice, IReadOnlyList<WheelChoice> Members);

public sealed record WheelModeSection(OperationMode Mode, IReadOnlyList<WheelEntry> Entries);

/// <summary>Everything Flipr offers for one kind of file (Photos, Video …), by wheel mode.</summary>
public sealed record WheelCategory(string Title, string Glyph, IReadOnlyList<WheelModeSection> Modes);
