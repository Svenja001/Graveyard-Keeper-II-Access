namespace GraveyardKeeper2Accessibility;

/// <summary>
/// Where to stand, and which way to face, so that the game picks a given object when the player
/// presses E.
///
/// <para>
/// <b>Reported (2026-09-27): "I walk to something and it won't activate."</b> The mill and the
/// potter's wheel never worked. The log showed why. Walking to the millstone left the kitchen chest
/// as the game's target, so E opened the chest. At the anvil it was the hardening bucket, the
/// furnace or the potter's wheel. At the church planning table and a grave it was nothing at all.
/// </para>
///
/// <para>
/// <b>How the game picks.</b> <c>PlayerInteractionComponent.Update</c> puts a small box in front of
/// the player (<c>initialLocalPosition</c> scaled by the facing direction). Then it takes up to
/// eight colliders overlapping it. A big item lying on the ground among them always wins. Otherwise
/// it picks the interactable object whose <i>pivot</i> is nearest the box. The facing is not free
/// either: the animator rounds it to north, east, south or west
/// (<c>BasicNpcSteppedRotationPreset</c>), and <c>PlayerPhysicalBody.UpdatePlayerData</c> copies
/// the rounded direction back. So after a walk that ends beside a crowded workbench, what E
/// reaches depends on the last step of the path. The mod has no say in that.
/// </para>
///
/// <para>
/// So a stand point is chosen the way the game would judge it. Points due west, east, south and
/// north of the thing, on the floor, are each tested with the game's own query for the box
/// the player would have there. A point counts only when the answer is the thing itself.
/// </para>
/// </summary>
internal static class InteractionSpot
{
    /// <summary>A place to stand and the cardinal direction to face from it.</summary>
    internal readonly struct Spot
    {
        internal readonly Vector3 Position;
        internal readonly Vector2 Facing;

        internal Spot(Vector3 position, Vector2 facing)
        {
            Position = position;
            Facing = facing;
        }
    }

    /// <summary>The layer mask <c>PlayerInteractionComponent</c> queries with.</summary>
    private const int Mask = 65600;

    /// <summary>The game reads at most this many colliders; more are silently ignored.</summary>
    private static readonly Collider[] Hits = new Collider[8];

    private static readonly AccessTools.FieldRef<PlayerInteractionComponent, BoxCollider> Box =
        AccessTools.FieldRefAccess<PlayerInteractionComponent, BoxCollider>("interactionCollider");

    private static readonly AccessTools.FieldRef<PlayerInteractionComponent, Vector3> BoxOffset =
        AccessTools.FieldRefAccess<PlayerInteractionComponent, Vector3>("initialLocalPosition");

    private static readonly Vector2[] Cardinals =
    {
        Vector2.right, Vector2.left, Vector2.up, Vector2.down,
    };

    /// <summary>How far back from the thing to try standing, nearest first.</summary>
    private static readonly float[] Distances = { 0.3f, 0.5f, 0.75f, 1f, 1.25f, 1.5f, 1.75f, 2f, 2.5f, 3f };

    /// <summary>Sideways shifts, so a thing flanked by a wall or a neighbour still gets a way in.</summary>
    private static readonly float[] Shifts = { 0f, 0.3f, -0.3f, 0.6f, -0.6f };

    /// <summary>
    /// Every spot the player can walk to from which the game would pick <paramref name="target"/>,
    /// best first (closest to the player). Empty when the thing is not spawned, not interactable,
    /// or cannot be picked from anywhere reachable.
    /// </summary>
    internal static List<Spot> Find(PlayerController player, WgoData target)
    {
        var spots = new List<Spot>();
        if (player == null || target == null || !target.IsInteractable) return spots;

        var interaction = player.PlayerInteractionComponent;
        var graph = Reachability.FloorGraph(player);
        if (interaction == null || graph == null || Box(interaction) == null) return spots;
        var wgo = FindWgo(target);
        if (wgo == null) return spots;

        var start = graph.GetNearest(player.MovablePosition).node;
        if (start == null) return spots;

        var extents = Box(interaction).bounds.extents;
        var from = player.MovablePosition;
        var scored = new List<(Spot spot, float score)>();

        foreach (var facing in Cardinals)
        {
            var back = new Vector3(-facing.x, 0f, -facing.y);
            var side = new Vector3(-facing.y, 0f, facing.x);

            foreach (var distance in Distances)
                foreach (var shift in Shifts)
                {
                    var probe = target.Position + back * distance + side * shift;
                    var nearest = graph.GetNearest(probe);
                    if (nearest.node == null || !nearest.node.Walkable) continue;

                    // The floor point must be where we asked. Snapping elsewhere changes both the
                    // facing that makes sense and the box position the test was for.
                    if (Flat(nearest.position - probe) > 0.15f) continue;
                    if (Mathf.Abs(nearest.position.y - target.Position.y) > 2.5f) continue;
                    if (!Pathfinding.PathUtilities.IsPathPossible(start, nearest.node)) continue;

                    var stand = nearest.position;
                    if (Picks(player, interaction, extents, stand, facing) != target) continue;

                    var spot = new Spot(stand, facing);
                    if (scored.TrueForAll(s => Flat(s.spot.Position - stand) > 0.2f))
                        scored.Add((spot, Flat(stand - from)));
                }
        }

        scored.Sort((a, b) => a.score.CompareTo(b.score));
        foreach (var (spot, _) in scored) spots.Add(spot);
        return OnWorkSide(player, wgo, spots);
    }

