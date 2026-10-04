namespace GraveyardKeeper2Accessibility;

/// <summary>
/// Fast travel: the map that opens when the player uses a travel stone.
///
/// <para>
/// <c>UIMapWindow</c> is another window with no navigation items. A mouse picks a destination by
/// hovering its icon; a pad steers a virtual cursor over the picture (<c>MapVirtualCursor</c>) until
/// it touches one; the keyboard only pans the picture. So the stones on it can be neither found nor
/// chosen without sight, and Enter does nothing - the window's own confirm is <c>GameKey.Select</c>,
/// and even that only presses the stone the cursor already touches.
/// </para>
///
/// <para>
/// The mod therefore keeps its own list, the way <see cref="DialogWindowReader"/> does for the
/// confirmation boxes: the stones the map drew as <b>pressable</b> - activated, and not the one the
/// player is standing at (<c>MapPageWidget.UpdateMilestones</c> decides both, and the mod only reads
/// its answer, so a stone that is not unlocked can never be offered). Arrows step through them, a
/// letter jumps to the next one starting with it, and Enter travels through
/// <c>MapPageWidget.OnPressMapMilestone</c> - the very method a click calls, so the teleport, the
/// preset and the stone's story expression all run exactly as they would for a sighted player.
/// </para>
/// </summary>
internal static class TravelMapReader
{
    private static ManualLogSource _log;

    private static readonly AccessTools.FieldRef<LazyWidget<MapPageWidgetData>, MapPageWidgetData> MapData =
        AccessTools.FieldRefAccess<LazyWidget<MapPageWidgetData>, MapPageWidgetData>("data");

    private static readonly AccessTools.FieldRef<MapPageWidget, Dictionary<string, UIMapMilestone>> CreatedMilestones =
        AccessTools.FieldRefAccess<MapPageWidget, Dictionary<string, UIMapMilestone>>("createdMilestones");

    /// <summary>The destinations on the map now open, sorted by name.</summary>
    private static readonly List<UIMapMilestone> _stones = new();

    private static int _index;
    private static bool _hasNavigated;
    private static MapPageWidgetData _readData;

    internal static void Init(ManualLogSource log) => _log = log;

    internal static bool SpeaksForItself(LazyWidgetBase window) => window is UIMapWindow;

