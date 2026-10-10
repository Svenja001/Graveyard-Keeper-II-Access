namespace GraveyardKeeper2Accessibility;

/// <summary>
/// Keeps the game's interaction target on the thing a walk went to, as the GK1 mod did.
///
/// <para>
/// <b>Reported (2026-10-09): "it says arrived, but there is nothing to interact."</b> Even from a
/// stand point that <see cref="InteractionSpot"/> had tested, the game often picked nothing or a
/// neighbour. The resurrection table failed from three spots in a row, and the bed from its first.
/// The last step of the path and the animator's rounding of the facing decide what the box in front
/// of the player touches, and the mod cannot fully control either.
/// </para>
///
/// <para>
/// <b>But E does not look again.</b> <c>PlayerInputHandler</c> and <c>Flow_TryInteract</c> use
/// whatever <c>WgoUnderInteraction</c> holds. Only <c>PlayerInteractionComponent.Update</c>
/// chooses it, once per frame. So while the hold is on, that <c>Update</c> enters the wanted thing
/// instead of running its own pick. The hold ends when the player moves or turns, when the thing
/// goes away, or when another walk starts. After that the game picks as usual again.
/// </para>
///
/// <para>
/// Entering goes through the game's own <c>DoEnterInteraction</c>, so its rules still apply: no
/// target while controls are taken, and none in tutorial mode outside the tutorial's own list.
/// When the game refuses, the hold lets go and the game picks as usual.
/// </para>
/// </summary>
internal static class FocusLock
{
    private static readonly Action<PlayerInteractionComponent, Wgo> EnterWgo =
        AccessTools.MethodDelegate<Action<PlayerInteractionComponent, Wgo>>(
            AccessTools.Method(typeof(PlayerInteractionComponent), "DoEnterInteraction", new[] { typeof(Wgo) }));

    private static readonly Action<PlayerInteractionComponent> LeaveWgo =
        AccessTools.MethodDelegate<Action<PlayerInteractionComponent>>(
            AccessTools.Method(typeof(PlayerInteractionComponent), "DoLeaveWgoInteraction"));

    private static readonly Action<PlayerInteractionComponent> LeaveDrop =
        AccessTools.MethodDelegate<Action<PlayerInteractionComponent>>(
            AccessTools.Method(typeof(PlayerInteractionComponent), "DoLeaveDropViewInteraction"));

    /// <summary>How far the thing's nearest edge may be from the player for the hold to make sense.</summary>
    private const float Reach = 1.6f;

    /// <summary>Moving further than this from where the hold began lets go.</summary>
    private const float MoveTolerance = 0.3f;

    private static Wgo _wgo;
    private static Vector3 _at;
    private static Vector2 _facing;

    internal static bool IsHolding => _wgo != null;

    /// <summary>
    /// Makes <paramref name="target"/> the game's interaction target and keeps it there while the
    /// player stands still. False when it is out of reach or the game refuses it.
    /// </summary>
    internal static bool Hold(WgoData target)
    {
        Release(null);

        var player = MainGame.PlayerController;
        var interaction = player == null ? null : player.PlayerInteractionComponent;
        var wgo = FindWgo(target);
        if (interaction == null || wgo == null || wgo.IsDespawning || !target.IsInteractable) return false;

        var distance = EdgeDistance(wgo, player.MovablePosition);
        if (distance > Reach)
        {
            Plugin.Log?.LogInfo($"[Focus] Not holding '{target.id}': {distance:0.0} m away.");
            return false;
        }

        _wgo = wgo;
        _at = player.MovablePosition;
        _facing = player.MovableDirection;

        try
        {
            Apply(interaction);
        }
        catch (Exception ex)
        {
            Plugin.Log?.LogWarning($"[Focus] Could not hold '{target.id}': {ex.Message}");
            _wgo = null;
            return false;
        }

        if (interaction.WgoUnderInteraction != wgo)
        {
            Plugin.Log?.LogInfo($"[Focus] The game refused '{target.id}' as its target.");
            _wgo = null;
            return false;
        }

        Plugin.Log?.LogInfo($"[Focus] Holding '{target.id}' ({distance:0.0} m from its edge).");
        return true;
    }

    /// <summary>Ends the hold; the game picks on its own again from the next frame.</summary>
    internal static void Release(string why)
    {
        if (_wgo == null) return;
        if (why != null) Plugin.Log?.LogInfo($"[Focus] Released '{(_wgo.Data == null ? "?" : _wgo.Data.id)}': {why}.");
        _wgo = null;
    }

    /// <summary>
    /// Instead of the game's own pick, while the hold is on. A paused component is left to the game,
    /// which returns straight away; when the controls come back, the hold applies again.
    /// </summary>
    [HarmonyPatch(typeof(PlayerInteractionComponent), "Update")]
    [HarmonyPrefix]
    private static bool PlayerInteractionComponent_Update(PlayerInteractionComponent __instance)
    {
        if (_wgo == null) return true;

        try
        {
            var player = MainGame.PlayerController;
            if (player == null || player.PlayerInteractionComponent != __instance) return true;
            if (__instance.IsPaused) return true;

            var why = LetGo(player);
            if (why != null)
            {
                Release(why);
                return true;
            }

            Apply(__instance);
            if (__instance.WgoUnderInteraction == _wgo) return false;

            Release("the game refused it");
            return true;
        }
        catch (Exception ex)
        {
            Plugin.Log?.LogWarning($"[Focus] Hold failed: {ex.Message}");
            _wgo = null;
            return true;
        }
    }

    /// <summary>Why the hold should end now, or null to keep it.</summary>
    private static string LetGo(PlayerController player)
    {
        if (_wgo == null || _wgo.IsDespawning || _wgo.Data == null || !_wgo.Data.IsInteractable) return "it is gone";
        if (Flat(player.MovablePosition - _at) > MoveTolerance) return "the player moved";
        if (Vector2.Dot(player.MovableDirection.normalized, _facing.normalized) < 0.9f) return "the player turned";
        return null;
    }

    /// <summary>Makes the held thing the target, the way the game's own <c>Update</c> switches.</summary>
    private static void Apply(PlayerInteractionComponent interaction)
    {
        if (interaction.WgoUnderInteraction == _wgo && interaction.BigDropUnderInteraction == null) return;
        LeaveDrop(interaction);
        if (interaction.WgoUnderInteraction != _wgo)
        {
            LeaveWgo(interaction);
            EnterWgo(interaction, _wgo);
        }
    }

    /// <summary>
    /// Flat distance from the player to the nearest point of the thing's colliders. The pivot alone
    /// says little for a long table or a bed.
    /// </summary>
    private static float EdgeDistance(Wgo wgo, Vector3 from)
    {
        var best = Flat(wgo.Data.Position - from);
        foreach (var collider in wgo.GetComponentsInChildren<Collider>())
        {
            if (collider == null || !collider.enabled) continue;
            var point = collider.bounds.ClosestPoint(from);
            best = Mathf.Min(best, Flat(point - from));
        }
        return best;
    }

    private static Wgo FindWgo(WgoData data)
    {
        if (data == null) return null;
        foreach (var wgo in Navigator.SpawnedWgos)
            if (wgo != null && wgo.Data == data) return wgo;
        return null;
    }

    private static float Flat(Vector3 v) => new Vector2(v.x, v.z).magnitude;
}
