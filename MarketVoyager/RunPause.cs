namespace MarketVoyager;

// A pause never abandons an in-flight transaction or starts another one.
public sealed class RunPause
{
    public bool Requested { get; private set; }
    public bool Paused { get; private set; }
    private DateTime began;
    public TimeSpan Toggle(DateTime now)
    {
        if (!Requested) { Requested = true; return TimeSpan.Zero; }
        var elapsed = Paused ? now - began : TimeSpan.Zero;
        Reset(); return elapsed;
    }
    public bool Enter(bool safePhase, bool transactionPending, bool moving, DateTime now)
    {
        if (Requested && !transactionPending && !moving && safePhase && !Paused)
        { Paused = true; began = now; }
        return Paused;
    }
    public void Reset() { Requested = Paused = false; }
}
