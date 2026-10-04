using TMPro;
using UnityEngine.UI;

namespace GraveyardKeeper2Accessibility;

/// <summary>
/// Menu accessibility, in about two hundred lines, because Graveyard Keeper II already has the
/// hard part built.
///
/// The GK1 mod had to invent a UI focus model from nothing: enumerate a window's elements, track
/// an index, synthesise hover and click. It was the single largest piece of that codebase. GK2
/// ships its own model - <see cref="GamepadNavigationController"/> moving focus between
/// <see cref="GamepadNavigationItem"/>s - so this file does not move focus at all. It listens.
///
/// The one obstacle is that the whole path is gated on <see cref="LazyInput.IsGamepadActive"/>:
/// <c>LazyWindowInputController.Enable()</c> only switches the navigation controller on when a
/// gamepad is present, so on a keyboard it stays dark and the arrow keys do nothing.
/// <see cref="LazyInput.ForceGamepadActivityState"/> sets a flag read *only* by the public
/// <c>IsGamepadActive</c> property, while the method that turns keystrokes into
/// <c>GameKey</c>s keeps reading the private field that still tracks real hardware. So one call
/// at startup makes every window believe a gamepad is attached - enabling focus navigation
/// everywhere - while the keyboard goes on producing input normally.
///
/// Known cosmetic consequence: on-screen prompts switch to controller glyphs. Harmless for a
/// blind player, odd for a sighted one, and the reason tips must eventually be read from the
/// keyboard binding rather than from the glyph on screen.
/// </summary>
internal static class UiNarrator
{
    private static ManualLogSource _log;
    private static bool _subscribed;

    /// <summary>
    /// The last thing focus landed on. Kept so a repeat key can say it again without the player
    /// having to arrow off the control and back to hear it.
    /// </summary>
    private static string _lastFocusLabel;

    /// <summary>What focus narration last said, for readers that need to say it again after their own line.</summary>
    internal static string LastFocusLabel => _lastFocusLabel;

    /// <summary>
    /// The control that currently has focus, or null. <see cref="MenuKeys"/> reads this to find
    /// the navigation controller the player is actually listening to, rather than guessing at one
    /// from the window.
    /// </summary>
    internal static GamepadNavigationItem FocusedItem { get; private set; }

    /// <summary>The chest-window side focus was last on, so it is named only on crossing.</summary>
    private static string _lastSection;

    /// <summary>
    /// Until this time (unscaled), a focus change is queued behind whatever is being said instead of
    /// interrupting it. Readers that speak a whole window set it, because the window's first focus
    /// often lands a moment after their summary - and would otherwise cut it off.
    /// </summary>
    internal static float QueueFocusUntil;

    internal static void Init(ManualLogSource log)
    {
        _log = log;

        // Before any window opens. LazyWindowInputController samples the flag in Enable(), so a
        // window that opened first would stay unnavigable for its whole lifetime.
        try
        {
            LazyInput.ForceGamepadActivityState(true);
            _log.LogInfo("[UI] Forced gamepad activity state on - window focus navigation should now be live on the keyboard.");
        }
        catch (Exception ex)
        {
            _log.LogError($"[UI] Could not force gamepad activity state: {ex.GetType().Name}: {ex.Message}. " +
                          "Arrow-key navigation in menus will not work.");
        }

        if (_subscribed) return;
        try
        {
            GamepadNavigationItem.OnFocusStatic += OnItemFocused;
            LazyWindowsStackController.OnWindowOpened += OnWindowOpened;
            LazyWindowsStackController.OnWindowClosed += OnWindowClosed;
            LazyWindowsStackController.OnAllWindowsClosed += OnAllWindowsClosed;
            _subscribed = true;
            _log.LogInfo("[UI] Subscribed to focus and window-stack events.");
        }
        catch (Exception ex)
        {
            _log.LogError($"[UI] Could not subscribe to UI events: {ex.GetType().Name}: {ex.Message}");
        }
    }

