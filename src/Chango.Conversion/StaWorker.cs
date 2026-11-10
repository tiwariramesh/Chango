namespace Chango.Conversion;

/// <summary>Runs work on dedicated background STA threads (WPF imaging objects require STA).</summary>
public static class StaWorker
{
    /// <summary>
    /// Files worked on at once by <see cref="ForEachAsync{T}"/>: half the processor's threads, at most four. libvips
    /// spreads each picture over several threads too, and four full-size photos in memory at once is plenty.
    /// </summary>
    public static int Parallelism { get; } = Math.Clamp(Environment.ProcessorCount / 2, 1, 4);

    /// <summary>
    /// Runs <paramref name="work"/> on a new STA thread. Jobs the user is waiting for run at normal priority; work
    /// nobody waits on yet (size estimates) passes <see cref="ThreadPriority.BelowNormal"/>.
    /// </summary>
    public static Task RunAsync(Action work, string name = "Chango worker", ThreadPriority priority = ThreadPriority.Normal)
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try { work(); tcs.SetResult(); }
            catch (OperationCanceledException oce) { tcs.SetCanceled(oce.CancellationToken); }
            catch (Exception ex) { tcs.SetException(ex); }
        })
        { IsBackground = true, Name = name, Priority = priority };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return tcs.Task;
    }

    /// <summary>
    /// Runs <paramref name="work"/> for each item on up to <see cref="Parallelism"/> STA threads, taking items in
    /// order. Stops handing out items once <paramref name="ct"/> is cancelled; the first exception (or the
    /// cancellation) is rethrown after every thread has finished its current item.
    /// </summary>
    public static Task ForEachAsync<T>(IReadOnlyList<T> items, Action<T> work, CancellationToken ct, string name = "Chango worker")
    {
        if (items.Count <= 1)
            return RunAsync(() => { foreach (var item in items) { ct.ThrowIfCancellationRequested(); work(item); } }, name);
        int next = -1;
        var workers = Enumerable.Range(0, Math.Min(Parallelism, items.Count)).Select(i => RunAsync(() =>
        {
            int index;
            while ((index = Interlocked.Increment(ref next)) < items.Count)
            {
                ct.ThrowIfCancellationRequested();
                work(items[index]);
            }
        }, $"{name} {i + 1}")).ToArray();
        return Task.WhenAll(workers);
    }
}
