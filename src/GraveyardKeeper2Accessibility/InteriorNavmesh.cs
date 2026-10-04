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
            if (_builtFor == id && _graph != null && Covers(_graph, zone))
            {
                EnsureUpper(zone);
                return _graph;
            }
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
            _upper = null;
            _upperFor = null;
            EnsureUpper(zone);
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

        return Scan(template, spare, zone, zone.Center, "floor") ? spare : null;
    }

    /// <summary>Scans <paramref name="slot"/> as a floor plate at <paramref name="center"/>'s height over the zone.</summary>
    private static bool Scan(Pathfinding.RecastGraph template, Pathfinding.RecastGraph slot, WorldZoneData zone, Vector3 center, string what)
    {
        CopySettings(template, slot);
        LazySingleton<GlobalNavigationManager>.Instance.InitRecastGraph(
            (LazyConsts.Navigation.Graph)slot.graphIndex, center, zone.wholeZoneRect.size);

        var dropped = DropFloorless(slot);
        var kept = new List<string>();
        var islands = what == "floor" ? 0 : DropIslands(slot, out kept);
        var walkable = 0;
        slot.GetNodes(node => { if (node.Walkable) walkable++; });
        Plugin.Log?.LogInfo(
            $"[Nav] '{zone.id}' has no navmesh of its own; scanned its {what} on graph {slot.graphIndex} " +
            $"(settings of graph {template.graphIndex}): {walkable} walkable node(s), {dropped} dropped for no floor, " +
            $"{islands} on patches no ladder or door reaches{(kept.Count == 0 ? "" : ", kept: " + string.Join(", ", kept))}; " +
            $"centre {center}, size {zone.wholeZoneRect.size}.");
        return walkable > 0;
    }

    /// <summary>
    /// <para>
    /// <b>Reported (2026-10-04): nothing could be done at the warehouse order board.</b> The board and
    /// the side door stand on a platform 2.4 m above the floor, reached by <c>descent_ladder</c>.
    /// The room's scan is a floor plate at the zone's centre height, trimmed to where physics finds
    /// floor, in bounds 1 m tall - the platform was never on it, and a first try with taller bounds
    /// found nothing either (the platform's colliders are not rasterized). The walk ended under the
    /// board, where E reaches nothing; and from the side door, on the platform, a walk planned on
    /// the floor 2.4 m below carried the player off the edge and out of the world.
    /// </para>
    /// <para>
    /// So a raised floor gets a graph of its own, scanned the same way at its height: the height of
    /// a door that opens onto it or of a ladder's top. A second graph rather than a second plate in
    /// the first, which would hang 2.4 m over the ground floor and could make it too low to walk
    /// under. Ladders join the two (<see cref="LadderRoute"/>, <see cref="Reachability"/>).
    /// </para>
    /// </summary>
    private static Pathfinding.RecastGraph _upper;
    private static string _upperFor;
    private static float _upperCheckedAt = -100f;

    /// <summary>The mod's room graphs in use: the floor and, where there is one, the raised floor.</summary>
    internal static IEnumerable<Pathfinding.RecastGraph> Graphs
    {
        get
        {
            if (_graph != null && _graph.CountNodes() > 0) yield return _graph;
            if (_upper != null && _upper.CountNodes() > 0) yield return _upper;
        }
    }

    private static void EnsureUpper(WorldZoneData zone)
    {
        if (_upperFor == zone.id || Time.unscaledTime - _upperCheckedAt < 2f) return;
        _upperCheckedAt = Time.unscaledTime;

        var level = RaisedLevel(zone);
        if (!level.HasValue) return;

        var graphs = AstarPath.active == null || AstarPath.active.data == null ? null : AstarPath.active.data.graphs;
        var template = graphs == null ? null : At(graphs, (int)LazyConsts.Navigation.Graph.PlayerHouse) ?? At(graphs, (int)LazyConsts.Navigation.Graph.Church);
        if (template == null) return;

        var sceneGraphs = GraphHelper.Instance.SceneGraphsData.GetBakeGroupsByGraphIndex();
        Pathfinding.RecastGraph slot = null;
        foreach (var candidate in SpareSlots)
        {
            var g = At(graphs, (int)candidate);
            if (g == null || g == _graph || sceneGraphs.ContainsKey((int)candidate)) continue;
            if (g != _upper && g.CountNodes() > 0) continue;
            slot = g;
            break;
        }
        _upperFor = zone.id;
        if (slot == null)
        {
            Plugin.Log?.LogWarning($"[Nav] No free graph slot for the raised floor in '{zone.id}'.");
            return;
        }

        var center = new Vector3(zone.Center.x, level.Value, zone.Center.z);
        _upper = Scan(template, slot, zone, center, $"raised floor at height {level.Value - zone.Center.y:0.0}") ? slot : null;
    }

    /// <summary>Higher than this above the room's floor is a raised floor, not a step.</summary>
    private const float RaisedAbove = 1f;

    /// <summary>
    /// The height of a raised floor in the zone: a door or a ladder end standing more than a step
    /// above the room's floor. Null when there is none, or nothing has spawned yet to tell.
    /// </summary>
    private static float? RaisedLevel(WorldZoneData zone)
    {
        var half = zone.wholeZoneRect.size * 0.5f;
        bool Inside(Vector3 p) => Mathf.Abs(p.x - zone.Center.x) <= half.x && Mathf.Abs(p.z - zone.Center.z) <= half.y;

        float? fromLadder = null;
        foreach (var wgo in Navigator.SpawnedWgos)
        {
            if (wgo == null || wgo.Data == null || wgo.Data.Definition == null) continue;
            if (wgo.Data.Definition.interactionType == WGODef.InteractionType.Ladder)
            {
                var top = wgo.GetComponentInChildren<Ladder>()?.TopPart;
                var at = top == null ? null : top.TpPoint != null ? top.TpPoint : top.StartPoint;
                if (at != null && Inside(at.position) && at.position.y - zone.Center.y > RaisedAbove && !fromLadder.HasValue)
                    fromLadder = at.position.y;
            }
            else if (wgo.Data.id != null && wgo.Data.id.StartsWith("tp_", StringComparison.OrdinalIgnoreCase))
            {
                // A door's position is on its floor; a ladder's top point may hang a little above it.
                var p = wgo.Data.Position;
                if (Inside(p) && p.y - zone.Center.y > RaisedAbove) return p.y;
            }
        }
        return fromLadder;
    }

    /// <summary>
    /// A raised floor's plate also finds floor on the tops of crates and shelves of about that
    /// height. Each is its own navmesh area, and a target snapped onto one reads as "no route". So
    /// an area is kept only when a ladder ends on it or a door opens onto it.
    /// </summary>
    private static int DropIslands(Pathfinding.RecastGraph graph, out List<string> kept)
    {
        kept = new List<string>();
        var entries = new List<(Vector3 at, string what)>();
        foreach (var wgo in Navigator.SpawnedWgos)
        {
            if (wgo == null || wgo.Data == null || wgo.Data.Definition == null) continue;
            if (wgo.Data.Definition.interactionType == WGODef.InteractionType.Ladder)
            {
                var ladder = wgo.GetComponentInChildren<Ladder>();
                foreach (var end in LadderRoute.Ends(ladder)) entries.Add((end, wgo.Data.id));
            }
            else if (wgo.Data.id != null && wgo.Data.id.StartsWith("tp_", StringComparison.OrdinalIgnoreCase))
                entries.Add((wgo.Data.Position, wgo.Data.id));
        }

        // Scanned before the room's objects have spawned: nothing to judge by, so keep everything
        // rather than drop a platform that does have a way up.
        if (entries.Count == 0)
        {
            kept.Add("all (no ladders or doors spawned yet)");
            return 0;
        }

        var raised = new Dictionary<uint, List<Pathfinding.GraphNode>>();
        graph.GetNodes(node =>
        {
            if (!node.Walkable) return;
            if (!raised.TryGetValue(node.Area, out var list)) raised[node.Area] = list = new List<Pathfinding.GraphNode>();
            list.Add(node);
        });

        var drop = new List<Pathfinding.GraphNode>();
        foreach (var pair in raised)
        {
            string reachedBy = null;
            foreach (var (at, what) in entries)
            {
                if (pair.Value.Exists(n => Vector3.Distance((Vector3)n.position, at) < 1.5f)) { reachedBy = what; break; }
            }

            if (reachedBy == null) { drop.AddRange(pair.Value); continue; }
            var y = ((Vector3)pair.Value[0].position).y;
            kept.Add($"{pair.Value.Count} node(s) at y {y:0.0} by '{reachedBy}'");
        }

        if (drop.Count == 0) return 0;
        AstarPath.active.AddWorkItem(new Pathfinding.AstarWorkItem(() =>
        {
            foreach (var node in drop) node.Walkable = false;
        }));
        AstarPath.active.FlushWorkItems();
        return drop.Count;
    }

    /// <summary>Generous: a floor a little uneven or a step's height off still counts.</summary>
    private const float FloorProbeUp = 0.6f;
    private const float FloorProbeDown = 1.2f;

    /// <summary>The player, things lying about and the trigger layers are no floor.</summary>
    private static readonly int FloorMask = Physics.DefaultRaycastLayers & ~(
        (1 << LazyConsts.Layers.PLAYER) | (1 << LazyConsts.Layers.DROP) | (1 << LazyConsts.Layers.PLAYER_DROP) |
        (1 << LazyConsts.Layers.FIGHTER) | (1 << LazyConsts.Layers.WORLD_ZONE) | (1 << LazyConsts.Layers.SOUND_ZONE) |
        (1 << LazyConsts.Layers.BUILD_AREA) | (1 << LazyConsts.Layers.GD_ZONE) | (1 << LazyConsts.Layers.BUFF_AREA) |
        (1 << LazyConsts.Layers.CUSTOM_GRAVITY_FIELD) | (1 << LazyConsts.Layers.PLAYER_INVISIBLE_WALLS));

    /// <summary>
    /// <para>
    /// <b>Reported (2026-10-03): the player fell through the warehouse floor</b> on a walk to its
    /// side exit, and kept falling until they quit. InitRecastGraph lays a flat plate over the
    /// whole zone rectangle (<c>AddRecastFloorToGatherer</c>). The game's own rooms cut it open with
    /// their baked <c>navigationHoles</c>. A room the game never scans has none, so the plate
    /// was walkable over stairwells, the basement hatch and past the walls. The walk is kinematic
    /// and goes wherever the mesh does; when it ended there, gravity took the player.
    /// </para>
    /// <para>
    /// So every walkable triangle is checked against real physics: a short ray down from its
    /// centre must hit a solid collider, or the triangle is shut.
    /// </para>
    /// </summary>
    private static int DropFloorless(Pathfinding.RecastGraph graph)
    {
        var dropped = 0;
        AstarPath.active.AddWorkItem(new Pathfinding.AstarWorkItem(() =>
        {
            graph.GetNodes(node =>
            {
                if (!node.Walkable) return;
                var at = (Vector3)node.position;
                if (Physics.Raycast(at + Vector3.up * FloorProbeUp, Vector3.down, FloorProbeUp + FloorProbeDown,
                        FloorMask, QueryTriggerInteraction.Ignore)) return;
                node.Walkable = false;
                dropped++;
            });
        }));
        AstarPath.active.FlushWorkItems();
        return dropped;
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
