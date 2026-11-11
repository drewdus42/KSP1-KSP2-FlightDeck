using System;
using System.Collections.Concurrent;
using System.Threading;

namespace FlightDeck.Core
{
    public interface IMainThreadDispatcher
    {
        bool IsMainThread { get; }

        /// <summary>Runs work on the game's main thread, blocking the caller until it completes.</summary>
        T Invoke<T>(Func<T> action, int timeoutMilliseconds);
    }

    /// <summary>
    /// KSP's API is single threaded. HTTP requests arrive on worker threads, so every
    /// game call is posted here and drained from the addon's Update() method.
    /// </summary>
    public sealed class QueueDispatcher : IMainThreadDispatcher
    {
        private readonly ConcurrentQueue<WorkItem> _queue = new ConcurrentQueue<WorkItem>();
        private readonly int _mainThreadId;

        public QueueDispatcher()
        {
            _mainThreadId = Thread.CurrentThread.ManagedThreadId;
        }

        public bool IsMainThread
        {
            get { return Thread.CurrentThread.ManagedThreadId == _mainThreadId; }
        }

        public int PendingCount
        {
            get { return _queue.Count; }
        }

        public T Invoke<T>(Func<T> action, int timeoutMilliseconds)
        {
            if (action == null)
            {
                throw new ArgumentNullException("action");
            }

            if (IsMainThread)
            {
                return action();
            }

            var item = new WorkItem { Work = delegate { return action(); } };
            _queue.Enqueue(item);

            if (!item.Done.Wait(timeoutMilliseconds))
            {
                // The caller already gave up; make sure a queued command (staging, throttle...)
                // does not fire seconds later when the game finally resumes.
                item.Cancelled = true;
                item.Done.Set();
                throw new TimeoutException("The game did not answer in time. It may be loading, paused, or minimised.");
            }

            if (item.Error != null)
            {
                throw item.Error;
            }

            return (T)item.Result;
        }

        /// <summary>Drains pending work. Call once per frame from Update().</summary>
        public int Pump(int maxItems = 16)
        {
            int processed = 0;
            WorkItem item;
            while (processed < maxItems && _queue.TryDequeue(out item))
            {
                if (!item.Cancelled)
                {
                    try
                    {
                        item.Result = item.Work();
                    }
                    catch (Exception ex)
                    {
                        item.Error = ex;
                    }
                }

                item.Done.Set();
                processed++;
            }

            return processed;
        }

        private sealed class WorkItem
        {
            public Func<object> Work;
            public object Result;
            public Exception Error;
            public volatile bool Cancelled;
            public readonly ManualResetEventSlim Done = new ManualResetEventSlim(false);
        }
    }
}
