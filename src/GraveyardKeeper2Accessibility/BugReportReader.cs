using TMPro;

namespace GraveyardKeeper2Accessibility;

/// <summary>
/// "Fehler melden" - the game's bug report window, opened from the menu.
///
/// <para>
/// It could be heard but not used. On opening the window puts the cursor in its title field, and
/// while a field has the cursor it switches the game's input off entirely
/// (<c>LazyInput.SetInputActivity(false)</c>), so no navigation key reaches anything. Tab only
/// swaps between the two text fields, and Escape closed the whole window. The categories and the
/// Send button are <c>UIDialogWindowButton</c>s drawn with <c>replaceForGamepad: false</c>, which
/// gives them no hotkey at all - they can only be clicked. So a report could be typed but never
/// sent.
/// </para>
///
/// <para>
/// The mod keeps its own list, the way <see cref="TravelMapReader"/> does: title, description,
/// each category, Send, Cancel. Arrows step through it; Enter puts the cursor in a field or presses
/// a button through its own <c>onClick</c>, so the report goes out exactly as a click sends it.
/// Escape while typing leaves the field instead of throwing the report away.
/// </para>
/// </summary>
internal static class BugReportReader
{
    private static ManualLogSource _log;

    private static readonly AccessTools.FieldRef<UIBugReportWindow, TMP_InputField> TitleInput =
        AccessTools.FieldRefAccess<UIBugReportWindow, TMP_InputField>("titleInput");
    private static readonly AccessTools.FieldRef<UIBugReportWindow, TMP_InputField> StepsInput =
        AccessTools.FieldRefAccess<UIBugReportWindow, TMP_InputField>("stepsInput");
    private static readonly AccessTools.FieldRef<UIBugReportWindow, List<UIDialogWindowButton>> CategoryButtons =
        AccessTools.FieldRefAccess<UIBugReportWindow, List<UIDialogWindowButton>>("categoryButtons");
    private static readonly AccessTools.FieldRef<UIBugReportWindow, List<string>> CategoryIds =
        AccessTools.FieldRefAccess<UIBugReportWindow, List<string>>("categoryIds");
    private static readonly AccessTools.FieldRef<UIBugReportWindow, string> SelectedCategory =
        AccessTools.FieldRefAccess<UIBugReportWindow, string>("selectedCategory");
    private static readonly AccessTools.FieldRef<UIBugReportWindow, UIDialogWindowButton> SendButton =
        AccessTools.FieldRefAccess<UIBugReportWindow, UIDialogWindowButton>("sendButton");
    private static readonly AccessTools.FieldRef<UIBugReportWindow, UIDialogWindowButton> CancelButton =
        AccessTools.FieldRefAccess<UIBugReportWindow, UIDialogWindowButton>("cancelButton");
    private static readonly AccessTools.FieldRef<UIBugReportWindow, TMP_Text> StatusLabel =
        AccessTools.FieldRefAccess<UIBugReportWindow, TMP_Text>("statusLabel");
    private static readonly AccessTools.FieldRef<UIDialogWindowButton, TextMeshProUGUI> ButtonLabel =
        AccessTools.FieldRefAccess<UIDialogWindowButton, TextMeshProUGUI>("label");

    /// <summary>One place the cursor can be: a text field, or a button (a category, Send, Cancel).</summary>
    private sealed class Stop
    {
        public TMP_InputField Field;
        public UIDialogWindowButton Button;
        public string CategoryId;
    }

    private static int _index;
    private static bool _hasNavigated;

    /// <summary>The field that had the cursor last frame, so starting and stopping typing can be said.</summary>
    private static TMP_InputField _lastTyping;

    /// <summary>
    /// The window puts the cursor in the title field itself, a frame after opening. The opening line
    /// already says so, and saying it again would talk over it.
    /// </summary>
    private static bool _awaitingAutoFocus;

    private static string _openingLine;

    internal static bool Ready { get; private set; }

    internal static void Init(ManualLogSource log)
    {
        _log = log;
        Ready = AccessTools.Method(typeof(UIBugReportWindow), "LateUpdate") != null &&
                AccessTools.Field(typeof(UIBugReportWindow), "titleInput") != null &&
                AccessTools.Field(typeof(UIBugReportWindow), "categoryButtons") != null;
        if (!Ready) _log?.LogWarning("[BugReport] The bug report window has changed; it will not be readable.");
    }

    internal static bool SpeaksForItself(LazyWidgetBase window) => window is UIBugReportWindow;

    private static UIBugReportWindow Active() =>
        LazyWindowsStackController.ActiveWindow is UIBugReportWindow window && window.IsShown ? window : null;

