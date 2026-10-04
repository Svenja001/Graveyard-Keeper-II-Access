namespace GraveyardKeeper2Accessibility;

/// <summary>
/// The ladder between the player's level and a target on another one.
///
/// <para>
/// <b>Reported (2026-10-04): "I can never do anything at the order board."</b> The warehouse
/// order board (<c>town_chalk_board</c>) and the side door stand on a platform 2.4 m up; the floor
/// the front door leads onto is joined to it only by <c>descent_ladder</c>. A ladder is no part of
/// any navmesh - E puts the player in <c>LadderPlayerState</c>, Up and Down move along it, and the
/// climb ends at the other <c>LadderEdgePart</c> - so the walk could only end under the board.
/// </para>
///
/// <para>
/// A ladder is chosen by its ends' heights: one end on the player's level and reachable from
/// them, the other on the target's level. That works whether or not the target's level is on the
/// mesh (a room scanned before <see cref="InteriorNavmesh"/> was made tall, or a scene's own).
/// </para>
/// </summary>
internal static class LadderRoute
{
    internal readonly struct Link
    {
        internal readonly WgoData Data;
        internal readonly Vector3 NearEnd;
        internal readonly Vector3 FarEnd;

        internal Link(WgoData data, Vector3 nearEnd, Vector3 farEnd)
        {
            Data = data;
            NearEnd = nearEnd;
            FarEnd = farEnd;
        }

        internal bool Up => FarEnd.y > NearEnd.y;
        internal string Name => ObjectNames.Of(Data?.id);
    }

    /// <summary>A height difference bigger than a step: another level.</summary>
    private const float LevelStep = 1.2f;

    /// <summary>Ladders further away than this are not worth a walk to try.</summary>
    private const float MaxDetour = 60f;

    /// <summary>Where the ladder's two ends are: where a climb starts and where it puts the player.</summary>
    internal static IEnumerable<Vector3> Ends(Ladder ladder)
    {
        if (ladder == null) yield break;
        foreach (var part in new[] { ladder.BotPart, ladder.TopPart })
        {
            if (part == null) continue;
            if (part.StartPoint != null) yield return part.StartPoint.position;
            if (part.TpPoint != null) yield return part.TpPoint.position;
        }
    }

    /// <summary>
    /// The ladder to climb on the way to <paramref name="target"/>, or null when the target is on
    /// the player's level, can be walked to, or no ladder joins the two levels.
    /// </summary>
    internal static Link? Find(PlayerController player, Vector3 target)
    {
        try
        {
            var graph = Reachability.FloorGraph(player);
            if (player == null || graph == null) return null;

            var from = player.MovablePosition;
            if (Mathf.Abs(target.y - from.y) < LevelStep) return null;

            var start = graph.GetNearest(from).node;
            if (start == null) return null;

            // Reachable on its own level (stairs, a ramp, a slope): no ladder needed.
            var goal = graph.GetNearest(target);
            if (goal.node != null && Mathf.Abs(goal.position.y - target.y) < 1f && Flat(goal.position - target) < 1.5f &&
                Pathfinding.PathUtilities.IsPathPossible(start, goal.node))
                return null;

            Link? best = null;
            var bestScore = float.MaxValue;
            foreach (var wgo in Navigator.SpawnedWgos)
            {
                if (wgo == null || wgo.Data == null || wgo.Data.Definition == null) continue;
                if (wgo.Data.Definition.interactionType != WGODef.InteractionType.Ladder) continue;

                var ladder = wgo.GetComponentInChildren<Ladder>();
                if (ladder == null || ladder.BotPart == null || ladder.TopPart == null ||
                    ladder.BotPart.StartPoint == null || ladder.TopPart.StartPoint == null) continue;

                var bot = ladder.BotPart.StartPoint.position;
                var top = ladder.TopPart.StartPoint.position;
                var botIsNear = Mathf.Abs(bot.y - from.y) <= Mathf.Abs(top.y - from.y);
                var near = botIsNear ? bot : top;
                var far = botIsNear ? top : bot;

                if (Mathf.Abs(near.y - from.y) > LevelStep || Mathf.Abs(far.y - target.y) > LevelStep) continue;
                if (Flat(near - from) > MaxDetour) continue;

                var nearest = graph.GetNearest(near);
                if (nearest.node == null || Flat(nearest.position - near) > 2f) continue;
                if (!Pathfinding.PathUtilities.IsPathPossible(start, nearest.node)) continue;

                var score = Flat(near - from) + Flat(far - target);
                if (score >= bestScore) continue;
                bestScore = score;
                best = new Link(wgo.Data, near, far);
            }

            if (best.HasValue)
                Plugin.Log?.LogInfo($"[Walk] Ladder '{best.Value.Data.id}' joins the levels: {best.Value.NearEnd} -> {best.Value.FarEnd}.");
            return best;
        }
        catch (Exception ex)
        {
            Plugin.Log?.LogWarning($"[Walk] Could not look for a ladder: {ex.Message}");
            return null;
        }
    }

    /// <summary>True while the player is on a ladder.</summary>
    internal static bool Climbing(PlayerController player)
    {
        try { return player != null && player.LadderClimbController != null && player.LadderClimbController.IsClimbActive; }
        catch { return false; }
    }

    private static float Flat(Vector3 v) => new Vector2(v.x, v.z).magnitude;
}
