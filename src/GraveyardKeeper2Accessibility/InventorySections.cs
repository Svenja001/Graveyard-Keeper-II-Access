namespace GraveyardKeeper2Accessibility;

/// <summary>
/// The parts of one inventory list: your own items, each bag you carry, and the zone's chests the
/// game lists below them.
///
/// <para>
/// On screen every bag sits under the main inventory with its own heading. All their cells share
/// one navigation group, though, so arrowing runs from the last row of the inventory straight into
/// the first bag and on into the next with nothing to say where one ends. The focus announcement
/// names the part whenever focus crosses into it, and on the character page F6 jumps to the next
/// one, as it jumps between the sides of a chest (ChestSections, which uses the parts as extra
/// stops on your side).
/// </para>
/// </summary>
internal static class InventorySections
{
    private static readonly AccessTools.FieldRef<CharMainPageWidget, BagInventoryWidget> OpenBagWidget =
        AccessTools.FieldRefAccess<CharMainPageWidget, BagInventoryWidget>("bagInventoryWidget");

    private static readonly AccessTools.FieldRef<GamepadNavigationController, List<GamepadNavigationItem>> Registered =
        AccessTools.FieldRefAccess<GamepadNavigationController, List<GamepadNavigationItem>>("selectableItems");

    /// <summary>Part number of the bag opened beside the character page's inventory.</summary>
    internal const int OpenBag = 1000;

    /// <summary>
    /// Which part of its list the cell is in: the index among the list's drawn inventories, or
    /// <see cref="OpenBag"/>. -1 when the list has only one part, or the cell is in none.
    /// </summary>
    internal static int PartOf(GamepadNavigationItem item)
    {
        if (item == null) return -1;

        var multi = item.GetComponentInParent<MultiInventoryWidget>();
        if (multi == null)
        {
            var page = item.GetComponentInParent<CharMainPageWidget>();
            var open = page != null ? OpenBagWidget(page) : null;
            return open != null && item.transform.IsChildOf(open.transform) ? OpenBag : -1;
        }

        var drawn = Drawn(multi);
        if (drawn.Count < 2) return -1;
        return drawn.FindIndex(w => item.transform.IsChildOf(w.transform));
    }

    /// <summary>The part's spoken name ("Inventar", "Kleine Tasche 2", ...), or null as for PartOf.</summary>
    internal static string PartName(GamepadNavigationItem item)
    {
        var part = PartOf(item);
        if (part < 0) return null;

        if (part == OpenBag)
        {
            var page = item.GetComponentInParent<CharMainPageWidget>();
            return Loc.Fmt("inventory.open_bag", Heading(OpenBagWidget(page)));
        }

        // Two bags of one kind carry the same heading; number them in screen order, so the
        // second "Kleine Tasche" is told apart from the first.
        var drawn = Drawn(item.GetComponentInParent<MultiInventoryWidget>());
        var names = drawn.Select(Heading).ToList();
        var name = names[part];
        if (names.Count(n => n == name) < 2) return name;
        return Loc.Fmt("inventory.numbered_part", name, names.Take(part + 1).Count(n => n == name));
    }

    /// <summary>
    /// On the character page's inventory: moves focus to the next part that has cells to focus.
    /// False anywhere else, so F6 keeps its other jobs.
    /// </summary>
    internal static bool TryCycle()
    {
        var focused = UiNarrator.FocusedItem;
        if (focused == null || focused.Controller == null) return false;
        if (!(LazyWindowsStackController.ActiveWindow is CharacterWindow)) return false;

        var page = focused.GetComponentInParent<CharMainPageWidget>();
        if (page == null) return false;

        var parts = new SortedDictionary<int, List<GamepadNavigationItem>>();
        foreach (var nav in page.GetComponentsInChildren<GamepadNavigationItem>(includeInactive: false))
        {
            if (nav == null || !nav.Active || !nav.TryGetComponent<UIItemCell>(out _)) continue;

            var part = PartOf(nav);
            if (part < 0) continue;

            if (!parts.TryGetValue(part, out var list)) parts[part] = list = new List<GamepadNavigationItem>();
            list.Add(nav);
        }

        var current = PartOf(focused);
        Plugin.Log?.LogInfo($"[Keys] Inventory parts from {current}: " +
            string.Join(", ", parts.Select(kv => $"{kv.Key} {kv.Value.Count} ({kv.Value.Count(HasItem)} filled)")));

        // From the tool belt or the perks there is no current part; F6 then starts at the top.
        var keys = parts.Keys.ToList();
        if (keys.Count == 0 || (keys.Count == 1 && keys[0] == current))
        {
            ScreenReader.Say(Loc.Get("inventory.no_other_part"));
            return true;
        }

        var later = keys.Where(k => k > current).ToList();
        return Focus(focused.Controller, FirstOf(parts[later.Count > 0 ? later[0] : keys[0]]));
    }

    /// <summary>The first thing actually in a part, so a bag with one item at the end does not open on empty slots.</summary>
    internal static GamepadNavigationItem FirstOf(List<GamepadNavigationItem> cells) =>
        cells.FirstOrDefault(HasItem) ?? cells[0];

    /// <summary>Focuses a cell, rescanning the controller first if the cell was drawn after its last scan.</summary>
    internal static bool Focus(GamepadNavigationController controller, GamepadNavigationItem target)
    {
        // SetFocusedItem only knows items from the controller's last scan and falls back to the
        // very first item otherwise; a bag's cells can be drawn after that scan.
        if (!Registered(controller).Contains(target)) controller.ReinitItems(focusOnFirstActive: false);
        if (!Registered(controller).Contains(target)) return false;

        controller.SetFocusedItem(target);
        return true;
    }

    internal static bool HasItem(GamepadNavigationItem nav)
    {
        return nav.TryGetComponent<UIItemCell>(out var cell) && cell.DisplayingItem != null && !cell.DisplayingItem.IsEmpty;
    }

    private static List<InventoryWidget> Drawn(MultiInventoryWidget multi) =>
        multi.DrawnInventories.Where(w => w != null && w.gameObject.activeInHierarchy).ToList();

    /// <summary>What the part's heading shows - InventoryHeaderWidget.UpdateHeader's choice of text.</summary>
    private static string Heading(InventoryWidget widget)
    {
        var data = widget?.Data;
        var custom = data?.InventoryHeaderWidgetData?.CustomHeaderId;
        var id = !string.IsNullOrEmpty(custom) ? custom : data?.Inventory?.ViewId;
        return string.IsNullOrEmpty(id) ? Loc.Get("inventory.unnamed_part") : TmpText.Clean(LLBase.L(id));
    }
}
