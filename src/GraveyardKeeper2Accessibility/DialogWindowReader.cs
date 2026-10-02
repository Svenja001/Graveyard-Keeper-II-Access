namespace GraveyardKeeper2Accessibility;

/// <summary>
/// The confirmation boxes - "return to the menu without saving?", "are you sure?", "you need 3
/// planks".
///
/// <para>
/// These were completely silent, and the focus log said why: <c>UIDialogWindow</c> opens and closes
/// without raising a single focus event, because it registers no <c>GamepadNavigationItem</c>s at
/// all. It is not navigated - it is answered by pressing a key. That is the plan's open question 3
/// ("how many windows register no navigation items") getting its first concrete answer, and the
/// shape of the fallback those windows need: read the window from its <i>data</i>, not from its
/// focus.
/// </para>
///
/// <para>
/// <b>Reading it was only half the problem.</b> <c>UIDialogWindowButton.Update</c> fires its button
/// on <c>LazyInput.GetKeyDown(keyToReplace)</c>, which is <c>GameKey.Back</c> for cancel and
/// <c>GameKey.Select</c> for confirm. <c>Back</c> is bound to Escape and works. <c>Select</c> has no
/// keyboard binding whatsoever - so confirming was impossible from the keyboard, mod or no mod, and
/// a player could read the question perfectly and still be unable to answer yes.
/// <see cref="MenuKeys"/> routes Enter here for that reason.
/// </para>
/// </summary>
internal static class DialogWindowReader
{
    /// <summary>
    /// Reads the whole box at once: what it is asking, and what the answers are. There is nothing
    /// to arrow through, so unlike a normal window this has to be delivered as a single statement
    /// rather than discovered control by control.
    /// </summary>
    [HarmonyPatch(typeof(UIDialogWindow), nameof(UIDialogWindow.Open))]
    [HarmonyPostfix]
    private static void UIDialogWindow_Open(UIDialogWindowData data)
    {
        try
        {
            if (data == null) return;

            // A new box starts on its first answer, so Enter without arrowing is predictable.
            _selectedIndex = 0;
            _hasNavigated = false;

            var parts = new List<string>();
            Add(parts, data.Header);
            Add(parts, data.Information);
            Add(parts, data.InformationBot);

            // "Needs 5 planks, you have 3" - the counter is the entire reason some of these boxes
            // exist, and on screen it is a number in a colour that means enough or not enough.
            if (data.DrawItemCounter)
            {
                var itemName = string.IsNullOrEmpty(data.ItemName) ? null : TmpText.Clean(LLBase.L(data.ItemName));
                if (!string.IsNullOrWhiteSpace(itemName))
                    parts.Add(Loc.Fmt("dialog.item_count", data.HasItemCount, data.NeedItemCount, itemName));
            }

            var options = Options(data);
            if (!string.IsNullOrWhiteSpace(options))
                parts.Add(options);

            if (parts.Count == 0) return;

            var line = string.Join(". ", parts);

            // Logged with the button count because "the question was read but not the options" has
            // two very different causes - no buttons in the data, or the line being cut off after
            // it was spoken - and only the log tells them apart.
            Plugin.Log?.LogInfo(
                $"[Dialog] {(data.ButtonsData == null ? 0 : data.ButtonsData.Count)} button(s) => \"{line}\"");

            ScreenReader.Say(line);
        }
        catch (Exception ex)
        {
            Plugin.Log?.LogError($"[Dialog] Failed to read the dialog window: {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>
    /// The answers, each with the key that gives it. Naming the key matters more here than
    /// anywhere else in the mod: these boxes are the ones that throw away unsaved progress, and
    /// "yes, no" without saying which key is which invites exactly the wrong guess.
    /// </summary>
    private static string Options(UIDialogWindowData data)
    {
        if (data.ButtonsData == null || data.ButtonsData.Count == 0) return null;

        var parts = new List<string>();

        foreach (var button in data.ButtonsData)
        {
            if (button == null) continue;

            var text = string.IsNullOrEmpty(button.text) ? null : TmpText.Clean(LLBase.L(button.text));
            if (string.IsNullOrWhiteSpace(text)) continue;

            var key = KeyNameFor(button.keyToReplace);
            parts.Add(key == null ? text : Loc.Fmt("dialog.option_with_key", text, key));
        }

        return parts.Count == 0 ? null : Loc.Fmt("dialog.options", string.Join(", ", parts));
    }

    /// <summary>
    /// The keyboard key that answers with this button, in words. Deliberately the *keyboard* key
    /// and not the glyph on screen: forcing gamepad mode means the box is drawing controller
    /// buttons, which is the one thing a keyboard player must not be told to press.
    /// </summary>
    private static string KeyNameFor(GameKey key)
    {
        if (key == null) return null;
        if (key.value == GameKey.Select.value) return Loc.Get("key.enter");
        if (key.value == GameKey.Back.value) return Loc.Get("key.escape");
        return null;
    }

    private static void Add(List<string> parts, string raw)
    {
        if (string.IsNullOrEmpty(raw)) return;

        var clean = TmpText.Clean(LLBase.L(raw));
        if (string.IsNullOrWhiteSpace(clean) || parts.Contains(clean)) return;

        parts.Add(clean);
    }

    /// <summary>
    /// Which answer the player is currently on.
    ///
    /// <para>
    /// The mod has to track this itself. <c>UIDialogWindow</c> raises no focus events - the log
    /// across a whole open-and-close cycle contains none - so arrowing between Ja and Nein was
    /// completely silent even though the window had been read out on opening. This is the
    /// "mod-owned reading cursor" the plan expected to need for windows the game's focus model
    /// does not cover; it is the first one.
    /// </para>
    /// </summary>
    private static int _selectedIndex;

    /// <summary>
    /// Whether the player has moved between the answers yet on this box. The first arrow press
    /// says where the cursor already is rather than moving it: the opening readout named the
    /// answers but not which one Enter would press, so the first thing the player needs is that,
    /// not the next one along.
    /// </summary>
    private static bool _hasNavigated;

    private static readonly AccessTools.FieldRef<UIDialogWindowButton, TMPro.TextMeshProUGUI> ButtonLabel =
        AccessTools.FieldRefAccess<UIDialogWindowButton, TMPro.TextMeshProUGUI>("label");

    /// <summary>The answers on the box currently on top, in the order they are laid out.</summary>
    private static List<UIDialogWindowButton> ActiveButtons()
    {
        var window = LazyWindowsStackController.ActiveWindow as UIDialogWindow;
        if (window == null) return null;

        var buttons = new List<UIDialogWindowButton>();
        foreach (var button in window.GetComponentsInChildren<UIDialogWindowButton>(includeInactive: false))
        {
            if (button == null) continue;

            var lazyButton = button.LazyButton;
            if (lazyButton == null || !lazyButton.interactable) continue;

            buttons.Add(button);
        }

        return buttons;
    }

    /// <summary>
    /// Moves between the answers and says the one landed on. Returns false when the window on top
    /// is not one of these boxes, so the caller falls through to ordinary menu navigation.
    /// </summary>
    internal static bool TryNavigateActiveDialog(int delta)
    {
        var buttons = ActiveButtons();
        if (buttons == null) return false;
        if (buttons.Count == 0) return true;

        if (_hasNavigated)
            _selectedIndex = ((_selectedIndex + delta) % buttons.Count + buttons.Count) % buttons.Count;
        else
            _hasNavigated = true;

        _selectedIndex = Mathf.Clamp(_selectedIndex, 0, buttons.Count - 1);
        ScreenReader.Say(ButtonText(buttons[_selectedIndex]) ?? string.Empty);
        return true;
    }

    /// <summary>
    /// Presses the answer the player is on. Returns false when the window on top is not one of
    /// these boxes.
    /// </summary>
    internal static bool TryConfirmActiveDialog()
    {
        var buttons = ActiveButtons();
        if (buttons == null) return false;

        if (buttons.Count > 0)
        {
            var index = Mathf.Clamp(_selectedIndex, 0, buttons.Count - 1);
            buttons[index].LazyButton.onClick?.Invoke();
            return true;
        }

        // A box with no pressable answer is dismissed some other way - usually Escape alone. Still
        // returns true: this *is* a dialog window, and falling through to navigate a window with no
        // navigation items would do nothing useful anyway.
        return true;
    }

    /// <summary>One answer, with the keyboard key that gives it.</summary>
    private static string ButtonText(UIDialogWindowButton button)
    {
        var label = ButtonLabel(button);
        var text = label == null ? null : TmpText.Clean(label.text);
        if (string.IsNullOrWhiteSpace(text)) return null;

        var key = KeyNameFor(button.KeyToReplace);
        return key == null ? text : Loc.Fmt("dialog.option_with_key", text, key);
    }
}
