namespace GraveyardKeeper2Accessibility;

/// <summary>
/// Getting the player into a story zone so that the game notices.
///
/// <para>
/// <b>Walking to a zone could end "in the right place" with nothing happening</b> (user,
/// 2026-09-27, intro). Two separate causes:
/// </para>
///
/// <para>
/// 1. <b>The walk ended beside the volume, not in it.</b> The target was the collider's bounds
/// centre, a point in mid-air; the navmesh snap, or the ring search for a reachable spot beside
/// an unreachable target (up to 5 m out), could land just outside a small zone. <see cref="Spot"/>
/// picks a reachable floor point that is inside by the game's own test.
/// </para>
///
/// <para>
/// 2. <b>The game can drop an entry.</b> <c>GDZone.OnTriggerEnter</c> ignores the player while
/// <c>PlayerPhysicalBody.IsMuted</c> is set, and that is set for two physics steps every time the
/// body switches between kinematic and dynamic - which a walk does when it starts and when it
/// ends. Unity sends one enter per overlap, so a dropped one never comes back, and the zone's
/// <c>isPlayerInside</c> stays false while the player stands in it. <see cref="Enter"/> hands the
/// game that entry: it calls the zone's own <c>OnTriggerEnter</c> with the player's collider, so
/// everything a real entry does happens, and nothing else.
/// </para>
/// </summary>
internal static class ZoneEntry
{
    private static ManualLogSource _log;

    internal static void Init(ManualLogSource log) => _log = log;

    /// <summary>
    /// Whether <paramref name="position"/> is inside the zone's footprint - the test the game's
    /// own <c>OnTriggerExit</c> uses: a vertical ray from far below through the point.
    /// </summary>
    internal static bool Contains(GDZone zone, Vector3 position)
    {
        var collider = zone == null ? null : zone.GetComponent<Collider>();
        if (collider == null || !collider.enabled) return false;
        return collider.Raycast(new Ray(position + Vector3.down * 100f, Vector3.up), out _, 200f);
    }

    /// <summary>A flat point for lists and directions: the middle of the footprint.</summary>
    internal static Vector3 Centre(GDZone zone)
    {
        var collider = zone.GetComponent<Collider>();
        return collider != null ? collider.bounds.center : zone.transform.position;
    }

    private const float Margin = 1f;
    private const float MinStep = 0.75f;
    private const int MaxSteps = 20;

    /// <summary>
    /// A floor point inside the zone that the player can walk to, or null when none is found.
    /// Points at least <see cref="Margin"/> inside on all four sides are preferred, so the walk
    /// does not stop on the edge; a zone thinner than that takes any inside point. Of those, the
    /// one nearest the player - the shortest way in, not the middle of a large area.
    /// </summary>
    internal static Vector3? Spot(PlayerController player, GDZone zone)
    {
        try
        {
            var graph = Reachability.FloorGraph(player);
            var collider = zone == null ? null : zone.GetComponent<Collider>();
            if (graph == null || collider == null) return null;

            var from = player.MovablePosition;
            var start = graph.GetNearest(from).node;
            if (start == null) return null;

            var bounds = collider.bounds;
            var y = Mathf.Clamp(from.y, bounds.min.y, bounds.max.y);
            var stepsX = Mathf.Clamp(Mathf.CeilToInt(bounds.size.x / MinStep), 1, MaxSteps);
            var stepsZ = Mathf.Clamp(Mathf.CeilToInt(bounds.size.z / MinStep), 1, MaxSteps);

            Vector3? deep = null, edge = null;
            float deepDistance = float.MaxValue, edgeDistance = float.MaxValue;

            for (var ix = 0; ix <= stepsX; ix++)
            for (var iz = 0; iz <= stepsZ; iz++)
            {
                var probe = new Vector3(
                    bounds.min.x + bounds.size.x * ix / stepsX, y,
                    bounds.min.z + bounds.size.z * iz / stepsZ);

                var nearest = graph.GetNearest(probe);
                if (nearest.node == null || !nearest.node.Walkable) continue;
                var point = nearest.position;
                if (!Contains(zone, point)) continue;
                if (!Pathfinding.PathUtilities.IsPathPossible(start, nearest.node)) continue;

                var distance = Flat(point - from);
                var isDeep = Contains(zone, point + Vector3.right * Margin) && Contains(zone, point + Vector3.left * Margin)
                    && Contains(zone, point + Vector3.forward * Margin) && Contains(zone, point + Vector3.back * Margin);

                if (isDeep && distance < deepDistance) { deep = point; deepDistance = distance; }
                if (distance < edgeDistance) { edge = point; edgeDistance = distance; }
            }

            return deep ?? edge;
        }
        catch (Exception ex)
        {
            _log?.LogWarning($"[Zone] Could not find a spot inside '{Name(zone)}': {ex.Message}");
            return null;
        }
    }

    private static readonly MethodInfo OnTriggerEnter = AccessTools.Method(typeof(GDZone), "OnTriggerEnter");

    /// <summary>
    /// Gives the game the entry it missed. False when the player's body is muted right now (the
    /// game would ignore it too) or its trigger collider cannot be found.
    /// </summary>
    internal static bool Enter(GDZone zone)
    {
        var player = MainGame.PlayerController;
        var body = player == null ? null : player.PhysicalBody;
        if (body == null || body.IsMuted || OnTriggerEnter == null) return false;

        var collider = PlayerTrigger(body);
        if (collider == null)
        {
            _log?.LogWarning("[Zone] The player's trigger collider (layer 10) was not found.");
            return false;
        }

        OnTriggerEnter.Invoke(zone, new object[] { collider });
        return WorldAnnouncer.ZoneInside(zone);
    }

    /// <summary>
    /// The collider <c>GDZone.OnTriggerEnter</c> accepts: layer 10, with the player's
    /// <c>IPhysicallyMutable</c> on it or below it.
    /// </summary>
    private static Collider PlayerTrigger(PlayerPhysicalBody body)
    {
        foreach (var collider in body.transform.root.GetComponentsInChildren<Collider>())
        {
            if (collider == null || !collider.enabled || collider.gameObject.layer != 10) continue;
            if (collider.gameObject.GetComponentInChildren<IPhysicallyMutable>() != null) return collider;
        }
        return null;
    }

    internal static string Name(GDZone zone) =>
        zone == null ? "?" : string.IsNullOrEmpty(zone.customTag) ? zone.name : zone.customTag;

    private static float Flat(Vector3 v) => new Vector2(v.x, v.z).magnitude;
}
