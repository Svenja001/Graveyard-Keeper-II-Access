namespace GraveyardKeeper2Accessibility;

/// <summary>
/// Finds a way over a bridge the navmesh does not cross.
///
/// <para>
/// <b>Why this exists.</b> The scene's Recast navmesh stops at the ends of the village bridge: the
/// player walked over it by hand, and from the far side every autowalk said "no route" - to the
/// tavern, the travel stone, home. The bridge is perfectly walkable; the navmesh just has no floor
/// on it, so the search fails, and that failure is otherwise trustworthy (see
/// <c>AutoWalk.FailOrFallback</c>). The long-distance GD graph would cross it, but that graph also
/// walks through story blockages and is off by default for that reason.
/// </para>
///
/// <para>
/// <b>What it does.</b> Bridges are the <c>GDZone</c>s named "…Bridge…" - the ones that mark the
/// first crossing (<c>insp_bridge_village</c> and friends). They switch themselves off after that
/// first visit, so disabled ones are searched too. Around each, the navmesh is sampled for a spot
/// reachable from the player and a spot connected to the target; the closest such pair is the two
/// ends of the span. The span is walked in a straight line by the game's own mover
/// (<c>MovementType.Direct</c>), then the route continues from the far end.
/// </para>
///
/// <para>
/// <b>And it refuses unsafe spans.</b> A straight walk is kinematic and would pass through anything -
/// over water where a bridge is broken, through a gate. So the span is accepted only when every
/// quarter metre of it has walkable ground under it, tested exactly as
/// <c>PlayerInvisibleWalls.IsRaycastHitWalkableGround</c> does (the check that stops the player
/// stepping off an edge), and the player's own body swept along it hits nothing solid.
/// </para>
/// </summary>
internal static class BridgeCrossing
{
    internal readonly struct Span
    {
        internal readonly string Name;
        internal readonly Vector3 NearEnd;
        internal readonly Vector3 FarEnd;
        internal readonly bool IsDoor;

        internal Span(string name, Vector3 nearEnd, Vector3 farEnd, bool isDoor)
        {
            Name = name;
            NearEnd = nearEnd;
            FarEnd = farEnd;
            IsDoor = isDoor;
        }
    }

    /// <summary>Layers the game counts as walkable ground (<c>PlayerInvisibleWalls</c>, 6144).</summary>
    private const int WalkableGroundMask = 6144;
    private const float GroundRayLength = 0.6f;
    private const float GroundRayYOffset = 0.05f;

    /// <summary>Longest span accepted. The village bridge is about 6 m of missing navmesh.</summary>
    private const float MaxSpan = 25f;

    /// <summary>A straight walk keeps its starting height, so the two ends must be level.</summary>
    private const float MaxHeightDifference = 0.6f;

    private const float SampleStep = 1f;
    private const float MinSearchRadius = 8f;
    private const float MaxSearchRadius = 20f;
    private const float GroundStep = 0.25f;
    private const int PairsToTest = 12;

    /// <summary>
    /// A bridge between the player and <paramref name="target"/>, or null when none is found or
    /// none is safe to walk. Logs what it tried, so a refused bridge can be diagnosed from the log.
    /// </summary>
    internal static Span? Find(PlayerController player, Vector3 target)
    {
        try
        {
            var graph = Reachability.FloorGraph(player);
            if (graph == null) return null;

            var from = player.MovablePosition;
            var start = graph.GetNearest(from).node;
            var goal = graph.GetNearest(target).node;
            if (start == null || goal == null) return null;

            // Nearest detour first: a bridge on the way beats one behind the player.
            var bridges = Bridges()
                .OrderBy(b => Flat(b.Center - from) + Flat(target - b.Center))
                .ToList();

            foreach (var bridge in bridges)
            {
                var span = TrySpan(graph, bridge, start, goal, player);
                if (span.HasValue) return span;
            }

            if (bridges.Count > 0)
                Plugin.Log?.LogInfo($"[Walk] No usable bridge towards {target} (checked {bridges.Count}).");
        }
        catch (Exception ex)
        {
            Plugin.Log?.LogWarning($"[Walk] Bridge search failed: {ex.GetType().Name}: {ex.Message}");
        }

        return null;
    }

