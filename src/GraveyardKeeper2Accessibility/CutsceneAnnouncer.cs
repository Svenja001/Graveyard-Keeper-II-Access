namespace GraveyardKeeper2Accessibility;

/// <summary>
/// Says when the game takes control away and when it gives it back.
///
/// <para>
/// During a cutscene the HUD is hidden, the camera moves on its own and input does nothing. On
/// screen that is unmistakable - black bars slide in. Without them it is indistinguishable from
/// the game having stopped responding, which is exactly how it reads when a key press produces
/// nothing several times in a row.
/// </para>
///
/// <para>
/// <b>Note the inverted flag.</b> <c>HUD.SetDisableState(type, isEnabled)</c> takes the state of
/// the <i>HUD</i>, not of the cutscene: <c>UICinematic.EnableCinematic</c> passes
/// <c>isEnabled: false</c>, and <c>DisableCinematic</c> passes <c>true</c> when it finishes. So
/// <c>false</c> means a cutscene has started. The debug line the game prints reads
/// <c>isDisabled:[True]</c> for the same call, which says the opposite of what it does - worth
/// knowing before reading a log and concluding a cutscene never ended.
/// </para>
/// </summary>
internal static class CutsceneAnnouncer
{
    private static bool _inCutscene;

    [HarmonyPatch(typeof(HUD), nameof(HUD.SetDisableState))]
    [HarmonyPostfix]
    private static void HUD_SetDisableState(HudStateType type, bool isEnabled)
    {
        try
        {
            if (type != HudStateType.Cinematic && type != HudStateType.CinematicsScene) return;

            // isEnabled is the HUD's state, so false is a cutscene starting.
            var started = !isEnabled;
            if (started == _inCutscene) return;

            _inCutscene = started;
            Plugin.Log?.LogInfo($"[Cutscene] {(started ? "started" : "ended")} ({type}).");

            // Only the pre-rendered scenes (CinematicsSceneDisplayManager) can be skipped: holding
            // Escape for three seconds fills UICinematicsSkipWidget. In-engine cutscenes cannot.
            var key = !started ? "cutscene.ended"
                : type == HudStateType.CinematicsScene ? "cutscene.started_skippable"
                : "cutscene.started";

            // Queued rather than interrupting: a cutscene almost always begins or ends next to a
            // line of dialogue, and the line matters more than the announcement.
            ScreenReader.Say(Loc.Get(key), interrupt: false);
        }
        catch (Exception ex)
        {
            Plugin.Log?.LogError($"[Cutscene] Announce failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>True while the game is playing a cutscene and input is not the player's.</summary>
    internal static bool InCutscene => _inCutscene;
}
