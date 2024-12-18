using Grayjay.Desktop.POC;
using System.Collections.Concurrent;

namespace Grayjay.ClientServer.Threading
{
    public class ManagedThreadPool
    {
        private readonly object _queueLock = new object();
        private readonly ConcurrentQueue<Action> _queue = new ConcurrentQueue<Action>();
        private readonly List<Thread> _threads = new List<Thread>();

        private bool _active = false;

        public ManagedThreadPool(int threadCount) 
        {
            Logger.i(nameof(ManagedThreadPool), $"New threadpool created ({threadCount})");
            _active = true;
            AdjustThreadCount(threadCount);
        }

        ~ManagedThreadPool()
        {
            Stop();
        }


        public void Run(Action task)
        {
            if (task == null)
                throw new ArgumentNullException(nameof(task));

            lock (_queueLock)
            {
                if (!_active)
                    throw new InvalidOperationException("ThreadPool has been stopped.");

                _queue.Enqueue(task);
                Monitor.Pulse(_queueLock);
            }
        }

        public void AdjustThreadCount(int count)
        {
            lock(_threads)
            {
                int toAdd = count - _threads.Count;
                for (int i = 0; i < count; i++)
                {
                    Thread worker = new Thread(ManagedThread)
                    {
                        IsBackground = true
                    };
                    _threads.Add(worker);
                    worker.Start();
                }

                //TODO: Reduce threadpool
            }
        }


        public void Stop()
        {
            lock (_queueLock)
            {
                if (_active)
                    return;

                _active = false;
                Monitor.PulseAll(_queueLock);
            }

            foreach (Thread worker in _threads)
            {
                if (worker.IsAlive)
                    worker.Join();
            }
        }

        private void ManagedThread()
        {
            while (_active)
            {
                Action task = null;
                lock (_queueLock)
                {
                    while (_active && _queue.Count == 0)
                        Monitor.Wait(_queueLock);

                    if (!_active && _queue.Count == 0)
                        return;


                    if (!_queue.TryDequeue(out task))
                        task = null;
                }
                task?.Invoke();
            }
        }
    }
}
