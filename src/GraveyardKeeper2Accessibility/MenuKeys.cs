namespace GraveyardKeeper2Accessibility;

/// <summary>
/// Arrow keys and Enter in menus.
///
/// Forcing the gamepad-active flag (see <see cref="UiNarrator"/>) switches the game's focus
/// navigation on, but it does not give the keyboard a way to drive it. The game binds
/// <c>GameKey.Up</c>/<c>Down</c> to the movement keys, so W and S already traverse a window - and
/// binds <c>GameKey.Select</c> to nothing on the keyboard at all, because menus were designed to be
/// clicked with the mouse or driven with a pad. Hence: W and S move, arrows do nothing, Enter does
/// nothing.
///
/// <para>
/// <b>Why this does not simply add the missing key bindings.</b> It looks like a two-line fix -
/// append a <c>KeyBinding</c> for <c>GameKey.Up</c> with <c>UpArrow</c> to
/// <c>GameBindings.keyBindings</c> - and it would corrupt the player's saved settings.
/// <c>GameSettings</c> persists that list and restores it by matching on <c>gameKey.value</c>,
/// taking the <i>first</i> match every time (<c>FillKeyBindingsForSave</c>,
/// <c>MigrateSavedBindingsToCurrentDefaults</c>). Two entries for <c>GameKey.Up</c> therefore save
/// as two records with the same key id, and on the next load both are written onto the same first
/// binding - so the player's W silently becomes UpArrow, on disk, permanently. The count
/// comparisons in <c>IsSameBindingsAsCurrentSource</c> would trip the migration path as well.
/// </para>
///
/// <para>
/// So the mod owns these keys itself and leaves the game's binding data untouched. It reads the
/// keys directly and calls the navigation controller the same way
/// <c>LazyWindowInputController</c> does - <c>Navigate(GUIDirection)</c> and
/// <c>SelectFocusedItem()</c>, both public. W and S keep working through the game's own path;
/// nothing is overridden, only added.
/// </para>
/// </summary>
internal static class MenuKeys
{
    private static ManualLogSource _log;
    private static ConfigEntry<bool> _enabled;
    private static ConfigEntry<KeyboardShortcut> _itemMoveKey;
    private static ConfigEntry<KeyboardShortcut> _switchSideKey;

    // Hold-to-repeat, so a long list does not need one keystroke per row. Unscaled, because most
    // of these windows pause the game.
    private const float RepeatDelay = 0.4f;
    private const float RepeatInterval = 0.09f;

    private static KeyCode _heldKey = KeyCode.None;
    private static GUIDirection _heldDirection;
    private static float _nextRepeatAt;

    internal static void Init(ManualLogSource log, ConfigFile config)
    {
        _log = log;
        _enabled = config.Bind(
            "Menus", "ArrowKeysAndEnter", true,
            "Arrow keys move between menu controls and Enter activates the focused one. " +
            "The game's own W/S/A/D navigation keeps working either way. Turn this off if it " +
            "conflicts with something.");
        _itemMoveKey = ModKeys.Bind(config,
            "Menus", "MoveItem", new KeyboardShortcut(KeyCode.Space),
            "Moves the focused item across - out of a chest into your bag, or back. The game's own " +
            "key for this (ItemMove) exists for controllers and the right mouse button only.");
        _switchSideKey = ModKeys.Bind(config,
            "Menus", "SwitchSide", new KeyboardShortcut(KeyCode.F6),
            "In a chest, shop or conveyor chest: jumps between your bag, the chest, and the " +
            "conveyor slots.");
    }

    /// <summary>Called once per frame from the plugin. Cheap and silent when no menu is open.</summary>
    internal static void Update()
    {
        if (_enabled == null || !_enabled.Value) return;

        var enter = Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter);

        // The travel map has no navigation items either: the mod keeps the list of destinations.
        if (TravelMapReader.TryKeys(enter))
        {
            _heldKey = KeyCode.None;
            return;
        }

