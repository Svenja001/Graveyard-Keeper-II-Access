namespace GraveyardKeeper2Accessibility;

/// <summary>
/// The trading window (<c>UIVendorWindow</c>).
///
/// <para>
/// On 2026-09-25 items could be moved into the deal with Space, but the player could not trade:
/// nothing said what anything cost, which of the four lists focus was in, or what the deal came to,
/// and the deal button listens for <c>GameKey.AcceptVendorDeal</c>, a controller button with no
/// keyboard key. Everything here is read from <c>UIVendorWindowData</c> - the same delegates the
/// window draws its prices and its greyed-out button from - so it cannot disagree with the screen.
/// </para>
///
/// <para>
/// The four lists: your bag (left), what you are selling (left deal row), the trader's goods
/// (right), what you are buying (right deal row). F6 jumps between them, F accepts the deal, F8
/// says the deal so far, Escape clears the deal or, with nothing in it, closes the window.
/// </para>
/// </summary>
internal static class VendorReader
{
    internal static bool Ready { get; private set; }

    private static class G
    {
        internal static readonly AccessTools.FieldRef<LazyWidget<UIVendorWindowData>, UIVendorWindowData> Data =
            AccessTools.FieldRefAccess<LazyWidget<UIVendorWindowData>, UIVendorWindowData>("data");

        internal static readonly AccessTools.FieldRef<UIVendorWindow, MultiInventoryWidget> PlayerInv =
            AccessTools.FieldRefAccess<UIVendorWindow, MultiInventoryWidget>("playerInventoryWidget");

        internal static readonly AccessTools.FieldRef<UIVendorWindow, MultiInventoryWidget> VendorInv =
            AccessTools.FieldRefAccess<UIVendorWindow, MultiInventoryWidget>("vendorInventoryWidget");

        internal static readonly AccessTools.FieldRef<UIVendorWindow, VendorDealInventoryWidget> Sell =
            AccessTools.FieldRefAccess<UIVendorWindow, VendorDealInventoryWidget>("sellDealInventoryWidget");

