namespace GraveyardKeeper2Accessibility;

/// <summary>
/// Whether the player can walk to a spot at all right now - for leaving out of the object list
/// what cannot be reached yet.
///
/// <para>
/// <b>Reported (2026-09-26): many categories held nothing the player could use or reach.</b> The
/// list is built from every object loaded within 250 m, and that includes the far side of cliffs,
/// rivers and closed gates, and the insides of buildings (other scenes, placed elsewhere in the
/// world - the log shows "no route" to the house's pots from outside). Offering them makes a list
/// of forty categories where a sighted player sees a dozen things around them.
/// </para>
///
/// <para>
/// The navmesh already knows: A* gives every node an <c>Area</c>, the id of the connected region it
/// belongs to, and <c>PathUtilities.IsPathPossible</c> is nothing more than comparing those. So the
/// areas reachable from the player are collected once, joined across the bridges
/// <see cref="BridgeCrossing"/> can cross (the navmesh has no floor on them), and a spot counts as
/// reachable when a floor point beside it lies in one of them. Rebuilt every few seconds at most,
/// and at once when the player's own area changes - removing rubble renumbers the areas.
/// </para>
/// </summary>
internal static class Reachability
{
    private static readonly HashSet<uint> Areas = new();
    private static Pathfinding.RecastGraph _graph;
    private static uint _playerArea;
    private static float _builtAt = -100f;
    private const float Lifetime = 5f;

    /// <summary>
    /// Brings the reachable areas up to date. False when there is no navmesh to ask, in which case
    /// <see cref="CanReach"/> lets everything through rather than empty the list.
    /// </summary>
    internal static bool Refresh(float range)
    {
        try
        {
            var player = MainGame.PlayerController;
            var graph = FloorGraph(player);
            var node = graph == null ? null : graph.GetNearest(player.MovablePosition).node;
            if (node == null)
            {
                _graph = null;
                return false;
            }

            if (graph == _graph && node.Area == _playerArea && Time.unscaledTime - _builtAt < Lifetime) return true;

            _graph = graph;
            _playerArea = node.Area;
            _builtAt = Time.unscaledTime;

            Areas.Clear();
            Areas.Add(node.Area);

            var links = BridgeCrossing.Links(graph, player, range);
            for (var changed = true; changed;)
            {
                changed = false;
                foreach (var (a, b) in links)
                {
                    if (Areas.Contains(a) && Areas.Add(b)) changed = true;
                    if (Areas.Contains(b) && Areas.Add(a)) changed = true;
                }
            }
            return true;
        }
        catch (Exception ex)
        {
            Plugin.Log?.LogWarning($"[Nav] Could not work out what is reachable: {ex.Message}");
            _graph = null;
            return false;
        }
    }

    /// <summary>
    /// True when there is floor within reach of <paramref name="position"/> that the player can walk
    /// to. The thing's own spot is often inside it - a rock, a workbench - so a ring of spots around
    /// it is tried too, the same idea as autowalk's approach.
    /// </summary>
    internal static bool CanReach(Vector3 position)
    {
        if (_graph == null) return true;

        try
        {
            foreach (var offset in Probes)
            {
                var probe = position + offset;
                var nearest = _graph.GetNearest(probe);
                if (nearest.node == null || !nearest.node.Walkable) continue;
                if (Flat(nearest.position - position) > MaxReach) continue;

                // Floor at the top of a cliff is close on the map to a rock at its foot.
                if (Mathf.Abs(nearest.position.y - position.y) > MaxClimb) continue;
                if (Areas.Contains(nearest.node.Area)) return true;
            }
        }
        catch
        {
            return true;
        }
        return false;
    }

