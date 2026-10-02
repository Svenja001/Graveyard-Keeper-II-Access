namespace GraveyardKeeper2Accessibility;

/// <summary>
/// Putting things on the hotbar (1–4).
///
/// <para>
/// The game's only way in is the inventory context menu, "An Schnellleiste anheften", which opens
/// <c>UIHotBarSelectionWindow</c>: the four slots, waiting for 1–4 or a click. That window was
/// unusable blind - it says nothing, its slots read as bare "Leerer Platz", and the context menu
/// itself sits behind the inventory's second action, which on a keyboard shares Space with the
/// game's Select (that uses the item). So:
/// </para>
/// <list type="bullet">
///   <item>In the inventory, 1–4 on a focused item pins it to that slot directly.</item>
///   <item>The slot window, however it was opened, says what is being pinned and what the slots
///   hold; arrows choose a slot, Enter or 1–4 pins, Escape cancels.</item>
///   <item>Every pin says where the item went and what it replaced.</item>
/// </list>
/// <para>
/// 1–4 are read from the game's own UseHotBarItem bindings, so a player who moved those keys keeps
/// them. Pinning goes through <c>PlayerData.SetHotBarItemAtIndex</c>, the call both of the game's
/// paths make, which also takes the item off any other slot it was on.
/// </para>
/// </summary>
internal static class HotBarReader
{
    internal static bool Ready { get; private set; }

    private static class G
    {
        internal static readonly AccessTools.FieldRef<LazyWidget<UIHotBarSelectionWindowData>, UIHotBarSelectionWindowData> Data =
            AccessTools.FieldRefAccess<LazyWidget<UIHotBarSelectionWindowData>, UIHotBarSelectionWindowData>("data");
    }

    private static readonly GameKey[] SlotKeys =
    {
        GameKey.UseHotBarItem1, GameKey.UseHotBarItem2, GameKey.UseHotBarItem3, GameKey.UseHotBarItem4,
    };

    /// <summary>The slot the arrows are on in the slot window, 0-based.</summary>
    private static int _slot;

    /// <summary>The slot window being read, so a redraw does not announce it a second time.</summary>
    private static UIHotBarSelectionWindow _announced;

    private static bool _pinnedWhileOpen;

    internal static void Init()
    {
        try
        {
            System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(typeof(G).TypeHandle);
            Ready = true;
        }
        catch (Exception ex)
        {
            Plugin.Log?.LogError($"[HotBar] The hotbar slot window is not supported by this game version: " +
                                 $"{(ex.InnerException ?? ex).GetType().Name}: {(ex.InnerException ?? ex).Message}");
        }
    }

    private static UIHotBarSelectionWindow Active =>
        Ready ? LazyWindowsStackController.ActiveWindow as UIHotBarSelectionWindow : null;

    internal static bool SpeaksForItself(LazyWidgetBase window) => Ready && window is UIHotBarSelectionWindow;

    /// <summary>
    /// Cells inside the slot window say nothing: the mod reads that window and moves through its
    /// slots itself, and the game's first focus would otherwise talk over the opening line.
    /// </summary>
    internal static bool SilencesCell(UIItemCell cell) =>
        Ready && cell != null && cell.GetComponentInParent<UIHotBarSelectionWindow>() != null;

    // ---- keys ---------------------------------------------------------------------------------

