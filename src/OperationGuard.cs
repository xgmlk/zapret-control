using System.Threading;

namespace ZapretControl
{
    // Process-wide gate for operations that stop/modify the zapret service or
    // payload. Background auto-updates and user actions must not overlap.
    public static class OperationGuard
    {
        private static int _busy;

        public static bool IsBusy
        {
            get { return Interlocked.CompareExchange(ref _busy, 0, 0) != 0; }
        }

        public static bool TryEnter()
        {
            return Interlocked.CompareExchange(ref _busy, 1, 0) == 0;
        }

        public static void Exit()
        {
            Interlocked.Exchange(ref _busy, 0);
        }
    }
}
