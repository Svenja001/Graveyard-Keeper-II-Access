namespace GraveyardKeeper2Accessibility;

/// <summary>
/// Every key the mod binds goes through here, so the startup check can compare all of them with the
/// game's keyboard bindings - not just the status keys - and so a default that turned out to clash
/// can be moved for players who already have a config file.
/// </summary>
internal static class ModKeys
{
    private sealed class Entry
    {
        internal string Name;
        internal ConfigEntry<KeyboardShortcut> Config;
        internal bool SharedOnPurpose;
    }

    private static readonly List<Entry> Entries = new();

    /// <summary>
    /// Binds a key. <paramref name="sharedOnPurpose"/> marks a key that deliberately does what the
    /// game's own key on it does in the same place (F starts a craft, as the game's Action does), so
    /// the clash check stays quiet about it.
    /// </summary>
    internal static ConfigEntry<KeyboardShortcut> Bind(ConfigFile config, string section, string key,
        KeyboardShortcut defaultValue, string description, bool sharedOnPurpose = false)
    {
        var entry = config.Bind(section, key, defaultValue, description);
        Entries.Add(new Entry { Name = key, Config = entry, SharedOnPurpose = sharedOnPurpose });
        return entry;
    }

    /// <summary>
    /// A default that moved: a config still holding the old default gets the new one. A key the
    /// player chose themselves is left alone.
    /// </summary>
    internal static ConfigEntry<KeyboardShortcut> Moved(this ConfigEntry<KeyboardShortcut> entry, KeyboardShortcut oldDefault)
    {
        if (entry.Value.ToString() == oldDefault.ToString() && entry.Value.ToString() != ((KeyboardShortcut)entry.DefaultValue).ToString())
        {
            Plugin.Log?.LogInfo($"[Keys] {entry.Definition.Key}: moved from {oldDefault} to {entry.DefaultValue}, " +
                                "because the old key is one of the game's own.");
            entry.Value = (KeyboardShortcut)entry.DefaultValue;
        }
        return entry;
    }

    /// <summary>Mod keys, by main key, for keys pressed without modifiers.</summary>
    internal static IEnumerable<(KeyCode Key, string Name)> Unshared() =>
        Entries.Where(e => !e.SharedOnPurpose && e.Config.Value.Modifiers.Count() == 0 && e.Config.Value.MainKey != KeyCode.None)
               .Select(e => (e.Config.Value.MainKey, e.Name));
}
