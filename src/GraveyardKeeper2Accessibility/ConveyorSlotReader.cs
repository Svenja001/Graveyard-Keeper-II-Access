using LazyBearTechnology;
using UnityEngine;

namespace GraveyardKeeper2Accessibility;

/// <summary>
/// The four side slots of a factory chest (<c>UIConveyorChestSlot</c>): which item leaves the
/// chest on which side.
///
/// <para>
/// They were read as plain item cells, "empty slot", with nothing to say which side of the chest
/// a slot is, whether a belt runs there, or that Enter sets it (user, 2026-10-24: "it says empty
/// slot but I don't really get how to change it, and don't know if I am on the right spot"). On
/// screen each slot sits on its side of a chest picture, with an in or out arrow and a question
/// mark while nothing is chosen.
/// </para>
///
/// <para>
/// <b>Which side.</b> A slot with a belt knows that belt (<c>ConveyorChestSlotData.ConveyorWgoData</c>),
/// so the side is where the belt stands from the chest, in compass terms (north is +z, as
/// everywhere in the mod). A slot without a belt falls back to its own <c>slotPosDirection</c>.
/// </para>
///
/// <para>
/// <b>Setting one.</b> Enter on an out slot starts the game's choosing mode
/// (<c>SetChestItemSettingMode</c>): the bag is switched off and Enter on an item in the chest
/// makes it the slot's item (<c>SetItemSlot</c>). Both are announced, since focus jumps away
/// from the slot the moment choosing starts.
/// </para>
/// </summary>
internal static class ConveyorSlotReader
{
    internal static bool Ready { get; private set; }

    private static class G
    {
        internal static readonly AccessTools.FieldRef<UIConveyorChestSlot, UIConveyorChestSlot.State> State =
            AccessTools.FieldRefAccess<UIConveyorChestSlot, UIConveyorChestSlot.State>("state");

        internal static readonly MethodInfo StartChoosing =
            AccessTools.Method(typeof(UIConveyorChestWindow), "SetChestItemSettingMode", new[] { typeof(UIConveyorChestSlot) })
            ?? throw new MissingMethodException("UIConveyorChestWindow.SetChestItemSettingMode(slot)");

        internal static readonly MethodInfo Chosen =
            AccessTools.Method(typeof(UIConveyorChestWindow), "SetItemSlot")
            ?? throw new MissingMethodException("UIConveyorChestWindow.SetItemSlot");
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
            Plugin.Log?.LogError($"[Chest] Factory chest sides are not supported by this game version: " +
                                 $"{(ex.InnerException ?? ex).GetType().Name}: {(ex.InnerException ?? ex).Message}");
        }
    }

    /// <summary>"east: out, clay. Enter chooses another item", or null for any other cell.</summary>
    internal static string DescribeCell(UIItemCell cell)
    {
        if (!Ready || cell == null) return null;
        var slot = cell.GetComponentInParent<UIConveyorChestSlot>(includeInactive: true);
        if (slot == null || slot.ItemCell != cell) return null;

        var side = SideName(slot);
        var item = slot.SlotData?.slotItemId;
        var name = string.IsNullOrEmpty(item) ? null : ItemText.Name(item);
        string text;
        switch (G.State(slot))
        {
            case UIConveyorChestSlot.State.NotConnected:
                text = Loc.Get("chest.slot_none");
                break;
            case UIConveyorChestSlot.State.EmptyIn:
                text = Loc.Get("chest.slot_in");
                break;
            case UIConveyorChestSlot.State.EmptyOut:
                text = Loc.Get("chest.slot_out_empty");
                break;
            case UIConveyorChestSlot.State.FilledOut:
                text = Loc.Fmt("chest.slot_out", name ?? item);
                break;
            default:
                text = Loc.Get("chest.slot_choosing");
                break;
        }
        return Loc.Fmt("chest.side", side, text);
    }

    /// <summary>The compass side of the chest a slot is on: from its belt when it has one.</summary>
    private static string SideName(UIConveyorChestSlot slot)
    {
        try
        {
            var belt = slot.SlotData?.ConveyorWgoData;
            var window = LazyWindowsStackController.ActiveWindow as UIConveyorChestWindow;
            var chest = window == null ? null : Traverse.Create(window).Field("data").GetValue<UIBaseChestWindowData>()?.Chest;
            if (belt != null && chest != null)
            {
                var d = belt.Position - chest.Position;
                var side = Mathf.Abs(d.x) >= Mathf.Abs(d.z) ? (d.x >= 0 ? "factory.east" : "factory.west") : (d.z >= 0 ? "factory.north" : "factory.south");
                return Loc.Get(side);
            }
        }
        catch (Exception ex)
        {
            Plugin.Log?.LogWarning($"[Chest] Could not place a chest side: {ex.Message}");
        }

        var dir = slot.SlotData != null ? slot.SlotData.slotPosDirection : slot.ChestPosDirection;
        switch (dir)
        {
            case Direction.Up: return Loc.Get("factory.north");
            case Direction.Right: return Loc.Get("factory.east");
            case Direction.Down: return Loc.Get("factory.south");
            case Direction.Left: return Loc.Get("factory.west");
            default: return Loc.Fmt("chest.slot_number", slot.SlotIndex + 1);
        }
    }

    private static string _choosingSide;

    [HarmonyPatch(typeof(UIConveyorChestWindow), "SetChestItemSettingMode", typeof(UIConveyorChestSlot))]
    [HarmonyPostfix]
    private static void UIConveyorChestWindow_StartChoosing(UIConveyorChestWindow __instance, UIConveyorChestSlot conveyorChestSlot)
    {
        try
        {
            if (!__instance.IsItemSelectionModActive || conveyorChestSlot == null) return;
            _choosingSide = SideName(conveyorChestSlot);
            Plugin.Log?.LogInfo($"[Chest] Choosing the item for the {_choosingSide} side.");
            ScreenReader.Say(Loc.Fmt("chest.slot_choose_now", _choosingSide));
        }
        catch (Exception ex)
        {
            Plugin.Log?.LogError($"[Chest] Choosing announcement failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    [HarmonyPatch(typeof(UIConveyorChestWindow), "SetItemSlot")]
    [HarmonyPostfix]
    private static void UIConveyorChestWindow_Chosen(UIItemCell cell)
    {
        try
        {
            var item = cell?.DisplayingItem;
            if (item == null || string.IsNullOrEmpty(item.id)) return;
            var name = ItemText.Name(item.id) ?? item.id;
            Plugin.Log?.LogInfo($"[Chest] The {_choosingSide} side now sends out '{item.id}'.");
            ScreenReader.Say(Loc.Fmt("chest.slot_chosen", _choosingSide ?? "", name));
        }
        catch (Exception ex)
        {
            Plugin.Log?.LogError($"[Chest] Chosen announcement failed: {ex.GetType().Name}: {ex.Message}");
        }
    }
}
