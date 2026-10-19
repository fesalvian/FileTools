// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.Threading;
using System.Threading.Tasks;

namespace FileTools
{
    public static class BatchOperations
    {
        // A mutex recovers after a crashed worker; keep acquire/release on the same dedicated thread.
        public static Task<T> RunAsync<T>(Func<Task<T>> operation, CancellationToken token)
        {
            return Task.Run(() =>
            {
                string user = System.Security.Principal.WindowsIdentity.GetCurrent().User.Value;
                using (var gate = new Mutex(false, "Local\\FileTools.Worker." + user))
                {
                    bool acquired = false;
                    try
                    {
                        try { acquired = WaitHandle.WaitAny(new[] { gate, token.WaitHandle }) == 0; }
                        catch (AbandonedMutexException) { acquired = true; }
                        token.ThrowIfCancellationRequested();
                        return operation().GetAwaiter().GetResult();
                    }
                    finally { if (acquired) gate.ReleaseMutex(); }
                }
            }, token);
        }
    }
}
