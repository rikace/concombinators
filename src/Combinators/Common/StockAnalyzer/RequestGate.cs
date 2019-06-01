using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace StockAnalyzer.CS
{
    public class RequestGate
    {
        readonly SemaphoreSlim semaphore;
        public RequestGate(int count) =>
            semaphore = new SemaphoreSlim(initialCount: count, maxCount: count);

        public async Task<IDisposable> AsyncAcquire(TimeSpan timeout, CancellationToken cancellationToken = new CancellationToken())
        {
            var ok = await semaphore.WaitAsync(timeout, cancellationToken);
            if (ok)
            {
                Thread.BeginCriticalRegion();
                return new SemaphoreSlimRelease(semaphore);
            }
            throw new Exception("couldn't acquire a semaphore");
        }
        private class SemaphoreSlimRelease : IDisposable
        {
            SemaphoreSlim semaphore;
            public SemaphoreSlimRelease(SemaphoreSlim semaphore) => this.semaphore = semaphore;
            public void Dispose()
            {
                Thread.EndCriticalRegion();
                semaphore.Release();
            }
        }
    }
}