    /// <summary>
    /// Pairs of navmesh areas that a bridge within <paramref name="range"/> joins, by a span that
    /// <see cref="Find"/> would accept - for <see cref="Reachability"/>, so things across a bridge
    /// autowalk can cross still count as reachable, and things across a broken one do not.
    /// </summary>
    internal static List<(uint A, uint B)> Links(Pathfinding.RecastGraph graph, PlayerController player, float range)
    {
        var links = new List<(uint, uint)>();
        try
        {
            var from = player.MovablePosition;
            foreach (var bridge in Bridges())
            {
                if (Flat(bridge.Center - from) > range) continue;

                var byArea = new Dictionary<uint, List<Vector3>>();
                var seen = new HashSet<Pathfinding.GraphNode>();
                var steps = Mathf.CeilToInt(bridge.Radius / SampleStep);
                for (var x = -steps; x <= steps; x++)
                for (var z = -steps; z <= steps; z++)
                {
                    var probe = bridge.Center + new Vector3(x * SampleStep, 0f, z * SampleStep);
                    if (Flat(probe - bridge.Center) > bridge.Radius) continue;

                    var nearest = graph.GetNearest(probe);
                    if (nearest.node == null || !nearest.node.Walkable) continue;
                    if (Flat(nearest.position - probe) > SampleStep) continue;
                    if (!seen.Add(nearest.node)) continue;

                    if (!byArea.TryGetValue(nearest.node.Area, out var list)) byArea[nearest.node.Area] = list = new List<Vector3>();
                    list.Add(nearest.position);
                }

                var areas = byArea.Keys.ToList();
                for (var i = 0; i < areas.Count; i++)
                for (var j = i + 1; j < areas.Count; j++)
                {
                    if (Joined(player, bridge, byArea[areas[i]], byArea[areas[j]])) links.Add((areas[i], areas[j]));
                }
            }
        }
        catch (Exception ex)
        {
            Plugin.Log?.LogWarning($"[Walk] Bridge link search failed: {ex.GetType().Name}: {ex.Message}");
        }
        return links;
    }

    /// <summary>True when some pair of spots, one from each side, is a span that is safe to walk.</summary>
    private static bool Joined(PlayerController player, Bridge bridge, List<Vector3> near, List<Vector3> far)
    {
        foreach (var (a, b, _) in Candidates(bridge, near, far))
            if (Unsafe(player, a, b) == null) return true;
        return false;
    }

    /// <summary>
    /// The spans worth testing, best first. Over a bridge: each near spot with its closest far
    /// spot, shortest first. Through a door: the shortest spans are straight through the wall
    /// beside it, so every pair is ranked by how close both ends are to the door, and more are
    /// tried - the body sweep throws out the ones through the wall.
    /// </summary>
    private static List<(Vector3 a, Vector3 b, float d)> Candidates(Bridge bridge, List<Vector3> near, List<Vector3> far)
    {
        var pairs = new List<(Vector3 a, Vector3 b, float d)>();
        if (bridge.IsDoor)
        {
            foreach (var a in near)
            foreach (var b in far)
                if (Mathf.Abs(b.y - a.y) <= MaxHeightDifference) pairs.Add((a, b, Flat(b - a)));
            return pairs
                .OrderBy(p => Flat(p.a - bridge.Center) + Flat(p.b - bridge.Center))
                .Take(DoorPairsToTest)
                .ToList();
        }

        foreach (var a in near)
        {
            var best = far.OrderBy(b => Flat(b - a)).First();
            var d = Flat(best - a);
            if (d <= MaxSpan && Mathf.Abs(best.y - a.y) <= MaxHeightDifference) pairs.Add((a, best, d));
        }
        return pairs.OrderBy(p => p.d).Take(PairsToTest).ToList();
    }

    private const int DoorPairsToTest = 40;

    private readonly struct Bridge
    {
        internal readonly string Name;
        internal readonly Vector3 Center;
        internal readonly float Radius;
        internal readonly bool IsDoor;

        internal Bridge(string name, Vector3 center, float radius, bool isDoor = false)
        {
            Name = name;
            Center = center;
            Radius = radius;
            IsDoor = isDoor;
        }
    }

    /// <summary>
    /// Every bridge zone in the loaded scenes, active or not. <c>FindObjectsByType</c> would skip
    /// the ones the story has already switched off, which are exactly the bridges the player has
    /// crossed before and will want to cross back.
    /// </summary>
    private static List<Bridge> Bridges()
    {
        var result = new List<Bridge>();
        foreach (var zone in Resources.FindObjectsOfTypeAll<GDZone>())
        {
            if (zone == null || !zone.gameObject.scene.IsValid()) continue;

            var tag = string.IsNullOrEmpty(zone.customTag) ? zone.name : zone.customTag;
            if (tag.IndexOf("bridge", StringComparison.OrdinalIgnoreCase) < 0) continue;

            // A disabled collider reports empty bounds, so the box is read from its own fields.
            var center = zone.transform.position;
            var radius = MinSearchRadius;
            var box = zone.GetComponent<BoxCollider>();
            if (box != null)
            {
                center = zone.transform.TransformPoint(box.center);
                var size = Vector3.Scale(box.size, zone.transform.lossyScale);
                radius = Mathf.Clamp(Mathf.Max(Mathf.Abs(size.x), Mathf.Abs(size.z)) * 0.5f + 6f, MinSearchRadius, MaxSearchRadius);
            }

            result.Add(new Bridge(Navigator.Humanise(tag), center, radius));
        }

        // Doors in the same scene as the player. The door's navmesh block is swapped per door state
        // and is not reliably right: after the resurrection room door opened, the room was cut off
        // on the navmesh, every exit left out of the list as "out of reach", and the player stuck
        // (2026-09-27). The body sweep in Unsafe refuses a door that is shut.
        foreach (var wgo in Navigator.SpawnedWgos)
        {
            var id = wgo == null || wgo.IsDespawning || wgo.Data == null ? null : wgo.Data.id;
            if (id == null || id.StartsWith("tp_", StringComparison.Ordinal)) continue;
            if (id.IndexOf("door", StringComparison.OrdinalIgnoreCase) < 0) continue;
            result.Add(new Bridge(ObjectNames.Of(id), wgo.Data.Position, DoorRadius, isDoor: true));
        }
        return result;
    }