    /// <summary>
    /// Arrows, Enter and 1–4 in the slot window; 1–4 on an item in the inventory. False when
    /// neither applies, so the key goes on to its usual meaning.
    /// </summary>
    internal static bool TryKeys(bool enter)
    {
        if (!Ready) return false;

        try
        {
            var window = Active;
            if (window != null) return TryWindowKeys(window, enter);

            var digit = PressedSlot();
            if (digit < 0) return false;

            var item = FocusedInventoryItem();
            if (item == null) return false;

            Pin(item, digit);
            return true;
        }
        catch (Exception ex)
        {
            Plugin.Log?.LogError($"[HotBar] Key handling failed: {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    private static bool TryWindowKeys(UIHotBarSelectionWindow window, bool enter)
    {
        var item = G.Data(window)?.Item;

        var digit = PressedSlot();
        if (digit >= 0 || enter)
        {
            if (item == null || item.IsEmpty) return false;
            PinFromWindow(window, item, digit >= 0 ? digit : _slot);
            return true;
        }

        var step = 0;
        if (Input.GetKeyDown(KeyCode.RightArrow) || Input.GetKeyDown(KeyCode.DownArrow)) step = 1;
        else if (Input.GetKeyDown(KeyCode.LeftArrow) || Input.GetKeyDown(KeyCode.UpArrow)) step = -1;
        if (step == 0) return false;

        var count = SlotCount();
        _slot = ((_slot + step) % count + count) % count;
        Say(SlotText(_slot));
        return true;
    }

    /// <summary>The slot whose hotbar key went down this frame, or -1.</summary>
    private static int PressedSlot()
    {
        for (var i = 0; i < SlotKeys.Length; i++)
        {
            var key = GameKeys.Find(SlotKeys[i], out _)?.keyCode ?? KeyCode.Alpha1 + i;
            if (Input.GetKeyDown(key)) return i;
        }
        return -1;
    }

    private static Item FocusedInventoryItem() => FocusedInventoryCell()?.DisplayingItem;

    /// <summary>
    /// The focused cell when it holds an item of the player's own inventory on the character page -
    /// not the tool belt, not a bag's contents, not a recipe or a chest. Null otherwise.
    /// </summary>
    internal static UIItemCell FocusedInventoryCell()
    {
        if (LazyWindowsStackController.ActiveWindow is not CharacterWindow) return null;

        var focused = UiNarrator.FocusedItem;
        if (focused == null) return null;
        var cell = focused.GetComponent<UIItemCell>() ?? focused.GetComponentInParent<UIItemCell>();
        if (cell == null || cell.GetComponentInParent<CharMainPageWidget>() == null) return null;
        if (cell.GetComponentInParent<ToolBeltInventoryWidget>() != null) return null;

        var item = cell.DisplayingItem;
        if (item == null || item.IsEmpty) return null;

        var held = MainGame.PlayerData?.inventory?.GetItemById(item.id);
        return held == null || held.IsEmpty ? null : cell;
    }

    // ---- pinning ------------------------------------------------------------------------------

    private static void Pin(Item item, int slot)
    {
        var def = item.Definition;
        if (def == null || !def.CanBePinnedToHotBar)
        {
            Say(Loc.Fmt("hotbar.cannot_pin", ItemText.Name(item.id)));
            return;
        }

        Plugin.Log?.LogInfo($"[HotBar] Pinning '{item.id}' to slot {slot + 1}.");
        MainGame.PlayerData.SetHotBarItemAtIndex(item.id, slot);
    }

    private static void PinFromWindow(UIHotBarSelectionWindow window, Item item, int slot)
    {
        Plugin.Log?.LogInfo($"[HotBar] Pinning '{item.id}' to slot {slot + 1} from the slot window.");
        MainGame.PlayerData.SetHotBarItemAtIndex(item.id, slot);
        if (LazyWindowsStackController.ActiveWindow == window) window.Close();
    }

    /// <summary>The slot's previous item, so the announcement can say what was replaced.</summary>
    [HarmonyPatch(typeof(PlayerData), nameof(PlayerData.SetHotBarItemAtIndex))]
    [HarmonyPrefix]
    private static void SetHotBarItemAtIndex_Prefix(PlayerData __instance, int index, out string __state)
    {
        __state = null;
        try
        {
            var pinned = __instance.pinnedItems;
            if (pinned != null && index >= 0 && index < pinned.Length) __state = pinned[index];
        }
        catch { /* the announcement just leaves out the replaced item */ }
    }

    [HarmonyPatch(typeof(PlayerData), nameof(PlayerData.SetHotBarItemAtIndex))]
    [HarmonyPostfix]
    private static void SetHotBarItemAtIndex_Postfix(string equippedItem, int index, string __state)
    {
        try
        {
            if (Active != null) _pinnedWhileOpen = true;

            var name = ItemText.Name(equippedItem);
            if (string.IsNullOrEmpty(__state) || __state == equippedItem)
                Say(Loc.Fmt("hotbar.pinned", name, index + 1));
            else
                Say(Loc.Fmt("hotbar.pinned_replacing", name, index + 1, ItemText.Name(__state)));
        }
        catch (Exception ex)
        {
            Plugin.Log?.LogError($"[HotBar] Announcing a pin failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    // ---- the slot window ----------------------------------------------------------------------

    [HarmonyPatch(typeof(UIHotBarSelectionWindow), nameof(UIHotBarSelectionWindow.Redraw))]
    [HarmonyPostfix]
    private static void UIHotBarSelectionWindow_Redraw(UIHotBarSelectionWindow __instance)
    {
        try
        {
            if (ReferenceEquals(_announced, __instance)) return;
            var item = G.Data(__instance)?.Item;
            if (item == null) return;

            _announced = __instance;
            _pinnedWhileOpen = false;

            // Start on the slot the item already has, else the first free one.
            var pinned = MainGame.PlayerData?.pinnedItems ?? Array.Empty<string>();
            _slot = Array.IndexOf(pinned, item.id);
            if (_slot < 0) _slot = Array.FindIndex(pinned, string.IsNullOrEmpty);
            if (_slot < 0) _slot = 0;

            var slots = Enumerable.Range(0, SlotCount()).Select(SlotText);
            Say(string.Join(". ", new[] { Loc.Fmt("hotbar.window", ItemText.Name(item.id)) }
                .Concat(slots)
                .Append(Loc.Fmt("hotbar.window_hint", _slot + 1))));
        }
        catch (Exception ex)
        {
            Plugin.Log?.LogError($"[HotBar] Reading the slot window failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    [HarmonyPatch(typeof(UIHotBarSelectionWindow), nameof(UIHotBarSelectionWindow.Close))]
    [HarmonyPostfix]
    private static void UIHotBarSelectionWindow_Close(UIHotBarSelectionWindow __instance)
    {
        // The game closes this window twice on a pin; only the first close counts.
        if (!ReferenceEquals(_announced, __instance)) return;
        _announced = null;
        if (!_pinnedWhileOpen) Say(Loc.Get("hotbar.cancelled"));
    }

    private static int SlotCount() => Math.Max(1, MainGame.PlayerData?.pinnedItems?.Length ?? 4);

    /// <summary>"Platz 2: Bier, 3" - the same wording as the Y key.</summary>
    private static string SlotText(int i)
    {
        var pinned = MainGame.PlayerData?.pinnedItems;
        var id = pinned != null && i < pinned.Length ? pinned[i] : null;
        if (string.IsNullOrEmpty(id)) return Loc.Fmt("status.slot_empty", i + 1);

        var held = ItemText.Held(id);
        return held < 0
            ? Loc.Fmt("status.slot", i + 1, ItemText.Name(id))
            : Loc.Fmt("status.slot_count", i + 1, ItemText.Name(id), held);
    }

    /// <summary>"Schnellleiste 2" for an inventory item that is pinned, else null.</summary>
    internal static string PinnedNote(string itemId)
    {
        if (string.IsNullOrEmpty(itemId)) return null;
        try
        {
            var pinned = MainGame.PlayerData?.pinnedItems;
            var i = pinned == null ? -1 : Array.IndexOf(pinned, itemId);
            return i < 0 ? null : Loc.Fmt("hotbar.on_slot", i + 1);
        }
        catch
        {
            return null;
        }
    }

    private static void Say(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        Plugin.Log?.LogInfo($"[HotBar] \"{text}\"");
        ScreenReader.Say(text);
    }
}
