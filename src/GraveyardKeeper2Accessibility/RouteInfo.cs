namespace GraveyardKeeper2Accessibility;

/// <summary>
/// How long the walk to something really is, and what it passes through - as opposed to the
/// straight-line distance the object list gives.
///
/// <para>
/// <b>Reported (2026-09-26): autowalk sometimes went a very long way round.</b> The log shows why:
/// the marble source is 27 m from the home base in a straight line, but it lies at the foot of a
/// cliff, and the only way down is through the village - well over a hundred metres. The route
/// back up then crossed the forest guards' zone and set off their scene. The pathfinder was right
/// both times; what was missing is that a sighted player sees the cliff and the long road, and the
/// mod said "27 metres". So the list now says when a target is a detour, Home says the real length
/// of the way, and the story zones the way passes through - the places where the game may take over
/// - are named before setting off.
/// </para>
///
/// <para>
/// The route comes from the same navmesh and the same pathfinder autowalk uses
/// (<c>GlobalNavigationManager.CalculatePath</c> on the scene's recast graph, the call behind
/// <c>MovementComponent.FindPathRecastGraph</c>), to the same spot autowalk would walk to, so what
/// is said is the walk that would happen.
/// </para>
/// </summary>
internal static class RouteInfo
{
    private static ManualLogSource _log;

    /// <summary>Bumped for every request, so an answer that arrives after the selection moved on is dropped.</summary>
    private static int _probe;

    internal static void Init(ManualLogSource log) => _log = log;

    /// <summary>Forget any route still being worked out - the player has moved on or started walking.</summary>
    internal static void Cancel() => _probe++;

    /// <summary>
    /// A route this much longer than the straight line is worth warning about. Both tests, because
    /// a 4 m route to something 2 m away is twice as long and not a detour at all.
    /// </summary>
    internal static bool IsDetour(float route, float straight) => route > straight * 1.5f && route - straight > 15f;

    /// <summary>
    /// Works out the route to <paramref name="target"/> and speaks it, queued behind whatever was
    /// just said. With <paramref name="full"/> false (paging through the list) it speaks only when
    /// there is something to warn about - a detour, or a story zone on the way - so the list stays
    /// as quick as it was.
    /// </summary>
    internal static void Probe(NavTarget target, bool full)
    {
        var id = ++_probe;
        try
        {
            if (!target.IsValid) return;

            var player = MainGame.PlayerController;
            var graph = Reachability.FloorGraph(player);
            if (graph == null) return;

            var start = player.MovablePosition;
            var end = AutoWalk.WalkPoint(player, target.Position);
            var straight = Flat(target.Position - start);

            // Close enough that the way cannot be much of a detour; not worth a path search.
            if (!full && straight < 5f) return;

            var startNode = graph.GetNearest(start).node;
            var endNode = graph.GetNearest(end).node;
            if (startNode == null || endNode == null) return;

            if (!Pathfinding.PathUtilities.IsPathPossible(startNode, endNode))
            {
                // Often a bridge the navmesh does not cover, which autowalk crosses on its own -
                // so this is only said when asked, never while paging.
                if (full) ScreenReader.Say(Loc.Get("route.none"), interrupt: false);
                return;
            }

            var mask = Pathfinding.GraphMask.FromGraphIndex(graph.graphIndex);
            LazySingleton<GlobalNavigationManager>.Instance.CalculatePath(mask, start, end, path =>
            {
                try
                {
                    if (id != _probe) return;
                    if (path == null || path.error || path.vectorPath == null || path.vectorPath.Count == 0)
                    {
                        if (full) ScreenReader.Say(Loc.Get("route.none"), interrupt: false);
                        return;
                    }

                    var length = path.GetTotalLength();
                    _log?.LogInfo($"[Route] '{target.Id}': route {length:0} m, straight {straight:0} m.");
                    var text = Describe(length, straight, path.vectorPath, start, full);
                    if (text != null) ScreenReader.Say(text, interrupt: false);
                }
                catch (Exception ex)
                {
                    _log?.LogWarning($"[Route] Could not read the route: {ex.Message}");
                }
            });
        }
        catch (Exception ex)
        {
            _log?.LogWarning($"[Route] Could not work out the route: {ex.Message}");
        }
    }

