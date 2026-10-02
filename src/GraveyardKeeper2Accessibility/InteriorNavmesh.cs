namespace GraveyardKeeper2Accessibility;

/// <summary>
/// A navmesh for the rooms the game gives none.
///
/// <para>
/// <b>Reported (2026-10-01): inside the town guard barracks the list was still empty</b>, after the
/// 2026-09-27 fix that looked for the player's floor on every loaded graph. The game's Player.log
/// shows why: each interior world zone scans its own recast graph when the game loads
/// (<c>WorldZoneData.PrepareForGame</c> → <c>GlobalNavigationManager.InitRecastGraph</c>, logged as
/// "InitRecastGraph graph: PlayerHouse, …" for the house, church, mine, cellars), but
/// <c>town_guard_barracks</c> has <c>navigationGraph = None</c>. There is no navmesh in there at
/// all; the player walks by physics and nobody paths. So every floor test failed, and every
/// object - the exit three metres away included - was "parked off the map".
/// </para>
///
/// <para>
/// The fix does what the game does for its other rooms: <c>InitRecastGraph</c> over the zone's
/// rectangle, into a graph slot the game leaves empty, with the settings of the player house's
/// graph (an interior the game does scan, so its layers and agent size are right for walls).
/// <see cref="Reachability.FloorGraph"/> then finds floor under the player on it like on any other
/// graph, and <see cref="AutoWalk"/> walks it through the game's graph-mask <c>StartPath</c> - the
/// one NPCs use inside zones.
/// </para>
/// </summary>
internal static class InteriorNavmesh
{
    /// <summary>Graph slots the game declares but never scans, tried in this order.</summary>
    private static readonly LazyConsts.Navigation.Graph[] SpareSlots =
    {
        LazyConsts.Navigation.Graph.TestZombieZone,
        LazyConsts.Navigation.Graph.DevPlayground,
    };

    /// <summary>Bigger than any room the game scans itself (the conveyor hall is 28 x 38 m).</summary>
    private const float MaxSide = 60f;

    private static string _builtFor;
    private static Pathfinding.RecastGraph _graph;
    private static readonly Dictionary<string, float> FailedAt = new();

    /// <summary>
    /// The mod's own graph when it covers the zone the player is in, building it on first need.
    /// Null where the zone has a navmesh of its own, or is no room (a bridge in the open has no
    /// floor either, and scanning a whole outdoor zone for it would be wrong).
    /// </summary>
    internal static Pathfinding.RecastGraph For(PlayerController player)
    {
        try
        {
            var zone = player == null || MainGame.PlayerData == null ? null : MainGame.PlayerData.CurrentWorldZoneData;
            if (zone == null || zone.navigationGraph != LazyConsts.Navigation.Graph.None) return null;
            if (zone.wholeZoneRect.width <= 0f || zone.wholeZoneRect.height <= 0f) return null;
            if (zone.wholeZoneRect.width > MaxSide || zone.wholeZoneRect.height > MaxSide) return null;

            var id = zone.id ?? "";
            if (_builtFor == id && _graph != null && Covers(_graph, zone)) return _graph;
            if (FailedAt.TryGetValue(id, out var at) && Time.unscaledTime - at < 30f) return null;

            // Covered by the scene's navmesh after all: an outdoor zone, nothing to build.
            var scene = player.SceneRecastGraph;
            if (scene != null && scene.CountNodes() > 0)
            {
                var nearest = scene.GetNearest(zone.Center);
                if (nearest.node != null && Flat(nearest.position - zone.Center) < 15f) return null;
            }

            var graph = Build(zone);
            if (graph == null)
            {
                FailedAt[id] = Time.unscaledTime;
                return null;
            }
            _builtFor = id;
            _graph = graph;
            return graph;
        }
        catch (Exception ex)
        {
            Plugin.Log?.LogWarning($"[Nav] Could not give the room a navmesh: {ex.Message}");
            return null;
        }
    }

