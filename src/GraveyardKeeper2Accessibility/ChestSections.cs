namespace GraveyardKeeper2Accessibility;

/// <summary>
/// The sides of a two-sided item window: your bag on the left, the chest on the right, and on a
/// conveyor chest the slots that feed the belt.
///
/// <para>
/// On screen the sides sit next to each other under their own headings. Through focus alone they
/// are one long run of "Käse", "Leerer Platz", ... with nothing to say where the bag ends and the
/// chest begins - and the conveyor chest does not even set up the left/right groups the plain
/// chest has, so arrowing never left the bag. F6 jumps to the next side (Tab is the game's
/// inventory key); the focus announcement names a side whenever focus crosses into it, whichever
/// way it got there.
/// </para>
/// </summary>
internal static class ChestSections
{
    private static readonly AccessTools.FieldRef<UIBaseChestWindow, MultiInventoryWidget> LeftWidget =
        AccessTools.FieldRefAccess<UIBaseChestWindow, MultiInventoryWidget>("leftMultiInventoryWidget");

    private static readonly AccessTools.FieldRef<UIBaseChestWindow, MultiInventoryWidget> RightWidget =
        AccessTools.FieldRefAccess<UIBaseChestWindow, MultiInventoryWidget>("rightMultiInventoryWidget");

    private enum Side { None, Player, Storage, ConveyorSlots }

    private static readonly Side[] Order = { Side.Player, Side.Storage, Side.ConveyorSlots };

    /// <summary>The side's spoken name, or null outside a two-sided window.</summary>
    internal static string SectionName(GamepadNavigationItem item)
    {
        var side = SideOf(item, out _);
        return side switch
        {
            Side.Player => TmpText.Clean(LLBase.L("ui_player")),
            Side.Storage => TmpText.Clean(LLBase.L("ui_storage")),
            Side.ConveyorSlots => Loc.Get("chest.conveyor_slots"),
            _ => null,
        };
    }

    /// <summary>Moves focus to the next side that has anything in it. False outside such a window.</summary>
    internal static bool TryCycle()
    {
        var focused = UiNarrator.FocusedItem;
        if (focused == null || focused.Controller == null) return false;

        var current = SideOf(focused, out var window);
        if (current == Side.None || window == null) return false;

        var bySide = new Dictionary<Side, List<GamepadNavigationItem>>();
        foreach (var nav in window.GetComponentsInChildren<GamepadNavigationItem>(includeInactive: false))
        {
            if (nav == null || !nav.Active || !nav.TryGetComponent<UIItemCell>(out _)) continue;

            var side = SideOf(nav, out _);
            if (side == Side.None) continue;

            if (!bySide.TryGetValue(side, out var list)) bySide[side] = list = new List<GamepadNavigationItem>();
            list.Add(nav);
        }

        // Whether the chest side is reachable at all is only confirmable at the keyboard; this line
        // tells "no cells found" apart from "cells found but not focusable" afterwards.
        Plugin.Log?.LogInfo($"[Keys] Chest sides from {current}: " +
            string.Join(", ", bySide.Select(kv => $"{kv.Key} {kv.Value.Count} ({kv.Value.Count(HasItem)} filled)")));

        var start = Array.IndexOf(Order, current);
        for (var step = 1; step < Order.Length; step++)
        {
            var side = Order[(start + step) % Order.Length];
            if (!bySide.TryGetValue(side, out var cells) || cells.Count == 0) continue;

            // The first thing actually in it, so a chest with one item in slot twelve does not
            // open on eleven empty slots.
            var target = cells.FirstOrDefault(HasItem) ?? cells[0];
            var controller = focused.Controller;

            // SetFocusedItem only knows items from the controller's last scan and falls back to
            // the very first item otherwise; a chest's side can be drawn after that scan.
            if (!Registered(controller).Contains(target)) controller.ReinitItems(focusOnFirstActive: false);
            if (!Registered(controller).Contains(target)) return false;

            controller.SetFocusedItem(target);
            return true;
        }

        ScreenReader.Say(Loc.Get("chest.no_other_side"));
        return true;
    }

    private static readonly AccessTools.FieldRef<GamepadNavigationController, List<GamepadNavigationItem>> Registered =
        AccessTools.FieldRefAccess<GamepadNavigationController, List<GamepadNavigationItem>>("selectableItems");

    private static bool HasItem(GamepadNavigationItem nav)
    {
        return nav.TryGetComponent<UIItemCell>(out var cell) && cell.DisplayingItem != null && !cell.DisplayingItem.IsEmpty;
    }

    private static Side SideOf(GamepadNavigationItem item, out UIBaseChestWindow window)
    {
        window = null;
        if (item == null) return Side.None;

        window = item.GetComponentInParent<UIBaseChestWindow>();
        if (window == null) return Side.None;

        var t = item.transform;
        if (item.GetComponentInParent<UIConveyorChestSlot>(includeInactive: true) != null) return Side.ConveyorSlots;

        var left = LeftWidget(window);
        if (left != null && t.IsChildOf(left.transform)) return Side.Player;

        var right = RightWidget(window);
        if (right != null && t.IsChildOf(right.transform)) return Side.Storage;

        return Side.None;
    }
}