    internal static void Shutdown()
    {
        if (!_subscribed) return;
        try
        {
            GamepadNavigationItem.OnFocusStatic -= OnItemFocused;
            LazyWindowsStackController.OnWindowOpened -= OnWindowOpened;
            LazyWindowsStackController.OnWindowClosed -= OnWindowClosed;
            LazyWindowsStackController.OnAllWindowsClosed -= OnAllWindowsClosed;
        }
        catch { /* the game may already be tearing down */ }
        _subscribed = false;
    }

    /// <summary>Says the focused control again, for a player who missed it or lost their place.</summary>
    internal static void RepeatFocus()
    {
        if (string.IsNullOrEmpty(_lastFocusLabel))
        {
            // A window with nothing to focus: read what it shows instead of "nothing selected".
            var text = WindowText(LazyWindowsStackController.ActiveWindow);
            ScreenReader.Say(text ?? Loc.Get("ui.nothing_focused"));
            return;
        }

        // Deliberately not SayMenu: repeating is the entire point of this call, and SayMenu would
        // swallow it as a duplicate of the line the player just failed to catch.
        ScreenReader.Say(_lastFocusLabel);
    }

    // These run inside the game's own event dispatch. An exception escaping here would propagate
    // into the UI code that raised it, so every handler swallows and logs instead.

