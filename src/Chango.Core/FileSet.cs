using System.Collections;

namespace Chango.Core;

/// <summary>An immutable, de-duplicated set of absolute file paths received from a drop.</summary>
public sealed class FileSet : IReadOnlyList<string>
{
    private readonly string[] _paths;

    public FileSet(IEnumerable<string> paths)
    {
        _paths = paths.Where(p => !string.IsNullOrWhiteSpace(p))
                      .Select(Path.GetFullPath)
                      .Distinct(StringComparer.OrdinalIgnoreCase)
                      .ToArray();
    }

    public static FileSet Empty { get; } = new([]);

    public int Count => _paths.Length;
    public string this[int index] => _paths[index];
    public bool IsEmpty => _paths.Length == 0;

    /// <summary>Lower-case extensions including the dot (".png").</summary>
    public IEnumerable<string> Extensions => _paths.Select(p => Path.GetExtension(p).ToLowerInvariant()).Distinct();

    public bool All(Func<string, bool> predicate) => _paths.Length > 0 && _paths.All(predicate);

    public IEnumerator<string> GetEnumerator() => ((IEnumerable<string>)_paths).GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => _paths.GetEnumerator();
}
