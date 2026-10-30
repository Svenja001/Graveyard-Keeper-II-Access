namespace GraveyardKeeper2Accessibility;

/// <summary>
/// Item pickers (<c>UIMultiInventoryWindow</c>): choosing a seed, fertilizer, alchemy ingredient,
/// organ, grave decoration, offering or zombie equipment.
///
/// <para>
/// The picker shows the whole inventory and every chest it draws from, and only greys out what
/// does not fit - bread and wine in a seed picker, and every empty slot. All of it stays an arrow
/// stop, so finding the one seed meant arrowing through everything (user, 2026-10-30; Storage
/// Access used to skip them, and it is gone). Here the arrows stop only on items that can be
/// chosen: Right and Down go to the next, Left and Up to the previous, in the order the picker
/// lists them. The picker opens on the first one, and says when nothing fits.
/// </para>
/// </summary>
internal static class PickerNav
{
    private static readonly AccessTools.FieldRef<UIItemCell, ItemRelatedWidgetState> State =
        AccessTools.FieldRefAccess<UIItemCell, ItemRelatedWidgetState>("widgetState");

    private static UIMultiInventoryWindow _settled;

    private static UIMultiInventoryWindow Active => LazyWindowsStackController.ActiveWindow as UIMultiInventoryWindow;

    /// <summary>The cells that can be chosen, in the picker's order.</summary>
    private static List<GamepadNavigationItem> Choosable(UIMultiInventoryWindow window)
    {
        var list = new List<GamepadNavigationItem>();
        foreach (var cell in window.GetComponentsInChildren<UIItemCell>(includeInactive: false))
        {
            var item = cell.DisplayingItem;
            var nav = cell.GamepadNavigationItem;
            if (item == null || item.IsEmpty || nav == null || !nav.isActiveAndEnabled || !nav.Active) continue;
            if (State(cell) == ItemRelatedWidgetState.Disabled) continue;
            list.Add(nav);
        }
        return list;
    }

    /// <summary>
    /// Once per picker: moves focus off a greyed-out first cell onto the first choosable one.
    /// Called every frame a controller is resolved.
    /// </summary>
    internal static void Settle(GamepadNavigationController controller)
    {
        var window = Active;
        if (window == null) { _settled = null; return; }
        if (ReferenceEquals(window, _settled)) return;
        _settled = window;
        try
        {
            var list = Choosable(window);
            Plugin.Log?.LogInfo($"[Picker] {list.Count} item(s) can be chosen.");
            if (list.Count == 0)
            {
                ScreenReader.Say(Loc.Get("picker.none"));
                return;
            }
            if (!list.Contains(controller.FocusedItem)) controller.SetFocusedItem(list[0]);
        }
        catch (Exception ex)
        {
            Plugin.Log?.LogError($"[Picker] Settling the focus failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>An arrow in a picker: the next or previous choosable item. False outside pickers.</summary>
    internal static bool TryArrow(GamepadNavigationController controller, GUIDirection direction)
    {
        var window = Active;
        if (window == null) return false;
        try
        {
            var list = Choosable(window);
            if (list.Count == 0)
            {
                ScreenReader.Say(Loc.Get("picker.none"));
                return true;
            }
            var at = list.IndexOf(controller.FocusedItem);
            var forward = direction == GUIDirection.Right || direction == GUIDirection.Down;
            var next = at < 0 ? 0 : at + (forward ? 1 : -1);
            if (next < 0 || next >= list.Count)
            {
                // At either end: say the item again rather than nothing.
                UiNarrator.RepeatFocus();
                return true;
            }
            controller.SetFocusedItem(list[next]);
            return true;
        }
        catch (Exception ex)
        {
            Plugin.Log?.LogError($"[Picker] Arrow failed, the game's own step runs: {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }
}
