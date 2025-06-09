using System;
using System.Runtime.InteropServices;

namespace PendleCodeMonkey.MC68000EmulatorLib
{
    /// <summary>
    /// Acquire/release a lock.  This is a global lock for the emulator.  Using any
    /// additional lock methods should be carefully reviewed to avoid deadlocks.
    /// </summary>
    public static class Lock
    {
        [DllImport("macse_rust.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int lock_acquire();

        [DllImport("macse_rust.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int lock_release();

        public static IDisposable Acquire()
        {
            if (lock_acquire() != 0)
            {
                throw new InvalidOperationException("Failed to acquire Rust lock");
            }
            return new Releaser();
        }

        private sealed class Releaser : IDisposable
        {
            private bool _disposed = false;
            public void Dispose()
            {
                if (!_disposed)
                {
                    if (lock_release() != 0)
                    {
                        const string errorMessage = "Failed to release Rust lock - Attempted to release lock from a different thread";
                        Console.WriteLine(errorMessage);
                        throw new InvalidOperationException(errorMessage);
                    }
                    _disposed = true;
                }
            }
        }
    }
}
