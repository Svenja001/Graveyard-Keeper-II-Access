namespace GraveyardKeeper2Accessibility;

/// <summary>
/// The figures game update 1.012 added to the map: zombies against their limit, the PR campaign's
/// days left, town reputation, the trade orders, the morgue, and the church and graveyard ratings.
///
/// <para>
/// The map shows each one in a hint that appears only while the mouse or the pad's virtual cursor
/// hovers the day icon it belongs to (<c>UIMapDayActivityIcon</c> → <c>UIMapActivityIconHint</c>).
/// They are read here from the same data, by a status key that works anywhere, map open or not.
/// Each part is left out when its zone does not exist yet, which is also when the game's hint
/// would have nothing to show.
/// </para>
/// </summary>
internal static class MapOverview
{
    private static readonly MethodInfo FindOrder =
        AccessTools.Method(typeof(UIVendorOrdersWorldWidgetData), "FindOrder");

    internal static string Describe()
    {
        var parts = new List<string>();
        Add(parts, "zombies", Zombies);
        Add(parts, "pr machine", PrMachine);
        Add(parts, "town", Town);
        Add(parts, "orders", Orders);
        Add(parts, "morgue", Morgue);
        Add(parts, "church", Church);
        return parts.Count == 0 ? Loc.Get("overview.none") : string.Join(". ", parts);
    }

    private static void Add(List<string> parts, string what, Func<string> part)
    {
        try
        {
            var text = part();
            if (!string.IsNullOrWhiteSpace(text)) parts.Add(text);
        }
        catch (Exception ex)
        {
            Plugin.Log?.LogWarning($"[Overview] Could not read the {what}: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static WorldZoneData Zone(string id) => MainGame.WorldData?.GetWorldZoneDataById(id);

    private static string Zombies()
    {
        var zone = Zone("resurrection");
        if (zone == null) return null;
        var count = Mathf.FloorToInt(MainGame.PlayerData.GetRes("cur_zombies_count"));
        return Loc.Fmt("overview.zombies", count, Mathf.FloorToInt(zone.GetTotalQuality()));
    }

    private static string PrMachine()
    {
        if (MainGame.WorldData?.GetWgoDataByCustomTag("panic_reduction_machine") == null) return null;
        var days = Mathf.FloorToInt(MainGame.PlayerData.GetRes("pr_machine_left_active_days"));
        return days > 0 ? Loc.Fmt("overview.pr_days", days) : Loc.Get("overview.pr_idle");
    }

    private static string Town() =>
        Loc.Fmt("status.town", TmpText.Clean(LLBase.L("town_zone")), MainGame.Instance.GameSave.townSystem.Quality);

    /// <summary>
    /// "Orders: 3 of 5 Pottery, 0 of 2 Wine". The map's hint draws only the numbers, one line per
    /// accepted order; the names come from walking the same orders in the same order.
    /// </summary>
    private static string Orders()
    {
        if (!UIVendorOrdersWorldWidgetData.TryCreate(out var data) || data.Lines.Count == 0) return null;

        var names = new List<string>();
        var system = MainGame.Instance.GameSave.vendorSystem;
        foreach (var id in system.currentOrders)
        {
            if (id == null || id.IsEmpty) continue;
            var def = (FindOrder?.Invoke(null, new object[] { system, id }) as VendorOrderData)?.Definition;
            if (def != null && def.count > 0) names.Add(ItemText.Name(def.itemId));
        }

        var lines = new List<string>();
        for (var i = 0; i < data.Lines.Count; i++)
        {
            var line = data.Lines[i];
            lines.Add(names.Count == data.Lines.Count
                ? Loc.Fmt("overview.order", line.Delivered, line.Required, names[i])
                : Loc.Fmt("overview.order_unnamed", line.Delivered, line.Required));
        }
        return Loc.Fmt("overview.orders", string.Join(", ", lines));
    }

    private static string Morgue()
    {
        var zone = Zone("morgue");
        if (zone == null) return null;
        var quality = TmpText.Clean(zone.GetQualityString());
        return string.IsNullOrWhiteSpace(quality) ? null : $"{ZoneName("morgue")}: {quality}";
    }

    private static string Church()
    {
        var parts = new List<string>();
        foreach (var id in new[] { "church", "graveyard" })
        {
            var zone = Zone(id);
            if (zone != null) parts.Add(Loc.Fmt("overview.rating", ZoneName(id), Mathf.FloorToInt(zone.GetTotalQuality())));
        }
        return parts.Count == 0 ? null : string.Join(", ", parts);
    }

    private static string ZoneName(string id)
    {
        var name = TmpText.Clean(LLBase.L("wz_" + id));
        return string.IsNullOrWhiteSpace(name) || name == "wz_" + id ? id : name;
    }
}
