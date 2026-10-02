using System.Text;

namespace GraveyardKeeper2Accessibility;

/// <summary>
/// Reads out tooltips.
///
/// This is the highest-value hook in the game. Every tooltip - item details, craft requirements,
/// perks, talents, quality, needs lists, vendor orders, progress ticks - is built as a
/// <c>List&lt;LazyWidgetDataBase&gt;</c> and handed to one method. That list is *structured data*,
/// not rendered text, so it can be turned into a sentence properly rather than scraped off labels.
/// In the GK1 mod the same ground took four separate subsystems.
///
/// <para>
/// <b>The plan named the wrong method.</b> It said to patch <c>UITooltip.Show</c>, the public
/// static entry point. That method has <i>no callers anywhere in the game</i>: all twenty-one
/// <c>ShowItemCell</c> / <c>ShowPerk</c> / <c>ShowSimpleInfo</c>-style helpers skip it and call the
/// private <c>ShowAtTargetAndCalculateOffsets</c> directly. Patching <c>Show</c> would have caught
/// nothing at all, and would have looked like a tooltip system that simply did not fire.
/// <c>ShowAtTarget</c> is the real funnel - the single method every tooltip reaches, whatever
/// built it.
/// </para>
/// </summary>
internal static class TooltipReader
{
    /// <summary>
    /// The last tooltip spoken, so one that redraws or follows the cursor is not read twice.
    /// Cleared when the tooltip hides, so returning to the same control reads it again - which is
    /// what a player who came back to check is asking for.
    /// </summary>
    private static string _lastSpoken;

    /// <summary>
    /// The tooltip on screen right now, in full, or null when none is showing. Kept separately from
    /// <see cref="_lastSpoken"/> so the details key can read it again on request - the automatic
    /// reading is often cut short by the next thing said.
    /// </summary>
    internal static string Current { get; private set; }

    /// <summary>
    /// Widget data types seen but not yet rendered, logged once each. A tooltip that silently
    /// drops a section is the kind of gap that never gets reported, because nothing sounds wrong -
    /// there is simply less than there should be.
    /// </summary>
    private static readonly HashSet<string> UnhandledLogged = new();

    /// <summary>
    /// Set by a reader whose focus line already says everything the next tooltip will: that tooltip
    /// is kept for the details key but not spoken.
    /// </summary>
    internal static bool SuppressNext;

    [HarmonyPatch(typeof(UITooltip), "ShowAtTarget")]
    [HarmonyPostfix]
    private static void UITooltip_ShowAtTarget(List<LazyWidgetDataBase> dataList)
    {
        var suppress = SuppressNext;
        SuppressNext = false;
        try
        {
            var text = Render(dataList);
            if (string.IsNullOrWhiteSpace(text)) return;

            Current = text;
            if (suppress) _lastSpoken = text;
            if (text == _lastSpoken) return;

            _lastSpoken = text;

            // Does not interrupt. A tooltip appears *because* the player just focused something,
            // so the control's own name is still being read; cutting it off to say the detail
            // would lose the thing the detail is about. Moving on speaks with interrupt, which
            // flushes any tooltip still queued - so arrowing quickly never falls behind.
            ScreenReader.Say(text, interrupt: false);
        }
        catch (Exception ex)
        {
            Plugin.Log?.LogError($"[Tooltip] Failed to read tooltip: {ex.GetType().Name}: {ex.Message}");
        }
    }

    [HarmonyPatch(typeof(UITooltip), "HideTooltip")]
    [HarmonyPostfix]
    private static void UITooltip_HideTooltip() => _lastSpoken = Current = null;

    [HarmonyPatch(typeof(UITooltip), "DoHideAnimation")]
    [HarmonyPostfix]
    private static void UITooltip_DoHideAnimation() => _lastSpoken = Current = null;

    /// <summary>
    /// Turns a tooltip's widget list into one spoken string.
    ///
    /// Separators are the game's own grouping - they are what visually divides a name from its
    /// description, or a description from its cost - so they become sentence breaks, and the
    /// widgets between them are joined with commas. That keeps the structure the designer built
    /// audible instead of flattening everything into one run-on line.
    /// </summary>
    private static string Render(List<LazyWidgetDataBase> dataList)
    {
        if (dataList == null || dataList.Count == 0) return null;

        var groups = new List<List<string>>();
        var current = new List<string>();

        foreach (var data in dataList)
        {
            switch (data)
            {
                case null:
                    break;

                case UITooltipTextWidgetData text:
                    Add(current, TmpText.Clean(text.Text));
                    break;

                case UITooltipSeparatorWidgetData:
                    if (current.Count > 0)
                    {
                        groups.Add(current);
                        current = new List<string>();
                    }
                    break;

                case UITooltipNeedsItemWidgetData needs:
                    Add(current, RenderNeeds(needs));
                    break;

                default:
                    NoteUnhandled(data);
                    break;
            }
        }

        if (current.Count > 0) groups.Add(current);
        if (groups.Count == 0) return null;

        var sb = new StringBuilder();
        foreach (var group in groups)
        {
            if (sb.Length > 0) sb.Append(". ");
            sb.Append(string.Join(", ", group));
        }

        return sb.ToString();
    }

    /// <summary>
    /// The requirements list on a craft tooltip: what it needs, and how much of it you have.
    ///
    /// The count you hold is the whole question a player asks of this panel, and it is the one
    /// thing the visual version conveys without words - a cell is simply tinted when you are
    /// short. So it is spoken as "5 wood, you have 2", not "5 wood".
    /// </summary>
    private static string RenderNeeds(UITooltipNeedsItemWidgetData needs)
    {
        var cells = needs.CraftItemCellsData;
        if (cells == null || cells.Count == 0) return null;

        var parts = new List<string>();

        foreach (var cell in cells)
        {
            var item = cell?.currentItem;
            if (item == null) continue;

            var name = ItemText.Name(item.Id);
            if (string.IsNullOrWhiteSpace(name)) name = item.Id;

            var need = item.GetCount(cell.WgoData);

            // A group requirement ("any ore") is not a single item id, so asking the inventory how
            // many of it you hold would count nothing. Name it and give the amount only.
            if (item.IsGroup || cell.MultiInventory == null)
            {
                parts.Add(Loc.Fmt("tooltip.need", need, name));
                continue;
            }

            var have = cell.MultiInventory.GetTotalCount(item.Id);
            parts.Add(Loc.Fmt("tooltip.need_have", have, need, name));
        }

        return parts.Count == 0 ? null : Loc.Fmt("tooltip.needs", string.Join("; ", parts));
    }

    private static void Add(List<string> parts, string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;

        // Tooltips repeat themselves more than one would expect - a header and its first line are
        // often the same string - and hearing it twice reads as a stutter.
        if (parts.Contains(value)) return;

        parts.Add(value);
    }

    private static void NoteUnhandled(LazyWidgetDataBase data)
    {
        var name = data.GetType().Name;
        if (!UnhandledLogged.Add(name)) return;

        Plugin.Log?.LogWarning(
            $"[Tooltip] No spoken form for '{name}' yet - that part of the tooltip is being " +
            "dropped. Worth adding if it turns up on something useful.");
    }
}