    /// <summary>
    /// Reads the map once per opening: where the player is, how many places they can go, and the
    /// keys. After <c>Redraw</c>, because that is when <c>UpdateMilestones</c> has decided which
    /// stones are unlocked.
    /// </summary>
    [HarmonyPatch(typeof(UIMapWindow), nameof(UIMapWindow.Redraw))]
    [HarmonyPostfix]
    private static void UIMapWindow_Redraw(UIMapWindow __instance)
    {
        try
        {
            var data = MapData(__instance);
            if (data == null || ReferenceEquals(data, _readData)) return;
            _readData = data;

            Collect(__instance);
            _index = 0;
            _hasNavigated = false;

            var parts = new List<string> { Loc.Get("map.title") };
            if (!string.IsNullOrEmpty(data.CurrentMilestone))
                parts.Add(Loc.Fmt("map.here", StoneName(data.CurrentMilestone, MainGame.WorldData?.GetWgoData(data.CurrentMilestone))));

            if (!data.MilestonesInteractable)
                parts.Add(Loc.Get("map.view_only"));
            else if (_stones.Count == 0)
                parts.Add(Loc.Get("map.none"));
            else
                parts.Add(Loc.Fmt("map.count", _stones.Count, Entry(_stones[0])));

            var line = string.Join(". ", parts);
            _log?.LogInfo($"[Map] {_stones.Count} destination(s) => \"{line}\" " +
                          $"[{string.Join(", ", _stones.Select(s => s.WgoData.id))}]");
            ScreenReader.Say(line);
        }
        catch (Exception ex)
        {
            _log?.LogError($"[Map] Reading the travel map failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>
    /// The stones the map drew as pressable. <c>IsInteractable</c> is the map's own verdict - it is
    /// false for a stone not yet activated, for the stone underfoot, and for every stone when the
    /// map was opened only to look at - so nothing locked can slip into the list.
    /// </summary>
    private static void Collect(UIMapWindow window)
    {
        _stones.Clear();

        var created = CreatedMilestones(window.MapPageWidget);
        if (created == null) return;

        foreach (var stone in created.Values)
        {
            if (stone == null || !stone.gameObject.activeSelf || !stone.IsInteractable) continue;
            if (stone.WgoData == null || stone.WgoData.IsHidden) continue;
            _stones.Add(stone);
        }

        _stones.Sort((a, b) => string.Compare(StoneName(a), StoneName(b), StringComparison.CurrentCultureIgnoreCase));
    }

    /// <summary>
    /// The map on top, when it is the one a travel stone opened. Null otherwise, and the keys fall
    /// through to everything else.
    /// </summary>
    private static UIMapWindow ActiveMap() =>
        LazyWindowsStackController.ActiveWindow is UIMapWindow map && map.IsShown ? map : null;

    /// <summary>
    /// Arrows, letters, Enter and Escape while the map is open. Returns true when it took the key.
    /// </summary>
    internal static bool TryKeys(bool enter)
    {
        var map = ActiveMap();
        if (map == null) return false;

        try
        {
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                // The game closes the window on Back by itself; this only says that it did, since
                // the window-closed announcement is held back for windows that speak for themselves.
                ScreenReader.Say(Loc.Get("map.closed"));
                return false;
            }

            if (_stones.Count == 0)
            {
                if (enter) ScreenReader.Say(Loc.Get(_readData != null && !_readData.MilestonesInteractable ? "map.view_only" : "map.none"));
                return enter;
            }

            if (enter)
            {
                Travel(map);
                return true;
            }

            if (Input.GetKeyDown(KeyCode.DownArrow) || Input.GetKeyDown(KeyCode.RightArrow)) { Move(map, 1); return true; }
            if (Input.GetKeyDown(KeyCode.UpArrow) || Input.GetKeyDown(KeyCode.LeftArrow)) { Move(map, -1); return true; }
            if (Input.GetKeyDown(KeyCode.Home)) { Select(map, 0); return true; }
            if (Input.GetKeyDown(KeyCode.End)) { Select(map, _stones.Count - 1); return true; }

            var typed = Input.inputString;
            if (!string.IsNullOrEmpty(typed) && char.IsLetter(typed[0]))
                return JumpToLetter(map, typed[0]);

            return false;
        }
        catch (Exception ex)
        {
            _log?.LogError($"[Map] Key handling on the travel map failed: {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    /// <summary>The repeat key: the destination the cursor is on, or the whole opening line again.</summary>
    internal static bool TryRepeat()
    {
        if (ActiveMap() == null) return false;

        if (_hasNavigated && _stones.Count > 0)
            ScreenReader.Say(Entry(_stones[Mathf.Clamp(_index, 0, _stones.Count - 1)]));
        else if (_stones.Count > 0)
            ScreenReader.Say(Loc.Fmt("map.count", _stones.Count, Entry(_stones[0])));
        else
            ScreenReader.Say(Loc.Get(_readData != null && !_readData.MilestonesInteractable ? "map.view_only" : "map.none"));
        return true;
    }

    /// <summary>
    /// One step through the list. The first press says where the cursor already is - the opening
    /// line named the first destination, but not that Enter would go there.
    /// </summary>
    private static void Move(UIMapWindow map, int delta)
    {
        var target = _hasNavigated ? ((_index + delta) % _stones.Count + _stones.Count) % _stones.Count : _index;
        Select(map, target);
    }

    private static bool JumpToLetter(UIMapWindow map, char letter)
    {
        letter = char.ToUpperInvariant(letter);
        for (var step = 1; step <= _stones.Count; step++)
        {
            var i = (_index + step) % _stones.Count;
            var name = StoneName(_stones[i]);
            if (!string.IsNullOrEmpty(name) && char.ToUpperInvariant(name[0]) == letter)
            {
                Select(map, i);
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Lands on a destination and says it. Also hands it to the map as the hovered stone, so the
    /// picture highlights it and the game's own Select key (Space) presses the same one Enter does.
    /// </summary>
    private static void Select(UIMapWindow map, int index)
    {
        _hasNavigated = true;
        _index = Mathf.Clamp(index, 0, _stones.Count - 1);
        var stone = _stones[_index];

        // OnExitMapMilestone dereferences the current selection before checking it, so it is only
        // called when there is one.
        var widget = map.MapPageWidget;
        if (widget.CurrentSelected != null && widget.CurrentSelected != stone)
            map.OnExitMapMilestone(widget.CurrentSelected);
        if (widget.CurrentSelected != stone)
            map.OnEnterMapMilestone(stone);

        ScreenReader.Say(Entry(stone));
    }

    private static void Travel(UIMapWindow map)
    {
        var stone = _stones[Mathf.Clamp(_index, 0, _stones.Count - 1)];
        if (stone == null || !stone.IsInteractable)
        {
            ScreenReader.Say(Loc.Get("map.none"));
            return;
        }

        var name = StoneName(stone);
        _log?.LogInfo($"[Map] Travelling to '{stone.WgoData.id}' ({name}).");
        ScreenReader.Say(Loc.Fmt("map.travel", name));

        _readData = null;
        map.MapPageWidget.OnPressMapMilestone(stone);
    }

    /// <summary>"Quarry, 240 metres: 200 north, 130 west" - the direction is where the stone lies from here.</summary>
    private static string Entry(UIMapMilestone stone)
    {
        var name = StoneName(stone);
        var player = MainGame.PlayerController;
        if (player == null) return name;

        var from = player.transform.position;
        var to = stone.WgoData.Position;
        var offset = new Vector2(to.x - from.x, to.z - from.z);
        return Loc.Fmt("nav.object", name, Mathf.RoundToInt(offset.magnitude), Navigator.KeyDirections(offset));
    }

    private static string StoneName(UIMapMilestone stone) => StoneName(stone.WgoData.id, stone.WgoData);

    /// <summary>
    /// The place a stone stands at. Two stones (9 and 12) are both in the flooded quarter, and two
    /// (15, 16) have no place in their id at all - those fall back to the zone the stone belongs
    /// to, then to their number. The distance and direction said with each entry tell the pairs
    /// apart.
    /// </summary>
    private static string StoneName(string id, WgoData wgo)
    {
        var place = ObjectNames.MilestonePlace(id);
        if (place != null) return place;

        var zone = wgo?.WorldZoneData?.id;
        if (!string.IsNullOrEmpty(zone))
        {
            var zoneName = TmpText.Clean(LLBase.L("wz_" + zone));
            if (!string.IsNullOrWhiteSpace(zoneName) && zoneName != "wz_" + zone) return zoneName;
        }

        return Loc.Fmt("obj.milestone_number", ObjectNames.MilestoneNumber(id));
    }
}
