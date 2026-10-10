namespace GraveyardKeeper2Accessibility;

/// <summary>
/// What state an object is in right now, said after its name in the object list: how far a
/// station has got with what it is making, and why a rock or tree cannot be worked yet.
///
/// <para>
/// <b>On screen both are drawn, not written.</b> A furnace shows a filling bar over its icon, and
/// a stone the player is not skilled enough for shows a greyed tool with a small number beside it
/// - only when the player is already standing at it. The list is how a blind player surveys the
/// base and the woods, so it has to carry what a sighted player takes in at a glance across the
/// screen: "furnace, making bronze ingots, 40 percent" and "stones, needs pickaxe mastery 4, you
/// have 2", before walking over.
/// </para>
///
/// <para>
/// The tests are the game's own. The lock mirrors
/// <c>WGOInteractionHandlerBase.GetInteractionInfoByUsingTool</c> (tool on the belt, talent level
/// against <c>WGODef.MasteryLock</c>, a planted seed's own lock overriding it); the craft state is
/// the station's <c>CraftComponent</c>, which runs whether or not anyone is looking.
/// </para>
/// </summary>
internal static class ObjectStatus
{
    private static ManualLogSource _log;

    internal static void Init(ManualLogSource log) => _log = log;

    /// <summary>"making bronze ingot, 40 percent, 15 more queued", or null when there is nothing to say.</summary>
    internal static string Of(WgoData data)
    {
        if (data == null || data.Definition == null) return null;

        var parts = new List<string>(3);
        try
        {
            var military = MilitaryReader.Status(data);
            if (military != null) parts.Add(military);
        }
        catch (Exception ex)
        {
            _log?.LogWarning($"[Status] Could not read the fight state of '{data.id}': {ex.Message}");
        }

        try
        {
            var extension = Extension(data);
            if (extension != null) parts.Add(extension);
        }
        catch (Exception ex)
        {
            _log?.LogWarning($"[Status] Could not read the workbench link of '{data.id}': {ex.Message}");
        }

        try
        {
            var craft = Craft(data);
            if (craft != null) parts.Add(craft);
        }
        catch (Exception ex)
        {
            _log?.LogWarning($"[Status] Could not read the craft state of '{data.id}': {ex.Message}");
        }

        try
        {
            // A fertilized bed looks different on screen; in the list it says so.
            if (data.Definition.interactionType == WGODef.InteractionType.Garden)
            {
                var fertilizers = CraftReader.FertilizerNames(data);
                if (fertilizers.Count > 0) parts.Add(Loc.Fmt("status.fertilized", string.Join(", ", fertilizers)));
            }
        }
        catch (Exception ex)
        {
            _log?.LogWarning($"[Status] Could not read the fertilizer of '{data.id}': {ex.Message}");
        }

        try
        {
            var stock = Stock(data);
            if (stock != null) parts.Add(stock);
        }
        catch (Exception ex)
        {
            _log?.LogWarning($"[Status] Could not read the stock of '{data.id}': {ex.Message}");
        }

        try
        {
            var locked = WorkLock(data);
            if (locked != null) parts.Add(locked);
        }
        catch (Exception ex)
        {
            _log?.LogWarning($"[Status] Could not read the work lock of '{data.id}': {ex.Message}");
        }

        return parts.Count == 0 ? null : string.Join(", ", parts);
    }

