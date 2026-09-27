namespace pixinit.Application.Scanning;

// One storage worker globally per composed app. A blocked native operation holds
// its lease until it returns; no queued replacement workers can accumulate.
public sealed class StorageOperationGate
{
    private int active;
    public IDisposable Enter()
    {
        if (Interlocked.CompareExchange(ref active, 1, 0) != 0) throw new InvalidOperationException("A storage operation is still finishing.");
        return new Lease(this);
    }
    private sealed class Lease(StorageOperationGate owner) : IDisposable
    {
        private StorageOperationGate? gate = owner;
        public void Dispose() { var value = Interlocked.Exchange(ref gate, null); if (value is not null) Volatile.Write(ref value.active, 0); }
    }
}
