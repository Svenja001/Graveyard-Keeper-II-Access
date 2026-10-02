using TMPro;
using UnityEngine.EventSystems;

namespace GraveyardKeeper2Accessibility;

/// <summary>
/// One key per thing a sighted player reads off the HUD at a glance. Same letters as the Graveyard
/// Keeper mod, so a player coming from it does not learn them twice:
///
/// <list type="bullet">
///   <item><b>H</b> - energy, insanity and active buffs (the bar in the top corner, and the icons under it)</item>
///   <item><b>R</b> - money</item>
///   <item><b>P</b> - red, green and blue tech points, and town happiness</item>
///   <item><b>Q</b> - day of the week, day number and time (the day wheel)</item>
///   <item><b>G</b> - the zone you are in and its rating (the zone label)</item>
///   <item><b>Y</b> - the four hotbar slots</item>
///   <item><b>O</b> - full details of the focused item, or the tooltip on screen again</item>
/// </list>
///
/// Every value is read from the same data the HUD widget draws from, not scraped off its labels,
/// so the spoken answer and the screen cannot disagree.
/// </summary>
internal static class StatusKeys
{
    private static ManualLogSource _log;

    private static ConfigEntry<KeyboardShortcut> _vitalsKey;
    private static ConfigEntry<KeyboardShortcut> _moneyKey;
    private static ConfigEntry<KeyboardShortcut> _pointsKey;
    private static ConfigEntry<KeyboardShortcut> _timeKey;
    private static ConfigEntry<KeyboardShortcut> _zoneKey;
    private static ConfigEntry<KeyboardShortcut> _hotbarKey;
    private static ConfigEntry<KeyboardShortcut> _detailsKey;

    private static bool _bindingsLogged;

    internal static void Init(ManualLogSource log, ConfigFile config)
    {
        _log = log;

        _vitalsKey = ModKeys.Bind(config, "Keys", "Vitals", new KeyboardShortcut(KeyCode.H),
            "Says your energy, insanity and active buffs.");
        _moneyKey = ModKeys.Bind(config, "Keys", "Money", new KeyboardShortcut(KeyCode.K),
            "Says how much money you have.").Moved(new KeyboardShortcut(KeyCode.R));
        _pointsKey = ModKeys.Bind(config, "Keys", "TechPoints", new KeyboardShortcut(KeyCode.L),
            "Says your red, green and blue tech points, and town happiness.").Moved(new KeyboardShortcut(KeyCode.P));
        _timeKey = ModKeys.Bind(config, "Keys", "DayAndTime", new KeyboardShortcut(KeyCode.Z),
            "Says the day of the week, the day number and the time.").Moved(new KeyboardShortcut(KeyCode.Q));
        _zoneKey = ModKeys.Bind(config, "Keys", "Zone", new KeyboardShortcut(KeyCode.G),
            "Says the zone you are standing in and its rating.");
        _hotbarKey = ModKeys.Bind(config, "Keys", "Hotbar", new KeyboardShortcut(KeyCode.Y),
            "Says what is in the four hotbar slots.");
        _detailsKey = ModKeys.Bind(config, "Keys", "ItemDetails", new KeyboardShortcut(KeyCode.O),
            "Full details of the focused item, or reads the tooltip on screen again.");
    }