    /// <summary>
    /// "Way 140 metres, a long detour. Passes through: village, forest guards. Careful, a story
    /// zone on the way: forest guards" - or null when <paramref name="full"/> is false and there is
    /// nothing unusual about the way.
    /// </summary>
    internal static string Describe(float length, float straight, List<Vector3> path, Vector3 start, bool full)
    {
        var detour = IsDetour(length, straight);
        ZonesAlong(path, start, out var zones, out var story);

        var parts = new List<string>(3);
        if (full || detour)
        {
            parts.Add(Loc.Fmt(detour ? "route.detour" : "route.length", Mathf.RoundToInt(length)));
            if (zones.Count > 0) parts.Add(Loc.Fmt("route.through", string.Join(", ", zones)));
        }
        if (story.Count > 0) parts.Add(Loc.Fmt("route.story_zones", string.Join(", ", story)));

        return parts.Count == 0 ? null : string.Join(". ", parts);
    }

    /// <summary>
    /// The named zones the path enters, in the order it reaches them, and which of those the story
    /// reacts to. The zone the player stands in already is left out - it is not "on the way".
    /// </summary>
    private static void ZonesAlong(List<Vector3> path, Vector3 start, out List<string> zones, out List<string> story)
    {
        zones = new List<string>();
        story = new List<string>();
        if (path == null || path.Count == 0) return;

        GDZone[] all;
        try { all = UnityEngine.Object.FindObjectsByType<GDZone>(FindObjectsSortMode.None); }
        catch { return; }
        if (all == null) return;

        var hits = new List<(int Index, string Name, bool Story)>();
        foreach (var zone in all)
        {
            if (zone == null || !zone.isActiveAndEnabled || Navigator.IsAmbience(zone)) continue;
            var collider = zone.GetComponent<Collider>();
            if (collider == null || !collider.enabled) continue;
            if (Inside(collider, start)) continue;

            var index = FirstInside(collider, path);
            if (index < 0) continue;

            var tag = string.IsNullOrEmpty(zone.customTag) ? zone.name : zone.customTag;
            var isStory = StoryListeners.AwaitsZone(zone) || Navigator.HasAnyScript(zone);
            hits.Add((index, Navigator.Humanise(tag), isStory));
        }

        foreach (var hit in hits.OrderBy(h => h.Index))
        {
            if (string.IsNullOrWhiteSpace(hit.Name)) continue;
            if (!zones.Contains(hit.Name) && zones.Count < 4) zones.Add(hit.Name);
            if (hit.Story && !story.Contains(hit.Name) && story.Count < 3) story.Add(hit.Name);
        }
    }

    /// <summary>Index along the path where it first enters the collider, sampled every metre and a half; -1 if never.</summary>
    private static int FirstInside(Collider collider, List<Vector3> path)
    {
        var bounds = collider.bounds;
        bounds.Expand(new Vector3(0.5f, 3f, 0.5f));

        var step = 0;
        for (var i = 0; i < path.Count; i++)
        {
            var a = path[i];
            var b = i + 1 < path.Count ? path[i + 1] : a;
            var samples = Math.Max(1, Mathf.CeilToInt(Vector3.Distance(a, b) / 1.5f));
            for (var s = 0; s < samples; s++, step++)
            {
                var point = Vector3.Lerp(a, b, (float)s / samples);
                if (!bounds.Contains(point)) continue;
                if (Inside(collider, point)) return step;
            }
        }
        return -1;
    }

    /// <summary>
    /// True when a spot on the floor is inside the zone. Tested at the floor and at body height,
    /// because the zones are triggered by the player's body and not all of them reach the ground.
    /// </summary>
    private static bool Inside(Collider collider, Vector3 point)
    {
        // ClosestPoint is exact for the primitive shapes and convex meshes; anything else falls
        // back to the bounding box, which is a little generous but never misses.
        var exact = collider is BoxCollider || collider is SphereCollider || collider is CapsuleCollider
                    || (collider is MeshCollider mesh && mesh.convex);

        foreach (var lift in Lifts)
        {
            var p = point + Vector3.up * lift;
            if (exact ? (collider.ClosestPoint(p) - p).sqrMagnitude < 0.01f : collider.bounds.Contains(p)) return true;
        }
        return false;
    }

    private static readonly float[] Lifts = { 0.1f, 1f };

    private static float Flat(Vector3 v) => new Vector2(v.x, v.z).magnitude;
}