    /// <summary>Still ours and still over this zone - the game might rescan the slot for a fight.</summary>
    private static bool Covers(Pathfinding.RecastGraph graph, WorldZoneData zone) =>
        graph.CountNodes() > 0 && Flat(graph.forcedBoundsCenter - zone.Center) < 0.5f;

    private static Pathfinding.RecastGraph Build(WorldZoneData zone)
    {
        var graphs = AstarPath.active == null || AstarPath.active.data == null ? null : AstarPath.active.data.graphs;
        if (graphs == null) return null;

        var template = At(graphs, (int)LazyConsts.Navigation.Graph.PlayerHouse)
                       ?? At(graphs, (int)LazyConsts.Navigation.Graph.Church);
        if (template == null)
        {
            Plugin.Log?.LogWarning("[Nav] No interior graph to copy settings from; the room stays without a navmesh.");
            return null;
        }

        var sceneGraphs = GraphHelper.Instance.SceneGraphsData.GetBakeGroupsByGraphIndex();
        Pathfinding.RecastGraph spare = null;
        foreach (var slot in SpareSlots)
        {
            var candidate = At(graphs, (int)slot);
            if (candidate == null || sceneGraphs.ContainsKey((int)slot)) continue;
            // Ours from an earlier room, or never scanned by the game.
            if (candidate != _graph && candidate.CountNodes() > 0) continue;
            spare = candidate;
            break;
        }
        if (spare == null)
        {
            Plugin.Log?.LogWarning($"[Nav] No free graph slot for a navmesh in '{zone.id}'.");
            return null;
        }

        CopySettings(template, spare);
        LazySingleton<GlobalNavigationManager>.Instance.InitRecastGraph(
            (LazyConsts.Navigation.Graph)spare.graphIndex, zone.Center, zone.wholeZoneRect.size);

        var walkable = 0;
        spare.GetNodes(node => { if (node.Walkable) walkable++; });
        Plugin.Log?.LogInfo(
            $"[Nav] '{zone.id}' has no navmesh of its own; scanned one on graph {spare.graphIndex} " +
            $"(settings of graph {template.graphIndex}): {walkable} walkable node(s), centre {zone.Center}, size {zone.wholeZoneRect.size}.");
        return walkable > 0 ? spare : null;
    }

    private static Pathfinding.RecastGraph At(Pathfinding.NavGraph[] graphs, int index) =>
        index >= 0 && index < graphs.Length ? graphs[index] as Pathfinding.RecastGraph : null;

    /// <summary>What decides the shape of the mesh; not the bounds, which InitRecastGraph sets.</summary>
    private static void CopySettings(Pathfinding.RecastGraph from, Pathfinding.RecastGraph to)
    {
        to.characterRadius = from.characterRadius;
        to.contourMaxError = from.contourMaxError;
        to.cellSize = from.cellSize;
        to.walkableHeight = from.walkableHeight;
        to.walkableClimb = from.walkableClimb;
        to.maxSlope = from.maxSlope;
        to.maxEdgeLength = from.maxEdgeLength;
        to.minRegionSize = from.minRegionSize;
        to.editorTileSize = from.editorTileSize;
        to.useTiles = from.useTiles;
        to.scanEmptyGraph = from.scanEmptyGraph;
        to.dimensionMode = from.dimensionMode;
        to.backgroundTraversability = from.backgroundTraversability;
        to.relevantGraphSurfaceMode = from.relevantGraphSurfaceMode;
        to.rotation = from.rotation;
        to.nearestSearchOnlyXZ = from.nearestSearchOnlyXZ;
        to.initialPenalty = from.initialPenalty;
        to.perLayerModifications = from.perLayerModifications;
        // A copy, not the same object: InitRecastGraph adds and removes its floor callback on it.
        to.collectionSettings = (Pathfinding.RecastGraph.CollectionSettings)MemberwiseCloneMethod.Invoke(from.collectionSettings, null);
    }

    private static readonly System.Reflection.MethodInfo MemberwiseCloneMethod =
        typeof(object).GetMethod("MemberwiseClone", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

    private static float Flat(Vector3 v) => new Vector2(v.x, v.z).magnitude;
}
