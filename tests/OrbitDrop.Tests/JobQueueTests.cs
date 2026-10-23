using OrbitDrop.Core;
using OrbitDrop.Jobs;
using OrbitDrop.UI.Toasts;

namespace OrbitDrop.Tests;

public class JobQueueTests
{
    /// <summary>Test provider: reports per file after an optional delay, can fail on demand or throw outright.</summary>
    private sealed class FakeProvider(TimeSpan delay, Func<string, OutcomeStatus>? status = null, bool throws = false) : IOperationProvider
    {
        public string Id => "fake";
        public static OperationDescriptor Op { get; } = new("fake.op", "fake", OperationMode.Convert, "FAKE", null, "%1", "Fake", "Faking", 0);
        public IEnumerable<OperationDescriptor> GetOperations(FileSet files, OperationMode mode) => [Op];

        public async Task ExecuteAsync(OperationDescriptor operation, FileSet files, OperationContext context,
            IProgress<FileOutcome> progress, CancellationToken ct)
        {
            if (throws) throw new InvalidOperationException("engine exploded");
            foreach (var f in files)
            {
                await Task.Delay(delay, ct);
                var s = status?.Invoke(f) ?? OutcomeStatus.Succeeded;
                progress.Report(new FileOutcome(f, s, s == OutcomeStatus.Succeeded ? f + ".out" : null, s == OutcomeStatus.Failed ? "nope" : null));
            }
        }
    }

    private static Job Start(IOperationProvider p, params string[] files) =>
        new JobQueue(new OperationRegistry([p])).Enqueue(FakeProvider.Op, new FileSet(files.Select(f => @"C:\x\" + f)), Ctx.Default());

    [Fact]
    public async Task Reports_one_outcome_per_file()
    {
        var job = Start(new FakeProvider(TimeSpan.Zero), "a", "b", "c");
        await job.Completion;
        Assert.Equal(JobState.Completed, job.State);
        Assert.Equal(3, job.Succeeded);
    }

    [Fact]
    public async Task Cancel_marks_remaining_files_canceled()
    {
        var job = Start(new FakeProvider(TimeSpan.FromMilliseconds(200)), "a", "b", "c", "d");
        await Task.Delay(50);
        job.Cancel();
        await job.Completion;
        Assert.Equal(JobState.Canceled, job.State);
        Assert.Equal(4, job.Outcomes.Count);
        Assert.Contains(job.Outcomes, o => o.Status == OutcomeStatus.Canceled);
        Assert.Equal((ToastKind.Info, "Canceled"), (ToastManager.Summarize(job).Kind, ToastManager.Summarize(job).Title));
    }

    [Fact]
    public async Task Provider_exception_becomes_failed_outcomes_not_a_crash()
    {
        var job = Start(new FakeProvider(TimeSpan.Zero, throws: true), "a", "b");
        await job.Completion;
        Assert.Equal(2, job.Failed);
        Assert.All(job.Outcomes, o => Assert.Equal("engine exploded", o.Message));
        Assert.Equal(ToastKind.Error, ToastManager.Summarize(job).Kind);
    }

    [Fact]
    public async Task Partial_failure_is_summarised_as_a_warning()
    {
        var job = Start(new FakeProvider(TimeSpan.Zero, f => f.EndsWith("b") ? OutcomeStatus.Failed : OutcomeStatus.Succeeded), "a", "b", "c");
        await job.Completion;
        var (kind, title, detail) = ToastManager.Summarize(job);
        Assert.Equal(ToastKind.Warning, kind);
        Assert.Equal("2 saved · 1 failed", title);
        Assert.Contains("nope", detail);
    }

    [Fact]
    public async Task Single_success_names_the_new_file()
    {
        var job = Start(new FakeProvider(TimeSpan.Zero), "photo.jpg");
        await job.Completion;
        var (kind, title, _) = ToastManager.Summarize(job);
        Assert.Equal(ToastKind.Success, kind);
        Assert.Equal("Saved photo.jpg.out", title);
    }
}
