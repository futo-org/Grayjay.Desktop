namespace Grayjay.ClientServer;

public class Debouncer
{
    private CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();
    private readonly TimeSpan _delay;
    private readonly Action _action;

    public Debouncer(TimeSpan delay, Action action)
    {
        this._delay = delay;
        this._action = action;
    }

    public void Call()
    {
        _cancellationTokenSource.Cancel();
        _cancellationTokenSource = new CancellationTokenSource();

        Task.Delay(_delay, _cancellationTokenSource.Token)
            .ContinueWith(t =>
            {
                if (t.IsCompletedSuccessfully)
                {
                    _action();
                }
            }, TaskScheduler.Default);
    }
}