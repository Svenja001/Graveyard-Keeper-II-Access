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
        WatchStatAchievements();
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
            _log?.LogInfo($"[Progress] Saying: \"{text}\"");
            ScreenReader.SayProtected(text);
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
            ScreenReader.SayProtected(Loc.Fmt("progress.inspiration_done", name, talent));
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
            ScreenReader.SayProtected(Loc.Fmt("progress.perk_revealed", name));
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
            // Watched means it was still locked when its stat was sent just before - Steam unlocking
            // it from that stat is this unlock, not one from an earlier session.
            var watched = _statWatch.Remove(platformId);
            if (!watched && SteamManager.Initialized
                && SteamUserStats.GetAchievement(platformId, out var already) && already)
            {
                _log?.LogInfo($"[Progress] Unlock call for '{platformId}', which Steam already has; not said.");
                return;
            }
            QueueAchievement(platformId);
        }
        catch (Exception ex)
        {
            _log?.LogError($"[Progress] Achievement announce failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>
    /// Counted achievements (fish caught, orders, sermons, inspirations) send their count to a Steam
    /// stat before the unlock call, and Steam unlocks an achievement tied to that stat by itself -
    /// with its sound. By the time <c>TryUnlockAchievementOnPlatform</c> runs, Steam already has it,
    /// so the check there skipped it silently. Achievements still locked when their stat is sent are
    /// watched for a while instead, and said when Steam reports them unlocked.
    /// </summary>
    [HarmonyPatch(typeof(AchievementsSystem), "TrySetAchievementProgressOnPlatform")]
    [HarmonyPrefix]
    private static void AchievementsSystem_TrySetProgress(object achievementData)
    {
        try
        {
            if (!SteamManager.Initialized
                || achievementData is not ObjectLinkedToDefinition<AchievementDefinition> data) return;
            var platformId = data.Definition?.GetIdForPlatformUnlock();
            if (string.IsNullOrEmpty(platformId) || platformId == "-1") return;
            if (SteamUserStats.GetAchievement(platformId, out var already) && already) return;
            _statWatch[platformId] = Time.unscaledTime + StatWatchSeconds;
        }
        catch (Exception ex)
        {
            _log?.LogError($"[Progress] Achievement progress watch failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>Achievements whose Steam stat just moved, with when to stop watching them (unscaled time).</summary>
    private static readonly Dictionary<string, float> _statWatch = new();

    /// <summary>How long a stat-driven achievement is watched; the game flushes stats to Steam within this.</summary>
    private const float StatWatchSeconds = 20f;

    private static float _nextStatPoll;

    private static void WatchStatAchievements()
    {
        if (_statWatch.Count == 0) return;
        var now = Time.unscaledTime;
        if (now < _nextStatPoll) return;
        _nextStatPoll = now + 0.5f;
        try
        {
            if (!SteamManager.Initialized) { _statWatch.Clear(); return; }
            foreach (var pair in _statWatch.ToList())
            {
                if (SteamUserStats.GetAchievement(pair.Key, out var done) && done)
                {
                    _statWatch.Remove(pair.Key);
                    QueueAchievement(pair.Key);
                }
                else if (now > pair.Value)
                    _statWatch.Remove(pair.Key);
            }
        }
        catch (Exception ex)
        {
            _statWatch.Clear();
            _log?.LogError($"[Progress] Achievement watch failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static void QueueAchievement(string platformId)
    {
        string name = null;
        if (SteamManager.Initialized)
            name = SteamUserStats.GetAchievementDisplayAttribute(platformId, "name");
        if (string.IsNullOrWhiteSpace(name))
        {
            var local = TmpText.Clean(LLBase.L(platformId));
            name = string.IsNullOrWhiteSpace(local) ? platformId : local;
        }

        _log?.LogInfo($"[Progress] Achievement unlocked: '{platformId}' => \"{name}\"; said once the scene is over.");
        _achievements.Add(Loc.Fmt("progress.achievement", name));
        if (_achievements.Count == 1) _achievementSince = Time.unscaledTime;
    }
}
