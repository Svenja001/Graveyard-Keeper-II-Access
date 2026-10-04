using System;
using System.Collections.Generic;
using System.Linq;

namespace GraveyardKeeper2Accessibility;

/// <summary>
/// The tool belt on the character page: one fixed slot per tool type (hammer, small tools, book,
/// reagents, …). On screen an empty slot is only a greyed silhouette of the tool that belongs
/// there, and the tool in it silently raises a mastery - <c>PlayerController.GetMasteryLevelForTalentBranch</c>
/// adds every belt tool's <c>talentBonus</c> to the talents in its <c>talentIds</c>. So a slot
/// says which tool type it takes, what is in it and its bonus, and which tools of that type would
/// raise the mastery further: where they are made and whether the recipe is learned yet (and if
/// not, which tech teaches it). That is the list of what is still to build.
/// </summary>
internal static class ToolSlotReader
{
    /// <summary>A tool-belt slot's text, or null when the cell is not one.</summary>
    internal static string DescribeCell(UIItemCell cell)
    {
        if (cell == null) return null;

        try
        {
            var zombie = cell.GetComponentInParent<ZombieEquipmentInventoryWidget>();
            if (zombie != null) return DescribeZombieCell(zombie, cell);

            if (cell.GetComponentInParent<ToolBeltInventoryWidget>() == null) return null;
            var fixedCell = cell.GetComponentInParent<UIFixedTypeItemCell>();
            if (fixedCell == null || fixedCell.ItemType == ItemType.None) return null;

            var type = fixedCell.ItemType;
            var slot = ObjectStatus.ToolName(type);
            var shown = cell.DisplayingItem;
            var held = shown != null && !shown.IsEmpty && shown.Definition != null ? shown.Definition : null;

            var parts = new List<string>();
            if (held == null)
                parts.Add(Loc.Fmt("toolslot.empty", slot));
            else
            {
                var bonus = Bonus(held);
                parts.Add(bonus == null
                    ? Loc.Fmt("toolslot.holds", slot, ItemText.Name(held.id))
                    : Loc.Fmt("toolslot.holds_bonus", slot, ItemText.Name(held.id), bonus));
            }

            var better = Candidates(type, held == null ? 0 : held.talentBonus);
            if (better.Count > 0)
                parts.Add(Loc.Fmt(held == null ? "toolslot.fits" : "toolslot.better", string.Join("; ", better)));

            return string.Join(". ", parts);
        }
        catch (Exception ex)
        {
            Plugin.Log?.LogWarning($"[ToolSlot] Could not describe cell '{cell.name}': {ex.Message}");
            return null;
        }
    }

    private static readonly AccessTools.FieldRef<ZombieEquipmentInventoryWidget, UIFixedTypeItemCell> ZombieCollar =
        AccessTools.FieldRefAccess<ZombieEquipmentInventoryWidget, UIFixedTypeItemCell>("collarCell");
    private static readonly AccessTools.FieldRef<ZombieEquipmentInventoryWidget, UIFixedTypeItemCell> ZombieArmor =
        AccessTools.FieldRefAccess<ZombieEquipmentInventoryWidget, UIFixedTypeItemCell>("armorCell");
    private static readonly AccessTools.FieldRef<ZombieEquipmentInventoryWidget, UIGroupsItemCell> ZombieTool =
        AccessTools.FieldRefAccess<ZombieEquipmentInventoryWidget, UIGroupsItemCell>("toolCell");
    private static readonly AccessTools.FieldRef<ZombieEquipmentInventoryWidget, UIGroupsItemCell> ZombieWeapon =
        AccessTools.FieldRefAccess<ZombieEquipmentInventoryWidget, UIGroupsItemCell>("weaponCell");

    /// <summary>
    /// A slot in the zombie window (collar, armour, tool, weapon). On screen an empty one is only a
    /// silhouette, so all four read "empty slot" alike; this says which slot it is.
    /// </summary>
    private static string DescribeZombieCell(ZombieEquipmentInventoryWidget widget, UIItemCell cell)
    {
        string key = null;
        if (ZombieCollar(widget)?.UIItemCell == cell) key = "zombieslot.collar";
        else if (ZombieArmor(widget)?.UIItemCell == cell) key = "zombieslot.armor";
        else if (ZombieTool(widget)?.UIItemCell == cell) key = "zombieslot.tool";
        else if (ZombieWeapon(widget)?.UIItemCell == cell) key = "zombieslot.weapon";
        if (key == null) return null;

        var slot = Loc.Get(key);
        var shown = cell.DisplayingItem;
        var empty = shown == null || shown.IsEmpty || string.IsNullOrEmpty(shown.id) || shown.id == "empty";
        return empty ? Loc.Fmt("toolslot.empty", slot) : Loc.Fmt("toolslot.holds", slot, ItemText.Name(shown.id));
    }

