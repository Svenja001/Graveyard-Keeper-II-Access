namespace GraveyardKeeper2Accessibility;

/// <summary>
/// The item menu in the inventory (<c>UIContextMenuWindow</c>): use / plant / equip, pin to
/// hotbar, destroy.
///
/// <para>
/// The game opens it with the cell's second action - the right mouse button, or a controller
/// button. On a keyboard the mod's Space reaches that action too, but Space is also the game's
/// Select, which uses or equips the item in the same press. So the menu gets a key of its own,
/// the Windows context-menu keys: Shift+F10 (configurable) and the Applications key. It calls the
/// same <c>OnGamepadPress2</c> and nothing else.
/// </para>
/// <para>
/// The menu's entries are focus items with a text label, so focus narration reads them; this adds
/// "unavailable" to greyed-out ones (Use on an item that cannot be used, Destroy on a quest item),
/// and says what was destroyed - the game removes the whole stack without asking.
/// </para>
/// </summary>
internal static class ItemMenuReader
{
    private static ConfigEntry<KeyboardShortcut> _key;

    private static readonly AccessTools.FieldRef<UIContextMenuWindowWidget, LazyButton> Button =
        AccessTools.FieldRefAccess<UIContextMenuWindowWidget, LazyButton>("button");

    private static readonly AccessTools.FieldRef<LazyWidget<CharMainPageWidgetData>, CharMainPageWidgetData> PageData =
        AccessTools.FieldRefAccess<LazyWidget<CharMainPageWidgetData>, CharMainPageWidgetData>("data");

    internal static void Init(ConfigFile config)
    {
        _key = ModKeys.Bind(config,
            "Menus", "ItemMenu", new KeyboardShortcut(KeyCode.F10, KeyCode.LeftShift),
            "On an item in your inventory: opens its menu (use, pin to hotbar, destroy) without using " +
            "the item. The Applications key does the same.");
    }

    internal static bool SpeaksForItself(LazyWidgetBase window) => window is UIContextMenuWindow;

    /// <summary>Opens the focused inventory item's menu. False when the key was not pressed.</summary>
    internal static bool TryKeys()
    {
        if (_key == null || !(_key.Value.IsDown() || Input.GetKeyDown(KeyCode.Menu))) return false;

        try
        {
            var cell = HotBarReader.FocusedInventoryCell();
            if (cell == null)
            {
                Say(Loc.Get("itemmenu.not_here"));
                return true;
            }
            if (!cell.IsInteractable || cell.OnItemCellPress2 == null)
            {
                Say(Loc.Get("itemmenu.none"));
                return true;
            }

            // With a bag open, the second action moves the item into the bag instead of opening the menu.
            var page = cell.GetComponentInParent<CharMainPageWidget>();
            if (page != null && PageData(page)?.IsBagShown == true)
            {
                Say(Loc.Get("itemmenu.bag_open"));
                return true;
            }

            var name = ItemText.Name(cell.DisplayingItem.id);
            Plugin.Log?.LogInfo($"[ItemMenu] Opening the menu for '{cell.DisplayingItem.id}'.");

            // The first entry gets focus as the menu opens; let it follow this line, not cut it off.
            UiNarrator.QueueFocusUntil = Time.unscaledTime + 0.6f;
            Say(Loc.Fmt("itemmenu.open", name));
            cell.OnGamepadPress2();
        }
        catch (Exception ex)
        {
            Plugin.Log?.LogError($"[ItemMenu] Opening the item menu failed: {ex.GetType().Name}: {ex.Message}");
        }
        return true;
    }

    /// <summary>A menu entry's label, with "unavailable" when it is greyed out. Null for anything else.</summary>
    internal static string DescribeWidget(GamepadNavigationItem item)
    {
        var widget = item.GetComponentInParent<UIContextMenuWindowWidget>();
        if (widget == null) return null;

        var label = TmpText.Clean(widget.GetComponentInChildren<TMPro.TMP_Text>(includeInactive: false)?.text);
        if (string.IsNullOrWhiteSpace(label)) return null;

        var button = Button(widget);
        return button != null && !button.interactable ? Loc.Fmt("itemmenu.unavailable", label) : label;
    }

    [HarmonyPatch(typeof(PlayerInventoryUIItemOpHandler), "TryDestroyItem")]
    [HarmonyPrefix]
    private static void TryDestroyItem_Prefix(UIItemCell cell, out string __state)
    {
        __state = null;
        try
        {
            var item = cell?.DisplayingItem;
            if (item == null || item.IsEmpty || item.Definition == null || item.Definition.CanNotBeDestroyed) return;
            var name = ItemText.Name(item.id);
            __state = item.Count > 1 ? Loc.Fmt("inventory.item_many", name, item.Count) : name;
        }
        catch { /* then nothing is said */ }
    }

    [HarmonyPatch(typeof(PlayerInventoryUIItemOpHandler), "TryDestroyItem")]
    [HarmonyPostfix]
    private static void TryDestroyItem_Postfix(string __state)
    {
        if (__state != null) Say(Loc.Fmt("itemmenu.destroyed", __state));
    }

    private static void Say(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        Plugin.Log?.LogInfo($"[ItemMenu] \"{text}\"");
        ScreenReader.Say(text);
    }
}
