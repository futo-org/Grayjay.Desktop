namespace Grayjay.ClientServer.Sabr
{
    public class AsyncSignal
    {
        private TaskCompletionSource _tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly object _lock = new object();

        public Task WaitTask
        {
            get { lock (_lock) return _tcs.Task; }
        }

        public void Set()
        {
            TaskCompletionSource previous;
            lock (_lock)
            {
                previous = _tcs;
                _tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            }
            previous.TrySetResult();
        }

        public async Task<bool> WaitAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            var task = WaitTask;
            if (task.IsCompleted)
                return true;
            try
            {
                await task.WaitAsync(timeout, cancellationToken);
                return true;
            }
            catch (TimeoutException)
            {
                return false;
            }
        }
    }
}