    private static void OnItemFocused(GamepadNavigationItem item)
    {
        try
        {
            if (item == null) return;
            _silentWindow = null;
            FocusedItem = item;
            var label = DescribeItem(item);

            // Name the side of a chest window whenever focus crosses into it - see ChestSections.
            var section = ChestSections.SectionName(item) ?? VendorReader.SectionName(item);
            if (section != null && section != _lastSection && !string.IsNullOrEmpty(label))
                label = Loc.Fmt("chest.side", section, label);
            _lastSection = section;

            // Logged as well as spoken, and logged even when there is nothing to say. Whether
            // arrow keys actually traverse a window is a thing only a person at the keyboard can
            // confirm, and "I heard nothing" has several very different causes - focus never
            // moved, focus moved but the control has no readable text, or it was read and the
            // dedup swallowed it. This line tells them apart afterwards from the log alone.
            _log?.LogInfo($"[UI] Focus -> '{item.name}' (group {item.group}) => \"{label}\"");

            if (string.IsNullOrEmpty(label)) return;

            _lastFocusLabel = label;

            // A dialogue's answer list places focus on its first option by itself. Speaking that
            // would interrupt the question and the option list queued a moment earlier - see
            // DialogueReader.SuppressFocusUntil. The label is still remembered above, so the
            // repeat key can read it and arrowing away speaks normally.
            if (Time.unscaledTime < DialogueReader.SuppressFocusUntil) return;

            ScreenReader.SayMenu(label, interrupt: Time.unscaledTime >= QueueFocusUntil);
        }
        catch (Exception ex)
        {
            _log?.LogError($"[UI] Focus handler failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static void OnWindowOpened(LazyWidgetBase window)
    {
        try
        {
            // A control means something different in a new window, so the same name must be
            // allowed to be spoken again - see ScreenReader.ClearMenuContext.
            ScreenReader.ClearMenuContext();
            _lastFocusLabel = null;
            FocusedItem = null;
            BodyWindowsReader.Reset();
            CraftReader.Reset();
            StationsReader.Reset();
            if (window is CharacterWindow) TreesReader.Reset();

            var name = WindowName(window);
            _log?.LogInfo($"[UI] Window opened: {window?.GetType().Name} => \"{name}\"");

            // Some windows are read in full by a dedicated reader, and this event does not fire in
            // a fixed order relative to the window's own Open - so announcing the window name here
            // can interrupt that reading part-way through. That is what swallowed the yes/no
            // options on the confirmation boxes: the question was spoken, then "dialog window
            // opened" cut off everything after it. The character window names its page instead.
            if (SpeaksForItself(window) || TreesReader.AnnouncesOpening(window) || FolioReader.AnnouncesOpening(window)) return;

            if (!string.IsNullOrEmpty(name))
                ScreenReader.Say(Loc.Fmt("ui.window_opened", name));

            _silentWindow = window;
            _silentSince = Time.unscaledTime;
        }
        catch (Exception ex)
        {
            _log?.LogError($"[UI] Window-opened handler failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static void OnWindowClosed(LazyWidgetBase window)
    {
        try
        {
            ScreenReader.ClearMenuContext();
            _lastFocusLabel = null;
            FocusedItem = null;

            if (ReferenceEquals(window, _silentWindow)) _silentWindow = null;

            var name = WindowName(window);
            _log?.LogInfo($"[UI] Window closed: {window?.GetType().Name} => \"{name}\"");

            if (SpeaksForItself(window)) return;

            if (!string.IsNullOrEmpty(name))
                ScreenReader.Say(Loc.Fmt("ui.window_closed", name));
        }
        catch (Exception ex)
        {
            _log?.LogError($"[UI] Window-closed handler failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    // ---- windows nothing reads ---------------------------------------------------------------

    /// <summary>
    /// A window that opened with no reader of its own, waiting to see whether focus lands in it.
    ///
    /// <para>
    /// <b>Reported (2026-09-26): the research result opened, said nothing, and the player was
    /// stuck</b> until someone sighted told them Space closes it. It has no navigation items, so
    /// focus narration never fired. That window now has its own reader, but the next one like it
    /// would strand the player the same way - so any window that no reader claims and that gets no
    /// focus within a moment is read out from its visible text, with a hint about which keys
    /// usually close it. F8 reads it again.
    /// </para>
    /// </summary>
    private static LazyWidgetBase _silentWindow;
    private static float _silentSince;
    private const float SilentDelay = 0.6f;

    internal static void Update()
    {
        try
        {
            if (_silentWindow == null || Time.unscaledTime - _silentSince < SilentDelay) return;
            var window = _silentWindow;
            _silentWindow = null;

            if (!ReferenceEquals(LazyWindowsStackController.ActiveWindow, window) || FocusedItem != null) return;
            var text = WindowText(window);
            if (text == null) return;

            _log?.LogInfo($"[UI] No focus in {window.GetType().Name}; read its text: \"{text}\"");
            ScreenReader.Say($"{text}. {Loc.Get("ui.silent_window_keys")}", interrupt: false);
        }
        catch (Exception ex)
        {
            _log?.LogError($"[UI] Reading a window without focus failed: {ex.GetType().Name}: {ex.Message}");
            _silentWindow = null;
        }
    }

    /// <summary>Every visible text in a window, top to bottom as laid out, without repeats; null when there is none.</summary>
    private static string WindowText(LazyWidgetBase window)
    {
        if (window == null) return null;
        try
        {
            var seen = new HashSet<string>();
            var parts = new List<string>();
            foreach (var label in window.GetComponentsInChildren<TMP_Text>(false))
            {
                if (label == null || !label.isActiveAndEnabled) continue;
                var text = TmpText.Clean(label.text);
                if (string.IsNullOrWhiteSpace(text) || !seen.Add(text)) continue;
                parts.Add(text.Trim().TrimEnd('.'));
            }
            return parts.Count == 0 ? null : string.Join(". ", parts);
        }
        catch
        {
            return null;
        }
    }

    private static void OnAllWindowsClosed()
    {
        try
        {
            ScreenReader.ClearMenuContext();
            _lastFocusLabel = null;
            FocusedItem = null;
        }
        catch (Exception ex)
        {
            _log?.LogError($"[UI] All-windows-closed handler failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>
    /// A spoken name for a window. Windows carry no title the mod can read, so this is keyed on
    /// the class name - which is stable, unlike anything on screen, and is exactly the
    /// "never match on a translated label" rule the GK1 mod settled on.
    ///
    /// An unnamed window still gets announced, under a tidied version of its class name. That is
    /// deliberately visible rather than silent: hearing "save slots window" is how the next window
    /// worth naming properly gets found.
    /// </summary>
    /// <summary>
    /// True for windows another reader speaks in full, so this one keeps quiet about them opening
    /// and closing. A confirmation box says what it is by asking its question; prefixing that with
    /// "dialog window opened" adds nothing and risks talking over the answer options.
    /// </summary>
    private static bool SpeaksForItself(LazyWidgetBase window) =>
        window is UIDialogWindow || window is UIAutopsyWindow || window is UIGraveWindow ||
        window is UIEmbalmWindow || CraftReader.IsStationWindow(window) || StationsReader.IsStationWindow(window) ||
        TreesReader.SpeaksForItself(window) || ItemCountReader.SpeaksForItself(window) ||
        QuestPageReader.SpeaksForItself(window) || VendorReader.SpeaksForItself(window) ||
        BindingsReader.SpeaksForItself(window) || SurveyResultReader.SpeaksForItself(window) ||
        MilitaryReader.SpeaksForItself(window) || HotBarReader.SpeaksForItself(window) ||
        ItemMenuReader.SpeaksForItself(window) || TravelMapReader.SpeaksForItself(window);

    private static string WindowName(LazyWidgetBase window)
    {
        if (window == null) return null;
        var type = window.GetType().Name;
        return Loc.Find("window." + type) ?? Prettify(type);
    }

    /// <summary>
    /// What to say about a focused control.
    ///
    /// <see cref="GamepadNavigationItem"/> holds no text of its own - it is a navigation marker
    /// attached beside the visuals - so the label is gathered from the text components under it.
    /// Both TextMeshPro and legacy uGUI text are collected, because a handful of the game's
    /// widgets still use the old component.
    ///
    /// Only active components are read: a widget commonly keeps several labels in its hierarchy
    /// and enables the one that applies (locked vs unlocked, price vs "sold out"), so the inactive
    /// ones are not merely redundant, they are wrong.
    /// </summary>
    private static string DescribeItem(GamepadNavigationItem item)
    {
        var parts = new List<string>();

        try
        {
            // Station cells first: recipe and queue cells are item cells drawn without an item, which
            // the generic item-cell reading would call empty - see CraftReader.
            var ordered = DescribeSwitch(item) ?? DialogueReader.DescribeWidget(item) ?? OrdersReader.DescribeWidget(item) ?? CraftReader.DescribeWidget(item) ?? StationsReader.DescribeWidget(item) ??
                          TreesReader.DescribeWidget(item) ?? QuestPageReader.DescribeWidget(item) ?? FolioReader.DescribeWidget(item) ?? DescribeItemCell(item) ??
                          BuildingReader.DescribeWidget(item) ?? MilitaryReader.DescribeWidget(item) ?? ItemMenuReader.DescribeWidget(item);
            if (ordered != null) return VendorReader.WithPrice(item, ordered);

            foreach (var tmp in item.GetComponentsInChildren<TMP_Text>(includeInactive: false))
                AddPart(parts, tmp == null ? null : tmp.text);

            foreach (var legacy in item.GetComponentsInChildren<Text>(includeInactive: false))
                AddPart(parts, legacy == null ? null : legacy.text);
        }
        catch (Exception ex)
        {
            _log?.LogWarning($"[UI] Could not read labels from '{item.name}': {ex.Message}");
        }

        // No text at all: an icon-only button, a colour swatch, a map pin. The object name is a
        // poor label, but it is a label, and silence here reads to the player as a broken mod
        // rather than as an unnamed control.
        if (parts.Count == 0)
            return Prettify(item.name);

        var label = string.Join(", ", parts);
        return label.Length > 300 ? label.Substring(0, 300) : label;
    }

    private static readonly AccessTools.FieldRef<UISwitchButton, TMP_Text> SwitchHeader =
        AccessTools.FieldRefAccess<UISwitchButton, TMP_Text>("headerLabel");

    private static readonly AccessTools.FieldRef<UISwitchButton, TMP_Text> SwitchAmount =
        AccessTools.FieldRefAccess<UISwitchButton, TMP_Text>("amountLabel");

    /// <summary>
    /// An option switch (language, graphics tier, v-sync, …), named before its value, or null when
    /// the focused control is not one.
    ///
    /// Worth the special case because reading the hierarchy in order gets these exactly backwards.
    /// <c>UISwitchButton</c> lays its value out before its heading, so the generic pass says
    /// "fullscreen, screen mode" - the answer before the question, which is precisely the order a
    /// listener cannot use. Sighted players read the row left to right and see both at once; a
    /// listener needs to know what is being set before hearing what it is set to.
    /// </summary>
    private static string DescribeSwitch(GamepadNavigationItem item)
    {
        var button = item.GetComponentInParent<UISwitchButton>() ?? item.GetComponentInChildren<UISwitchButton>();
        if (button == null) return null;

        var parts = new List<string>();
        var header = SwitchHeader(button);
        var amount = SwitchAmount(button);

        AddPart(parts, header == null ? null : header.text);
        AddPart(parts, amount == null ? null : amount.text);

        // A switch with neither label is not a switch worth special-casing; fall back to the
        // generic pass rather than returning something empty.
        return parts.Count == 0 ? null : string.Join(", ", parts);
    }

    /// <summary>
    /// An inventory slot, named from the item in it, or null when the focused control is not one.
    ///
    /// Necessary because an item cell is almost entirely pictorial: the icon carries the identity
    /// and the only text in the whole control is a small stack count. Reading the labels under it
    /// therefore yields "7", or nothing at all for a single item, and an empty slot is
    /// indistinguishable from a full one. The item's own id is the locale key for its name, so the
    /// cell can say what it actually holds.
    /// </summary>
    private static string DescribeItemCell(GamepadNavigationItem item)
    {
        var cell = item.GetComponent<UIItemCell>() ?? item.GetComponentInParent<UIItemCell>();
        if (cell == null) return null;

        // Organ slots and grave parts mean their slot, not just their contents - see
        // BodyWindowsReader.
        var special = BodyWindowsReader.DescribeCell(cell);
        if (special != null) return special;

        // Tool-belt slots name their tool type and what would raise the mastery - see ToolSlotReader.
        var tool = ToolSlotReader.DescribeCell(cell);
        if (tool != null) return tool;

        // The hotbar slot window is read and driven by HotBarReader.
        if (HotBarReader.SilencesCell(cell)) return "";

        var displaying = cell.DisplayingItem;

        // An empty slot has to say so. Silence here reads as a broken mod, and "nothing" is a
        // genuinely useful answer when you are looking for room to put something.
        if (displaying == null || string.IsNullOrEmpty(displaying.id) || displaying.id == "empty")
            return Loc.Get("inventory.empty_slot");

        var name = ItemText.Name(displaying.id);
        if (string.IsNullOrWhiteSpace(name)) name = displaying.id;

        // A requirement cell ("3/5", red when short) - seeds, study ingredients, alchemy slots.
        var need = CraftReader.NeedText(cell, name);
        if (need != null) return need;

        var text = displaying.Count > 1 ? Loc.Fmt("inventory.item_many", name, displaying.Count) : name;

        // "Schnellleiste 2" on an inventory item that is pinned - see HotBarReader.
        var pinned = cell.GetComponentInParent<CharMainPageWidget>() == null ? null : HotBarReader.PinnedNote(displaying.id);
        return pinned == null ? text : $"{text}, {pinned}";
    }

    private static void AddPart(List<string> parts, string raw)
    {
        var clean = TmpText.Clean(raw);
        if (string.IsNullOrWhiteSpace(clean)) return;

        // A widget often renders the same string twice - a label and its drop shadow are two
        // components with identical text - and the shadow is a copy, not a second thing to say.
        if (parts.Contains(clean)) return;

        parts.Add(clean);
    }

    /// <summary>
    /// "UISaveSlotsWindow" -&gt; "save slots window", "btn_Continue(Clone)" -&gt; "btn Continue".
    /// A fallback for things the mod has no name for yet, not a substitute for naming them.
    /// </summary>
    private static string Prettify(string name)
    {
        if (string.IsNullOrEmpty(name)) return name;

        var clone = name.IndexOf("(Clone)", StringComparison.Ordinal);
        if (clone >= 0) name = name.Substring(0, clone);

        if (name.StartsWith("UI", StringComparison.Ordinal) && name.Length > 2 && char.IsUpper(name[2]))
            name = name.Substring(2);

        var sb = new System.Text.StringBuilder(name.Length + 8);
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (c == '_' || c == '-')
            {
                sb.Append(' ');
                continue;
            }

            // Split camel case, but keep runs of capitals together so "HUD" and "NPC" survive.
            if (i > 0 && char.IsUpper(c) && !char.IsUpper(name[i - 1]))
                sb.Append(' ');

            sb.Append(c);
        }

        return sb.ToString().Trim().ToLowerInvariant();
    }
}
