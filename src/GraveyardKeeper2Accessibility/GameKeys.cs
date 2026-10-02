namespace GraveyardKeeper2Accessibility;

/// <summary>
/// The game's live keyboard bindings, read safely.
///
/// <para>
/// <b>Why the binding dump used to fail.</b> <c>LazyInput.GameBindings</c> goes through
/// <c>LazyInput.Instance</c>, which runs <c>TryInit</c> when the game has not initialised input yet -
/// and <c>TryInit</c> touches Rewired's player list, which is null that early. Reading it on the
/// mod's first frame therefore threw a <c>NullReferenceException</c>. Everything here waits for the
/// game's own private <c>isInitialized</c> flag instead of forcing an init.
/// </para>
///
/// <para>
/// <b>Why it matters.</b> Several station actions sit on <c>GameKey</c>s whose keyboard key is not
/// known from the code (the defaults live in a data asset that ships in no readable file). A mod key
/// that duplicates the game's own would make one press act twice, so the mod asks the running game
/// before acting, and names the game's key when it has one.
/// </para>
///
/// <para>
/// Note that <c>KeyBinding.additionalKeyCodes</c> are <i>modifiers that must be held</i>
/// (<c>KeyboardController.Update</c> requires all of them), not alternative keys.
/// </para>
/// </summary>
internal static class GameKeys
{
    private static readonly AccessTools.FieldRef<bool> InputInitialized =
        AccessTools.StaticFieldRefAccess<bool>(AccessTools.Field(typeof(LazyInput), "isInitialized"));

    /// <summary>True once the game has set its input up, so reading bindings cannot force an early init.</summary>
    internal static bool Ready
    {
        get
        {
            try { return InputInitialized(); }
            catch { return false; }
        }
    }

    /// <summary>The game's keyboard bindings, or null when input is not ready yet.</summary>
    internal static List<KeyBinding> All()
    {
        if (!Ready) return null;
        try
        {
            return LazyInput.GameBindings?.keyBindings;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// The keyboard binding for a game key, or null when there is none - or when bindings cannot be
    /// read, which callers must treat as "unknown", not as "unbound".
    /// </summary>
    internal static KeyBinding Find(GameKey key, out bool known)
    {
        known = false;
        var all = All();
        if (all == null || key == null) return null;

        known = true;
        foreach (var binding in all)
        {
            if (binding?.gameKey == null || binding.gameKey.value != key.value) continue;
            if (binding.keyCode == KeyCode.None) continue;
            return binding;
        }
        return null;
    }

    /// <summary>
    /// True when the game already reacts to this mod key for the given game key, so the mod must
    /// leave the press to the game. Unknown bindings answer false: acting once more is the lesser
    /// failure next to a key that does nothing, and every caller checks the window is still on top.
    /// </summary>
    internal static bool GameHandles(GameKey key, KeyboardShortcut shortcut)
    {
        var binding = Find(key, out _);
        if (binding == null) return false;
        if (binding.keyCode != shortcut.MainKey) return false;

        var mods = binding.additionalKeyCodes ?? Array.Empty<KeyCode>();
        var theirs = new HashSet<KeyCode>(mods);
        var ours = new HashSet<KeyCode>(shortcut.Modifiers ?? Enumerable.Empty<KeyCode>());
        return theirs.SetEquals(ours);
    }

    /// <summary>"F", "Ctrl+Q" - the game's keyboard key for this action, or null when it has none or it is unknown.</summary>
    internal static string Name(GameKey key)
    {
        var binding = Find(key, out _);
        if (binding == null) return null;

        var parts = new List<string>();
        if (binding.additionalKeyCodes != null)
            parts.AddRange(binding.additionalKeyCodes.Where(k => k != KeyCode.None).Select(KeyName));
        parts.Add(KeyName(binding.keyCode));
        return string.Join("+", parts);
    }

    /// <summary>A key as a player would call it: "Space", "Left Ctrl", "1".</summary>
    internal static string KeyName(KeyCode key)
    {
        var name = key.ToString();
        if (name.StartsWith("Alpha", StringComparison.Ordinal) && name.Length == 6) return name.Substring(5);
        if (name.StartsWith("Left", StringComparison.Ordinal) && name.Length > 4 && name != "LeftArrow") return "Left " + name.Substring(4);
        if (name.StartsWith("Right", StringComparison.Ordinal) && name.Length > 5 && name != "RightArrow") return "Right " + name.Substring(5);
        return name;
    }

    /// <summary>"F", "Ctrl+F6" - a mod key as the player would read it.</summary>
    internal static string Name(KeyboardShortcut shortcut) => shortcut.ToString();
}
