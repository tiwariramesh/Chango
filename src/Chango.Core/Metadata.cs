namespace Chango.Core;

public enum MetadataFieldKind { Text, MultiLine, List, Rating, Number, Date }

/// <summary>An editable property. <see cref="Key"/> is a Windows property name ("System.Title").</summary>
public sealed record MetadataField(string Key, string Label, MetadataFieldKind Kind, string? Value, string? Hint = null);

/// <summary>What the Info window shows for one file: read-only facts, then editable fields.</summary>
public sealed record MetadataSnapshot(
    string Path,
    string Kind,
    IReadOnlyList<KeyValuePair<string, string>> Facts,
    IReadOnlyList<MetadataField> Fields,
    bool HasLocation);

/// <summary>Changes made in the Info window. A null value clears the property.</summary>
public sealed record MetadataEdit(IReadOnlyDictionary<string, string?> Changes, bool RemoveLocation)
{
    public bool IsEmpty => Changes.Count == 0 && !RemoveLocation;
}
