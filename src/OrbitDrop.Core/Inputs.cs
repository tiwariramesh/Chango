namespace OrbitDrop.Core;

/// <summary>The part of a video or audio file to keep, chosen in the Trim window.</summary>
public sealed record TrimRange(TimeSpan Start, TimeSpan End)
{
    public TimeSpan Length => End - Start;
}
