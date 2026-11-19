using System.Reflection;
using TMPro;

namespace GraveyardKeeper2Accessibility;

/// <summary>
/// A reliable "not researched yet" on every item that can still go on the research table.
///
/// <para>
/// <b>Reported (2026-11-14):</b> "it's not always shown whether I can still research something, so
/// I can't look for things reliably" - with the "Forscher" inspiration at 281 of 300. The game marks
/// an unstudied item in its tooltip with "Untersuchung: nicht abgeschlossen" <i>only when that study
/// has tech icons attached</i> (<c>techRed/Green/Blue &gt; 0</c> in <c>UITooltip.AddItemWidgets</c>).
/// Every study without them says nothing at all, or at most "Runen: ???" when the item has runes.
/// </para>
///
/// <para>
/// What the game counts as studiable is exactly what the table's picker offers
/// (<c>UIResourceBasedCraftWindowData</c>): every <c>SurveyDef</c> that is not a one-time study
/// already completed (<c>KnowledgeSystem.IsSurveyCompleted</c>). Science-fuel studies
/// (<c>isScienceFuelCraft</c>) are repeatable decompositions and get no note. Only one-time studies
/// feed "Forscher" (<c>AddInspiration("researcher", 1)</c> on all 373 of them).
/// </para>
///
/// <para>
/// A temporary list of what was left, with where each item comes from, was written to
/// <c>BepInEx\research_left.txt</c> on 2026-11-14 and then removed; the file stays as reference.
/// The source scan (crafts, traders, fishing, drops, start items) is described in PLAN.md.
/// </para>
/// </summary>
internal static class ResearchList
{
    private static ManualLogSource _log;

    internal static bool Ready { get; private set; }

    private static class G
    {
        internal static readonly FieldInfo TooltipInstance = AccessTools.Field(typeof(UITooltip), "instance")
            ?? throw new MissingFieldException("UITooltip.instance");

        internal static readonly AccessTools.FieldRef<UITooltip, TextStyle> SmallText =
            AccessTools.FieldRefAccess<UITooltip, TextStyle>("smallDescriptionTextStyle");
    }

    internal static void Init(ManualLogSource log)
    {
        _log = log;
        try
        {
            System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(typeof(G).TypeHandle);
            Ready = true;
        }
        catch (Exception ex)
        {
            _log?.LogError($"[Research] The tooltip note is not supported by this game version: " +
                           $"{(ex.InnerException ?? ex).GetType().Name}: {(ex.InnerException ?? ex).Message}");
        }
    }

    /// <summary>
    /// Adds the game's own "Untersuchung: nicht abgeschlossen" line to items whose study has no tech
    /// icons, where the game leaves it out. Same text, same conditions otherwise.
    /// </summary>
    [HarmonyPatch(typeof(UITooltip), "AddItemWidgets")]
    [HarmonyPostfix]
    private static void UITooltip_AddItemWidgets(List<LazyWidgetDataBase> widgetData, ItemDef itemDef, bool notShowStudyWidget)
    {
        try
        {
            if (notShowStudyWidget || widgetData == null || itemDef == null) return;
            var survey = GameBalance.GetSurveyDefForItemOrNull(itemDef.id);
            if (survey == null || survey.isScienceFuelCraft) return;
            if (survey.techRed > 0 || survey.techGreen > 0 || survey.techBlue > 0) return; // the game said it
            if (MainGame.Instance?.GameSave?.knowledgeSystem == null) return;
            if (MainGame.Instance.GameSave.knowledgeSystem.IsSurveyCompleted(survey)) return;

            var tooltip = G.TooltipInstance.GetValue(null) as UITooltip;
            if (tooltip == null) return;

            if (widgetData.Count > 0 && !(widgetData[widgetData.Count - 1] is UITooltipSeparatorWidgetData))
                widgetData.Add(new UITooltipSeparatorWidgetData());
            widgetData.Add(new UITooltipTextWidgetData(LLBase.L("hint_survey_noun") + ":\n" + LLBase.L("survey_not_complete"),
                TextAlignmentOptions.Center, G.SmallText(tooltip)));
        }
        catch (Exception ex)
        {
            _log?.LogError($"[Research] Adding the study note failed: {ex.GetType().Name}: {ex.Message}");
        }
    }
}
