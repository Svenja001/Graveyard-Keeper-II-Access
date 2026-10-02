namespace GraveyardKeeper2Accessibility;

/// <summary>
/// The Foliant (<c>alchemy_recipe_book</c>): the alchemy recipe book, <c>UIAlchemyFolioWindow</c>.
///
/// <para>
/// <b>Reported (2026-09-27): "not perfectly accessible".</b> The log showed the book opening on
/// one page and the player arrowing through the same three potions every time. The book has up to
/// eight pages: recipes by difficulty (simple, medium, hard, epic, heroic), then one list per rune
/// colour of the ingredients known to give that rune. The pages are icon-only tabs, and they turn
/// only on <c>GameKey.NextTab</c>/<c>PrevTab</c>, which are bound to the controller's shoulder
/// buttons and to nothing on the keyboard. So the other pages could not be reached, nothing said
/// that they existed, and each entry was read as "2 rote Runen" with the rune colour hanging after
/// the number.
/// </para>
///
/// <para>
/// Now the book is announced with its page, and Left/Right (or Ctrl+Left/Right, as in the
/// building window) turn the pages through the window's own tab handlers. Each entry says its
/// runes as "2 rot, 1 grün", followed by what the thing does.
/// </para>
/// </summary>
internal static class FolioReader
{
    internal static bool Ready { get; private set; }

    private static class G
    {
        internal static readonly AccessTools.FieldRef<LazyWidget<UIAlchemyFolioWindowData>, UIAlchemyFolioWindowData> Data =
            AccessTools.FieldRefAccess<LazyWidget<UIAlchemyFolioWindowData>, UIAlchemyFolioWindowData>("data");

        internal static readonly AccessTools.FieldRef<LazyWidget<UIAlchemyFormulaWidgetData>, UIAlchemyFormulaWidgetData> Entry =
            AccessTools.FieldRefAccess<LazyWidget<UIAlchemyFormulaWidgetData>, UIAlchemyFormulaWidgetData>("data");

        internal static readonly AccessTools.FieldRef<UIAlchemyFolioWindow, AlchemyFormulaTab> CurrentTab =
            AccessTools.FieldRefAccess<UIAlchemyFolioWindow, AlchemyFormulaTab>("currentTab");

        internal static readonly MethodInfo VisibleTabs = AccessTools.Method(typeof(UIAlchemyFolioWindow), "GetVisibleTabs");
        internal static readonly MethodInfo NextTab = AccessTools.Method(typeof(UIAlchemyFolioWindow), "OnPressedNextTab");
        internal static readonly MethodInfo PrevTab = AccessTools.Method(typeof(UIAlchemyFolioWindow), "OnPressedPrevTab");
    }

    internal static void Init()
    {
        try
        {
            System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(typeof(G).TypeHandle);
            if (G.VisibleTabs == null || G.NextTab == null || G.PrevTab == null)
                throw new MissingMethodException("UIAlchemyFolioWindow tab methods");
            Ready = true;
        }
        catch (Exception ex)
        {
            Plugin.Log?.LogError($"[Folio] The recipe book is not supported by this game version: " +
                                 $"{(ex.InnerException ?? ex).GetType().Name}: {(ex.InnerException ?? ex).Message}");
        }
    }

    /// <summary>The book names itself and its page when it opens, instead of "window opened".</summary>
    internal static bool AnnouncesOpening(LazyWidgetBase window) => Ready && window is UIAlchemyFolioWindow;

    /// <summary>
    /// Set while the book opens, so the first page drawn says the book's name and keys too. Open
    /// is patched rather than Close: Close lives on the generic <c>LazyWindow&lt;T&gt;</c>, and a
    /// patch there would run for every window in the game.
    /// </summary>
    private static bool _opening;

    [HarmonyPatch(typeof(UIAlchemyFolioWindow), nameof(UIAlchemyFolioWindow.Open))]
    [HarmonyPrefix]
    private static void UIAlchemyFolioWindow_Open_Prefix() => _opening = true;

