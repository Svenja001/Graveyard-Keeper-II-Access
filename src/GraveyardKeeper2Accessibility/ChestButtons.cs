namespace GraveyardKeeper2Accessibility;

/// <summary>
/// The chest window's "put every item the chest already holds into it" button.
///
/// <para>
/// It sits in the header of the player's side and is a mouse button: in gamepad mode the game
/// hides it (<c>InventoryHeaderWidget.ApplyMoveAllSimilarBtnVisibility</c>) behind
/// <c>GameKey.MoveAllItemsFromPlayer</c>, a controller button with no keyboard binding. So, as
/// with the corpse panel's take-body button (see <see cref="BodyWindowsReader"/>), the mod adds one
/// stop above the window's own: Up arrow from the top row of either side lands on it, Enter (or
/// the move-item key) presses it, Down goes back to where focus was. The game's focus is taken off
/// the cell underneath while the stop is current, so Enter cannot act on an item by accident.
/// Covers every <c>UIBaseChestWindow</c>: plain, conveyor, vegetable and wine chests.
/// </para>
/// </summary>
internal static class ChestButtons
{
    private static readonly AccessTools.FieldRef<UIBaseChestWindow, MultiInventoryWidget> LeftWidget =
        AccessTools.FieldRefAccess<UIBaseChestWindow, MultiInventoryWidget>("leftMultiInventoryWidget");

    private static readonly AccessTools.FieldRef<LazyWidget<UIBaseChestWindowData>, UIBaseChestWindowData> WindowData =
        AccessTools.FieldRefAccess<LazyWidget<UIBaseChestWindowData>, UIBaseChestWindowData>("data");

    // Virtual: the conveyor chests override it to do nothing while a slot is being assigned.
    private static readonly MethodInfo AllToChestPressed = AccessTools.Method(typeof(UIBaseChestWindow), "OnAllToChestPressed");

    private static bool _onStop;
    private static GamepadNavigationItem _returnItem;

    internal static void Reset()
    {
        _onStop = false;
        _returnItem = null;
    }

    /// <summary>Whether the stop is current. Clears itself when the game has put focus elsewhere.</summary>
    private static bool OnStop(out UIBaseChestWindow window)
    {
        window = LazyWindowsStackController.ActiveWindow as UIBaseChestWindow;
        if (!_onStop) return false;
        if (window == null || window.GetComponentInChildren<GamepadNavigationController>()?.FocusedItem != null)
        {
            Reset();
            return false;
        }
        return true;
    }

    private static bool Available(UIBaseChestWindow window, out string reason)
    {
        reason = null;
        if (window is UIConveyorChestWindow { IsItemSelectionModActive: true })
        {
            reason = Loc.Get("chest.all_similar_choosing");
            return false;
        }
        var left = LeftWidget(window);
        if (left == null || !left.IsMoveAllSimilarBtnInteractable)
        {
            reason = Loc.Get("chest.all_similar_none");
            return false;
        }
        return true;
    }

    private static string StopText(UIBaseChestWindow window)
    {
        var label = TmpText.Clean(LLBase.L("btn_hint_move_all_identical_items"));
        if (string.IsNullOrWhiteSpace(label) || label == "btn_hint_move_all_identical_items") label = Loc.Get("chest.all_similar_button");
        return Available(window, out var reason)
            ? Loc.Fmt("body.take_stop", label)
            : Loc.Fmt("body.take_stop_disabled", label, reason);
    }

