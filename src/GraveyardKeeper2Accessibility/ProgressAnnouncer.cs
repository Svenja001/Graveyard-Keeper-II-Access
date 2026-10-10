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

    /// <summary>Achievement lines not said yet, oldest first.</summary>
    private static readonly List<string> _achievements = new();

    /// <summary>When the oldest waiting achievement unlocked (unscaled time).</summary>
    private static float _achievementSince;

    /// <summary>How long speech must have been quiet before an achievement is said.</summary>
    private const float QuietBeforeAchievement = 1.5f;

    /// <summary>Said anyway after this long, so a scene that never gives control back cannot swallow it.</summary>
    private const float AchievementMaxWait = 90f;

    /// <summary>Says the waiting achievements once no scene or dialogue holds the player and speech is quiet.</summary>
    internal static void Update()
    {
        if (_achievements.Count == 0) return;
        try
        {
            var now = Time.unscaledTime;
            var player = MainGame.PlayerController;
            var busy = CutsceneAnnouncer.InCutscene || (player != null && !player.IsControlsEnabled)
                       || now - ScreenReader.LastSpokenAt < QuietBeforeAchievement;
            if (busy && now - _achievementSince < AchievementMaxWait) return;

            var text = string.Join(". ", _achievements);
            _achievements.Clear();
            ScreenReader.Say(text, interrupt: false);
        }
        catch (Exception ex)
        {
            _achievements.Clear();
            _log?.LogError($"[Progress] Saying the achievement failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

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
    ///
    /// <para>
    /// Not said at once: story achievements unlock in the middle of the scene that earns them, and
    /// a queued line there was talked over by the scene's own lines and the keys pressed right
    /// after it - "A Night to Remember" was unlocked and never heard (log of 2026-10-09). It waits
    /// in <see cref="Update"/> until the scene is over and speech has been quiet for a moment.
    /// </para>
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

            _log?.LogInfo($"[Progress] Achievement unlocked: '{platformId}' => \"{name}\"; said once the scene is over.");
            _achievements.Add(Loc.Fmt("progress.achievement", name));
            if (_achievements.Count == 1) _achievementSince = Time.unscaledTime;
        }
        catch (Exception ex)
        {
            _log?.LogError($"[Progress] Achievement announce failed: {ex.GetType().Name}: {ex.Message}");
        }
    }
}