        // Confirmation boxes come first, and are the reason Enter is handled before anything else
        // is resolved. They register no navigation items, so there is no controller to find, and
        // their confirm button listens for GameKey.Select - which has no keyboard binding at all.
        // Without this, "return to the menu without saving?" simply cannot be answered yes.
        if (enter && (DialogWindowReader.TryConfirmActiveDialog() || NotesReader.TryClose() || TutorialReader.TryConfirm() ||
                      StationsReader.TryConfirm() || TreesReader.TryConfirm() || ItemCountReader.TryConfirm() ||
                      BodyWindowsReader.TryEnter() || QuestPageReader.TryConfirm() || SurveyResultReader.TryConfirm() || MilitaryReader.TryConfirm()))
        {
            _heldKey = KeyCode.None;
            return;
        }

        // The quantity window's slider answers the arrows (and A/D, W/S) itself. Moving focus as
        // well would talk over the amount, and Space would try to move an item behind the window.
        if (ItemCountReader.IsActive)
        {
            _heldKey = KeyCode.None;
            return;
        }

        // The hotbar slot window: arrows pick a slot, Enter or 1-4 pins. And 1-4 on an item in the
        // inventory pins it straight to that slot - see HotBarReader.
        if (HotBarReader.TryKeys(enter) || ItemMenuReader.TryKeys())
        {
            _heldKey = KeyCode.None;
            return;
        }

        // Arrowing between the answers is handled there too, and for the same reason: the box
        // raises no focus events, so the mod tracks which answer the player is on itself.
        if (Input.GetKeyDown(KeyCode.DownArrow) || Input.GetKeyDown(KeyCode.RightArrow))
        {
            if (DialogWindowReader.TryNavigateActiveDialog(1)) { _heldKey = KeyCode.None; return; }
        }
        else if (Input.GetKeyDown(KeyCode.UpArrow) || Input.GetKeyDown(KeyCode.LeftArrow))
        {
            if (DialogWindowReader.TryNavigateActiveDialog(-1)) { _heldKey = KeyCode.None; return; }
        }

        if (Input.GetKeyDown(KeyCode.RightArrow) && TutorialReader.TryPage(1)) { _heldKey = KeyCode.None; return; }
        if (Input.GetKeyDown(KeyCode.LeftArrow) && TutorialReader.TryPage(-1)) { _heldKey = KeyCode.None; return; }

        // Crafting stations own F (start), Space (add to queue) and F6 (recipes / queue) while one
        // is on top - checked before Space and F6 mean "move item" and "switch chest side".
        if (VendorReader.TryKeys(_switchSideKey.Value.IsDown()) ||
            BuildingReader.TryTabKeys() || FolioReader.TryKeys() || BodyWindowsReader.TryTakeBody() || CraftReader.TryKeys() ||
            StationsReader.TryKeys() || TreesReader.TryKeys())
        {
            _heldKey = KeyCode.None;
            return;
        }

        if (_switchSideKey.Value.IsDown() && ChestSections.TryCycle())
        {
            _heldKey = KeyCode.None;
            return;
        }

        if (_itemMoveKey.Value.IsDown() && TryMoveFocusedItem())
        {
            _heldKey = KeyCode.None;
            return;
        }

        var controller = ResolveController();
        if (controller == null)
        {
            _heldKey = KeyCode.None;
            return;
        }

        if (enter)
        {
            controller.SelectFocusedItem();
            return;
        }

        if (TryDirection(controller, KeyCode.UpArrow, GUIDirection.Up)) return;
        if (TryDirection(controller, KeyCode.DownArrow, GUIDirection.Down)) return;
        if (TryDirection(controller, KeyCode.LeftArrow, GUIDirection.Left)) return;
        if (TryDirection(controller, KeyCode.RightArrow, GUIDirection.Right)) return;

