using System;
using System.Collections.Generic;
using System.Threading;

namespace ZapretControl
{
    // Bounds all network work of one strategy check. Strategies are tested one
    // at a time, and every independent probe inside a strategy shares the gate.
    internal sealed class NetworkGate : IDisposable
    {
        private readonly Semaphore _semaphore;
        private bool _disposed;

        internal NetworkGate(int limit)
        {
            int value = limit < 1 ? 1 : (limit > 16 ? 16 : limit);
            _semaphore = new Semaphore(value, value);
        }

        internal void Enter()
        {
            _semaphore.WaitOne();
        }

        internal void Exit()
        {
            try
            {
                _semaphore.Release();
            }
            catch (SemaphoreFullException)
            {
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            _semaphore.Close();
        }
    }

    internal static class ParallelWork
    {
        // One dedicated thread per target: probes block on network I/O and the
        // thread pool would inject replacements too slowly. The number of
        // concurrent connections is still bounded by NetworkGate.
        internal static void RunAll(Action[] actions, Action<Exception> onError)
        {
            if (actions == null || actions.Length == 0)
            {
                return;
            }
            Thread[] threads = new Thread[actions.Length];
            for (int i = 0; i < actions.Length; i++)
            {
                Action action = actions[i];
                Thread thread = new Thread(delegate()
                {
                    try
                    {
                        action();
                    }
                    catch (Exception ex)
                    {
                        if (onError != null)
                        {
                            onError(ex);
                        }
                    }
                });
                thread.IsBackground = true;
                threads[i] = thread;
                thread.Start();
            }
            for (int i = 0; i < threads.Length; i++)
            {
                threads[i].Join();
            }
        }

        internal static void RunAll(List<Action> actions, Action<Exception> onError)
        {
            if (actions == null)
            {
                return;
            }
            RunAll(actions.ToArray(), onError);
        }
    }
}