    /// <summary>How far either side of a door to look for floor. Rooms are small.</summary>
    private const float DoorRadius = 4f;

    private static Span? TrySpan(Pathfinding.RecastGraph graph, Bridge bridge, Pathfinding.GraphNode start, Pathfinding.GraphNode goal, PlayerController player)
    {
        var near = new List<Vector3>();
        var far = new List<Vector3>();
        var seen = new HashSet<Pathfinding.GraphNode>();

        var steps = Mathf.CeilToInt(bridge.Radius / SampleStep);
        for (var x = -steps; x <= steps; x++)
        for (var z = -steps; z <= steps; z++)
        {
            var probe = bridge.Center + new Vector3(x * SampleStep, 0f, z * SampleStep);
            if (Flat(probe - bridge.Center) > bridge.Radius) continue;

            var nearest = graph.GetNearest(probe);
            if (nearest.node == null || !nearest.node.Walkable) continue;
            if (Flat(nearest.position - probe) > SampleStep) continue;
            if (!seen.Add(nearest.node)) continue;

            if (Pathfinding.PathUtilities.IsPathPossible(start, nearest.node)) near.Add(nearest.position);
            else if (Pathfinding.PathUtilities.IsPathPossible(nearest.node, goal)) far.Add(nearest.position);
        }

        if (near.Count == 0 || far.Count == 0) return null;

        var refused = 0;
        string lastWhy = null;
        foreach (var (a, b, d) in Candidates(bridge, near, far))
        {
            var why = Unsafe(player, a, b);
            if (why == null)
            {
                Plugin.Log?.LogInfo($"[Walk] Bridge '{bridge.Name}': crossing {a} -> {b} ({d:0.0} m).");
                return new Span(bridge.Name, a, b, bridge.IsDoor);
            }
            // A door tries many spans, most of them through the wall; one line for all of them.
            if (bridge.IsDoor) { refused++; lastWhy = why; continue; }
            Plugin.Log?.LogInfo($"[Walk] Bridge '{bridge.Name}': span {a} -> {b} refused, {why}.");
        }

        if (refused > 0)
            Plugin.Log?.LogInfo($"[Walk] Door '{bridge.Name}': {refused} span(s) refused, last {lastWhy}.");
        return null;
    }

    /// <summary>Why a straight walk from a to b is not safe, or null when it is.</summary>
    internal static string Unsafe(PlayerController player, Vector3 a, Vector3 b)
    {
        var length = Flat(b - a);
        var count = Mathf.Max(1, Mathf.CeilToInt(length / GroundStep));
        var hits = new RaycastHit[10];

        for (var i = 0; i <= count; i++)
        {
            var point = Vector3.Lerp(a, b, i / (float)count) + Vector3.up * GroundRayYOffset;
            if (Physics.SphereCastNonAlloc(point, 0.01f, Vector3.down, hits, GroundRayLength, WalkableGroundMask) == 0)
                return $"no ground at {point}";
        }

        // Something solid across the span - a gate, a fence, rubble. Cast at about hip height with
        // the player's width, ignoring the ground itself, triggers, and the player's own body.
        var body = player.PhysicalBody == null ? null : player.PhysicalBody.Rb;
        var direction = new Vector3(b.x - a.x, 0f, b.z - a.z);
        var origin = a + Vector3.up * ObstacleHeight;
        foreach (var hit in Physics.SphereCastAll(origin, PlayerRadius, direction.normalized, length, ~WalkableGroundMask, QueryTriggerInteraction.Ignore))
        {
            if (hit.collider == null || hit.distance <= 0f) continue;
            if (body != null && hit.collider.attachedRigidbody == body) continue;
            if (hit.normal.y > 0.5f) continue;
            return $"blocked by '{hit.collider.name}' (layer {hit.collider.gameObject.layer}) at {hit.point}";
        }

        return null;
    }

    private const float ObstacleHeight = 0.5f;
    private const float PlayerRadius = 0.25f;

    private static float Flat(Vector3 v) => new Vector2(v.x, v.z).magnitude;
}
