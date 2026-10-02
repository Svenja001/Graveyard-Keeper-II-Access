namespace GraveyardKeeper2Accessibility;

/// <summary>
/// The controls screen (<c>UIGameBindingSettingsWindow</c>, pause menu → Steuerung): read every
/// keyboard binding and change them.
///
/// <para>
/// It was silent on 2026-09-25 - opened and closed with zero focus events. Its keyboard rows are
/// plain buttons with no navigation items, and the second list it draws for gamepads has no buttons
/// at all, so with gamepad mode forced there is nothing for focus to land on. This is a mod-owned
/// cursor instead, like the confirmation boxes: up/down arrows over "action: key", then Restore
/// defaults and OK.
/// </para>
///
/// <para>
/// Rebinding does not go through the row's own button. That button starts listening for a key in
/// its <c>Update</c> on the same frame, so the Enter that pressed it would become the new key. The
/// mod waits for the next frame, reads the key itself, and hands the result to the window's own
/// <c>UpdateBinding</c>, which clears the key from any other action, keeps SpeechSkip2 in step with
/// Interaction and saves - exactly as a mouse player's rebind would. While waiting, the window is
/// locked the way the game locks it, so Escape cancels instead of closing the screen.
/// </para>
/// </summary>
internal static class BindingsReader
{
    internal static bool Ready { get; private set; }

    private static class G
    {
        internal static readonly AccessTools.FieldRef<UIGameBindingSettingsWindow, List<GameKey>> KeysToBind =
            AccessTools.FieldRefAccess<UIGameBindingSettingsWindow, List<GameKey>>("keysToBind");

        internal static readonly AccessTools.FieldRef<UIGameBindingSettingsWindow, List<KeyBinding>> KeyBindings =
            AccessTools.FieldRefAccess<UIGameBindingSettingsWindow, List<KeyBinding>>("keyBindings");

        internal static readonly AccessTools.FieldRef<UIGameBindingSettingsWindow, Dictionary<GameKey, UIGameBindingElement>> Elements =
            AccessTools.FieldRefAccess<UIGameBindingSettingsWindow, Dictionary<GameKey, UIGameBindingElement>>("cachedBindingElements");

        internal static readonly AccessTools.FieldRef<UIGameBindingSettingsWindow, LazyButton> Ok =
            AccessTools.FieldRefAccess<UIGameBindingSettingsWindow, LazyButton>("okBtn");

        internal static readonly AccessTools.FieldRef<UIGameBindingSettingsWindow, LazyButton> Restore =
            AccessTools.FieldRefAccess<UIGameBindingSettingsWindow, LazyButton>("restoreDefaultBindings");

        internal static readonly MethodInfo UpdateBinding =
            AccessTools.Method(typeof(UIGameBindingSettingsWindow), "UpdateBinding") ?? throw new MissingMethodException("UpdateBinding");

