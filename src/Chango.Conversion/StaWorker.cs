namespace Chango.Conversion;

/// <summary>Runs work on a dedicated background STA thread (WPF imaging objects require STA).</summary>
public static class StaWorker
{
    public static Task RunAsync(Action work, string name = "Chango worker")
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try { work(); tcs.SetResult(); }
            catch (OperationCanceledException oce) { tcs.SetCanceled(oce.CancellationToken); }
            catch (Exception ex) { tcs.SetException(ex); }
        })
        { IsBackground = true, Name = name, Priority = ThreadPriority.BelowNormal };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return tcs.Task;
    }
}