        internal static readonly AccessTools.FieldRef<UIVendorWindow, VendorDealInventoryWidget> Buy =
            AccessTools.FieldRefAccess<UIVendorWindow, VendorDealInventoryWidget>("buyDealInventoryWidget");
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
            Plugin.Log?.LogError($"[Vendor] The trading window is not supported by this game version: " +
                                 $"{(ex.InnerException ?? ex).GetType().Name}: {(ex.InnerException ?? ex).Message}");
        }
    }

    internal static bool SpeaksForItself(LazyWidgetBase window) => Ready && window is UIVendorWindow;

    private static UIVendorWindow Active => Ready ? LazyWindowsStackController.ActiveWindow as UIVendorWindow : null;

    private enum Side { None, Bag, Selling, Goods, Buying }

    private static readonly Side[] Order = { Side.Bag, Side.Selling, Side.Goods, Side.Buying };

    private static Side SideOf(GamepadNavigationItem item, out UIVendorWindow window)
    {
        window = item == null ? null : item.GetComponentInParent<UIVendorWindow>();
        if (window == null) return Side.None;

        var t = item.transform;
        Component c;
        if ((c = G.Sell(window)) != null && t.IsChildOf(c.transform)) return Side.Selling;
        if ((c = G.Buy(window)) != null && t.IsChildOf(c.transform)) return Side.Buying;
        if ((c = G.PlayerInv(window)) != null && t.IsChildOf(c.transform)) return Side.Bag;
        if ((c = G.VendorInv(window)) != null && t.IsChildOf(c.transform)) return Side.Goods;
        return Side.None;
    }

    // ---- focus ------------------------------------------------------------------------------

    /// <summary>The list's spoken name, said when focus crosses into it. Null outside the window.</summary>
    internal static string SectionName(GamepadNavigationItem item)
    {
        if (!Ready) return null;
        return SideOf(item, out _) switch
        {
            Side.Bag => Loc.Get("vendor.side_bag"),
            Side.Selling => Loc.Get("vendor.side_selling"),
            Side.Goods => Loc.Get("vendor.side_goods"),
            Side.Buying => Loc.Get("vendor.side_buying"),
            _ => null,
        };
    }

    /// <summary>A cell's reading with its price added, or the reading unchanged outside the window.</summary>
    internal static string WithPrice(GamepadNavigationItem item, string label)
    {
        if (!Ready || item == null || string.IsNullOrEmpty(label)) return label;
        try
        {
            var side = SideOf(item, out var window);
            if (side == Side.None) return label;

            var cell = item.GetComponent<UIItemCell>() ?? item.GetComponentInParent<UIItemCell>();
            var shown = cell?.DisplayingItem;
            if (shown == null || shown.IsEmpty || shown.id == "empty") return label;

            if (!cell.IsInteractable)
                return $"{label}, {Loc.Get(side == Side.Goods ? "vendor.not_for_sale" : "vendor.not_bought")}";

            var data = G.Data(window);
            if (data == null) return label;
            var price = side switch
            {
                Side.Bag => data.PlayerInvPriceDelegate?.Invoke(shown, 1),
                Side.Selling => data.SellInvPriceDelegate?.Invoke(shown, 0),
                Side.Goods => data.VendorInvPriceDelegate?.Invoke(shown, 0),
                Side.Buying => data.BuyInvPriceDelegate?.Invoke(shown, 1),
                _ => null,
            };
            if (price == null) return label;
            var text = $"{label}, {Loc.Fmt("vendor.price_each", Money.ToSpeech(price.Value))}";
            var likes = side == Side.Bag ? Likes(data, shown.id) : null;
            return likes == null ? text : $"{text}, {likes}";
        }
        catch (Exception ex)
        {
            Plugin.Log?.LogWarning($"[Vendor] Could not price '{item.name}': {ex.GetType().Name}: {ex.Message}");
            return label;
        }
    }

    /// <summary>
    /// Whether selling this item to a town trader earns happiness (the player's "likes"), or null at
    /// a trader who never gives any. On screen it is a small heart icon on the cell. The rule is the
    /// game's own (<c>Vendor.HasHappinessForItem</c>, <c>Trading.GetTotalDealHappiness</c>): the
    /// trader's tier lists the items it likes, each worth <c>perOne</c> for up to <c>itemCount</c>
    /// sold a week, and the whole week is capped at <c>happinessCap</c>. Items already in the deal
    /// count as sold.
    /// </summary>
    private static string Likes(UIVendorWindowData data, string id)
    {
        var vendor = data.Vendor;
        if (vendor == null || !vendor.Definition.townVendor) return null;

        var info = vendor.CurrentTierData?.GetTownVendorProductInfo(id);
        if (info == null || info.perOne <= 0f) return Loc.Get("vendor.likes_none");

        var pending = Math.Max(0, data.GetPendingHappinessSoldCount?.Invoke(id) ?? 0);
        var left = info.itemCount - vendor.SoldItemsWithHappinessThisWeek.GetInt(id) - pending;
        if (left <= 0) return Loc.Get("vendor.likes_used_up");

        var inDeal = Math.Max(0f, data.GetTotalHappinessDealDelegate?.Invoke() ?? 0f);
        if (vendor.CurrentTierData.happinessCap.EvaluateFloat() - vendor.UsedHappinessThisWeek - inDeal <= 0f)
            return Loc.Get("vendor.likes_cap");

        return Loc.Fmt("vendor.likes", info.perOne.ToString("0.##"), left);
    }

    // ---- the deal ---------------------------------------------------------------------------

    private static string ItemsIn(InventoryWidgetDataBase widget)
    {
        var items = widget?.Inventory?.Data?.Inventory;
        if (items == null) return null;
        var counts = new List<(string Id, int Count)>();
        foreach (var item in items)
        {
            if (item == null || item.IsEmpty || item.id == "empty") continue;
            var i = counts.FindIndex(c => c.Id == item.id);
            if (i < 0) counts.Add((item.id, item.Count));
            else counts[i] = (item.id, counts[i].Count + item.Count);
        }
        if (counts.Count == 0) return null;
        return string.Join(", ", counts.Select(c => c.Count > 1 ? $"{c.Count} {ItemText.Name(c.Id)}" : ItemText.Name(c.Id)));
    }

    private static int PlayerMoney(UIVendorWindowData data) => data.PlayerMoneyWidgetData?.Money?.Invoke() ?? 0;
    private static int VendorMoney(UIVendorWindowData data) => data.VendorMoneyWidgetData?.Money?.Invoke() ?? 0;

    /// <summary>What is in the deal, what it comes to, and why it cannot be accepted if it cannot.</summary>
    private static string DealSummary(UIVendorWindowData data, bool full)
    {
        var selling = ItemsIn(data.DealSellInventoryWidgetData);
        var buying = ItemsIn(data.DealBuyInventoryWidgetData);
        if (selling == null && buying == null)
            return full ? Loc.Fmt("vendor.deal_empty", Money.ToSpeech(PlayerMoney(data))) : Loc.Get("vendor.deal_cleared");

        var parts = new List<string>();
        if (full && selling != null) parts.Add(Loc.Fmt("vendor.selling", selling));
        if (full && buying != null) parts.Add(Loc.Fmt("vendor.buying", buying));

        var total = data.DealMoneyWidgetData?.Money?.Invoke() ?? 0;
        parts.Add(total > 0 ? Loc.Fmt("vendor.you_get", Money.ToSpeech(total))
                : total < 0 ? Loc.Fmt("vendor.you_pay", Money.ToSpeech(-total))
                : Loc.Get("vendor.even"));

        if (data.Vendor != null && data.Vendor.Definition.townVendor)
        {
            var happiness = data.GetTotalHappinessDealDelegate?.Invoke() ?? 0f;
            if (happiness > 0.001f) parts.Add(Loc.Fmt("vendor.happiness", (Mathf.Floor(happiness * 100f) / 100f).ToString("0.##")));
        }

        parts.Add(WhyNot(data, total) ?? Loc.Fmt("vendor.accept_hint", CraftReader.StartKey.ToString()));
        return string.Join(". ", parts);
    }

    /// <summary>Why the deal button is greyed out, or null when it is not.</summary>
    private static string WhyNot(UIVendorWindowData data, int total)
    {
        if (data.ApplyButtonInteractableCondition == null || data.ApplyButtonInteractableCondition()) return null;
        if (PlayerMoney(data) + total < 0) return Loc.Get("vendor.no_money");
        if (VendorMoney(data) - total < 0) return Loc.Get("vendor.vendor_no_money");
        if (data.EnoughHappinessCondition != null && !data.EnoughHappinessCondition()) return Loc.Get("vendor.no_happiness");
        if (data.PlayerInventoryCanAcceptBuyItemsCondition != null && !data.PlayerInventoryCanAcceptBuyItemsCondition())
            return Loc.Get("vendor.bag_full");
        return Loc.Get("vendor.vendor_full");
    }

    [HarmonyPatch(typeof(UIVendorWindow), nameof(UIVendorWindow.Open))]
    [HarmonyPostfix]
    private static void UIVendorWindow_Open(UIVendorWindow __instance)
    {
        try
        {
            var data = G.Data(__instance);
            if (data?.Vendor == null) return;
            var name = TmpText.Clean(LLBase.L(data.Vendor.Definition.id));
            Say(Loc.Fmt("vendor.open", name, Money.ToSpeech(PlayerMoney(data)), Money.ToSpeech(VendorMoney(data)),
                        CraftReader.StartKey.ToString()));
        }
        catch (Exception ex)
        {
            Plugin.Log?.LogError($"[Vendor] Reading the trading window failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>After every change to the deal: the new balance, after the "moved" line.</summary>
    [HarmonyPatch(typeof(UIVendorWindow), nameof(UIVendorWindow.RedrawLite))]
    [HarmonyPostfix]
    private static void UIVendorWindow_RedrawLite(UIVendorWindow __instance)
    {
        try
        {
            if (_accepting) return;
            var data = G.Data(__instance);
            if (data == null) return;
            Say(DealSummary(data, full: false), interrupt: false);
        }
        catch (Exception ex)
        {
            Plugin.Log?.LogError($"[Vendor] Reading the deal failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static bool _accepting;

    // ---- keys -------------------------------------------------------------------------------

    /// <summary>F accepts the deal, F6 changes list. True when handled.</summary>
    internal static bool TryKeys(bool switchSide)
    {
        var window = Active;
        if (window == null) return false;

        try
        {
            var data = G.Data(window);
            if (data == null) return false;

            if (CraftReader.StartKey.IsDown())
            {
                Accept(data);
                return true;
            }

            if (switchSide)
            {
                Cycle(window);
                return true;
            }
        }
        catch (Exception ex)
        {
            Plugin.Log?.LogError($"[Vendor] Key handling failed: {ex.GetType().Name}: {ex.Message}");
        }
        return false;
    }

    /// <summary>F8 says the deal so far.</summary>
    internal static bool TryRepeat()
    {
        var window = Active;
        var data = window == null ? null : G.Data(window);
        if (data == null) return false;
        Say(DealSummary(data, full: true));
        return true;
    }

    private static void Accept(UIVendorWindowData data)
    {
        var selling = ItemsIn(data.DealSellInventoryWidgetData);
        var buying = ItemsIn(data.DealBuyInventoryWidgetData);
        if (selling == null && buying == null)
        {
            Say(Loc.Get("vendor.nothing_to_accept"));
            return;
        }

        var total = data.DealMoneyWidgetData?.Money?.Invoke() ?? 0;
        var why = WhyNot(data, total);
        if (why != null)
        {
            Say(why);
            return;
        }

        _accepting = true;
        try
        {
            data.OnApplyDealBtnClicked?.Invoke();
            data.OnRedraw?.Invoke();
        }
        finally
        {
            _accepting = false;
        }

        Plugin.Log?.LogInfo($"[Vendor] Deal accepted: sold [{selling}], bought [{buying}], balance {total}.");
        Say(Loc.Fmt("vendor.done", Money.ToSpeech(PlayerMoney(data))));
    }

    private static void Cycle(UIVendorWindow window)
    {
        var focused = UiNarrator.FocusedItem;
        var current = SideOf(focused, out _);

        var bySide = new Dictionary<Side, List<GamepadNavigationItem>>();
        foreach (var nav in window.GetComponentsInChildren<GamepadNavigationItem>(includeInactive: false))
        {
            if (nav == null || !nav.Active || !nav.TryGetComponent<UIItemCell>(out _)) continue;
            var side = SideOf(nav, out _);
            if (side == Side.None) continue;
            if (!bySide.TryGetValue(side, out var list)) bySide[side] = list = new List<GamepadNavigationItem>();
            list.Add(nav);
        }

        var start = Math.Max(0, Array.IndexOf(Order, current));
        for (var step = 1; step <= Order.Length; step++)
        {
            var side = Order[(start + step) % Order.Length];
            if (!bySide.TryGetValue(side, out var cells) || cells.Count == 0) continue;

            var target = cells.FirstOrDefault(n => n.TryGetComponent<UIItemCell>(out var c) && c.DisplayingItem != null &&
                                                   !c.DisplayingItem.IsEmpty && c.DisplayingItem.id != "empty") ?? cells[0];
            var controller = focused != null && focused.Controller != null
                ? focused.Controller
                : window.GetComponentInChildren<GamepadNavigationController>();
            if (controller == null) return;

            controller.ReinitItems(focusOnFirstActive: false);
            controller.SetFocusedItem(target);
            return;
        }
    }

    private static void Say(string text, bool interrupt = true)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        Plugin.Log?.LogInfo($"[Vendor] \"{text}\"");
        ScreenReader.Say(text, interrupt);
    }
}