    internal static void Update()
    {
        LogBindingsOnce();

        if (IsTyping()) return;

        try
        {
            if (_vitalsKey.Value.IsDown()) SayVitals();
            else if (_moneyKey.Value.IsDown()) SayMoney();
            else if (_pointsKey.Value.IsDown()) SayPoints();
            else if (_timeKey.Value.IsDown()) SayTime();
            else if (_zoneKey.Value.IsDown()) SayZone();
            else if (_hotbarKey.Value.IsDown()) SayHotbar();
            else if (_detailsKey.Value.IsDown()) SayDetails();
        }
        catch (Exception ex)
        {
            _log?.LogError($"[Status] Readout failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    // ---- H ------------------------------------------------------------------------------------

    private static void SayVitals()
    {
        var player = ResourceAnnouncer.CurrentPlayer();
        if (player == null) { NotInGame(); return; }

        var energy = Mathf.FloorToInt(player.GetRes("energy"));
        var maxEnergy = Mathf.RoundToInt(PlayerEnergyGameResSystem.GetSystem()?.Max ?? 100f);
        var insanity = Mathf.FloorToInt(player.GetRes("insanity"));

        var line = Loc.Fmt("status.vitals",
            TmpText.Clean(LLBase.L("energy")), energy, maxEnergy,
            TmpText.Clean(LLBase.L("insanity")), insanity);

        Say(line + ". " + DescribeBuffs());
    }

    /// <summary>
    /// The buff icons under the energy bar, with their timers. Mirrors <c>UIBuffsDisplayData</c>:
    /// active perks of type Buff that are not flagged hidden.
    /// </summary>
    private static string DescribeBuffs()
    {
        var perks = MainGame.Instance?.GameSave?.perkSystemData?.activePerks;
        var names = new List<string>();

        if (perks != null)
        {
            foreach (var perk in perks)
            {
                var def = perk?.Definition;
                if (def == null || def.isHidden || def.perkType != PerkType.Buff) continue;

                var name = TmpText.Clean(LLBase.L(perk.id));
                if (string.IsNullOrWhiteSpace(name)) continue;

                // A negative duration is a buff that lasts until something removes it; the game
                // draws no timer for it, and neither do we.
                if (!def.hiddenTimer && def.duration >= 0f)
                    name = Loc.Fmt("status.buff_timer", name, PerkSystemData.GetFormattedDuration(perk.currentDuration));

                names.Add(name);
            }
        }

        return names.Count == 0 ? Loc.Get("status.no_buffs") : Loc.Fmt("status.buffs", string.Join(", ", names));
    }

    // ---- R ------------------------------------------------------------------------------------

    private static void SayMoney()
    {
        var player = ResourceAnnouncer.CurrentPlayer();
        if (player == null) { NotInGame(); return; }

        Say(Loc.Fmt("status.money", Money.ToSpeech(player.GetRes("money"))));
    }

    // ---- P ------------------------------------------------------------------------------------

    private static void SayPoints()
    {
        var player = ResourceAnnouncer.CurrentPlayer();
        if (player == null) { NotInGame(); return; }

        var line = Loc.Fmt("status.points",
            Mathf.FloorToInt(player.GetRes("tech_red")),
            Mathf.FloorToInt(player.GetRes("tech_green")),
            Mathf.FloorToInt(player.GetRes("tech_blue")));

        // Happiness shares the HUD corner with the tech points, and is drawn as "value/limit" once
        // the town has a quality, exactly as here.
        var happiness = Mathf.FloorToInt(player.GetRes("happiness"));
        var limit = MainGame.Instance?.GameSave?.townSystem?.Quality ?? 0;
        line += ". " + (limit > 0
            ? Loc.Fmt("status.happiness_of", happiness, limit)
            : Loc.Fmt("status.happiness", happiness));

        Say(line);
    }

    // ---- Q ------------------------------------------------------------------------------------

    /// <summary>
    /// The day wheel. The game draws the time as a sun and moon travelling round it, with no clock
    /// anywhere, so the hour here is the wheel's own position read as a 24-hour clock: 0 is
    /// midnight (the sun at the bottom), 0.5 is noon (the sun at the top).
    /// </summary>
    private static void SayTime()
    {
        if (ResourceAnnouncer.CurrentPlayer() == null || EnvironmentEngine.Instance == null) { NotInGame(); return; }

        var data = EnvironmentEngine.Instance.Data;
        var time = Mathf.Repeat(data.TimeOfDay, 1f);

        var totalMinutes = Mathf.FloorToInt(time * 24f * 60f);
        var clock = $"{totalMinutes / 60}:{totalMinutes % 60:00}";

        var weekday = WeekdayName(data.CurrentDayNumber);
        Say(Loc.Fmt("status.time", weekday, data.Day, clock, Loc.Get(PhaseKey(time))));
    }

    /// <summary>
    /// The six days of the week, which the game shows only as icons on the wheel. Their ids come
    /// from the game's own list, so a new day or a changed order still resolves.
    /// </summary>
    private static string WeekdayName(int dayNumber)
    {
        foreach (var id in LazyConsts.ConstDefs.AllDays)
        {
            try
            {
                if (ConstDef.Get(id).IntValue != dayNumber) continue;
            }
            catch
            {
                continue;
            }

            return Loc.Find("icon." + id) ?? id;
        }

        return Loc.Fmt("status.weekday_unknown", dayNumber);
    }

    /// <summary>
    /// Night is where the wheel itself switches to night - stars out, windows lit - at 0.8 in the
    /// evening and 0.25 in the morning.
    /// </summary>
    private static string PhaseKey(float time)
    {
        if (time < 0.25f || time >= 0.8f) return "status.phase.night";
        if (time < 0.5f) return "status.phase.morning";
        if (time < 0.7f) return "status.phase.afternoon";
        return "status.phase.evening";
    }

    // ---- G ------------------------------------------------------------------------------------

    /// <summary>
    /// The zone label under the day wheel, built the way <c>WorldZoneWidget.Redraw</c> builds it -
    /// including its exceptions - so a zone the game keeps quiet about stays quiet here too.
    /// </summary>
    private static void SayZone()
    {
        var player = ResourceAnnouncer.CurrentPlayer();
        if (player == null) { NotInGame(); return; }

        if (player.insideTownZones != null && player.insideTownZones.Count > 0)
        {
            var line = Loc.Fmt("status.town", TmpText.Clean(LLBase.L("town_zone")),
                MainGame.Instance.GameSave.townSystem.Quality);

            var sub = player.insideTownSubZones;
            if (sub != null && sub.Count > 0 && sub[sub.Count - 1] != null)
                line += ", " + TmpText.Clean(LLBase.L(sub[sub.Count - 1].id));

            Say(line);
            return;
        }

        var zone = player.CurrentWorldZoneData;
        var display = zone?.Definition?.displayType ?? WorldZoneDef.DisplayType.None;
        if (zone == null || display == WorldZoneDef.DisplayType.Hidden)
        {
            Say(Loc.Get("status.no_zone"));
            return;
        }

        var name = TmpText.Clean(LLBase.L("wz_" + zone.id));

        var showsQuality = zone.IsContainer
                           && display != WorldZoneDef.DisplayType.None
                           && (zone.id != "resurrection" || player.GetResInt("zombies_limit_mechanic") != 0);
        if (!showsQuality)
        {
            Say(name);
            return;
        }

        var raw = zone.GetQualityString();
        _log?.LogInfo($"[Status] Zone '{zone.id}' quality string: {raw}");
        Say(Loc.Fmt("status.zone_rating", name, TmpText.Clean(raw)));
    }

    // ---- Y ------------------------------------------------------------------------------------

    private static void SayHotbar()
    {
        var player = ResourceAnnouncer.CurrentPlayer();
        if (player == null) { NotInGame(); return; }

        var pinned = player.pinnedItems;
        if (pinned == null || pinned.Length == 0)
        {
            Say(Loc.Get("status.hotbar_empty"));
            return;
        }

        var parts = new List<string>();
        for (var i = 0; i < pinned.Length; i++)
        {
            var id = pinned[i];
            if (string.IsNullOrEmpty(id))
            {
                parts.Add(Loc.Fmt("status.slot_empty", i + 1));
                continue;
            }

            var held = ItemText.Held(id);
            parts.Add(held < 0
                ? Loc.Fmt("status.slot", i + 1, ItemText.Name(id))
                : Loc.Fmt("status.slot_count", i + 1, ItemText.Name(id), held));
        }

        Say(string.Join(". ", parts));
    }

    // ---- O ------------------------------------------------------------------------------------

    /// <summary>
    /// The tooltip on screen, read again in full - or, when there is none, what the game knows
    /// about the focused item. The automatic reading of a tooltip happens as focus lands, and is
    /// often cut short by whatever is said next; this is the way to get all of it.
    /// </summary>
    private static void SayDetails()
    {
        var focused = UiNarrator.FocusedItem;
        var cell = focused == null ? null : focused.GetComponent<UIItemCell>() ?? focused.GetComponentInParent<UIItemCell>();
        var item = cell == null ? null : cell.DisplayingItem;

        if (item != null && !item.IsEmpty)
        {
            var parts = new List<string> { ItemText.Name(item.id) };

            var held = ItemText.Held(item.id);
            if (held > 0) parts.Add(Loc.Fmt("status.you_have", held));

            if (!string.IsNullOrEmpty(TooltipReader.Current))
                parts.Add(TooltipReader.Current);
            else if (ItemText.Description(item.id) is { } description)
                parts.Add(description);

            Say(string.Join(". ", parts));
            return;
        }

        if (!string.IsNullOrEmpty(TooltipReader.Current))
        {
            Say(TooltipReader.Current);
            return;
        }

        Say(Loc.Get("status.no_details"));
    }

    // ---- shared -------------------------------------------------------------------------------

    private static void Say(string line)
    {
        _log?.LogInfo($"[Status] {line}");
        ScreenReader.Say(line);
    }

    private static void NotInGame() => ScreenReader.Say(Loc.Get("nav.not_in_game"));

    /// <summary>
    /// A letter typed into a text field - naming a zombie, naming a save - is not a request for
    /// the time of day.
    /// </summary>
    private static bool IsTyping()
    {
        try
        {
            var selected = EventSystem.current == null ? null : EventSystem.current.currentSelectedGameObject;
            if (selected == null) return false;

            var tmp = selected.GetComponent<TMP_InputField>();
            if (tmp != null && tmp.isFocused) return true;

            var legacy = selected.GetComponent<UnityEngine.UI.InputField>();
            return legacy != null && legacy.isFocused;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Writes the game's keyboard bindings to the log once, and flags any that share a key with one
    /// of the mod's. The defaults live in a data asset, not in code, so the running game is the only
    /// place to find out which letters are already taken - and a shared key means one press does
    /// two things.
    /// </summary>
    private static void LogBindingsOnce()
    {
        if (_bindingsLogged) return;

        try
        {
            // Not LazyInput.GameBindings directly: before the game has set input up, that forces an
            // early init which throws - see GameKeys. Wait and try again next frame instead.
            var bindings = GameKeys.All();
            if (bindings == null) return;
            _bindingsLogged = true;

            var mine = new Dictionary<KeyCode, string>();
            foreach (var (key, name) in ModKeys.Unshared())
                mine[key] = mine.TryGetValue(key, out var other) ? $"{other}/{name}" : name;

            var summary = new List<string>();
            foreach (var binding in bindings)
            {
                if (binding?.gameKey == null) continue;

                // additionalKeyCodes are modifiers held with the key, not alternatives to it.
                var chord = new List<KeyCode>();
                if (binding.additionalKeyCodes != null) chord.AddRange(binding.additionalKeyCodes);
                chord.Add(binding.keyCode);

                var gameKeyName = Enumeration.GetNameOfStaticField<GameKey>(binding.gameKey.value);
                summary.Add($"{gameKeyName}({binding.localeId})={string.Join("+", chord)}");

                var key = binding.keyCode;
                if (key != KeyCode.None && (binding.additionalKeyCodes == null || binding.additionalKeyCodes.Length == 0)
                    && mine.TryGetValue(key, out var modKey))
                    _log?.LogWarning($"[Status] Key {key} is both the mod's {modKey} key and the game's '{gameKeyName}'. " +
                                     "Change one of them if pressing it does two things.");
            }

            _log?.LogInfo($"[Status] Game keyboard bindings: {string.Join(", ", summary)}");
        }
        catch (Exception ex)
        {
            _bindingsLogged = true;
            _log?.LogWarning($"[Status] Could not read the game's key bindings: {ex.Message}");
        }
    }
}
