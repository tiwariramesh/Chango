using Chango.Core;
using Chango.Windows.Interop;

namespace Chango.App;

/// <summary>
/// Collects choices for options marked <see cref="OperationDescriptor.NeedsInput"/> before their job starts:
/// a folder picker, the metadata editor, the photo editor, the trim window … Returning Ok = false cancels quietly.
/// </summary>
public sealed class InputBroker
{
    private readonly List<(string Prefix, Func<OperationDescriptor, FileSet, Win32.POINT, Task<(bool, object?)>> Ask)> _handlers = [];

    /// <summary>Registers the dialog for every option whose id starts with <paramref name="idPrefix"/>.</summary>
    public void Register(string idPrefix, Func<OperationDescriptor, FileSet, Win32.POINT, Task<(bool, object?)>> ask) =>
        _handlers.Add((idPrefix, ask));

    public Task<(bool Ok, object? Input)> AskAsync(OperationDescriptor op, FileSet files, Win32.POINT anchor)
    {
        foreach (var (prefix, ask) in _handlers)
            if (op.Id.StartsWith(prefix, StringComparison.Ordinal)) return ask(op, files, anchor);
        return Task.FromResult<(bool, object?)>((true, null));
    }
}