    /// <summary>
    /// An arrow in a chest window. True when the stop used it: Up from the top row, or any arrow
    /// while on the stop.
    /// </summary>
    internal static bool TryArrow(GamepadNavigationController controller, GUIDirection direction)
    {
        if (OnStop(out var window))
        {
            try
            {
                if (direction == GUIDirection.Up)
                {
                    ScreenReader.Say(StopText(window));
                    return true;
                }
                var back = _returnItem;
                Reset();
                if (back != null && back.isActiveAndEnabled) controller.SetFocusedItem(back);
                else controller.FocusOnFirstActive();
                return true;
            }
            catch (Exception ex)
            {
                Plugin.Log?.LogError($"[Chest] Leaving the move-all stop failed: {ex.GetType().Name}: {ex.Message}");
                Reset();
                return false;
            }
        }

        // A take-only chest (2026-10-13 update) has no such button on screen either.
        if (window == null || direction != GUIDirection.Up || TakeOnly(window)) return false;

        try
        {
            // Up from the top row: the game either leaves focus where it is or wraps to the bottom.
            // Neither is wanted here, so the wrap is switched off for this one step.
            var before = controller.FocusedItem;
            var loop = controller.loopVerticalNavigation;
            controller.loopVerticalNavigation = false;
            try { controller.Navigate(direction); }
            finally { controller.loopVerticalNavigation = loop; }
            if (before != null && controller.FocusedItem != before) return true;

            _returnItem = controller.FocusedItem ?? before;
            _returnItem?.Unfocus();
            _onStop = true;
            ScreenReader.Say(StopText(window));
            return true;
        }
        catch (Exception ex)
        {
            Plugin.Log?.LogError($"[Chest] Move-all stop failed: {ex.GetType().Name}: {ex.Message}");
            Reset();
            return false;
        }
    }

    /// <summary>Enter or the move-item key on the stop. False when the stop is not current.</summary>
    internal static bool TryPress()
    {
        if (!OnStop(out var window)) return false;
        try
        {
            if (!Available(window, out var reason))
            {
                ScreenReader.Say(reason);
                return true;
            }

            // The player's side loses exactly what the chest takes, so what moved is the difference
            // between before and after. The game's OnItemsRemove is no help here: for this move it
            // reports the container ("Inventar"), not the items taken out of it.
            var source = WindowData(window)?.FirstMultiInventoryData?.SelectedWidgetData?.Inventory;
            var before = Counts(source);
            AllToChestPressed.Invoke(window, null);
            var after = Counts(source);

            var parts = before
                .Select(kv => (id: kv.Key, count: kv.Value - (after.TryGetValue(kv.Key, out var left) ? left : 0)))
                .Where(p => p.count > 0)
                .Select(p => p.count > 1 ? $"{p.count} {ItemText.Name(p.id)}" : ItemText.Name(p.id))
                .ToList();
            Plugin.Log?.LogInfo($"[Chest] Moved all similar items into the chest: {string.Join(", ", parts)}.");
            ScreenReader.Say(parts.Count > 0 ? Loc.Fmt("menu.moved", string.Join(", ", parts)) : Loc.Get("chest.all_similar_done"));

            // The window may have redrawn and put focus on a cell; the stop follows the game then.
            OnStop(out _);
            return true;
        }
        catch (Exception ex)
        {
            Plugin.Log?.LogError($"[Chest] Moving all similar items failed: {ex.GetType().Name}: {ex.Message}");
            return true;
        }
    }

    /// <summary>
    /// A chest the player may only take from (<c>TakeOnlySerializedItemProperty</c>, since the
    /// 2026-10-13 update). The game hides the move-all button there and silently refuses any item
    /// moved in.
    /// </summary>
    internal static bool TakeOnly(UIBaseChestWindow window)
    {
        var chest = window == null ? null : WindowData(window)?.SecondMultiInventoryData?.SelectedWidgetData?.Inventory;
        return chest != null && chest.BlocksPlayerItemInsert;
    }

    /// <summary>How many of each item an inventory holds at the top level - bags are left alone by the move.</summary>
    private static Dictionary<string, int> Counts(Inventory inventory)
    {
        var counts = new Dictionary<string, int>();
        var items = inventory?.Data?.Inventory;
        if (items == null) return counts;
        foreach (var item in items)
        {
            if (item == null || item.IsEmpty || string.IsNullOrEmpty(item.id)) continue;
            counts[item.id] = (counts.TryGetValue(item.id, out var n) ? n : 0) + Math.Max(1, item.Count);
        }
        return counts;
    }

    /// <summary>F8 on the stop says the stop again.</summary>
    internal static bool TryRepeat()
    {
        if (!OnStop(out var window)) return false;
        ScreenReader.Say(StopText(window));
        return true;
    }
}
