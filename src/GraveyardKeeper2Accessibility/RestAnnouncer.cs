namespace GraveyardKeeper2Accessibility;

/// <summary>
/// Resting on the bench by the campfire (game update 1.012): sitting restores energy and passes
/// time faster, without saving and without curing insanity or lack of sleep.
///
/// <para>
/// The game shows nothing but a small "Exit" hint while the player sits
/// (<c>UIRestingExitWidget</c>). So the mod says when resting starts and stops, and when energy
/// is full - the moment a sighted player would see the bar top out and stand up.
/// </para>
///
/// <para>
/// <b>Standing up.</b> The widget accepts E or Escape only when gamepad mode is off. The mod
/// forces it on, which leaves only <c>GameKey.Back</c> (Escape on the keyboard). E is restored
/// here. It stands the player up one frame after the press, so the game's own interaction handler
/// cannot see the same E with control already back and sit them down again.
/// </para>
/// </summary>
internal static class RestAnnouncer
{
    private static bool _fullSaid;
    private static float _startedAt;
    private static bool _standPending;

    internal static bool IsResting
    {
        get
        {
            try { return MainGame.PlayerData?.energySystem?.IsResting ?? false; }
            catch { return false; }
        }
    }

    [HarmonyPatch(typeof(EnergySystem), nameof(EnergySystem.StartResting))]
    [HarmonyPrefix]
    private static void StartResting_Prefix(EnergySystem __instance, out bool __state) => __state = __instance.IsResting;

    [HarmonyPatch(typeof(EnergySystem), nameof(EnergySystem.StartResting))]
    [HarmonyPostfix]
    private static void StartResting_Postfix(EnergySystem __instance, bool __state)
    {
        try
        {
            if (__state || !__instance.IsResting) return;
            _startedAt = Time.unscaledTime;
            _standPending = false;
            var full = EnergyFull();
            _fullSaid = full;
            var line = Loc.Fmt(full ? "rest.started_full" : "rest.started", Energy());
            Plugin.Log?.LogInfo($"[Rest] Started resting: \"{line}\"");
            ScreenReader.Say(line);
        }
        catch (Exception ex)
        {
            Plugin.Log?.LogError($"[Rest] Announcing the rest failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    [HarmonyPatch(typeof(EnergySystem), nameof(EnergySystem.StopResting))]
    [HarmonyPrefix]
    private static void StopResting_Prefix(EnergySystem __instance, out bool __state) => __state = __instance.IsResting;

    [HarmonyPatch(typeof(EnergySystem), nameof(EnergySystem.StopResting))]
    [HarmonyPostfix]
    private static void StopResting_Postfix(EnergySystem __instance, bool __state)
    {
        try
        {
            if (!__state || __instance.IsResting) return;
            _standPending = false;
            var line = Loc.Fmt("rest.stopped", Energy());
            Plugin.Log?.LogInfo($"[Rest] Stopped resting: \"{line}\"");
            ScreenReader.Say(line);
        }
        catch (Exception ex)
        {
            Plugin.Log?.LogError($"[Rest] Announcing the end of the rest failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>Energy topping out while seated: said once per rest.</summary>
    [HarmonyPatch(typeof(EnergySystem), nameof(EnergySystem.UpdateRestLogic))]
    [HarmonyPostfix]
    private static void UpdateRestLogic_Postfix(EnergySystem __instance)
    {
        try
        {
            if (_fullSaid || !__instance.IsResting || !EnergyFull()) return;
            _fullSaid = true;
            Plugin.Log?.LogInfo("[Rest] Energy full while resting.");
            ScreenReader.Say(Loc.Get("rest.energy_full"));
        }
        catch (Exception ex)
        {
            _fullSaid = true;
            Plugin.Log?.LogError($"[Rest] Checking energy while resting failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>Called once per frame from the plugin: E stands the player up.</summary>
    internal static void Update()
    {
        try
        {
            if (!IsResting)
            {
                _standPending = false;
                return;
            }

            if (_standPending)
            {
                _standPending = false;
                Plugin.Log?.LogInfo("[Rest] E pressed: standing up.");
                MainGame.PlayerData.energySystem.StopResting();
                return;
            }

            // The E that sat the player down is not the one that stands them up; and a window on
            // top (pause menu, a dialogue) owns its own keys.
            if (Time.unscaledTime - _startedAt < 0.5f || LazyWindowsStackController.ActiveWindow != null) return;
            if (Input.GetKeyDown(KeyCode.E)) _standPending = true;
        }
        catch (Exception ex)
        {
            _standPending = false;
            Plugin.Log?.LogError($"[Rest] Standing up failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>Stands the player up properly, for the force-release key. False when not resting.</summary>
    internal static bool TryStandUp()
    {
        if (!IsResting) return false;
        MainGame.PlayerData.energySystem.StopResting();
        return true;
    }

    private static bool EnergyFull() => PlayerEnergyGameResSystem.GetSystem()?.HasMax() ?? false;

    private static int Energy()
    {
        var player = MainGame.PlayerData;
        return player == null ? 0 : Mathf.FloorToInt(player.GetRes("energy"));
    }
}
