using Chango.Core;

namespace Chango.Jobs;

public enum JobState { Queued, Running, Completed, Canceled }

/// <summary>
/// One wheel drop = one job. Raises <see cref="Changed"/> on the synchronization context it was created on
/// (the UI thread), so the UI can bind to it directly.
/// </summary>
public sealed class Job
{
    private readonly CancellationTokenSource _cts = new();
    private readonly List<FileOutcome> _outcomes = [];
    private readonly SynchronizationContext? _context;
    private readonly TaskCompletionSource _done = new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal Job(int id, OperationDescriptor operation, FileSet files)
    {
        Id = id;
        Operation = operation;
        Files = files;
        _context = SynchronizationContext.Current;
    }

    public int Id { get; }
    /// <summary>When the job was queued (UTC): anything it made is newer than this.</summary>
    public DateTime CreatedUtc { get; } = DateTime.UtcNow;
    public OperationDescriptor Operation { get; }
    public FileSet Files { get; }
    public JobState State { get; private set; } = JobState.Queued;
    public IReadOnlyList<FileOutcome> Outcomes { get { lock (_outcomes) return _outcomes.ToList(); } }
    public int Total => Files.Count;
    public int Processed { get { lock (_outcomes) return _outcomes.Count; } }
    public int Succeeded => Count(OutcomeStatus.Succeeded);
    public int Failed => Count(OutcomeStatus.Failed);
    public int Skipped => Count(OutcomeStatus.Skipped);
    public string? CurrentFile { get; private set; }
    /// <summary>Progress (0–1) within the current file, for long operations such as video.</summary>
    public double CurrentFraction { get; private set; }
    public Task Completion => _done.Task;
    public CancellationToken Token => _cts.Token;
    public bool IsFinished => State is JobState.Completed or JobState.Canceled;

    public event Action<Job>? Changed;

    public void Cancel()
    {
        if (!IsFinished) _cts.Cancel();
    }

    private int Count(OutcomeStatus s) { lock (_outcomes) return _outcomes.Count(o => o.Status == s); }

    internal void Report(FileOutcome outcome)
    {
        lock (_outcomes) _outcomes.Add(outcome);
        CurrentFile = outcome.Source;
        CurrentFraction = 0;
        Raise();
    }

    internal void ReportFraction(double fraction)
    {
        fraction = Math.Clamp(fraction, 0, 1);
        if (Math.Abs(fraction - CurrentFraction) < 0.01 && fraction < 1) return; // throttle UI updates
        CurrentFraction = fraction;
        Raise();
    }

    internal void SetState(JobState state)
    {
        State = state;
        Raise();
        if (IsFinished) _done.TrySetResult();
    }

    /// <summary>Gives every file that never got an outcome a terminal one (canceled or failed).</summary>
    internal void CompleteMissing(OutcomeStatus status, string message)
    {
        HashSet<string> seen;
        lock (_outcomes) seen = _outcomes.Select(o => o.Source).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var file in Files.Where(f => !seen.Contains(f)))
            lock (_outcomes) _outcomes.Add(new FileOutcome(file, status, Message: message));
    }

    private void Raise()
    {
        if (Changed is null) return;
        if (_context is null || SynchronizationContext.Current == _context) Changed(this);
        else _context.Post(_ => Changed?.Invoke(this), null);
    }

    private sealed class Reporter(Job job) : IProgress<FileOutcome>
    {
        public void Report(FileOutcome value) => job.Report(value);
    }

    internal IProgress<FileOutcome> CreateReporter() => new Reporter(this);
}

/// <summary>Runs jobs in the background with a concurrency limit. Never blocks the caller.</summary>
public sealed class JobQueue
{
    private readonly OperationRegistry _registry;
    private readonly SemaphoreSlim _slots;
    private int _nextId;
    private int _active;

    /// <summary>Jobs queued or running (e.g. so an automatic update waits until work is finished).</summary>
    public int ActiveJobs => Volatile.Read(ref _active);

    public JobQueue(OperationRegistry registry, int maxConcurrentJobs = 2)
    {
        _registry = registry;
        _slots = new SemaphoreSlim(Math.Max(1, maxConcurrentJobs));
    }

    public event Action<Job>? JobStarted;

    public Job Enqueue(OperationDescriptor operation, FileSet files, OperationContext context)
    {
        var job = new Job(Interlocked.Increment(ref _nextId), operation, files);
        Interlocked.Increment(ref _active);
        _ = job.Completion.ContinueWith(_ => Interlocked.Decrement(ref _active), TaskScheduler.Default);
        var provider = _registry.ProviderFor(operation);
        JobStarted?.Invoke(job);
        _ = Task.Run(() => RunAsync(job, provider, context));
        return job;
    }

    private async Task RunAsync(Job job, IOperationProvider provider, OperationContext context)
    {
        try
        {
            await _slots.WaitAsync(job.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            job.CompleteMissing(OutcomeStatus.Canceled, "Canceled");
            job.SetState(JobState.Canceled);
            return;
        }

        try
        {
            job.SetState(JobState.Running);
            await provider.ExecuteAsync(job.Operation, job.Files, context with { ReportFileFraction = job.ReportFraction }, job.CreateReporter(), job.Token).ConfigureAwait(false);
            job.CompleteMissing(OutcomeStatus.Failed, "The operation did not report a result for this file.");
            job.SetState(JobState.Completed);
        }
        catch (OperationCanceledException)
        {
            job.CompleteMissing(OutcomeStatus.Canceled, "Canceled");
            job.SetState(JobState.Canceled);
        }
        catch (Exception ex)
        {
            job.CompleteMissing(OutcomeStatus.Failed, ex.Message);
            job.SetState(JobState.Completed);
        }
        finally
        {
            _slots.Release();
        }
    }
}