    [HarmonyPatch(typeof(UIBugReportWindow), nameof(UIBugReportWindow.Open))]
    [HarmonyPostfix]
    private static void UIBugReportWindow_Open(UIBugReportWindow __instance)
    {
        try
        {
            _index = 0;
            _hasNavigated = false;
            _lastTyping = null;
            _awaitingAutoFocus = true;

            // Escape in a TextMeshPro field puts back whatever was there when typing began - a whole
            // description gone for the key the player is told to press to stop typing.
            foreach (var field in new[] { TitleInput(__instance), StepsInput(__instance) })
                if (field != null) field.restoreOriginalTextOnEscape = false;

            var parts = new List<string>();
            Add(parts, BugReportLoc.L(BugReportLoc.Header));
            parts.Add(Loc.Fmt("bugreport.opening", FieldName(__instance, TitleInput(__instance))));
            Add(parts, BugReportLoc.L(BugReportLoc.Privacy));

            _openingLine = string.Join(". ", parts);
            _log?.LogInfo($"[BugReport] Opened => \"{_openingLine}\"");
            ScreenReader.Say(_openingLine);
        }
        catch (Exception ex)
        {
            _log?.LogError($"[BugReport] Reading the bug report window failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>
    /// Escape while typing leaves the field. The window's own <c>LateUpdate</c> closes the whole
    /// window on it instead, report and all, so that frame is skipped; the next frame it puts the
    /// game's input back by itself, because no field is focused any more.
    /// </summary>
    [HarmonyPatch(typeof(UIBugReportWindow), "LateUpdate")]
    [HarmonyPrefix]
    private static bool UIBugReportWindow_LateUpdate(UIBugReportWindow __instance)
    {
        try
        {
            if (!__instance.IsShown || !Input.GetKeyDown(KeyCode.Escape)) return true;

            var field = FocusedField(__instance);
            if (field == null) return true;

            field.DeactivateInputField();
            return false;
        }
        catch (Exception ex)
        {
            _log?.LogError($"[BugReport] Leaving the text field failed: {ex.GetType().Name}: {ex.Message}");
            return true;
        }
    }

    /// <summary>
    /// Every key while the window is on top. Returns true when the window is open, so none of the
    /// mod's other keys fire on letters typed into the report.
    /// </summary>
    internal static bool Update(bool repeat)
    {
        var window = Active();
        if (window == null)
        {
            _lastTyping = null;
            return false;
        }

        try
        {
            var typing = FocusedField(window);
            var wasTyping = _lastTyping;
            TrackTyping(window, typing);

            if (repeat)
            {
                Repeat(window, typing);
                return true;
            }

            // Everything else belongs to the field while the cursor is in it - including Enter, which
            // is a new line in the description.
            if (typing != null || wasTyping != null) return true;

            if (Input.GetKeyDown(KeyCode.Escape))
            {
                // The game closes the window on Back by itself; the closing is held back for windows
                // that speak for themselves, so this says it.
                ScreenReader.Say(Loc.Get("bugreport.closed"));
                return true;
            }

            var stops = Stops(window);
            if (stops.Count == 0) return true;

            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
            {
                Press(window, stops[Mathf.Clamp(_index, 0, stops.Count - 1)]);
                return true;
            }

            if (Input.GetKeyDown(KeyCode.DownArrow) || Input.GetKeyDown(KeyCode.RightArrow)) Move(window, stops, 1);
            else if (Input.GetKeyDown(KeyCode.UpArrow) || Input.GetKeyDown(KeyCode.LeftArrow)) Move(window, stops, -1);
            else if (Input.GetKeyDown(KeyCode.Home)) Select(window, stops, 0);
            else if (Input.GetKeyDown(KeyCode.End)) Select(window, stops, stops.Count - 1);

            return true;
        }
        catch (Exception ex)
        {
            _log?.LogError($"[BugReport] Key handling failed: {ex.GetType().Name}: {ex.Message}");
            return true;
        }
    }

    /// <summary>
    /// Says when the cursor goes into a field, moves to the other one (Tab), or leaves - and on
    /// leaving reads back what is in the field, since the typing itself is only as audible as the
    /// screen reader's own key echo.
    /// </summary>
    private static void TrackTyping(UIBugReportWindow window, TMP_InputField typing)
    {
        if (typing == _lastTyping) return;

        var previous = _lastTyping;
        _lastTyping = typing;

        if (typing != null && _awaitingAutoFocus)
        {
            _awaitingAutoFocus = false;
            return;
        }
        _awaitingAutoFocus = false;

        if (typing != null)
        {
            ScreenReader.Say(previous == null
                ? Loc.Fmt("bugreport.typing", FieldName(window, typing))
                : FieldLine(window, typing));
            return;
        }

        // Arrows carry on from the field just left.
        var stops = Stops(window);
        var at = stops.FindIndex(s => s.Field == previous);
        if (at >= 0)
        {
            _index = at;
            _hasNavigated = true;
        }
        ScreenReader.Say(Loc.Fmt("bugreport.left", FieldLine(window, previous)));
    }

    private static void Repeat(UIBugReportWindow window, TMP_InputField typing)
    {
        if (typing != null)
        {
            ScreenReader.Say(FieldLine(window, typing));
            return;
        }

        var stops = Stops(window);
        if (_hasNavigated && stops.Count > 0)
            ScreenReader.Say(StopLine(window, stops[Mathf.Clamp(_index, 0, stops.Count - 1)]));
        else
            ScreenReader.Say(_openingLine ?? string.Empty);
    }

    /// <summary>
    /// The window in reading order. Rebuilt on every key, because the categories are replaced when
    /// the server's list arrives after opening.
    /// </summary>
    private static List<Stop> Stops(UIBugReportWindow window)
    {
        var stops = new List<Stop>();

        void AddField(TMP_InputField field)
        {
            if (field != null && field.gameObject.activeInHierarchy) stops.Add(new Stop { Field = field });
        }

        void AddButton(UIDialogWindowButton button, string categoryId = null)
        {
            if (button != null && button.gameObject.activeInHierarchy) stops.Add(new Stop { Button = button, CategoryId = categoryId });
        }

        AddField(TitleInput(window));
        AddField(StepsInput(window));

        var buttons = CategoryButtons(window);
        var ids = CategoryIds(window);
        if (buttons != null && ids != null)
            for (var i = 0; i < buttons.Count && i < ids.Count; i++)
                AddButton(buttons[i], ids[i]);

        AddButton(SendButton(window));
        AddButton(CancelButton(window));
        return stops;
    }

    /// <summary>The first press says where the cursor already is, as on the travel map.</summary>
    private static void Move(UIBugReportWindow window, List<Stop> stops, int delta)
    {
        var target = _hasNavigated ? ((_index + delta) % stops.Count + stops.Count) % stops.Count : _index;
        Select(window, stops, target);
    }

    private static void Select(UIBugReportWindow window, List<Stop> stops, int index)
    {
        _hasNavigated = true;
        _index = Mathf.Clamp(index, 0, stops.Count - 1);
        ScreenReader.Say(StopLine(window, stops[_index]));
    }

    private static void Press(UIBugReportWindow window, Stop stop)
    {
        _hasNavigated = true;

        if (stop.Field != null)
        {
            // Said by TrackTyping on the next frame, once the field really has the cursor.
            stop.Field.Select();
            stop.Field.ActivateInputField();
            return;
        }

        var button = stop.Button.LazyButton;
        if (button == null || !button.interactable)
        {
            ScreenReader.Say(Loc.Fmt("bugreport.unavailable", ButtonText(stop.Button)));
            return;
        }

        var text = ButtonText(stop.Button);
        _log?.LogInfo($"[BugReport] Pressing '{text}'{(stop.CategoryId != null ? $" (category {stop.CategoryId})" : "")}.");
        button.onClick?.Invoke();

        if (stop.CategoryId != null)
        {
            ScreenReader.Say(StopLine(window, stop));
            return;
        }

        if (stop.Button == CancelButton(window))
        {
            ScreenReader.Say(Loc.Get("bugreport.closed"));
            return;
        }

        // Send: either the window closed and the "sending" box speaks for itself, or it refused -
        // and the only place it says why ("a title is required") is a line of text in the window.
        if (window.IsShown)
        {
            var status = StatusLabel(window);
            if (status != null && status.gameObject.activeInHierarchy)
            {
                var reason = TmpText.Clean(status.text);
                if (!string.IsNullOrWhiteSpace(reason)) ScreenReader.Say(reason);
            }
        }
    }

    private static string StopLine(UIBugReportWindow window, Stop stop)
    {
        if (stop.Field != null) return FieldLine(window, stop.Field);

        var text = ButtonText(stop.Button);
        if (stop.CategoryId != null)
            return Loc.Fmt(stop.CategoryId == SelectedCategory(window) ? "bugreport.category_selected" : "bugreport.category", text);

        var button = stop.Button.LazyButton;
        return button != null && !button.interactable ? Loc.Fmt("bugreport.unavailable", text) : text;
    }

    /// <summary>"Title, text field: the bridge has no railing" - or "empty".</summary>
    private static string FieldLine(UIBugReportWindow window, TMP_InputField field)
    {
        var content = field.text;
        return Loc.Fmt("bugreport.field", FieldName(window, field),
            string.IsNullOrWhiteSpace(content) ? Loc.Get("bugreport.empty") : content);
    }

    /// <summary>
    /// The field's placeholder is its only label on screen - the game writes the title and the
    /// "steps to reproduce" prompt into it.
    /// </summary>
    private static string FieldName(UIBugReportWindow window, TMP_InputField field)
    {
        var token = field == StepsInput(window) ? BugReportLoc.Steps : BugReportLoc.Title;
        var name = TmpText.Clean(BugReportLoc.L(token));
        return string.IsNullOrWhiteSpace(name) ? token : name;
    }

    private static string ButtonText(UIDialogWindowButton button)
    {
        var label = ButtonLabel(button);
        var text = label == null ? null : TmpText.Clean(label.text);
        return string.IsNullOrWhiteSpace(text) ? button.name : text;
    }

    private static TMP_InputField FocusedField(UIBugReportWindow window)
    {
        var title = TitleInput(window);
        if (title != null && title.isFocused) return title;
        var steps = StepsInput(window);
        return steps != null && steps.isFocused ? steps : null;
    }

    private static void Add(List<string> parts, string raw)
    {
        if (string.IsNullOrEmpty(raw)) return;
        var clean = TmpText.Clean(raw);
        if (!string.IsNullOrWhiteSpace(clean)) parts.Add(clean);
    }
}
