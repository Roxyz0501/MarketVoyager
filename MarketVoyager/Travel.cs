using System.Numerics;
using Dalamud.Plugin;
namespace MarketVoyager;

internal sealed class Travel(IDalamudPluginInterface pi)
{
    private CancellationTokenSource? pathCancellation;
    private Task<List<Vector3>>? path;
    public bool Loaded(string name) => pi.InstalledPlugins.Any(x => x.InternalName == name && x.IsLoaded);
    public bool Ready => Loaded("Lifestream") && Loaded("vnavmesh");
    public bool Busy => pi.GetIpcSubscriber<bool>("Lifestream.IsBusy").InvokeFunc();
    public bool AtLimsaAetheryte => pi.GetIpcSubscriber<uint>("Lifestream.GetActiveAetheryte").InvokeFunc() == 8;
    public bool CanVisit(string name) =>
        pi.GetIpcSubscriber<string, bool>("Lifestream.CanVisitSameDC").InvokeFunc(name) ||
        pi.GetIpcSubscriber<string, bool>("Lifestream.CanVisitCrossDC").InvokeFunc(name);
    public void ChangeWorld(string name)
    {
        if (Busy) throw new InvalidOperationException("Lifestream is busy");
        if (!AtLimsaAetheryte) throw new InvalidOperationException("Walk to the Limsa main aetheryte before changing world");
        var gateway = pi.GetIpcSubscriber<uint, int?>("Lifestream.GetWorldChangeAetheryteByTerritoryType").InvokeFunc(129);
        if (gateway is null) throw new InvalidOperationException("Limsa gateway unavailable");
        // This tour only includes worlds reachable through the aetheryte within the region.
        // Never request the legacy logout/character-selection DC transfer route.
        pi.GetIpcSubscriber<string, bool, string, bool, int?, bool?, bool?, object>("Lifestream.TPAndChangeWorld")
            .InvokeAction(name, false, "", true, gateway, false, false);
    }
    public bool MeshReady => pi.GetIpcSubscriber<bool>("vnavmesh.Nav.IsReady").InvokeFunc();
    public bool Move(Vector3 origin, Vector3 destination)
    {
        if (pi.GetIpcSubscriber<bool>("vnavmesh.Path.IsRunning").InvokeFunc() ||
            pi.GetIpcSubscriber<bool>("vnavmesh.SimpleMove.PathfindInProgress").InvokeFunc()) return false;
        pathCancellation = new();
        path = pi.GetIpcSubscriber<Vector3, Vector3, bool, CancellationToken, Task<List<Vector3>>>("vnavmesh.Nav.PathfindCancelable")
            .InvokeFunc(origin, destination, false, pathCancellation.Token);
        return true;
    }
    public void PollMove()
    {
        if (path is not { IsCompleted: true } complete) return;
        path = null;
        var points = complete.GetAwaiter().GetResult();
        pathCancellation?.Dispose(); pathCancellation = null;
        if (points.Count == 0) throw new InvalidOperationException("No walking path");
        pi.GetIpcSubscriber<List<Vector3>, bool, object>("vnavmesh.Path.MoveTo").InvokeAction(points, false);
    }
    public void StopMove()
    {
        pathCancellation?.Cancel(); pathCancellation?.Dispose(); pathCancellation = null;
        if (path != null) _ = path.ContinueWith(t => { _ = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
        path = null;
        pi.GetIpcSubscriber<object>("vnavmesh.Path.Stop").InvokeAction();
    }
    public void AbortTravel() => pi.GetIpcSubscriber<object>("Lifestream.Abort").InvokeAction();
}
