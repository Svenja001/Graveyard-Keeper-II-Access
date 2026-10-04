namespace GraveyardKeeper2Accessibility;

/// <summary>
/// The town order board (<c>UIVendorOrdersWindow</c>) and the traders' order list it opens
/// (<c>UIVendorOrdersSelectionWindow</c>).
///
/// <para>
/// Every order is a <c>UIVendorOrderWidget</c>: an item picture, a happiness reward, small icons
/// for urgent, repeatable, locked and done, and a grey or green button that takes the focus. Not a
/// word of text on the button, and an empty slot is a bare "+", so focus narration had nothing
/// to say. Read here from the widget's data, as <c>UIVendorOrderWidget.Redraw</c> draws it.
/// </para>
///
/// <para>
/// What Enter does, from the windows' handlers: on the board an empty or open slot opens the
/// traders' list (only while <c>chalk_board_enabled</c>, on the Day of Pride), a finished one
/// collects the reward. In the list, an order can be taken while the board is enabled, the order
/// is not done or done this week, and the trader's tier is high enough.
/// </para>
/// </summary>
internal static class OrdersReader
{
    internal static string DescribeWidget(GamepadNavigationItem item)
    {
        if (item == null) return null;
        var widget = item.GetComponentInParent<UIVendorOrderWidget>();
        var data = widget == null ? null : widget.Data;
        if (data == null) return null;

        try
        {
            var inList = widget.GetComponentInParent<UIVendorOrdersSelectionWindow>() != null;
            var boardOpen = MainGame.PlayerData != null && MainGame.PlayerData.GetResInt("chalk_board_enabled") > 0;
            var day = Loc.Find("icon.day_pride") ?? "day_pride";
            var parts = new List<string>();

            if (data.IsEmpty || data.VendorOrderData == null)
            {
                parts.Add(data.IndexInOrders >= 0 ? Loc.Fmt("orders.empty_slot", data.IndexInOrders + 1) : Loc.Get("orders.empty"));
                parts.Add(boardOpen ? Loc.Get("orders.enter_choose") : Loc.Fmt("orders.only_on", day));
                return string.Join(". ", parts);
            }

            var order = data.VendorOrderData;
            var def = order.Definition;
            var name = ItemText.Name(def.itemId);
            parts.Add($"{def.count} {name}");

            if (inList && data.Vendor != null)
                parts.Add(Loc.Fmt("orders.from", TraderName(data.Vendor.id)));

            var finished = order.State == VendorOrderState.Finished;
            var doneThisWeek = order.IsFinishedThisWeek;
            var locked = data.Vendor != null && data.Vendor.CurTier < order.Tier;

            if (finished) parts.Add(inList ? Loc.Get("orders.done") : Loc.Get("orders.done_collect"));
            else if (doneThisWeek) parts.Add(Loc.Get("orders.done_this_week"));
            else if (locked) parts.Add(Loc.Fmt("orders.locked", order.Tier));
            else
            {
                // Mirrors the widget's own count: what lies on the warehouse pallets plus what the
                // order has already taken.
                var has = OnPallets(def.itemId) + order.Count;
                parts.Add(Loc.Fmt("orders.progress", Mathf.Min(has, def.count), def.count));
                if (def.isUrgent) parts.Add(Loc.Get("orders.urgent"));
            }

            if (def.isRenewable && !locked) parts.Add(Loc.Get("orders.repeatable"));
            if (!locked)
            {
                var reward = def.happinessReward?.EvaluateFloat() ?? 0f;
                if (reward > 0f) parts.Add(Loc.Fmt("orders.reward", Mathf.RoundToInt(reward)));
            }

            if (inList)
            {
                var takeable = boardOpen && order.State == VendorOrderState.Default && !doneThisWeek && !locked;
                parts.Add(takeable ? Loc.Get("orders.enter_take") : boardOpen ? Loc.Get("orders.cannot_take") : Loc.Fmt("orders.only_on", day));
            }
            else if (!finished)
            {
                parts.Add(boardOpen ? Loc.Get("orders.enter_replace") : Loc.Fmt("orders.only_on", day));
            }

            return string.Join(". ", parts);
        }
        catch (Exception ex)
        {
            Plugin.Log?.LogWarning($"[Orders] Could not read an order: {ex.Message}");
            return null;
        }
    }

    private static int OnPallets(string itemId)
    {
        var world = MainGame.WorldData;
        if (world == null) return 0;
        return (world.GetWorldZoneDataById(VendorSystem.WAREHOUSE_ZONE_ID)?.CountItemsOnTownPalettes(itemId) ?? 0) +
               (world.GetWorldZoneDataById(VendorSystem.WAREHOUSE_CELLAR_ID)?.CountItemsOnTownPalettes(itemId) ?? 0);
    }

    /// <summary>The trader's name, as the list's portrait tooltip gives it.</summary>
    private static string TraderName(string vendorId)
    {
        var name = TmpText.Clean(LLBase.L(vendorId));
        return string.IsNullOrWhiteSpace(name) || name == vendorId ? ObjectNames.Of(vendorId) : name;
    }
}