    [HarmonyPatch(typeof(UIAlchemyFolioWindow), nameof(UIAlchemyFolioWindow.Open))]
    [HarmonyPostfix]
    private static void UIAlchemyFolioWindow_Open(UIAlchemyFolioWindow __instance)
    {
        try
        {
            _opening = false;
            var data = G.Data(__instance);
            if (data == null || data.HasAnyTabs) return;

            var line = Loc.Get("folio.empty");
            Plugin.Log?.LogInfo($"[Folio] \"{line}\"");
            ScreenReader.Say(line);
        }
        catch (Exception ex)
        {
            Plugin.Log?.LogError($"[Folio] Reading the empty book failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>A page was drawn: on opening, and on every page turn.</summary>
    [HarmonyPatch(typeof(UIAlchemyFolioWindow), nameof(UIAlchemyFolioWindow.DisplayTab))]
    [HarmonyPostfix]
    private static void UIAlchemyFolioWindow_DisplayTab(UIAlchemyFolioWindow __instance, AlchemyFormulaTab tab)
    {
        try
        {
            var data = G.Data(__instance);
            if (data == null) return;

            var pages = Pages(__instance);
            var index = pages.IndexOf(tab);
            var count = tab.IsRuneTab()
                ? data.RuneItems.TryGetValue(tab, out var items) ? items.Count : 0
                : data.Data.TryGetValue(tab, out var formulas) ? formulas.Count : 0;

            var page = Loc.Fmt(tab.IsRuneTab() ? "folio.page_runes" : "folio.page_recipes",
                PageName(tab), index + 1, Math.Max(pages.Count, 1), count);
            var opening = _opening;
            _opening = false;
            var line = opening ? $"{Loc.Get("folio.title")}. {page}. {Loc.Get("folio.keys")}" : page;

            Plugin.Log?.LogInfo($"[Folio] \"{line}\"");

            // Same order as the building window: the page first, then the entry the page focused
            // while it drew, which the page line would otherwise cut off.
            ScreenReader.Say(line);
            var focused = UiNarrator.FocusedItem;
            if (focused != null && focused.transform.IsChildOf(__instance.transform) && !string.IsNullOrEmpty(UiNarrator.LastFocusLabel))
                ScreenReader.Say(UiNarrator.LastFocusLabel, interrupt: false);
            UiNarrator.QueueFocusUntil = Time.unscaledTime + 0.6f;
        }
        catch (Exception ex)
        {
            Plugin.Log?.LogError($"[Folio] Reading the page failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>
    /// One entry: a recipe with the runes it needs, or on a rune page an ingredient with the runes
    /// it gives - and what it is, from its own description. Null for anything else.
    /// </summary>
    internal static string DescribeWidget(GamepadNavigationItem item)
    {
        if (!Ready || item == null) return null;

        var widget = item.GetComponentInChildren<UIAlchemyFormulaWidget>() ?? item.GetComponentInParent<UIAlchemyFormulaWidget>();
        if (widget == null) return null;

        var entry = G.Entry(widget);
        var def = entry?.GetDisplayItemDef();
        if (def == null) return null;

        var runes = entry.ItemDef != null ? def.GetRunesAsVector3Int() : entry.AlchemyFormulaDef.GetRunesAsVector3Int();
        var parts = new List<string> { ItemText.Name(def.id) };

        var colours = new List<string>();
        if (runes.x > 0) colours.Add(Loc.Fmt("folio.rune_red", runes.x));
        if (runes.y > 0) colours.Add(Loc.Fmt("folio.rune_green", runes.y));
        if (runes.z > 0) colours.Add(Loc.Fmt("folio.rune_blue", runes.z));
        if (colours.Count > 0) parts.Add(Loc.Fmt(entry.ItemDef != null ? "folio.gives" : "folio.needs", string.Join(", ", colours)));

        var description = ItemText.Description(def.id);
        if (!string.IsNullOrWhiteSpace(description)) parts.Add(description);

        return string.Join(". ", parts);
    }

    /// <summary>Left/Right, or Ctrl+Left/Right, turn the page while the book is open. False when not applicable.</summary>
    internal static bool TryKeys()
    {
        if (!Ready || !(LazyWindowsStackController.ActiveWindow is UIAlchemyFolioWindow window)) return false;

        var next = Input.GetKeyDown(KeyCode.RightArrow);
        var prev = Input.GetKeyDown(KeyCode.LeftArrow);
        if (!next && !prev) return false;

        try
        {
            if (Pages(window).Count <= 1)
            {
                ScreenReader.Say(Loc.Get("folio.one_page"));
                return true;
            }
            (next ? G.NextTab : G.PrevTab).Invoke(window, null);
        }
        catch (Exception ex)
        {
            Plugin.Log?.LogError($"[Folio] Page turn failed: {ex.GetType().Name}: {(ex.InnerException ?? ex).Message}");
        }
        return true;
    }

    /// <summary>The pages the book shows right now, in the order the page keys go through them.</summary>
    private static List<AlchemyFormulaTab> Pages(UIAlchemyFolioWindow window)
    {
        var result = new List<AlchemyFormulaTab>();
        if (G.VisibleTabs.Invoke(window, null) is List<UIFolioWindowTab> tabs)
            foreach (var tab in tabs)
                if (tab != null) result.Add(tab.Tab);
        return result;
    }

    private static string PageName(AlchemyFormulaTab tab) => Loc.Get("folio.tab." + tab);
}