        internal static readonly MethodInfo SetLock =
            AccessTools.Method(typeof(UIGameBindingSettingsWindow), "SetLockOnBindingButtons") ?? throw new MissingMethodException("SetLockOnBindingButtons");
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
            Plugin.Log?.LogError($"[Bindings] The controls screen is not supported by this game version: " +
                                 $"{(ex.InnerException ?? ex).GetType().Name}: {(ex.InnerException ?? ex).Message}");
        }
    }

    internal static bool SpeaksForItself(LazyWidgetBase window) => Ready && window is UIGameBindingSettingsWindow;

    private static UIGameBindingSettingsWindow Active =>
        Ready ? LazyWindowsStackController.ActiveWindow as UIGameBindingSettingsWindow : null;

    private static int _index;
    private static KeyBinding _capturing;
    private static int _captureFrame;
    private static bool _unlockNextFrame;

    private static readonly KeyCode[] AllKeys =
        ((KeyCode[])Enum.GetValues(typeof(KeyCode))).Where(k => k != KeyCode.None).Distinct().ToArray();

    // ---- rows -------------------------------------------------------------------------------

    /// <summary>The bindings in on-screen order, then Restore defaults and OK.</summary>
    private static List<KeyBinding> Bindings(UIGameBindingSettingsWindow window)
    {
        var all = G.KeyBindings(window) ?? new List<KeyBinding>();
        return (G.KeysToBind(window) ?? new List<GameKey>())
            .Select(key => all.Find(b => b.gameKey != null && b.gameKey.value == key.value))
            .Where(b => b != null)
            .ToList();
    }

    private static int RowCount(UIGameBindingSettingsWindow window) => Bindings(window).Count + 2;

    private static string RowText(UIGameBindingSettingsWindow window, int index)
    {
        var bindings = Bindings(window);
        if (index < bindings.Count)
        {
            var b = bindings[index];
            var text = Loc.Fmt("bind.row", ActionName(b), KeysName(b));
            return Fixed(b) ? $"{text}, {Loc.Get("bind.fixed_short")}" : text;
        }
        return index == bindings.Count
            ? ButtonLabel(G.Restore(window)) ?? Loc.Get("bind.restore")
            : ButtonLabel(G.Ok(window)) ?? "OK";
    }

    private static string ButtonLabel(LazyButton button)
    {
        var label = button == null ? null : button.GetComponentInChildren<TMPro.TMP_Text>(includeInactive: true);
        var text = label == null ? null : TmpText.Clean(label.text);
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    /// <summary>Escape and the left mouse button cannot be rebound; the game hides their button.</summary>
    private static bool Fixed(KeyBinding b) => b.keyCode == KeyCode.Escape || b.keyCode == KeyCode.Mouse0;

    private static string ActionName(KeyBinding b)
    {
        if (!string.IsNullOrEmpty(b.localeId))
        {
            var name = TmpText.Clean(LLBase.L(b.localeId));
            if (!string.IsNullOrWhiteSpace(name)) return name;
        }
        return Enumeration.GetNameOfStaticField<GameKey>(b.gameKey.value);
    }

    private static string KeysName(KeyBinding b)
    {
        if (b.keyCode == KeyCode.None) return Loc.Get("bind.none");
        var keys = new List<KeyCode> { b.keyCode };
        if (b.additionalKeyCodes != null) keys.AddRange(b.additionalKeyCodes.Where(k => k != KeyCode.None));
        return string.Join(" + ", keys.Select(KeyName));
    }

    internal static string KeyName(KeyCode key)
    {
        var name = key.ToString();
        if (name.Length == 1) return name;
        if (key >= KeyCode.Alpha0 && key <= KeyCode.Alpha9) return ((int)(key - KeyCode.Alpha0)).ToString();
        if (key >= KeyCode.Keypad0 && key <= KeyCode.Keypad9) return Loc.Fmt("keyname.keypad", (int)(key - KeyCode.Keypad0));
        return Loc.Find("keyname." + name) ?? name;
    }

    // ---- opening ----------------------------------------------------------------------------

    [HarmonyPatch(typeof(UIGameBindingSettingsWindow), nameof(UIGameBindingSettingsWindow.Redraw))]
    [HarmonyPostfix]
    private static void UIGameBindingSettingsWindow_Redraw(UIGameBindingSettingsWindow __instance)
    {
        try
        {
            _index = 0;
            _capturing = null;
            _unlockNextFrame = false;
            Say(Loc.Fmt("bind.open", Bindings(__instance).Count, RowText(__instance, 0)));
        }
        catch (Exception ex)
        {
            Plugin.Log?.LogError($"[Bindings] Reading the controls screen failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    // ---- keys -------------------------------------------------------------------------------

    /// <summary>
    /// Called every frame before the other key handlers. True when the controls screen used this
    /// frame's keys - always while it is waiting for a new key, so nothing else reacts to it.
    /// </summary>
    internal static bool Update()
    {
        var window = Active;
        if (window == null)
        {
            _capturing = null;
            _unlockNextFrame = false;
            return false;
        }

        try
        {
            if (_unlockNextFrame)
            {
                _unlockNextFrame = false;
                G.SetLock.Invoke(window, new object[] { false });
            }

            if (_capturing != null)
            {
                Capture(window);
                return true;
            }

            var count = RowCount(window);
            if (Input.GetKeyDown(KeyCode.DownArrow)) Move(window, (_index + 1) % count);
            else if (Input.GetKeyDown(KeyCode.UpArrow)) Move(window, (_index - 1 + count) % count);
            else if (Input.GetKeyDown(KeyCode.Home)) Move(window, 0);
            else if (Input.GetKeyDown(KeyCode.End)) Move(window, count - 1);
            else if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)) Activate(window);
            else if (Input.GetKeyDown(KeyCode.LeftArrow) || Input.GetKeyDown(KeyCode.RightArrow)) { }
            else return false;
            return true;
        }
        catch (Exception ex)
        {
            Plugin.Log?.LogError($"[Bindings] Key handling failed: {ex.GetType().Name}: {ex.Message}");
            _capturing = null;
            return false;
        }
    }

    /// <summary>F8 says the current row again.</summary>
    internal static bool TryRepeat()
    {
        var window = Active;
        if (window == null) return false;
        Say(_capturing != null ? Loc.Fmt("bind.press", ActionName(_capturing)) : RowText(window, _index));
        return true;
    }

    private static void Move(UIGameBindingSettingsWindow window, int index)
    {
        _index = Mathf.Clamp(index, 0, RowCount(window) - 1);
        Say(RowText(window, _index));
    }

    private static void Activate(UIGameBindingSettingsWindow window)
    {
        var bindings = Bindings(window);
        if (_index < bindings.Count)
        {
            var b = bindings[_index];
            if (Fixed(b))
            {
                Say(Loc.Get("bind.fixed"));
                return;
            }

            _capturing = b;
            _captureFrame = Time.frameCount;
            G.SetLock.Invoke(window, new object[] { true });
            Say(Loc.Fmt("bind.press", ActionName(b)));
            return;
        }

        if (_index == bindings.Count)
        {
            G.Restore(window)?.onClick?.Invoke();
            Plugin.Log?.LogInfo("[Bindings] Restored the default keys.");
            Say($"{Loc.Get("bind.restored")}. {RowText(window, _index)}");
            return;
        }

        G.Ok(window)?.onClick?.Invoke();
        if (LazyWindowsStackController.ActiveWindow == window) window.Close();
    }

    private static void Capture(UIGameBindingSettingsWindow window)
    {
        // The Enter that started this is still "down" on its own frame.
        if (Time.frameCount <= _captureFrame) return;

        foreach (var key in AllKeys)
        {
            if (!Input.GetKeyDown(key)) continue;

            if (key == KeyCode.Escape)
            {
                _capturing = null;
                // Unlocked a frame later, so the game's own Escape handling this frame still finds
                // the window locked and does not close it.
                _unlockNextFrame = true;
                Say(Loc.Get("bind.cancelled"));
                return;
            }

            if (Forbidden(key))
            {
                Say(Loc.Fmt("bind.forbidden", KeyName(key)));
                return;
            }

            Apply(window, key);
            return;
        }
    }

    /// <summary>The same keys <c>UIGameBindingElement</c> refuses.</summary>
    private static bool Forbidden(KeyCode key) =>
        (key >= KeyCode.F1 && key <= KeyCode.F12) ||
        key == KeyCode.LeftWindows || key == KeyCode.RightWindows || key == KeyCode.LeftMeta || key == KeyCode.RightMeta ||
        key == KeyCode.Menu || key == KeyCode.Print || key == KeyCode.ScrollLock || key == KeyCode.Break ||
        key == KeyCode.Pause || key == KeyCode.Numlock || key == KeyCode.Tilde || key == KeyCode.BackQuote ||
        key == KeyCode.Mouse0 || key == KeyCode.Escape;

    private static void Apply(UIGameBindingSettingsWindow window, KeyCode key)
    {
        var binding = _capturing;
        _capturing = null;

        // Who loses the key - UpdateBinding clears it from every other action. Only the visible
        // ones are worth naming; the hidden SpeechSkip2 follows Interaction by design.
        var lost = Bindings(window).Where(b => b != binding && b.keyCode == key).Select(ActionName).ToList();

        binding.keyCode = key;
        var element = G.Elements(window)?.Values.FirstOrDefault(e => e != null && e.keyBinding == binding);
        if (element != null)
        {
            G.UpdateBinding.Invoke(window, new object[] { element });
        }
        else
        {
            G.SetLock.Invoke(window, new object[] { false });
            GameSettings.Instance.SaveCurrentGameBindings();
        }

        Plugin.Log?.LogInfo($"[Bindings] {Enumeration.GetNameOfStaticField<GameKey>(binding.gameKey.value)} = {key}" +
                            (lost.Count > 0 ? $" (cleared from {string.Join(", ", lost)})" : ""));

        var text = Loc.Fmt("bind.row", ActionName(binding), KeysName(binding));
        if (lost.Count > 0) text += ". " + Loc.Fmt("bind.lost", string.Join(", ", lost));
        Say(text);
    }

    private static void Say(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        Plugin.Log?.LogInfo($"[Bindings] \"{text}\"");
        ScreenReader.Say(text);
    }
}
