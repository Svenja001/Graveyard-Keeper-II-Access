namespace GraveyardKeeper2Accessibility;

/// <summary>
/// Every sword and spear swing of the player goes toward the nearest live enemy in reach.
///
/// <para>
/// The game swings in the direction the player faces (<c>AttackComponent.PerformAttack</c> takes
/// the animation's direction unless it is handed one), and facing follows the last movement key or
/// the mouse. GK2 moves in eight directions and zombies come from anywhere, so without sight nearly
/// every swing would go into empty air. All of the player's melee states - default, continuous,
/// focused, spear - end in the one private <c>PerformAttack</c>, so a prefix there turns the player
/// toward the target and hands the game the direction, the same thing the mouse stance does with
/// <c>PlayerPhysicalBody.SetAimDirection</c>. Nothing in reach: the swing is left alone.
/// Bow shots use their own states and are not aimed. <c>AutoAim</c> in the config switches it off.
/// </para>
/// </summary>
internal static class CombatAim
{
    private const float Reach = 4f;

    private static ConfigEntry<bool> _enabled;

    internal static void Init(ConfigFile config)
    {
        _enabled = config.Bind("Fight", "AutoAim", true,
            "Turn each sword or spear swing toward the nearest enemy within 4 metres.");
    }

    [HarmonyPatch(typeof(AttackComponent), "PerformAttack",
        new[] { typeof(bool), typeof(Vector3), typeof(bool), typeof(Action), typeof(string), typeof(bool) })]
    [HarmonyPrefix]
    private static void AimAtNearest(AttackComponent __instance, ref bool useCustomDirection, ref Vector3 customDirection)
    {
        try
        {
            if (_enabled == null || !_enabled.Value || useCustomDirection) return;
            var player = MainGame.PlayerController;
            if (player == null || !ReferenceEquals(player.AttackComponent, __instance)) return;

            var from = player.MovablePosition;
            Vector2? best = null;
            var bestDistance = Reach;
            foreach (var wgo in Navigator.SpawnedWgos)
            {
                if (!FightAnnouncer.IsLiveEnemy(wgo)) continue;
                var offset = new Vector2(wgo.Data.Position.x - from.x, wgo.Data.Position.z - from.z);
                var distance = offset.magnitude;
                if (distance < 0.05f || distance > bestDistance) continue;
                bestDistance = distance;
                best = offset;
            }
            if (best == null) return;

            var direction = best.Value.normalized;
            player.PhysicalBody.SetAimDirection(direction);
            player.PhysicalBody.ClearAimDirection();
            useCustomDirection = true;
            customDirection = new Vector3(direction.x, 0f, direction.y);
        }
        catch (Exception ex)
        {
            Plugin.Log?.LogWarning($"[Fight] Aiming failed: {ex.GetType().Name}: {ex.Message}");
        }
    }
}
