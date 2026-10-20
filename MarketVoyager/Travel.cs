using System.Numerics;
using Dalamud.Plugin;
namespace MarketVoyager;

internal sealed class Travel(IDalamudPluginInterface pi)
{
    private CancellationTokenSource? pathCancellation;
    private Task<List<Vector3>>? path;
    private bool restoreCamera;
    public bool PathPending => path != null;
    public bool Loaded(string name) => pi.InstalledPlugins.Any(x => x.InternalName == name && x.IsLoaded);
    public bool Ready => Loaded("Lifestream") && Loaded("vnavmesh");
    public bool Busy => pi.GetIpcSubscriber<bool>("Lifestream.IsBusy").InvokeFunc();
    public bool AtLimsaAetheryte => pi.GetIpcSubscriber<uint>("Lifestream.GetActiveAetheryte").InvokeFunc() == 8;
    public bool TeleportToLimsa() => !Busy &&
        pi.GetIpcSubscriber<uint, byte, bool>("Lifestream.Teleport").InvokeFunc(8, 0);
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
    public bool Move(Vector3 origin, Vector3 destination, bool alignCamera = false)
    {
        if (path != null || pi.GetIpcSubscriber<bool>("vnavmesh.Path.IsRunning").InvokeFunc() ||
            pi.GetIpcSubscriber<bool>("vnavmesh.SimpleMove.PathfindInProgress").InvokeFunc()) return false;
        try
        {
            if (alignCamera && !pi.GetIpcSubscriber<bool>("vnavmesh.Path.GetAlignCamera").InvokeFunc())
            {
                restoreCamera = true;
                pi.GetIpcSubscriber<bool, object>("vnavmesh.Path.SetAlignCamera").InvokeAction(true);
            }
            pathCancellation = new();
            path = pi.GetIpcSubscriber<Vector3, Vector3, bool, CancellationToken, Task<List<Vector3>>>("vnavmesh.Nav.PathfindCancelable")
                .InvokeFunc(origin, destination, false, pathCancellation.Token);
            if (path == null) throw new InvalidOperationException("Path provider returned no task");
            return true;
        }
        catch
        {
            pathCancellation?.Dispose(); pathCancellation = null; path = null;
            RestoreCamera();
            throw;
        }
    }
    public bool PollMove()
    {
        if (path is not { IsCompleted: true } complete) return false;
        path = null;
        try
        {
            var points = complete.GetAwaiter().GetResult();
            if (points.Count == 0) throw new InvalidOperationException("No walking path");
            pi.GetIpcSubscriber<List<Vector3>, bool, object>("vnavmesh.Path.MoveTo").InvokeAction(points, false);
        }
        catch { RestoreCamera(); throw; }
        finally { pathCancellation?.Dispose(); pathCancellation = null; }
        return true;
    }
    public void StopMove()
    {
        pathCancellation?.Cancel(); pathCancellation?.Dispose(); pathCancellation = null;
        if (path != null) _ = path.ContinueWith(t => { _ = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
        path = null;
        try { pi.GetIpcSubscriber<object>("vnavmesh.Path.Stop").InvokeAction(); }
        finally { RestoreCamera(); }
    }
    private void RestoreCamera()
    {
        if (!restoreCamera) return;
        pi.GetIpcSubscriber<bool, object>("vnavmesh.Path.SetAlignCamera").InvokeAction(false);
        restoreCamera = false;
    }
    public void AbortTravel() => pi.GetIpcSubscriber<object>("Lifestream.Abort").InvokeAction();
}
