namespace GraveyardKeeper2Accessibility;

/// <summary>
/// The research result: the window the research table opens when a study is finished.
///
/// <para>
/// <b>Reported (2026-09-26): it said nothing, and the player was stuck in it.</b> It has no
/// navigation items - an item picture, its name, its runes and one OK button that listens for
/// <c>GameKey.Select</c> (Space) - so focus narration never fired, and nothing said that Space
/// closes it. Same shape as the confirmation boxes: read it from its data, and let Enter answer it.
/// </para>
/// </summary>
internal static class SurveyResultReader
{
    internal static bool Ready { get; private set; }

    private static class G
    {
        internal static readonly AccessTools.FieldRef<LazyWidget<UISurveyResultWindowData>, UISurveyResultWindowData> Data =
            AccessTools.FieldRefAccess<LazyWidget<UISurveyResultWindowData>, UISurveyResultWindowData>("data");
    }

    internal static void Init()
    {
        try
        {
            System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(typeof(G).TypeHandle);
            Ready = true;
        }
        catch (Exception ex)
        {
            Plugin.Log?.LogError($"[Survey] The research result is not supported by this game version: " +
                                 $"{(ex.InnerException ?? ex).GetType().Name}: {(ex.InnerException ?? ex).Message}");
        }
    }

    internal static bool SpeaksForItself(LazyWidgetBase window) => Ready && window is UISurveyResultWindow;

    [HarmonyPatch(typeof(UISurveyResultWindow), nameof(UISurveyResultWindow.Redraw))]
    [HarmonyPostfix]
    private static void UISurveyResultWindow_Redraw(UISurveyResultWindow __instance)
    {
        try
        {
            var def = G.Data(__instance)?.ItemDef;
            if (def == null) return;

            var name = TmpText.Clean(def.GetHeader());
            if (string.IsNullOrWhiteSpace(name)) name = ItemText.Name(def.id);
            var runes = def.GetRunesAsVector3Int();
            var line = Loc.Fmt("survey.result", name, runes.x, runes.y, runes.z);

            Plugin.Log?.LogInfo($"[Survey] \"{line}\"");
            ScreenReader.Say(line);
        }
        catch (Exception ex)
        {
            Plugin.Log?.LogError($"[Survey] Reading the research result failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>Enter closes the research result. False when it is not on top.</summary>
    internal static bool TryConfirm()
    {
        if (!Ready || !(LazyWindowsStackController.ActiveWindow is UISurveyResultWindow window)) return false;
        try
        {
            window.Close();
        }
        catch (Exception ex)
        {
            Plugin.Log?.LogError($"[Survey] Closing the research result failed: {ex.GetType().Name}: {ex.Message}");
        }
        return true;
    }
}
