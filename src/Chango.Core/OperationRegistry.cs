namespace Chango.Core;

/// <summary>Single source of truth for what the wheel may offer. The UI never hard-codes options.</summary>
public sealed class OperationRegistry
{
    private readonly Dictionary<string, IOperationProvider> _providers;

    public OperationRegistry(IEnumerable<IOperationProvider> providers)
    {
        _providers = providers.ToDictionary(p => p.Id, StringComparer.Ordinal);
    }

    public IReadOnlyCollection<IOperationProvider> Providers => _providers.Values;

    /// <summary>All options valid for every file in the set, in display order.</summary>
    public IReadOnlyList<OperationDescriptor> Query(FileSet files, OperationMode mode)
    {
        if (files.IsEmpty) return [];
        return _providers.Values
            .SelectMany(p => p.GetOperations(files, mode))
            .Where(d => d.Mode == mode)
            .OrderBy(d => d.Order)
            .ThenBy(d => d.Label, StringComparer.CurrentCulture)
            .ToList();
    }

    public IOperationProvider ProviderFor(OperationDescriptor descriptor) =>
        _providers.TryGetValue(descriptor.ProviderId, out var p)
            ? p
            : throw new KeyNotFoundException($"No provider '{descriptor.ProviderId}' for '{descriptor.Id}'");
}
