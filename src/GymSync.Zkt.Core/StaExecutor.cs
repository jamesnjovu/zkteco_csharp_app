using System.Collections.Concurrent;

namespace GymSync.Zkt.Core;

/// <summary>
/// Single-threaded STA worker that serialises all CZKEM COM calls.
/// CZKEM requires STA; ASP.NET Core runs MTA threads — every SDK call must go through here.
/// </summary>
public sealed class StaExecutor : IDisposable
{
    private readonly Thread _thread;
    private readonly BlockingCollection<Action> _queue = new();
    private readonly CancellationTokenSource _cts = new();

    public StaExecutor(string name = "ZkSta")
    {
        _thread = new Thread(Loop)
        {
            IsBackground = true,
            Name = name,
        };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
    }

    private void Loop()
    {
        try
        {
            foreach (var work in _queue.GetConsumingEnumerable(_cts.Token))
            {
                try { work(); }
                catch { /* swallowed; individual calls report via their TCS */ }
            }
        }
        catch (OperationCanceledException) { /* normal shutdown */ }
    }

    public T Invoke<T>(Func<T> func)
    {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        _queue.Add(() =>
        {
            try { tcs.SetResult(func()); }
            catch (Exception ex) { tcs.SetException(ex); }
        });
        return tcs.Task.GetAwaiter().GetResult();
    }

    public void Invoke(Action action) =>
        Invoke<object?>(() => { action(); return null; });

    public void Dispose()
    {
        _queue.CompleteAdding();
        _cts.Cancel();
        _thread.Join(TimeSpan.FromSeconds(2));
        _queue.Dispose();
        _cts.Dispose();
    }
}