    /// <summary>
    /// True for an object the story has put away rather than removed: moved to a spot with no
    /// floor anywhere near, or switched off.
    ///
    /// <para>
    /// <b>Reported (2026-09-27): the donkey.</b> When its quest ends, the flowscript's
    /// <c>Disable</c> parks it at (-199.68, 0, 0), off the map, without hiding it, and a story event
    /// is still pending on it. So it stayed on the story list and as the F5 objective. The player
    /// tried to walk to it seven times from all over the map and always heard "no route".
    /// Unlike <see cref="CanReach"/> this works for any scene and at any distance. It asks the
    /// object's own scene's navmesh whether there is floor near it at all, so it is safe for far
    /// story steps that the player's area test knows nothing about.
    /// </para>
    /// </summary>
    internal static bool IsParked(WgoData data)
    {
        if (data == null) return false;
        if (data.IsHidden) return true;

        try
        {
            // The navmesh under the player first: it is the one the player walks on. Reported
            // (2026-09-27): inside the town guard barracks and the warehouse every object -
            // mercenaries, Herbert, the chest, the exit - was judged parked, because the navmesh
            // looked up by the objects' scene id had no floor there. Home and cellar, in the same
            // scene, were fine; the town interiors far south have their floor on another graph.
            var own = FloorGraph(MainGame.PlayerController);
            if (HasFloorNear(own, data.Position)) return false;

            var graphs = GraphsOf(data.WorldId);
            if (graphs == null || graphs.Count == 0) return false;

            var asked = 0;
            foreach (var graph in graphs)
            {
                // A graph with no nodes is not loaded; it says nothing about where floor is.
                if (graph == null || graph.CountNodes() == 0) continue;
                asked++;
                if (HasFloorNear(graph, data.Position)) return false;
            }

            // Last, any loaded navmesh at all: parked means no floor anywhere near.
            var key = $"{Mathf.RoundToInt(data.Position.x)},{Mathf.RoundToInt(data.Position.z)}";
            if (!AnyFloorCache.TryGetValue(key, out var anyFloor) || Time.unscaledTime - anyFloor.at > 30f)
            {
                anyFloor = (AllRecastGraphs().Any(g => HasFloorNear(g, data.Position)), Time.unscaledTime);
                AnyFloorCache[key] = anyFloor;
            }
            if (anyFloor.floor) return false;
            if (asked == 0 && (own == null || own.CountNodes() == 0)) return false;

            ParkedDetail = $"scene '{data.WorldId}', {asked} of {graphs.Count} graph(s) with nodes, player graph {(own == null ? "none" : own.graphIndex.ToString())}, no floor on any loaded graph";
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Which navmeshes the last parked verdict asked, for the log.</summary>
    internal static string ParkedDetail { get; private set; } = "";

    private static readonly Dictionary<string, (bool floor, float at)> AnyFloorCache = new();

    private static IEnumerable<Pathfinding.RecastGraph> AllRecastGraphs()
    {
        var all = AstarPath.active == null || AstarPath.active.data == null ? null : AstarPath.active.data.graphs;
        if (all == null) yield break;
        foreach (var g in all)
            if (g is Pathfinding.RecastGraph recast && recast.CountNodes() > 0) yield return recast;
    }

    private static Pathfinding.RecastGraph _floorGraph;
    private static Vector3 _floorGraphAt;
    private static float _floorGraphTime = -100f;

    /// <summary>
    /// The navmesh the player is standing on. Normally the scene's own
    /// (<c>PlayerController.SceneRecastGraph</c>); where that has no floor under the player - the
    /// town interiors - whichever loaded recast graph does, including the one
    /// <see cref="InteriorNavmesh"/> scans for rooms that have none. Everything in the mod that
    /// asks the navmesh about the player's surroundings goes through here.
    /// </summary>
    internal static Pathfinding.RecastGraph FloorGraph(PlayerController player)
    {
        if (player == null) return null;
        var scene = player.SceneRecastGraph;
        var at = player.MovablePosition;

        try
        {
            if (IsUnder(scene, at)) return scene;

            if (_floorGraph != null && Time.unscaledTime - _floorGraphTime < 2f && Flat(_floorGraphAt - at) < 2f)
                return _floorGraph;

            // A room the game gave no navmesh at all (the town guard barracks): build one first,
            // so the search below finds it like any other loaded graph.
            InteriorNavmesh.For(player);

            Pathfinding.RecastGraph best = null;
            var bestDistance = float.MaxValue;
            foreach (var graph in AllRecastGraphs())
            {
                var nearest = graph.GetNearest(at);
                if (nearest.node == null || !nearest.node.Walkable) continue;
                var d = Flat(nearest.position - at) + Mathf.Abs(nearest.position.y - at.y);
                if (d < bestDistance) { bestDistance = d; best = graph; }
            }

            var chosen = best != null && bestDistance < 2.5f ? best : scene;
            if (chosen != scene && chosen != _floorGraph)
                Plugin.Log?.LogInfo($"[Nav] Floor under the player is on graph {chosen.graphIndex}, not the scene's {(scene == null ? "none" : scene.graphIndex.ToString())}.");
            _floorGraph = chosen;
            _floorGraphAt = at;
            _floorGraphTime = Time.unscaledTime;
            return chosen;
        }
        catch
        {
            return scene;
        }
    }

    private static bool IsUnder(Pathfinding.RecastGraph graph, Vector3 at)
    {
        if (graph == null) return false;
        var nearest = graph.GetNearest(at);
        return nearest.node != null && Flat(nearest.position - at) < 1.5f && Mathf.Abs(nearest.position.y - at.y) < 2f;
    }

    private static bool HasFloorNear(Pathfinding.NavGraph graph, Vector3 position)
    {
        if (graph == null) return false;
        var nearest = graph.GetNearest(position);
        return nearest.node != null && Flat(nearest.position - position) <= ParkedDistance;
    }

    /// <summary>
    /// Far enough from any floor to be a parking spot rather than a big thing whose centre is off
    /// the mesh - a building, or a bridge (the navmesh has no floor on those).
    /// </summary>
    private const float ParkedDistance = 15f;

    /// <summary>Graph indices per scene; the game logs an error for every unknown scene asked about.</summary>
    private static readonly Dictionary<string, List<int>> GraphIndices = new();

    private static List<Pathfinding.NavGraph> GraphsOf(string worldId)
    {
        if (string.IsNullOrEmpty(worldId) || AstarPath.active == null || AstarPath.active.data == null) return null;

        if (!GraphIndices.TryGetValue(worldId, out var indices))
        {
            indices = GraphHelper.Instance.SceneGraphsData.GetRecastGraphIndexByWorldId(worldId) ?? new List<int>();
            GraphIndices[worldId] = indices;
        }

        var all = AstarPath.active.data.graphs;
        var result = new List<Pathfinding.NavGraph>();
        foreach (var i in indices)
            if (i >= 0 && i < all.Length && all[i] != null) result.Add(all[i]);
        return result;
    }

    /// <summary>Farther than this from any reachable floor, the thing cannot be used from the floor either.</summary>
    private const float MaxReach = 4f;

    /// <summary>How far above or below the floor a thing may be and still be used from it.</summary>
    private const float MaxClimb = 2.5f;

    private static readonly Vector3[] Probes = BuildProbes();

    private static Vector3[] BuildProbes()
    {
        var list = new List<Vector3> { Vector3.zero };
        foreach (var radius in new[] { 1.5f, 3f })
            for (var i = 0; i < 8; i++)
            {
                var angle = i * Mathf.PI / 4f;
                list.Add(new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radius);
            }
        return list.ToArray();
    }

    private static float Flat(Vector3 v) => new Vector2(v.x, v.z).magnitude;
}
