namespace GraveyardKeeper2Accessibility;

/// <summary>
/// Says what the player lifts overhead, puts down, or hands over, and where a carried supply crate
/// is wanted.
///
/// <para>
/// Big drops - logs, bodies, supply crates ("Vorrat: Eisen") - are not taken into the inventory.
/// E lifts them overhead (<c>PlayerData.AddOverheadItem</c>), and nothing on screen but the sprite
/// says so. E or F with nothing in front of the player drops the load again
/// (<c>PlayerInputHandler</c> falls back to <c>DropOverheadItem</c>), so a walk that ends facing
/// "nothing" can put a crate on the floor without a word. On 2026-10-04 the player built a crate
/// for Jack, picked it up and could not tell whether it was still carried or where it went.
/// </para>
///
/// <para>
/// Crates go to the town pallets in the warehouse and its cellar (<c>InteractionType.TownPalette</c>,
/// ids <c>crates_big</c> / <c>crates_small</c>). A crate there only counts towards an order that has
/// been <b>accepted</b> on the warehouse chalk board (<c>VendorSystem.currentOrders</c>); the crate
/// of 2026-10-04 lay on the pallet through a night because no order slot asked for it. So the
/// delivery line says which accepted order the crate counts towards, or that none does.
/// </para>
/// </summary>
[HarmonyPatch]
internal static class CarryAnnouncer
{
    private static readonly string[] WarehouseZones = { VendorSystem.WAREHOUSE_ZONE_ID, VendorSystem.WAREHOUSE_CELLAR_ID };

    private static ManualLogSource _log;

    /// <summary>Inside a drop or a hand-over; the inner <c>RemoveOverheadItem</c> stays quiet.</summary>
    private static int _depth;

    /// <summary>A crate put on a town pallet; its order line waits a frame for <c>TryResolveOrders</c>.</summary>
    private static string _deliveredItem;
    private static int _deliveredFrame;

    internal static void Init(ManualLogSource log) => _log = log;