    /// <summary>
    /// What a station or bed is making and how far along it is. A station with an empty queue
    /// says nothing - most crafting objects are idle most of the time, and "idle" on every
    /// workbench would bury the ones that are busy.
    /// </summary>
    private static string Craft(WgoData data)
    {
        var craft = data.CraftComponent;
        if (craft == null) return null;

        // An automatic station (furnace, kiln) holds its output until it is taken: on screen, the
        // "take all" hint. Nothing else will happen there until the player comes.
        if (craft.Status == CraftComponentStatus.ReadyToFinishAutoCraft) return Loc.Get("status.ready_to_take");

        var element = craft.CurrentCraftElement;
        if (element == null || element.Def == null) return null;

        // Marked in remove mode: the building now waits to be knocked down with the work key.
        if (craft.IsDestroyingCraftActive)
        {
            var done = element.IsStarted ? Mathf.RoundToInt(Mathf.Clamp01(element.ProgressTimeNormalized) * 100f) : 0;
            return Loc.Fmt("status.demolish", done);
        }

        var name = CraftReader.RecipeName(element.Def, data);
        var type = data.Definition.interactionType;
        var growing = type == WGODef.InteractionType.Garden;

        // The research table's order is named after the research, not after what is studied.
        var researching = type == WGODef.InteractionType.Survey;
        if (researching) name = InputNames(element) ?? name;

        string line;
        if (element.IsStarted)
        {
            var percent = Mathf.RoundToInt(Mathf.Clamp01(element.ProgressTimeNormalized) * 100f);
            line = Loc.Fmt(growing ? "status.growing" : researching ? "status.researching" : "status.making", name, percent);
        }
        else
        {
            // Queued but not running: say why, since that is what the player walks over to fix.
            var status = element.CraftStatus;
            line = status == CraftStatus.OK || status == CraftStatus.Other
                ? Loc.Fmt("status.queued", name)
                : Loc.Fmt("status.queued_blocked", name, CraftReader.StatusText(status));
        }

        var more = QueuedAfter(craft, element);
        return more == null ? line : $"{line}, {more}";
    }

    /// <summary>"3 water" - what went into an order, or null when nothing is recorded.</summary>
    private static string InputNames(CraftElementBase element)
    {
        var input = element.CraftInput;
        if (input == null) return null;
        var names = input.Where(i => i != null && !i.IsEmpty)
            .GroupBy(i => i.id)
            .Select(g => { var n = g.Sum(i => Math.Max(1, i.Count)); return n > 1 ? $"{n} {ItemText.Name(g.Key)}" : ItemText.Name(g.Key); })
            .ToList();
        return names.Count == 0 ? null : string.Join(", ", names);
    }

    /// <summary>
    /// What a station holds for its work (user, 2026-09-26: "the flask shelf should say what is in
    /// it"). A storage station - the flask shelf, the woodshed - is filled by its own "fuel"
    /// recipe and says "12 of 40 flasks"; a station that burns fuel - the furnace - says how much
    /// of it is at hand. Both read exactly what <c>UIInfoWidget</c> shows in the station's panel.
    /// </summary>
    internal static string Stock(WgoData data)
    {
        if (data?.Definition == null) return null;
        var craft = data.CraftComponent;

        var crafts = craft?.AvailableCrafts;
        if (crafts != null && crafts.Count > 0 && crafts[0] != null && crafts[0].isFuelCraft)
        {
            var item = crafts[0].FuelItemDef;
            var inventory = data.Inventory?.Data;
            if (item == null || inventory == null) return null;
            var have = inventory.GetTotalCountInInventory(item.id);
            var room = data.Definition.emptyCellStackCount * inventory.InventorySize;
            return Loc.Fmt("status.stock", have, room, ItemText.Name(item.id));
        }

        if (!string.IsNullOrEmpty(data.Definition.fuelItemId) && data.Definition.FuelItemDef != null)
        {
            var multi = data.GetCraftableMultiInventory(true);
            if (multi == null) return null;
            var parts = new List<string> { Loc.Fmt("status.fuel", ItemText.Name(data.Definition.fuelItemId), multi.GetTotalCount(data.Definition.fuelItemId)) };
            if (!string.IsNullOrEmpty(data.Definition.fuelItemId2) && data.Definition.FuelItemDef2 != null)
                parts.Add(Loc.Fmt("status.fuel", ItemText.Name(data.Definition.fuelItemId2), multi.GetTotalCount(data.Definition.fuelItemId2)));
            return string.Join(", ", parts);
        }

        return null;
    }

