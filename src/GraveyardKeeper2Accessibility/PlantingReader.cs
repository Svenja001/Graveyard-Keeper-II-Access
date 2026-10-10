namespace GraveyardKeeper2Accessibility;

/// <summary>
/// Planting with a seed in hand.
///
/// <para>
/// The game's quick way to plant many beds: "Pflanzen" on a seed in the item menu (or the seed's
/// hotbar key) puts it in the player's hand (<c>PlayerData.SetInteractingItem</c>, the
/// <c>PlantingPlayerState</c>). From then on E on an empty bed plants that seed straight away -
/// <c>GardenInteractionHandler.Interact</c> skips the bed window - and the seed stays in hand
/// until it runs out or F, Escape or Tab puts it away. Fertilizer works the same way. All of it
/// was silent: the seed is a picture over the player's head, and a bed that refuses only shows a
/// thought bubble or nothing at all.
/// </para>
/// </summary>
internal static class PlantingReader
{
    private static readonly AccessTools.FieldRef<WGOInteractionHandlerBase, Wgo> AssignedWgo =
        AccessTools.FieldRefAccess<WGOInteractionHandlerBase, Wgo>("assignedWgo");

    /// <summary>The item last announced as held, so the game's re-sets after each planting stay quiet.</summary>
    private static string _held;

    /// <summary>The "in hand" line, waiting for the inventory to finish closing.</summary>
    private static string _pending;

    [HarmonyPatch(typeof(PlayerInventoryUIItemOpHandler), "TrySetInteractingItem")]
    [HarmonyPostfix]
    private static void TrySetInteractingItem_Postfix()
    {
        if (_pending == null) return;
        var text = _pending;
        _pending = null;
        Say(text);
    }

    /// <summary>True while SetInteractingItem clears the old item on its way to the new one.</summary>
    private static bool _swapping;

    [HarmonyPatch(typeof(PlayerData), nameof(PlayerData.SetInteractingItem))]
    [HarmonyPrefix]
    private static void SetInteractingItem_Prefix() => _swapping = true;

    [HarmonyPatch(typeof(PlayerData), nameof(PlayerData.SetInteractingItem))]
    [HarmonyPostfix]
    private static void SetInteractingItem_Postfix(PlayerData __instance)
    {
        _swapping = false;
        try
        {
            var item = __instance.interactingItem;
            if (item == null || item.IsEmpty || item.id == _held) return;
            _held = item.id;

            var text = Loc.Fmt(item.IsFertilizer ? "planting.holding_fertilizer" : "planting.holding",
                               ItemText.Name(item.id), Math.Max(0, ItemText.Held(item.id)));

            // From the item menu the inventory closes right after this, and "window closed" would
            // cut the line off - it is said after the close instead, see TrySetInteractingItem.
            if (LazyWindowsStackController.ActiveWindow is CharacterWindow) _pending = text;
            else Say(text);
        }
        catch (Exception ex)
        {
            Plugin.Log?.LogError($"[Planting] Announcing the held seed failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    [HarmonyPatch(typeof(PlayerData), nameof(PlayerData.RemoveInteractingItem))]
    [HarmonyPrefix]
    private static void RemoveInteractingItem_Prefix(PlayerData __instance, out string __state) =>
        __state = __instance.interactingItem?.id;

    [HarmonyPatch(typeof(PlayerData), nameof(PlayerData.RemoveInteractingItem))]
    [HarmonyPostfix]
    private static void RemoveInteractingItem_Postfix(string __state)
    {
        if (_swapping || string.IsNullOrEmpty(__state)) return;
        _held = null;
        try
        {
            Say(ItemText.Held(__state) == 0
                ? Loc.Fmt("planting.used_up", ItemText.Name(__state))
                : Loc.Fmt("planting.put_away", ItemText.Name(__state)));
        }
        catch (Exception ex)
        {
            Plugin.Log?.LogError($"[Planting] Announcing the put-away seed failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    // ---- E on a bed with the seed in hand ------------------------------------------------------

    private struct Before
    {
        internal string ItemId;
        internal bool Fertilizer;
        internal bool Applies;
    }

    [HarmonyPatch(typeof(GardenInteractionHandler), nameof(GardenInteractionHandler.Interact))]
    [HarmonyPrefix]
    private static void Interact_Prefix(GardenInteractionHandler __instance, out Before __state)
    {
        __state = default;
        try
        {
            var item = MainGame.PlayerData?.interactingItem;
            var craft = AssignedWgo(__instance)?.Data?.CraftComponent;
            if (item == null || craft == null || craft.IsStarted || !(item.IsSeed || item.IsFertilizer)) return;
            __state = new Before { ItemId = item.id, Fertilizer = item.IsFertilizer, Applies = true };
        }
        catch { /* then nothing is said */ }
    }

    [HarmonyPatch(typeof(GardenInteractionHandler), nameof(GardenInteractionHandler.Interact))]
    [HarmonyPostfix]
    private static void Interact_Postfix(GardenInteractionHandler __instance, PlayerController interactor, bool __result, Before __state)
    {
        if (!__state.Applies) return;
        try
        {
            var name = ItemText.Name(__state.ItemId);
            var left = Math.Max(0, ItemText.Held(__state.ItemId));
            if (__result)
            {
                var line = Loc.Fmt(__state.Fertilizer ? "planting.fertilized" : "planting.planted", name, left);
                // Planting: say whether the bed was fertilized first (user, 2026-10-10).
                if (!__state.Fertilizer)
                {
                    var fertilizer = CraftReader.FertilizerLine(AssignedWgo(__instance)?.Data, sayNone: true);
                    if (fertilizer != null) line = $"{line}. {fertilizer}";
                }
                Say(line);
                return;
            }
            Say(WhyNot(__instance, interactor, __state.ItemId, name));
        }
        catch (Exception ex)
        {
            Plugin.Log?.LogError($"[Planting] Announcing a planting failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>The same checks Interact makes, in its order, to say which one refused.</summary>
    private static string WhyNot(GardenInteractionHandler handler, PlayerController interactor, string itemId, string name)
    {
        var data = AssignedWgo(handler).Data;
        var item = MainGame.PlayerData.interactingItem ?? new Item(itemId);
        if (GardenInteractionHandler.TryFindGardenCraft(item, data, logWarning: false) == null)
            return Loc.Fmt("planting.wrong_bed", name);
        if (GardenInteractionHandler.HasAssignedGardenOrder(data))
            return Loc.Get("planting.bed_ordered");
        if (interactor != null && interactor.GetMasteryLevelForTalentBranch("talent_green") <= 0)
            return Loc.Fmt("planting.no_mastery", CraftReader.TalentName("talent_green"));
        return Loc.Fmt("planting.cannot", name);
    }

    private static void Say(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        Plugin.Log?.LogInfo($"[Planting] \"{text}\"");
        ScreenReader.Say(text);
    }
}