    internal static void Update()
    {
        if (_deliveredItem == null || Time.frameCount == _deliveredFrame) return;

        var id = _deliveredItem;
        _deliveredItem = null;
        try
        {
            var line = OrderLine(id);
            if (line != null) ScreenReader.Say(line, interrupt: false);
        }
        catch (Exception ex)
        {
            _log?.LogError($"[Carry] Order line failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    // ---- patches ------------------------------------------------------------------------------

    [HarmonyPostfix]
    [HarmonyPatch(typeof(PlayerData), nameof(PlayerData.AddOverheadItem))]
    private static void AfterAdd(PlayerData __instance, Item item)
    {
        try
        {
            if (!IsPlayer(__instance) || item == null || !Carries(__instance, item)) return;

            _log?.LogInfo($"[Carry] Picked up '{item.id}' ({__instance.OverheadCount} overhead).");
            var parts = new List<string> { Loc.Fmt("carry.picked", ItemText.Name(item.id)) };
            if (__instance.OverheadCount > 1) parts.Add(Loc.Fmt("carry.count", __instance.OverheadCount));

            var where = DeliveryLine(item);
            if (where != null) parts.Add(where);

            ScreenReader.Say(string.Join(". ", parts), interrupt: false);
        }
        catch (Exception ex)
        {
            _log?.LogError($"[Carry] Pickup announce failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(PlayerData), nameof(PlayerData.DropOverheadItem), typeof(Item))]
    private static void BeforeDrop(PlayerData __instance, Item item, out bool __state)
    {
        __state = IsPlayer(__instance) && Carries(__instance, item);
        _depth++;
    }

    [HarmonyFinalizer]
    [HarmonyPatch(typeof(PlayerData), nameof(PlayerData.DropOverheadItem), typeof(Item))]
    private static Exception AfterDrop(PlayerData __instance, Item item, bool __state, Exception __exception)
    {
        _depth--;
        if (__exception == null && __state && !Carries(__instance, item))
        {
            _log?.LogInfo($"[Carry] Dropped '{item.id}' on the ground.");
            ScreenReader.Say(Loc.Fmt("carry.dropped", ItemText.Name(item.id)), interrupt: false);
        }
        return __exception;
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(PlayerData), nameof(PlayerData.InsertOverheadItemTo), typeof(WgoData), typeof(Item))]
    private static void BeforeInsert(PlayerData __instance, Item item, out bool __state)
    {
        __state = IsPlayer(__instance) && Carries(__instance, item);
        _depth++;
    }

    [HarmonyFinalizer]
    [HarmonyPatch(typeof(PlayerData), nameof(PlayerData.InsertOverheadItemTo), typeof(WgoData), typeof(Item))]
    private static Exception AfterInsert(PlayerData __instance, WgoData wgoData, Item item, bool __state, Exception __exception)
    {
        _depth--;
        if (__exception != null || !__state || Carries(__instance, item)) return __exception;

        try
        {
            _log?.LogInfo($"[Carry] Put '{item.id}' into '{wgoData?.id}'.");
            ScreenReader.Say(Loc.Fmt("carry.inserted", ItemText.Name(item.id), ObjectNames.Of(wgoData?.id)), interrupt: false);

            if (IsTownPalette(wgoData))
            {
                _deliveredItem = item.id;
                _deliveredFrame = Time.frameCount;
            }
        }
        catch (Exception ex)
        {
            _log?.LogError($"[Carry] Hand-over announce failed: {ex.GetType().Name}: {ex.Message}");
        }
        return __exception;
    }

    /// <summary>Anything else that takes the load away: autopsy table, cremation, a story flow.</summary>
    [HarmonyPrefix]
    [HarmonyPatch(typeof(PlayerData), nameof(PlayerData.RemoveOverheadItem), typeof(Item))]
    private static void BeforeRemove(PlayerData __instance, Item item, out bool __state) =>
        __state = _depth == 0 && IsPlayer(__instance) && Carries(__instance, item);

    [HarmonyPostfix]
    [HarmonyPatch(typeof(PlayerData), nameof(PlayerData.RemoveOverheadItem), typeof(Item))]
    private static void AfterRemove(PlayerData __instance, Item item, bool __state)
    {
        if (!__state || Carries(__instance, item)) return;
        _log?.LogInfo($"[Carry] '{item.id}' taken off the player's head.");
        ScreenReader.Say(Loc.Fmt("carry.gone", ItemText.Name(item.id)), interrupt: false);
    }

    // ---- queries ------------------------------------------------------------------------------

    /// <summary>
    /// "Du trägst Vorrat: Eisen. Abgeben an: …" for the status key, or "nothing overhead".
    /// </summary>
    internal static string Describe()
    {
        var player = MainGame.PlayerData;
        var items = player?.OverheadItems?.Where(i => i != null && !i.IsEmpty).ToList();
        if (items == null || items.Count == 0) return Loc.Get("carry.nothing");

        var parts = new List<string> { Loc.Fmt("carry.carrying", string.Join(", ", items.Select(i => ItemText.Name(i.id)))) };
        foreach (var item in items.GroupBy(i => i.id).Select(g => g.First()))
        {
            var where = DeliveryLine(item);
            if (where != null) parts.Add(where);
        }
        return string.Join(". ", parts);
    }

    /// <summary>
    /// The town pallets that would take one of the player's carried crates, for the navigator's
    /// story list. Empty when nothing carried is something an order can ask for.
    /// </summary>
    internal static IEnumerable<WgoData> DeliveryTargets()
    {
        var items = MainGame.PlayerData?.OverheadItems;
        if (items == null) yield break;

        foreach (var item in items)
        {
            if (item == null || item.IsEmpty || !IsOrderable(item.id)) continue;
            foreach (var (data, _) in PalettesFor(item)) yield return data;
        }
    }

    /// <summary>"Abgeben an: Lieferpalette, Stadt-Lagerhaus. Kein angenommener Auftrag …".</summary>
    private static string DeliveryLine(Item item)
    {
        if (!IsOrderable(item.id)) return null;

        var places = PalettesFor(item)
            .Select(p => Loc.Fmt("carry.place", ObjectNames.Of(p.data.id), ZoneName(p.zone)))
            .Distinct()
            .ToList();
        if (places.Count == 0) return null;

        var line = Loc.Fmt("carry.deliver_to", string.Join("; ", places));
        var order = OrderLine(item.id);
        return order == null ? line : line + ". " + order;
    }

    /// <summary>
    /// Where the accepted orders stand for this item: how many are still wanted, done, or that no
    /// accepted order asks for it at all. Mirrors <c>VendorSystem.TryResolveOrders</c>.
    /// </summary>
    private static string OrderLine(string itemId)
    {
        var system = MainGame.Instance?.GameSave?.vendorSystem;
        if (system == null || !IsOrderable(itemId)) return null;

        var onPallets = WarehouseZones.Sum(z => MainGame.WorldData?.GetWorldZoneDataById(z)?.CountItemsOnTownPalettes(itemId) ?? 0);
        var name = ItemText.Name(itemId);
        var lines = new List<string>();

        foreach (var (order, _) in system.GetCurrentOrders())
        {
            var def = order?.Definition;
            if (def == null || def.itemId != itemId) continue;

            if (order.State == VendorOrderState.Finished)
            {
                lines.Add(Loc.Fmt("carry.order_done", $"{def.count} {name}", ObjectNames.Of("town_chalk_board")));
                continue;
            }

            var delivered = def.isUrgent ? 0 : order.Count;
            lines.Add(Loc.Fmt("carry.order_open", def.count - delivered, def.count, name));
        }

        if (lines.Count == 0)
        {
            lines.Add(Loc.Fmt("carry.order_none", ObjectNames.Of("town_chalk_board"), Loc.Find("icon.day_pride") ?? "day_pride"));
            if (onPallets > 0) lines.Add(Loc.Fmt("carry.on_pallets", onPallets, name, Loc.Find("icon.day_pride") ?? "day_pride"));
        }

        _log?.LogInfo($"[Carry] Orders for '{itemId}': {string.Join(" | ", lines)} (on pallets: {onPallets}).");
        return string.Join(". ", lines);
    }

    private static IEnumerable<(WgoData data, string zone)> PalettesFor(Item item)
    {
        var world = MainGame.WorldData;
        if (world == null) yield break;

        foreach (var zoneId in WarehouseZones)
        {
            var zone = world.GetWorldZoneDataById(zoneId);
            if (zone?.wgoDataList == null) continue;

            foreach (var uid in zone.wgoDataList)
            {
                var data = world.GetWgoData(uid);
                if (!IsTownPalette(data)) continue;

                bool fits;
                try { fits = data.Inventory != null && data.Inventory.CanAddItemToInventory(item); }
                catch { fits = false; }
                if (fits) yield return (data, zoneId);
            }
        }
    }

    /// <summary>Something a town order can ask for - the supply crates, not every log or body.</summary>
    private static bool IsOrderable(string itemId) =>
        !string.IsNullOrEmpty(itemId) && GameBalance.Me?.vendorOrderDefs != null &&
        GameBalance.Me.vendorOrderDefs.Any(d => d != null && d.itemId == itemId);

    private static bool IsTownPalette(WgoData data) =>
        data?.Definition != null && data.Definition.interactionType == WGODef.InteractionType.TownPalette;

    private static string ZoneName(string zoneId) => TmpText.Clean(LLBase.L("wz_" + zoneId));

    private static bool IsPlayer(PlayerData data) => data != null && ReferenceEquals(data, MainGame.PlayerData);

    private static bool Carries(PlayerData data, Item item)
    {
        var items = data?.OverheadItems;
        if (items == null || item == null) return false;
        for (var i = 0; i < items.Count; i++)
            if (ReferenceEquals(items[i], item)) return true;
        return false;
    }
}
