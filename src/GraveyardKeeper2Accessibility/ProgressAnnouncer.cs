using Steamworks;

namespace GraveyardKeeper2Accessibility;

/// <summary>
/// Progress the game only shows as a popup and a sound: an inspiration reaching its goal (the
/// "unlock" pling while working), a new perk being revealed, and an achievement unlocking (the
/// Steam overlay's sound).
///
/// Inspirations and perks are hooked at the popup's own <c>Draw</c>, which is where the pling is
/// played, so the speech matches the sound exactly - the notificator already filters out silent
/// loads and a locked inspiration tab before it gets there. Achievements have no popup of the
/// game's own; the one choke point is the call that hands them to Steam.
/// </summary>
internal static class ProgressAnnouncer
{
    private static ManualLogSource _log;

    internal static void Init(ManualLogSource log) => _log = log;

    [HarmonyPatch(typeof(UIInspirationNotification), nameof(UIInspirationNotification.Draw))]
    [HarmonyPostfix]
    private static void UIInspirationNotification_Draw(UIInspirationNotification __instance)
    {
        try
        {
            var id = __instance.InspirationId;
            if (string.IsNullOrEmpty(id)) return;

            var name = TmpText.Clean(LLBase.L(id));
            var talent = TreesReader.TalentName(__instance.InspirationTalentId);
            _log?.LogInfo($"[Progress] Inspiration completed: '{id}' ({__instance.InspirationTalentId}).");
            ScreenReader.Say(Loc.Fmt("progress.inspiration_done", name, talent), interrupt: false);
        }
        catch (Exception ex)
        {
            _log?.LogError($"[Progress] Inspiration announce failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    [HarmonyPatch(typeof(UITalentLevelUpRevealedNotification), nameof(UITalentLevelUpRevealedNotification.Draw))]
    [HarmonyPostfix]
    private static void UITalentLevelUpRevealedNotification_Draw(UITalentLevelUpRevealedNotification __instance)
    {
        try
        {
            var def = GameBalance.Me.GetData<TalentLevelUpDef>(__instance.TalentLevelUpId);
            if (def == null || string.IsNullOrEmpty(def.linkedPerk)) return;

            var name = TmpText.Clean(LLBase.L(def.linkedPerk));
            _log?.LogInfo($"[Progress] Perk revealed: '{def.linkedPerk}'.");
            ScreenReader.Say(Loc.Fmt("progress.perk_revealed", name), interrupt: false);
        }
        catch (Exception ex)
        {
            _log?.LogError($"[Progress] Perk announce failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>
    /// Also runs on load for achievements the save has but Steam was never told about, so one
    /// Steam already holds is skipped - Steam makes no sound for it either.
    /// </summary>
    [HarmonyPatch(typeof(AchievementsSystem), "TryUnlockAchievementOnPlatform")]
    [HarmonyPrefix]
    private static void AchievementsSystem_TryUnlock(string platformId)
    {
        try
        {
            if (string.IsNullOrEmpty(platformId)) return;

            string name = null;
            if (SteamManager.Initialized)
            {
                if (SteamUserStats.GetAchievement(platformId, out var already) && already) return;
                name = SteamUserStats.GetAchievementDisplayAttribute(platformId, "name");
            }
            if (string.IsNullOrWhiteSpace(name))
            {
                var local = TmpText.Clean(LLBase.L(platformId));
                name = string.IsNullOrWhiteSpace(local) ? platformId : local;
            }

            _log?.LogInfo($"[Progress] Achievement unlocked: '{platformId}' => \"{name}\".");
            ScreenReader.Say(Loc.Fmt("progress.achievement", name), interrupt: false);
        }
        catch (Exception ex)
        {
            _log?.LogError($"[Progress] Achievement announce failed: {ex.GetType().Name}: {ex.Message}");
        }
    }
}
