namespace GraveyardKeeper2Accessibility;

/// <summary>
/// The quest page of the character window ("Aufgaben") and the quest info window it opens.
///
/// <para>
/// On 2026-09-25 every node on the page read "lazy scrollable element": a node is a
/// <c>QuestTreeElementWidget</c> drawn from icons only, with the quest behind it in its data. Each
/// node is read from that data instead - title, state, task, what the story waits for, and who the
/// quest belongs to. The info window (Enter on a node) had nothing spoken either, and its OK
/// listens for <c>GameKey.Select</c> only while a gamepad is active; it is read in full on
/// opening and Enter closes it.
/// </para>
/// </summary>
internal static class QuestPageReader
{
    internal static bool Ready { get; private set; }

    private static class G
    {
        internal static readonly AccessTools.FieldRef<LazyWidget<UIQuestInfoWindowData>, UIQuestInfoWindowData> InfoData =
            AccessTools.FieldRefAccess<LazyWidget<UIQuestInfoWindowData>, UIQuestInfoWindowData>("data");

        internal static readonly AccessTools.FieldRef<UIQuestInfoWindow, TMPro.TextMeshProUGUI> Reward =
            AccessTools.FieldRefAccess<UIQuestInfoWindow, TMPro.TextMeshProUGUI>("repLabel");
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
            Plugin.Log?.LogError($"[QuestPage] The quest page is not supported by this game version: " +
                                 $"{(ex.InnerException ?? ex).GetType().Name}: {(ex.InnerException ?? ex).Message}");
        }
    }

    internal static bool SpeaksForItself(LazyWidgetBase window) => Ready && window is UIQuestInfoWindow;

    // ---- nodes ------------------------------------------------------------------------------

    /// <summary>What to say for a focused quest node, or null when the item is not one.</summary>
    internal static string DescribeWidget(GamepadNavigationItem item)
    {
        if (item == null || !Ready) return null;
        try
        {
            var element = item.GetComponent<LazyScrollableElement>() ?? item.GetComponentInParent<LazyScrollableElement>();
            if (element != null && element.Data is QuestTreeElementWidgetData data && data.questData != null)
                return Describe(data.questData, data.displayViewStatus, full: false);

            var widget = item.GetComponentInChildren<QuestTreeElementWidget>() ?? item.GetComponentInParent<QuestTreeElementWidget>();
            if (widget != null && widget.Data?.questData != null)
                return Describe(widget.Data.questData, widget.Data.displayViewStatus, full: false);
        }
        catch (Exception ex)
        {
            Plugin.Log?.LogWarning($"[QuestPage] Could not describe '{item.name}': {ex.GetType().Name}: {ex.Message}");
        }
        return null;
    }

    private static string Title(QuestData quest)
    {
        var title = TmpText.Clean(LLBase.L(quest.id));
        return string.IsNullOrWhiteSpace(title) || title == quest.id ? Navigator.Humanise(quest.id) : title;
    }

    private static string Describe(QuestData quest, QuestViewStatus view, bool full)
    {
        if (view == QuestViewStatus.Unknown || view == QuestViewStatus.Hidden) return Loc.Get("questpage.unknown");

        var parts = new List<string> { Title(quest) };
        parts.Add(view switch
        {
            QuestViewStatus.Completed => Loc.Get("questpage.done"),
            QuestViewStatus.Revealed => Loc.Get("questpage.running"),
            _ => Loc.Get("questpage.open"),
        });

        var npc = quest.Definition?.wgoNpcId;
        if (!string.IsNullOrEmpty(npc)) parts.Add(Loc.Fmt("questpage.for", ObjectNames.Of(npc)));

        var text = TmpText.Clean(quest.Description);
        if (!string.IsNullOrWhiteSpace(text) && !text.StartsWith("quest_", StringComparison.Ordinal)) parts.Add(text);

        if (quest.status == QuestStatus.InProgress)
        {
            var step = QuestReader.NextStep(quest);
            if (step != null) parts.Add(Loc.Fmt("quest.now", step));
        }

        if (!full && view != QuestViewStatus.Unknown) parts.Add(Loc.Get("questpage.enter"));
        return string.Join(". ", parts.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p.Trim().TrimEnd('.')));
    }

    // ---- the info window --------------------------------------------------------------------

    [HarmonyPatch(typeof(UIQuestInfoWindow), nameof(UIQuestInfoWindow.Redraw))]
    [HarmonyPostfix]
    private static void UIQuestInfoWindow_Redraw(UIQuestInfoWindow __instance)
    {
        try
        {
            var quest = G.InfoData(__instance)?.QuestData;
            if (quest == null) return;

            var parts = new List<string> { Describe(quest, quest.ViewStatus, full: true) };
            var reward = G.Reward(__instance);
            if (reward != null && reward.gameObject.activeSelf)
            {
                var text = TmpText.Clean(reward.text);
                if (!string.IsNullOrWhiteSpace(text)) parts.Add(text);
            }
            parts.Add(Loc.Get("questpage.close"));
            Say(string.Join(". ", parts));
        }
        catch (Exception ex)
        {
            Plugin.Log?.LogError($"[QuestPage] Reading the quest window failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>Enter closes the quest info window. False when it is not on top.</summary>
    internal static bool TryConfirm()
    {
        if (!Ready || !(LazyWindowsStackController.ActiveWindow is UIQuestInfoWindow window)) return false;
        try
        {
            window.Close();
        }
        catch (Exception ex)
        {
            Plugin.Log?.LogError($"[QuestPage] Closing the quest window failed: {ex.GetType().Name}: {ex.Message}");
        }
        return true;
    }

    private static void Say(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        Plugin.Log?.LogInfo($"[QuestPage] \"{text}\"");
        ScreenReader.Say(text);
    }
}
