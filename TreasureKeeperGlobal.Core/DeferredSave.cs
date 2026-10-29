namespace TreasureKeeperGlobal.Core;

// Coalesces repeated timestamp updates without postponing indefinitely under continuous traffic.
public sealed class DeferredSave
{
    public const long IntervalMs = 2000;
    public bool Dirty { get; private set; }
    private long nextAttempt;

    public void MarkDirty(long now)
    {
        if (!Dirty) nextAttempt = now + IntervalMs;
        Dirty = true;
    }

    public void Saved() => Dirty = false;

    public void Flush(long now, Action save, bool force = false)
    {
        if (!Dirty || (!force && now < nextAttempt)) return;
        nextAttempt = now + IntervalMs; // On failure retain dirty state, but avoid a per-frame retry/log storm.
        save();
        Saved();
    }
}
