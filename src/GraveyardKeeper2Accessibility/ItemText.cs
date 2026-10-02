namespace GraveyardKeeper2Accessibility;

/// <summary>
/// Spoken names and details for items.
///
/// <para>
/// <b>Why not just <c>LLBase.L(item.id)</c>.</b> Items with a star quality carry it in their id -
/// <c>carrot:2</c> is a two-star carrot - and only seven of those ids have text of their own. The
/// game names the rest with <c>ItemDef.GetHeader()</c>, which falls back to the part before the
/// colon. Looking the id up directly would read "carrot:2" aloud, or worse, the raw id of
/// something the player has never heard named.
/// </para>
///
/// <para>
/// <b>And the stars are said.</b> On screen they are a small star icon in the corner of the cell,
/// which is exactly the sort of fact that vanishes silently. It matters: a higher-star ingredient
/// makes a better dish, and a player choosing between two stacks of carrots has no other way to
/// tell them apart.
/// </para>
/// </summary>
internal static class ItemText
{
    internal static ItemDef Def(string id)
    {
        if (string.IsNullOrEmpty(id) || id == "empty") return null;

        try
        {
            return GameBalanceBase.Instance == null ? null : GameBalanceBase.Instance.GetDataOrNull<ItemDef>(id);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// The item's name, with its star quality (bronze, silver, gold) when it has one. Falls back to the id's locale entry
    /// for anything that is not an item definition, so it is safe to call on any id.
    /// </summary>
    internal static string Name(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;

        var def = Def(id);
        if (def == null) return ObjectNames.Of(id);

        var name = TmpText.Clean(def.GetHeader());
        if (string.IsNullOrWhiteSpace(name)) name = TmpText.Clean(LLBase.L(id));

        var stars = Stars(def);
        return string.IsNullOrEmpty(stars) ? name : Loc.Fmt("item.with_stars", name, stars);
    }

    /// <summary>
    /// "silver quality", or null for an item without a star quality. Players name the three star
    /// levels by their colour - bronze, silver, gold - and so does the game's own text ("Improves
    /// bronze items", on the potion that makes silver ones). A level past gold, should one appear,
    /// falls back to a star count.
    /// </summary>
    internal static string Stars(ItemDef def)
    {
        if (def == null || def.qualityType != ItemDef.QualityType.Star || def.quality <= 0) return null;
        return def.quality <= 3 ? Loc.Get("item.quality_" + def.quality) : Loc.Plural("item.stars", def.quality, def.quality);
    }

    /// <summary>
    /// The item's own description, or null when the game has none. <c>LLBase.L</c> returns the key
    /// unchanged for a missing entry, so "the description is its own key" means "there is none".
    /// </summary>
    internal static string Description(string id)
    {
        var def = Def(id);
        if (def == null) return null;

        var key = def.GetDescriptionLocale();
        var text = LLBase.L(key);
        if (string.IsNullOrWhiteSpace(text) || text == key) return null;

        return TmpText.Clean(text);
    }

    /// <summary>How many the player is carrying, bags included, or -1 when that cannot be read.</summary>
    internal static int Held(string id)
    {
        try
        {
            var inventory = MainGame.PlayerData?.Inventory;
            return inventory?.Data == null ? -1 : inventory.Data.GetTotalCountInInventory(id);
        }
        catch
        {
            return -1;
        }
    }
}