    /// <summary>"15 more queued", "more queued without end", or null when the queue ends here.</summary>
    private static string QueuedAfter(CraftComponent craft, CraftElementBase current)
    {
        var queue = craft.CraftElementsQueue;
        if (queue == null) return null;

        var count = 0;
        foreach (var element in queue)
        {
            if (element == null || ReferenceEquals(element, current)) continue;
            if (element.IsInfinite) return Loc.Get("status.more_endless");
            count += Math.Max(1, element.Count);
        }

        // The running order's own remainder counts too: "16 x bronze" is one element running with
        // 15 still to go.
        if (!current.IsInfinite && current.Count > 1) count += current.Count - 1;
        else if (current.IsInfinite) return Loc.Get("status.more_endless");

        return count > 0 ? Loc.Fmt("status.more", count) : null;
    }

    /// <summary>
    /// Why the player cannot work this yet: the tool is not on the belt, or the mastery is too low.
    /// Null when nothing stands in the way, so a rock that can be broken just says its name.
    /// </summary>
    private static string WorkLock(WgoData data)
    {
        var def = data.Definition;
        if (def.interactionType != WGODef.InteractionType.Work) return null;

        var player = MainGame.PlayerController;
        if (player == null) return null;

        var parts = new List<string>(2);

        var tool = def.toolAction == null ? ItemType.None : def.toolAction.actionableTool;
        if (tool != ItemType.None && tool != ItemType.Hand)
        {
            var belt = player.PlayerData?.toolBeltInventory;
            var item = belt?.GetItemByType(tool);
            if (item == null || item.IsEmpty) parts.Add(Loc.Fmt("status.needs_tool", ToolName(tool)));
        }

        var talent = string.IsNullOrEmpty(def.talent) ? null : GameBalance.Me.GetDataOrNull<TalentDef>(def.talent);

        // A planted seed carries its own lock, and while it does the game never refuses the work
        // (the hit just does less) - so there is nothing to warn about.
        if (talent != null && !def.noMasteryLock && data.GetGameResInt("seed_mastery_lock") <= 0)
        {
            var needed = def.MasteryLock;
            var level = player.GetMasteryLevelForTalentBranch(talent.id);
            if (level < needed)
                parts.Add(Loc.Fmt("status.needs_mastery", needed, CraftReader.TalentName(talent.id), level));
        }

        return parts.Count == 0 ? null : string.Join(", ", parts);
    }

    /// <summary>
    /// For a workbench add-on (the hardening bucket, the bellows): which workbench it belongs to
    /// and whether it is connected to one. Null for anything else.
    ///
    /// <para>
    /// <b>Reported (2026-09-26):</b> a hardening bucket was built, then could not be found in the
    /// list, and standing at it offered nothing to do. That is how add-ons work: they have no
    /// action of their own, and add their recipes to the workbench they stand against - the anvil's
    /// craft window shows a "hardening bucket" group. Placed anywhere else they do nothing, and
    /// the game says so only by drawing a connection line. The link is the game's own
    /// (<c>WgoData.WorkbenchParents</c>, set by <c>TryRegisterWorkbenchExtensionDelayed</c>).
    /// </para>
    /// </summary>
    internal static string Extension(WgoData data)
    {
        if (data == null || string.IsNullOrEmpty(data.id) || !GameBalance.Me.IsWorkbenchExtensionId(data.id)) return null;

        var parents = data.WorkbenchParents;
        if (parents != null && parents.Count > 0)
        {
            var world = MainGame.Instance?.GameSave?.WorldData;
            var names = parents.Select(p => world?.GetWgoData(p)?.id).Where(id => id != null)
                .Select(ObjectNames.Of).Distinct().ToList();
            if (names.Count > 0) return Loc.Fmt("status.extension_linked", string.Join(", ", names));
        }

        return Loc.Fmt("status.extension_unlinked", ParentNames(data.id));
    }

    /// <summary>"wooden anvil or iron anvil" - the workbenches an add-on can belong to.</summary>
    internal static string ParentNames(string extensionId)
    {
        if (!GameBalance.Me.TryGetParentWorkbenchDefsForExtension(extensionId, out var defs) || defs == null) return "?";
        var names = defs.Where(d => d != null).Select(d => ObjectNames.Of(d.id)).Distinct().ToList();
        return names.Count == 0 ? "?" : string.Join(Loc.Get("status.or"), names);
    }

    internal static string ToolName(ItemType tool) =>
        Loc.Find("itemtype." + tool) ?? Navigator.Humanise(tool.ToString());
}