        // Nothing pressed this frame - but a key may still be held down from an earlier one.
        if (_heldKey != KeyCode.None)
        {
            if (!Input.GetKey(_heldKey))
            {
                _heldKey = KeyCode.None;
            }
            else if (Time.unscaledTime >= _nextRepeatAt)
            {
                Step(controller, _heldDirection);
                _nextRepeatAt = Time.unscaledTime + RepeatInterval;
            }
        }
    }

    /// <summary>
    /// One arrow step. Some controls give an arrow a meaning of their own in the game - W/S change a
    /// craft amount, A/D the alchemy amount - so those get the first chance; everything else moves
    /// focus.
    /// </summary>
    private static void Step(GamepadNavigationController controller, GUIDirection direction)
    {
        if (CraftReader.TryArrow(direction) || StationsReader.TryArrow(direction) ||
            BodyWindowsReader.TryArrow(controller, direction)) return;
        controller.Navigate(direction);
    }

    private static bool TryDirection(GamepadNavigationController controller, KeyCode key, GUIDirection direction)
    {
        if (!Input.GetKeyDown(key)) return false;

        Step(controller, direction);
        _heldKey = key;
        _heldDirection = direction;
        _nextRepeatAt = Time.unscaledTime + RepeatDelay;
        return true;
    }

    /// <summary>
    /// The navigation controller these keys should drive, or null when no menu is open.
    ///
    /// Normally this is the controller owning whatever currently has focus, which is exactly the
    /// one the player is hearing. The fallback covers a window that opened without focusing
    /// anything: <c>Navigate</c> focuses the first item when nothing is focused yet, so reaching
    /// the controller at all is enough to get such a window moving.
    /// </summary>
    /// <summary>
    /// The item cell's second action, which every two-sided window (chests, conveyor chests, the
    /// vendor, a zombie worker) uses for "move to the other side". <c>UIBaseChestWindow</c> binds
    /// it to <c>GameKey.ItemMove</c>, a controller button with no keyboard binding - on a keyboard
    /// the game expects the right mouse button. So items could be looked at but never taken out.
    /// </summary>
    private static bool TryMoveFocusedItem()
    {
        try
        {
            var focused = UiNarrator.FocusedItem;
            if (focused == null || !focused.TryGetComponent<UIItemCell>(out var cell)) return false;

            var item = cell.DisplayingItem;
            if (item == null || item.IsEmpty)
            {
                ScreenReader.Say(Loc.Get("menu.move_nothing"));
                return true;
            }

            if (!cell.IsInteractable || cell.OnItemCellPress2 == null)
            {
                ScreenReader.Say(Loc.Get("menu.move_not_here"));
                return true;
            }

            var name = ItemText.Name(item.id);
            var count = item.Count;
            cell.OnGamepadPress2();

            // A stack of several can open the "how many?" window instead of moving at once; that
            // window speaks for itself, and "moved" would be untrue and talk over it.
            if (ItemCountReader.IsActive)
            {
                _log?.LogInfo($"[Keys] Moving item '{item.id}' x{count} asks how many.");
                return true;
            }
            _log?.LogInfo($"[Keys] Moved item '{item.id}' x{count}.");

            // In a station's picker slot (alchemy, study table, garden bed) the second action takes
            // the chosen item back out of the slot rather than moving a stack anywhere.
            var slot = CraftReader.IsSlotWindow(LazyWindowsStackController.ActiveWindow);
            ScreenReader.Say(slot ? Loc.Fmt("craft.removed", name)
                                  : Loc.Fmt("menu.moved", count > 1 ? $"{count} {name}" : name));
            return true;
        }
        catch (Exception ex)
        {
            _log?.LogError($"[Keys] Moving an item failed: {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    private static GamepadNavigationController ResolveController()
    {
        try
        {
            var focused = UiNarrator.FocusedItem;
            if (focused != null && focused.Controller != null)
                return focused.Controller;

            var window = LazyWindowsStackController.ActiveWindow;
            if (window == null) return null;

            return window.GetComponentInChildren<GamepadNavigationController>(includeInactive: false);
        }
        catch (Exception ex)
        {
            _log?.LogError($"[Keys] Could not resolve the navigation controller: {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }
}
