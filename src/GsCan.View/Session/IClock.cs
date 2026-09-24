using System;

namespace GsCan.View.Session
{
    /// <summary>
    /// Clock for cyclic TxSlot scheduling.
    /// </summary>
    public interface IClock
    {
        IDisposable Schedule(TimeSpan delay, Action callback);
    }
}