    /// <summary>
    /// Only the spots with a clear straight line to one of the thing's work spots (dock points),
    /// when it has any and at least one spot qualifies.
    ///
    /// <para>
    /// <b>Reported (2026-09-27): the resurrection room door.</b> With the door shut, the navmesh
    /// runs straight through it (the door's navmesh block is not reliably applied), and E picks the
    /// door just as well from inside the room. So the walk went through the closed door and stopped
    /// inside. Holding F then did nothing: the only work spot is outside, and the door is between.
    /// A work spot behind a wall or a door is on the far side of it; one behind the station itself
    /// is still reachable around it, and such spots are only dropped when better ones exist.
    /// </para>
    /// </summary>
    private static List<Spot> OnWorkSide(PlayerController player, Wgo wgo, List<Spot> spots)
    {
        if (spots.Count == 0 || wgo.DockPoints == null) return spots;

        var docks = new List<Vector3>();
        foreach (var dock in wgo.DockPoints)
            if (dock != null && dock.gameObject.activeInHierarchy) docks.Add(dock.transform.position);
        if (docks.Count == 0) return spots;

        var clear = spots.FindAll(s => docks.Exists(d => BridgeCrossing.Unsafe(player, s.Position, d) == null));
        if (clear.Count == 0 || clear.Count == spots.Count) return spots;

        Plugin.Log?.LogInfo($"[Walk] '{wgo.Data.id}': {spots.Count - clear.Count} of {spots.Count} spot(s) cut off from its work spot; using the other {clear.Count}.");
        return clear;
    }

    /// <summary>
    /// What the game would pick if the player stood at <paramref name="stand"/> facing
    /// <paramref name="facing"/>: the same query and the same rules as
    /// <c>PlayerInteractionComponent.Update</c>. Null for nothing, or when a big item on the ground
    /// would win.
    /// </summary>
    private static WgoData Picks(PlayerController player, PlayerInteractionComponent interaction, Vector3 extents, Vector3 stand, Vector2 facing)
    {
        var centre = stand + BoxCentreOffset(player, interaction, facing);

        Array.Clear(Hits, 0, Hits.Length);
        var count = Physics.OverlapBoxNonAlloc(centre, extents, Hits, Quaternion.identity, Mask, QueryTriggerInteraction.Collide);

        for (var i = 0; i < count; i++)
        {
            var drop = Hits[i] == null ? null : Hits[i].GetComponentInParent<DropView>();
            if (drop != null && drop.Data != null && !drop.IsDespawning && !drop.IsRiverDump && drop.Data.Size == ItemSize.Big)
                return null;
        }

        WgoData best = null;
        var bestDistance = float.PositiveInfinity;
        for (var i = 0; i < count; i++)
        {
            var wgo = Hits[i] == null ? null : Hits[i].GetComponentInParent<Wgo>();
            if (wgo == null || wgo.IsDespawning || wgo.Data == null || !wgo.Data.IsInteractable) continue;
            if (wgo.InteractionHandler is ConveyorCellInteractionHandler conveyor && !conveyor.HasInteraction(player)) continue;

            var distance = (wgo.Data.Position - centre).magnitude;
            if (distance >= bestDistance) continue;
            bestDistance = distance;
            best = wgo.Data;
        }
        return best;
    }

    /// <summary>
    /// Where the interaction box sits relative to the player's position when facing
    /// <paramref name="facing"/>: the game sets the box's local position to
    /// <c>initialLocalPosition</c> scaled by (x, 1, y) of the facing.
    /// </summary>
    private static Vector3 BoxCentreOffset(PlayerController player, PlayerInteractionComponent interaction, Vector2 facing)
    {
        var local = Vector3.Scale(BoxOffset(interaction), new Vector3(facing.x, 1f, facing.y));
        var parent = interaction.transform.parent;
        if (parent == null) return local;
        return parent.position - player.MovablePosition + parent.TransformVector(local);
    }

    /// <summary>The thing the game is targeting right now, for checking after a walk.</summary>
    internal static string CurrentTarget(PlayerController player, out WgoData wgo)
    {
        wgo = null;
        var interaction = player == null ? null : player.PlayerInteractionComponent;
        if (interaction == null) return null;

        var drop = interaction.BigDropUnderInteraction;
        if (drop != null && drop.Data != null) return drop.Data.Id;

        var target = interaction.WgoUnderInteraction;
        if (target == null || target.Data == null) return null;
        wgo = target.Data;
        return wgo.id;
    }

    private static Wgo FindWgo(WgoData data)
    {
        foreach (var wgo in Navigator.SpawnedWgos)
            if (wgo != null && wgo.Data == data) return wgo;
        return null;
    }

    private static float Flat(Vector3 v) => new Vector2(v.x, v.z).magnitude;
}
