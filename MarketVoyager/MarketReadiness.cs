namespace MarketVoyager;

internal static class MarketReadiness
{
    public static bool Ready(uint count, int displayedCount, bool updatePending, bool scrollPending, bool waiting) =>
        count <= 100 && displayedCount == count && !updatePending && !scrollPending &&
        (count > 0 || !waiting);
}
