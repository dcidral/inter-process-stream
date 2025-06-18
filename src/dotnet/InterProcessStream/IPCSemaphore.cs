using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace InterProcessStream
{
    internal unsafe class IPCSemaphore
    {
        private byte* signalAddress;

        const byte NOT_SIGNALED = 0;
        const byte SIGNALED = 1;
        // TODO: how to calculate an ideal spin count limit?
        const int MAX_SPIN_COUNT = 500;
        const int MAX_SLEEP_TIME = 100;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static byte CompareExchange(ref byte* destination, byte exchange, byte comparand)
        {
            IntPtr destinationPtr = (IntPtr)destination;
            return *(byte*)Interlocked.CompareExchange(ref destinationPtr, (IntPtr)(&exchange), (IntPtr)(&comparand));
        }

        public IPCSemaphore(byte* signalAddress, bool? initializeSignaled = null)
        {
            this.signalAddress = signalAddress;
            if (initializeSignaled != null)
            {
                *this.signalAddress = initializeSignaled.Value ? SIGNALED : NOT_SIGNALED;
            }
        }

        public bool IsSignaled { get { return *this.signalAddress == SIGNALED; } }

        public bool Wait(TimeSpan timeout, CancellationToken cancelToken = default)
        {
            if (CompareExchange(ref this.signalAddress, NOT_SIGNALED, SIGNALED) == SIGNALED)
                return true;

            // Spin Lock stage
            SpinWait spinner = new SpinWait();
            Stopwatch timer = Stopwatch.StartNew();

            while (spinner.Count < MAX_SPIN_COUNT && timer.Elapsed < timeout && !cancelToken.IsCancellationRequested)
            {
                if (CompareExchange(ref this.signalAddress, NOT_SIGNALED, SIGNALED) == SIGNALED)
                    return true;

                spinner.SpinOnce();
                /*
                I thought about doing an elaborated logic to determine wheter to spin, yield or
                sleep but SpinWait already does this internally in a better way than I could
                ever come up to.
                */
            }

            // Slow wait stage
            int sleepCount = 0;
            while (*this.signalAddress == NOT_SIGNALED && timer.Elapsed < timeout && !cancelToken.IsCancellationRequested)
            {
                sleepCount++;
                int sleepTime = Math.Min(sleepCount, MAX_SLEEP_TIME);
                Thread.Sleep(sleepTime);
            }

            return CompareExchange(ref this.signalAddress, NOT_SIGNALED, SIGNALED) == SIGNALED;
        }

        public void Signal()
        {
            *this.signalAddress = SIGNALED;
        }
    }
}