    /// <summary>"Bauen +2" - the mastery the tool adds, or null when it adds none.</summary>
    private static string Bonus(ItemDef def)
    {
        if (def.talentBonus <= 0 || def.talentIds == null || def.talentIds.Count == 0) return null;
        return string.Join(", ", def.talentIds.Select(t => Loc.Fmt("toolslot.bonus", CraftReader.TalentName(t), def.talentBonus)));
    }

    /// <summary>
    /// Every tool of this type that adds more mastery than <paramref name="current"/>, weakest
    /// first, one entry per name (quality variants share a name only when they share a bonus).
    /// </summary>
    private static List<string> Candidates(ItemType type, int current)
    {
        var defs = GameBalance.Me.itemDefs
            .Where(d => d != null && d.type == type && d.talentBonus > current && d.talentIds != null && d.talentIds.Count > 0)
            .OrderBy(d => d.talentBonus)
            .ThenBy(d => d.sortOrder);

        var result = new List<string>();
        var seen = new HashSet<string>();
        foreach (var def in defs)
        {
            var name = ItemText.Name(def.id);
            if (string.IsNullOrWhiteSpace(name) || !seen.Add(name)) continue;
            result.Add(string.Join(", ", new[] { name, Bonus(def), Source(def.id) }.Where(s => !string.IsNullOrEmpty(s))));
        }
        return result;
    }

    /// <summary>
    /// Where the tool is made and whether the player can make it yet: "made at the forge", or
    /// "recipe not learned, tech Iron tools", or "not craftable" when no recipe makes it at all.
    /// </summary>
    private static string Source(string itemId)
    {
        var balance = GameBalance.Me;
        var crafts = balance.craftDefs.Where(c => c != null && !c.isHidden && Outputs(balance, c, itemId)).ToList();
        if (crafts.Count == 0) return Loc.Get("toolslot.not_craftable");

        var stations = crafts.SelectMany(c => c.craftsIn ?? new List<string>())
            .Select(ObjectNames.Of)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Distinct()
            .ToList();
        var where = stations.Count == 0 ? null : string.Join(Loc.Get("toolslot.or"), stations);

        var unlocked = MainGame.Instance.GameSave.knowledgeSystem.unlockedCrafts;
        if (crafts.Any(c => !c.isNeedsUnlock || unlocked.Contains(c.id)))
            return where == null ? Loc.Get("toolslot.known") : Loc.Fmt("toolslot.known_at", where);

        var techs = balance.techDefs
            .Where(t => t != null && t.craftsAfterUnlock != null && crafts.Any(c => t.craftsAfterUnlock.Contains(c.id)))
            .Select(t => TmpText.Clean(LLBase.L(t.id)))
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Distinct()
            .ToList();
        var text = techs.Count == 0 ? Loc.Get("toolslot.unknown") : Loc.Fmt("toolslot.unknown_tech", string.Join(Loc.Get("toolslot.or"), techs));
        return where == null ? text : Loc.Fmt("toolslot.unknown_at", text, where);
    }

    private static bool Outputs(GameBalance balance, CraftDef craft, string itemId)
    {
        var outputs = craft.outputItems;
        if (outputs == null) return false;
        foreach (var o in outputs.chanceOutputItems)
            if (Matches(balance, o, itemId)) return true;
        foreach (var g in outputs.groupChanceOutputItems)
            foreach (var o in g.chanceItems)
                if (Matches(balance, o, itemId)) return true;
        return false;
    }

    private static bool Matches(GameBalance balance, ChanceOutputItem o, string itemId)
    {
        if (o == null) return false;
        if (!o.isStarGroup) return o.id == itemId;
        return balance.starGroupItemsCache.TryGetValue(o.id, out var group) && group.Any(d => d != null && d.id == itemId);
    }
}
