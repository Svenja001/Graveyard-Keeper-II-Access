namespace GraveyardKeeper2Accessibility;

/// <summary>
/// Finding things, and choosing which thing to walk to.
///
/// <para>
/// The interaction design is carried over from the Graveyard Keeper mod, deliberately unchanged,
/// because a player who has learned it there should not have to learn it twice: Page down and
/// Page up step through the objects in a category, Ctrl with them steps through the categories,
/// Home says the selection again, and Ctrl+Home walks there.
/// </para>
///
/// <para>
/// What <i>is</i> different is where the categories come from. In GK1 they had to be
/// reverse-engineered from tool keywords; GK2 states them outright in
/// <c>WGODef.InteractionType</c>, a forty-seven value enum that classifies every object in the
/// game by what can be done with it. The category list is built from the objects actually loaded
/// around the player, so it never offers an empty category.
/// </para>
///
/// <para>
/// Nothing sweeps the scene. <c>Wgo</c> raises static spawn and destroy events, so the mod keeps a
/// live set and only ever measures distances on a keypress - which is the answer to GK1's worst
/// performance problem, where scanning the world every frame was the single largest cost.
/// </para>
/// </summary>
internal static class Navigator
{
    private static ManualLogSource _log;

    private static ConfigEntry<KeyboardShortcut> _nextObjectKey;
    private static ConfigEntry<KeyboardShortcut> _prevObjectKey;
    private static ConfigEntry<KeyboardShortcut> _nextCategoryKey;
    private static ConfigEntry<KeyboardShortcut> _prevCategoryKey;
    private static ConfigEntry<KeyboardShortcut> _saySelectionKey;
    private static ConfigEntry<KeyboardShortcut> _walkToSelectionKey;
    private static ConfigEntry<KeyboardShortcut> _objectiveKey;
    private static ConfigEntry<KeyboardShortcut> _storyStepsKey;

    /// <summary>
    /// The order categories are offered in, most needed first. Anything not named here follows,
    /// alphabetically. The player spends most of their time looking for the thing the story wants,
    /// then for a person, a way through or a landmark, then for story props and things to gather -
    /// so those are one or two presses away instead of somewhere in a list of forty. The mod's own
    /// groups (people, doors, plants, ...) come from <see cref="CategoryOf"/>.
    /// </summary>
    private static readonly string[] CategoryPriority =
    {
        "story", "fight_points", "wgo.Flag", "wgo.FlagStand", "wgo.Barricade", "wgo.FightBuilder", "wgo.FighterContainer",
        "people", "doors", "named", "desks", "wgo.Script", "wgo.CustomInteraction", "drops",
        "plants", "trees", "rocks", "loot", "obstacles", "wgo.Grave", "graves_decorate", "wgo.Craft", "repairs", "wgo.Chest",
        "wgo.Work", "ladders", "enemies", "zones",
    };

    /// <summary>Everything currently spawned. Maintained by events, never scanned.</summary>
    private static readonly HashSet<Wgo> Spawned = new();

    /// <summary>Every live object view, for other components that need to look around the player.</summary>
    internal static IEnumerable<Wgo> SpawnedWgos => Spawned;

    /// <summary>
    /// Only objects this far away are offered. Generous rather than tight: the point of the
    /// category list is to reach things that are not already in front of you, and the world is
    /// chunk-streamed anyway, so what is loaded is already the practical limit.
    /// </summary>
    private const float NavRange = 250f;

    private static readonly List<string> Categories = new();
    private static readonly List<NavTarget> Objects = new();

    /// <summary>Rebuilt once per keypress and reused, rather than re-walked for every list.</summary>
    private static readonly List<NavTarget> AllTargets = new();
    private static int _categoryIndex = -1;
    private static int _objectIndex = -1;

    internal static void Init(ManualLogSource log, ConfigFile config)
    {
        _log = log;

        _nextObjectKey = ModKeys.Bind(config, "Keys", "NextObject", new KeyboardShortcut(KeyCode.PageDown),
            "Next object in the current category.");
        _prevObjectKey = ModKeys.Bind(config, "Keys", "PreviousObject", new KeyboardShortcut(KeyCode.PageUp),
            "Previous object in the current category.");
        _nextCategoryKey = ModKeys.Bind(config, "Keys", "NextCategory", new KeyboardShortcut(KeyCode.PageDown, KeyCode.LeftControl),
            "Next category of objects.");
        _prevCategoryKey = ModKeys.Bind(config, "Keys", "PreviousCategory", new KeyboardShortcut(KeyCode.PageUp, KeyCode.LeftControl),
            "Previous category of objects.");
        _saySelectionKey = ModKeys.Bind(config, "Keys", "SaySelection", new KeyboardShortcut(KeyCode.Home),
            "Says the selected object again - its state, distance and direction - and how long the way there really is.");
        _walkToSelectionKey = ModKeys.Bind(config, "Keys", "WalkToSelection", new KeyboardShortcut(KeyCode.Home, KeyCode.LeftControl),
            "Walks to the selected object. Press again, or any movement key, to stop.");
        _objectiveKey = ModKeys.Bind(config, "Keys", "Objective", new KeyboardShortcut(KeyCode.F3),
            "Says where the current objective is - whatever the game's tutorial arrow points at.");
        _storyStepsKey = ModKeys.Bind(config, "Keys", "StorySteps", new KeyboardShortcut(KeyCode.End),
            "Jumps straight to the list of things the story is waiting for you to use, and says the nearest.");

        try
        {
            Wgo.OnWgoSpawn += OnSpawn;
            Wgo.OnWgoDestroy += OnDestroy;
            WgoData.OnAnyInteractionEventChanged -= OnStoryEventChanged;
            WgoData.OnAnyInteractionEventChanged += OnStoryEventChanged;
            _log.LogInfo("[Nav] Watching object spawns.");
        }
        catch (Exception ex)
        {
            _log.LogError($"[Nav] Could not subscribe to object events: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static void OnSpawn(Wgo wgo)
    {
        if (wgo != null) Spawned.Add(wgo);
    }

    /// <summary>
    /// The story has added a step. Forget the current category, so the next Page down starts at
    /// the top of the list - which is the story steps - rather than wherever the player was last
    /// browsing. Silent: the flowscript is usually mid-dialogue when it does this.
    /// </summary>
    private static void OnStoryEventChanged(WgoData data)
    {
        try
        {
            if (data == null || !HasStoryEvent(data)) return;
            _categoryIndex = -1;
            _objectIndex = -1;
        }
        catch (Exception ex)
        {
            _log?.LogWarning($"[Nav] Story step tracking failed: {ex.Message}");
        }
    }

    private static void OnDestroy(Wgo wgo)
    {
        if (wgo != null) Spawned.Remove(wgo);
    }

    internal static void Update()
    {
        try
        {
            // Modified shortcuts are tested first. BepInEx only reports a plain shortcut as down
            // when no modifier is held, so the order is belt and braces rather than necessity.
            if (Down(_nextCategoryKey)) { StepCategory(1); return; }
            if (Down(_prevCategoryKey)) { StepCategory(-1); return; }
            if (Down(_walkToSelectionKey)) { WalkToSelection(); return; }
            if (Down(_nextObjectKey)) { StepObject(1); return; }
            if (Down(_prevObjectKey)) { StepObject(-1); return; }
            if (Down(_saySelectionKey)) { SaySelection(full: true); return; }
            if (Down(_storyStepsKey)) { JumpToStorySteps(); return; }
            if (Down(_objectiveKey)) SayObjective();
        }
        catch (Exception ex)
        {
            _log?.LogError($"[Nav] Key handling failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static bool Down(ConfigEntry<KeyboardShortcut> key) => key != null && key.Value.IsDown();

    // ---- selection -------------------------------------------------------------------------

    private static void StepCategory(int delta)
    {
        if (!TryGetPlayer(out var from, out _)) return;

        RebuildCategories(from);
        if (Categories.Count == 0)
        {
            ScreenReader.Say(Loc.Get("nav.nothing_nearby"));
            return;
        }

        _categoryIndex = Wrap(_categoryIndex < 0 ? 0 : _categoryIndex + delta, Categories.Count);
        RebuildObjects(from);
        _objectIndex = Objects.Count > 0 ? 0 : -1;

        // The category and its first object are said together, so one keypress answers both
        // "what is this category" and "where is the nearest one".
        var name = CategoryName(Categories[_categoryIndex]);
        var line = Loc.Fmt("nav.category", name, Objects.Count);

        var selection = SelectionText(from);
        ScreenReader.Say(selection == null ? line : $"{line}. {selection}");
        RouteInfo.Probe(Selected(), full: false);
    }

    /// <summary>
    /// Straight to "next story steps", from anywhere in the lists. When the story is waiting for
    /// nothing, says the current task instead, so the key always answers "what now?".
    /// </summary>
    private static void JumpToStorySteps()
    {
        if (!TryGetPlayer(out var from, out _)) return;

        RebuildCategories(from);
        var index = Categories.IndexOf("story");
        if (index < 0)
        {
            var task = QuestReader.CurrentTask();
            var none = Loc.Fmt("nav.category_empty", Loc.Get("nav.cat.Story"));
            ScreenReader.Say(task == null ? none : $"{none}. {Loc.Fmt("nav.task", task)}");
            return;
        }

        _categoryIndex = index;
        _objectIndex = -1;
        StepCategory(0);
    }

    private static void StepObject(int delta)
    {
        if (!TryGetPlayer(out var from, out _)) return;

        // Paging objects before ever choosing a category starts in the first one, so the feature
        // is usable without knowing the category keys exist.
        if (_categoryIndex < 0)
        {
            StepCategory(0);
            return;
        }

        RebuildCategories(from);
        if (Categories.Count == 0)
        {
            ScreenReader.Say(Loc.Get("nav.nothing_nearby"));
            return;
        }

        _categoryIndex = Mathf.Clamp(_categoryIndex, 0, Categories.Count - 1);
        RebuildObjects(from);

        if (Objects.Count == 0)
        {
            ScreenReader.Say(Loc.Fmt("nav.category_empty", CategoryName(Categories[_categoryIndex])));
            _objectIndex = -1;
            return;
        }

        _objectIndex = Wrap(_objectIndex < 0 ? 0 : _objectIndex + delta, Objects.Count);
        SaySelection(full: false);
    }

    /// <summary>
    /// Says the selection. <paramref name="full"/> (Home) adds the length of the way there and what
    /// it passes through; paging adds it only when the way is a detour or crosses a story zone.
    /// </summary>
    private static void SaySelection(bool full)
    {
        if (!TryGetPlayer(out var from, out _)) return;

        var selection = SelectionText(from);
        ScreenReader.Say(selection ?? Loc.Get("nav.nothing_selected"));
        if (selection != null) RouteInfo.Probe(Selected(), full);
    }

    /// <summary>
    /// "Furnace, making bronze ingot, 40 percent, 12 metres: 9 north, 8 west, 3 of 7", or null when
    /// nothing is selected. The state (see <see cref="ObjectStatus"/>) is read fresh on every
    /// keypress, so pressing Home again at a busy furnace follows its progress.
    /// </summary>
    private static string SelectionText(Vector3 from)
    {
        var target = Selected();
        if (!target.IsValid) return null;

        if (!TryGetPlayer(out _, out var facing)) return null;

        var status = ObjectStatus.Of(target.Data);
        var name = status == null ? target.Name : $"{target.Name}, {status}";

        return Loc.Fmt("nav.selection",
            Describe(name, target.Position, from, facing),
            _objectIndex + 1,
            Objects.Count);
    }

    /// <summary>The currently selected object, or an invalid target when there is none.</summary>
    internal static NavTarget Selected()
    {
        if (_objectIndex < 0 || _objectIndex >= Objects.Count) return default;
        return Objects[_objectIndex];
    }

    private static void WalkToSelection()
    {
        var target = Selected();
        if (!target.IsValid)
        {
            ScreenReader.Say(Loc.Get("nav.nothing_selected"));
            return;
        }

        AutoWalk.WalkTo(target);
    }

    private static void SayObjective()
    {
        if (!TryGetPlayer(out var from, out var facing)) return;

        var target = ObjectiveTarget();
        if (target.IsValid)
        {
            var status = ObjectStatus.Of(target.Data);
            var name = status == null ? target.Name : $"{target.Name}, {status}";
            ScreenReader.Say(Loc.Fmt("nav.objective", Describe(name, target.Position, from, facing)));
            RouteInfo.Probe(target, full: false);
            return;
        }

        // No arrow is showing. That is not the same as having nothing to do - the arrow is only
        // attached at scripted moments - so fall back to what the game says the current task is.
        // "Let us get out of here" is a direction a sighted player reads off the quest entry.
        var task = QuestReader.CurrentTask();
        ScreenReader.Say(task == null ? Loc.Get("nav.no_objective") : Loc.Fmt("nav.task", task));
    }

    // ---- the lists -------------------------------------------------------------------------

    /// <summary>
    /// Everything worth offering, gathered once per keypress.
    ///
    /// <para>
    /// <b>Three sources, because "the thing I am looking for" is not one kind of object.</b> An
    /// earlier version listed only objects with a non-<c>None</c> interaction type, and a player
    /// sent to gather mushrooms could not find a single one - mushrooms lie on the ground as
    /// <c>DropData</c>, not as interactable world objects. The same omission hid named landmarks
    /// and characters, which are frequently <c>None</c> because you walk up to them rather than
    /// operate them.
    /// </para>
    ///
    /// <para>
    /// So: interactables by their type, items on the ground, and anything else whose id resolves
    /// to a real name. That last test is the cheap way to separate a landmark from scenery -
    /// <c>LLBase.L</c> returns the id unchanged when there is no entry for it, so a name that
    /// differs from its id is a thing the game itself considers worth naming.
    /// </para>
    /// </summary>
    private static void RebuildTargets(Vector3 from)
    {
        AllTargets.Clear();
        RefreshQuestTokens();
        Reachability.Refresh(NavRange);
        _hiddenCount = 0;

        foreach (var wgo in Spawned)
        {
            // Unity objects compare equal to null once destroyed even while still in the set - a
            // destroy event can be missed when a whole scene chunk unloads at once.
            if (wgo == null) continue;

            var data = wgo.Data;
            if (data == null || data.Definition == null) continue;
            if (Flat(data.Position - from).magnitude > NavRange) continue;

            // Position, not BubblePos. BubblePos is the speech-bubble anchor - the object's
            // position plus an upward offset - so it hangs in the air above the thing. Asking the
            // pathfinder to walk to a point floating above the floor fails its reachability test,
            // which is silent, and the player is told "walking to X" and then simply never moves.
            // Listed a second time, on its own, when the story is waiting for the player to use it.
            if (IsStoryStep(data) && !Parked(data))
                AllTargets.Add(new NavTarget(data.id, data.Position, data.WorldId, "story", data: data));

            // Everything below is left out while it cannot be used or reached - only the story
            // list keeps it, since that is where the player finds out what to open next.
            if (!Available(data)) continue;

            // The flag in the player's hand is still a world object at the spot it was taken from.
            if (ReferenceEquals(wgo, MilitaryReader.CarriedFlag())) continue;

            if (AddGrave(data)) continue;

            var type = data.Definition.interactionType;
            if (type == WGODef.InteractionType.None && !HasRealName(data.id) && !IsExtension(data.id)) continue;

            AllTargets.Add(new NavTarget(data.id, data.Position, data.WorldId, CategoryOf(data.id, data.Definition), data: data));
        }

        AddGroundDrops(from);
        AddStoryZones(from);
        AddCapturePoints(from);
        AddFarStorySteps();
    }

    /// <summary>How many objects the last rebuild left out as unusable or out of reach, for the log.</summary>
    private static int _hiddenCount;

    /// <summary>
    /// Put away by the story (see <see cref="Reachability.IsParked"/>): left out everywhere, the
    /// story list and the objective included, since no one can walk to it.
    /// </summary>
    private static bool Parked(WgoData data)
    {
        if (!Reachability.IsParked(data)) return false;
        if (LoggedHidden.Add($"{data.id}@{Mathf.RoundToInt(data.Position.x)},{Mathf.RoundToInt(data.Position.z)}:parked"))
            _log?.LogInfo($"[Nav] Left out '{data.id}' at {data.Position}: switched off or parked off the map by the story ({(data.IsHidden ? "hidden" : Reachability.ParkedDetail)}).");
        return true;
    }

    /// <summary>
    /// True when the player could use this object now, or at least walk up to it.
    ///
    /// <para>
    /// <b>Reported (2026-09-26):</b> many categories held only things that were not unlocked or not
    /// reachable yet. Three tests, all the game's own: <c>IsHidden</c> (switched off by the story -
    /// the workshop conveyors after the intro, fighters not in play), <c>IsInteractable</c> (the
    /// flag the game's own interaction finder skips on; landmarks with no interaction are not held
    /// to it), and whether the navmesh connects the player to it (see <see cref="Reachability"/>).
    /// </para>
    /// </summary>
    private static bool Available(WgoData data)
    {
        if (Parked(data)) return false;

        string why = null;
        if (data.IsHidden) why = "hidden";
        else if (!data.IsInteractable && data.Definition.interactionType != WGODef.InteractionType.None && !IsExtension(data.id))
            why = "not interactable";
        else if (!Reachability.CanReach(data.Position)) why = "out of reach";
        if (why == null) return true;

        _hiddenCount++;

        // Things close by are the ones a player notices missing; say which and why, once each.
        if (TryGetPlayer(out var from, out _, quiet: true) && Flat(data.Position - from).magnitude < 15f
            && LoggedHidden.Add($"{data.id}@{Mathf.RoundToInt(data.Position.x)},{Mathf.RoundToInt(data.Position.z)}:{why}"))
            _log?.LogInfo($"[Nav] Left out '{data.id}' at {data.Position}: {why}.");
        return false;
    }

    private static readonly HashSet<string> LoggedHidden = new();

    private static bool IsExtension(string id)
    {
        try { return !string.IsNullOrEmpty(id) && GameBalance.Me.IsWorkbenchExtensionId(id); }
        catch { return false; }
    }

    /// <summary>
    /// A grave in any state, listed under graves with what state it is in. False for anything else.
    ///
    /// <para>
    /// <b>A grave is three different objects over its life</b>, and only the last is typed
    /// <c>Grave</c>, so the list of graves showed only filled ones. Read out of <c>GameBalance</c>
    /// (2026-09-25): a site marked at the blueprint desk is <c>grave_empty_place</c>, typed
    /// <c>Work</c> (dug with the shovel) - and filed under building spots, because of the
    /// "_place". Dug, it becomes <c>grave_empty</c>, typed <c>CustomInteraction</c>, and sat
    /// among "special objects". Only the burial turns it into <c>grave_ground</c>, the <c>Grave</c>.
    /// </para>
    ///
    /// <para>
    /// <b>Graves to decorate</b> get their own list as well, as in GK1: a grave with a body but no
    /// headstone or no fence. The parts are <c>AdditionalWgoPartsData</c> whose item is in the
    /// "gravetop" or "gravebot" group - the same test the grave window uses to refuse exhuming.
    /// </para>
    /// </summary>
    private static bool AddGrave(WgoData data)
    {
        var id = data.id;
        if (string.IsNullOrEmpty(id)) return false;

        string label;
        if (id.StartsWith("grave_empty_place", StringComparison.OrdinalIgnoreCase))
            label = Loc.Get("grave.marked");
        else if (id.StartsWith("grave_empty", StringComparison.OrdinalIgnoreCase))
            label = Loc.Get("grave.dug");
        else if (data.Definition.interactionType == WGODef.InteractionType.Grave)
        {
            var hasBody = GraveHasBody(data);
            var needs = GraveNeeds(data);
            label = Loc.Get(hasBody ? "grave.with_body" : "grave.no_body");
            if (needs != null) label = $"{label}, {needs}";

            if (hasBody && needs != null)
                AllTargets.Add(new NavTarget(id, data.Position, data.WorldId, "graves_decorate", label, data));
        }
        else return false;

        AllTargets.Add(new NavTarget(id, data.Position, data.WorldId, "wgo." + WGODef.InteractionType.Grave, label, data));
        return true;
    }

    private static bool GraveHasBody(WgoData data)
    {
        try
        {
            var items = data.Inventory?.Data?.Inventory;
            if (items == null) return false;
            foreach (var item in items)
            {
                var groups = item?.Definition?.itemGroupIds;
                if (groups != null && groups.Contains("body")) return true;
            }
        }
        catch (Exception ex)
        {
            _log?.LogWarning($"[Nav] Could not read a grave's body: {ex.Message}");
        }
        return false;
    }

    /// <summary>"needs a headstone and a fence", "needs a fence", or null when both are there.</summary>
    private static string GraveNeeds(WgoData data)
    {
        bool top = false, bot = false;
        try
        {
            var parts = data.AdditionalWgoPartsData;
            if (parts != null)
            {
                foreach (var part in parts)
                {
                    if (part == null) continue;
                    var def = GameBalance.Me.GetDataOrNull<ItemDef>(part.id);
                    if (def?.itemGroupIds == null) continue;
                    if (def.itemGroupIds.Contains("gravetop")) top = true;
                    if (def.itemGroupIds.Contains("gravebot")) bot = true;
                }
            }
        }
        catch (Exception ex)
        {
            _log?.LogWarning($"[Nav] Could not read a grave's decoration: {ex.Message}");
            return null;
        }

        if (top && bot) return null;
        return Loc.Get(!top && !bot ? "grave.needs_both" : !top ? "grave.needs_top" : "grave.needs_bot");
    }

    /// <summary>
    /// Objects the story waits on that are too far away to have a view - found in the world's own
    /// data instead of among spawned objects.
    ///
    /// <para>
    /// After the chapter 3 retreat the only marker is the door of a home on the far side of the
    /// map ("an abandoned Inquisitor base to the west"). Object views only exist near the camera,
    /// so a list built from them could never contain it. <c>WorldData</c> holds every object of
    /// every loaded scene whether drawn or not. Only the certain signals are used here - a pending
    /// story event or a quest check naming the object - not the quest-name guess, which across the
    /// whole world would match leftovers from earlier chapters.
    /// </para>
    /// </summary>
    private static void AddFarStorySteps()
    {
        foreach (var data in FarStorySteps())
        {
            var target = new NavTarget(data.id, data.Position, data.WorldId, "story", data: data);
            if (!AllTargets.Any(t => t.CategoryKey == "story" && t.SameAs(target))) AllTargets.Add(target);
        }
    }

    private static IEnumerable<WgoData> FarStorySteps()
    {
        var world = MainGame.WorldData;
        if (world == null || !world.HasCache) yield break;

        var scenes = world.LoadedScenes;
        foreach (var data in world.Cache.wgoDataByUidCache.Values)
        {
            if (data == null || data.Definition == null) continue;
            if (scenes != null && !scenes.Contains(data.WorldId)) continue;
            if ((HasStoryEvent(data) || StoryListeners.AwaitsWgo(data)) && !Parked(data)) yield return data;
        }
    }

    /// <summary>
    /// The invisible trigger volumes that drive the story - <c>GDZone</c>s.
    ///
    /// <para>
    /// <b>These are progression gates with no physical presence at all.</b> Walking into one runs a
    /// flowscript: an NPC remarks on something, a door unlocks, the next beat begins. A sighted
    /// player crosses them without ever knowing they exist, because they walk through space rather
    /// than from object to object. A player navigating by a list of objects can walk past one
    /// forever and never enter it - which is exactly how the prison intro deadlocks, with an exit
    /// that does nothing because the hint that arms it was never triggered.
    /// </para>
    ///
    /// <para>
    /// Only zones that actually run something are listed, and they are found on a keypress rather
    /// than tracked, because they have no registry and no spawn event. A scan per keypress is
    /// affordable; a scan per frame would not be.
    /// </para>
    /// </summary>
    /// <summary>
    /// The capture points of the fight being prepared or fought: where squads hold a line and where
    /// the base is. They are scene components, not world objects, so nothing else lists them.
    /// </summary>
    private static void AddCapturePoints(Vector3 from)
    {
        try
        {
            foreach (var point in FightAnnouncer.ActiveCapturePoints())
            {
                if (point == null) continue;
                var position = point.transform.position;
                if (Flat(position - from).magnitude > NavRange) continue;

                var owner = point.OwnedByTeam == LazyConsts.Fighting.TeamType.Player ? Loc.Get("mil.owner_ours") : Loc.Get("mil.owner_theirs");
                var label = Loc.Fmt("mil.point_label", MilitaryReader.PointName(point), owner, Mathf.RoundToInt(100f * point.CurrentProgress));
                if (point.enemies.Count > 0 || point.allies.Count > 0)
                    label = $"{label}, {Loc.Fmt("mil.point_occupants", point.allies.Count, point.enemies.Count)}";
                AllTargets.Add(new NavTarget("capture_point", position, null, "fight_points", label));
            }
        }
        catch (Exception ex)
        {
            _log?.LogWarning($"[Nav] Could not read capture points: {ex.Message}");
        }
    }

    private static void AddStoryZones(Vector3 from)
    {
        try
        {
            var zones = UnityEngine.Object.FindObjectsByType<GDZone>(FindObjectsSortMode.None);
            if (zones == null) return;

            foreach (var zone in zones)
            {
                if (zone == null || !zone.isActiveAndEnabled || IsAmbience(zone)) continue;

                // A tag alone does nothing unless a quest is listening for it right now; listing
                // those sent the player to zones that could not respond.
                var awaited = StoryListeners.AwaitsZone(zone);
                if (!awaited && !HasAnyScript(zone)) continue;

                // The middle of the volume, so walking there is certain to cross the boundary
                // rather than clip its edge.
                var collider = zone.GetComponent<Collider>();
                var position = collider != null ? collider.bounds.center : zone.transform.position;
                if (Flat(position - from).magnitude > NavRange) continue;

                var tag = string.IsNullOrEmpty(zone.customTag) ? zone.name : zone.customTag;
                AllTargets.Add(new NavTarget(Humanise(tag), position, null, "zones", zone: zone));
                if (awaited) AllTargets.Add(new NavTarget(Humanise(tag), position, null, "story", zone: zone));
            }
        }
        catch (Exception ex)
        {
            _log?.LogWarning($"[Nav] Could not read story zones: {ex.Message}");
        }
    }

    /// <summary>
    /// True when entering, leaving or crossing this zone runs a flowscript of its own. Most story
    /// zones have none and work through their <c>customTag</c> instead, which only matters while a
    /// quest listens for it - see <see cref="StoryListeners"/>.
    /// </summary>
    internal static bool HasAnyScript(GDZone zone)
    {
        return HasScript(zone.onEnter) || HasScript(zone.onExit)
            || HasScript(zone.onCrossedToLeft) || HasScript(zone.onCrossedToRight)
            || HasScript(zone.onCrossedToUp) || HasScript(zone.onCrossedToDown);
    }

    private static bool HasScript(GDZone.GDZoneEvent e) => e != null && e.flowScript != null;

    /// <summary>
    /// Zones that only switch sound or music. They run flowscripts too, so the script test alone
    /// lets them through - and "Sound Conveyors", a hundred metres away in a place the story had
    /// not opened yet, was offered as a story trigger and walked to from inside the prison.
    /// </summary>
    internal static bool IsAmbience(GDZone zone)
    {
        var tag = string.IsNullOrEmpty(zone.customTag) ? zone.name : zone.customTag;
        if (LooksLikeAmbience(tag)) return true;

        return LooksLikeAmbience(ScriptName(zone.onEnter)) || LooksLikeAmbience(ScriptName(zone.onExit));
    }

    private static string ScriptName(GDZone.GDZoneEvent e) => HasScript(e) ? e.flowScript.name : null;

    private static bool LooksLikeAmbience(string name)
    {
        if (string.IsNullOrEmpty(name)) return false;
        var lower = name.ToLowerInvariant();
        return lower.Contains("sound") || lower.Contains("music") || lower.Contains("ambien")
            || lower.Contains("_amb") || lower.StartsWith("amb", StringComparison.Ordinal)
            || lower.Contains("audio") || lower.Contains("snapshot");
    }

    /// <summary>
    /// "GDZone_Intro_Prison_Note" -> "Intro Prison Note". These names are developer tags, not
    /// translated text, so this is the best that can be done - and a rough name is far better than
    /// omitting a zone the story will not continue without.
    /// </summary>
    internal static string Humanise(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return raw;

        if (raw.StartsWith("GDZone_", StringComparison.OrdinalIgnoreCase)) raw = raw.Substring(7);
        else if (raw.StartsWith("GDZone", StringComparison.OrdinalIgnoreCase)) raw = raw.Substring(6);

        return raw.Replace('_', ' ').Replace('-', ' ').Trim();
    }

    /// <summary>
    /// Items lying in the world. They are not <c>Wgo</c>s and raise no spawn event, so they are
    /// read from the loaded scenes' own drop lists - still no scanning of the scene graph, just a
    /// walk of a list the game already maintains.
    /// </summary>
    private static void AddGroundDrops(Vector3 from)
    {
        try
        {
            var manager = LazySingleton<GameSceneManager>.Instance;
            var scenes = manager == null ? null : manager.LoadedGameScenes;
            if (scenes == null) return;

            foreach (var scene in scenes)
            {
                var sceneData = scene == null ? null : scene.GameSceneData;
                if (sceneData == null || sceneData.droppedItems == null) continue;

                foreach (var drop in sceneData.droppedItems)
                {
                    if (drop == null || drop.IsRemoving || string.IsNullOrEmpty(drop.Id)) continue;
                    if (Flat(drop.Position - from).magnitude > NavRange) continue;
                    if (!Reachability.CanReach(drop.Position)) { _hiddenCount++; continue; }

                    AllTargets.Add(new NavTarget(drop.Id, drop.Position, drop.WorldId, "drops"));
                }
            }
        }
        catch (Exception ex)
        {
            _log?.LogWarning($"[Nav] Could not read ground items: {ex.Message}");
        }
    }

    /// <summary>
    /// True when a flowscript is waiting for the player to use this object.
    ///
    /// <para>
    /// <b>This is the game's own "do this next".</b> When the story wants an interaction, the
    /// flowscript calls <c>WgoData.AddInteractionEvent</c> on the object - the prison logs
    /// "Added interaction event: work, wgo: intro_prison_blockage" the moment Larry says it is time
    /// to leave - and the event is consumed when the player uses it. It persists in the save and
    /// needs no tutorial arrow, so it is the most reliable pointer the mod has.
    /// </para>
    /// </summary>
    private static bool HasStoryEvent(WgoData data)
    {
        var events = data.Events;
        return events != null && events.Count > 0;
    }

    /// <summary>
    /// A story event on the object, or an object the quest in progress is plainly about.
    ///
    /// <para>
    /// <b>Not every step is marked with an event.</b> "Clear the path" after the prison waits for
    /// <c>intro_scout_barricade_weak</c> to be <i>destroyed</i>, so nothing is added to it and the
    /// events test alone left the list empty while the player stood next to a half-broken barricade.
    /// The quest in progress was <c>2_intro_scout_barricade_with_axe</c>: the developers name story
    /// objects after the beat they belong to, so an object id that shares the quest's chapter
    /// prefix (the first two words) and at least one more word with it is the thing the quest means.
    /// </para>
    /// </summary>
    private static bool IsStoryStep(WgoData data)
    {
        if (HasStoryEvent(data) || StoryListeners.AwaitsWgo(data)) return true;
        if (string.IsNullOrEmpty(data.id) || QuestTokens.Count == 0) return false;

        var words = data.id.ToLowerInvariant().Split('_');
        if (words.Length < 3) return false;

        foreach (var quest in QuestTokens)
        {
            if (quest.Length < 3 || quest[0] != words[0] || quest[1] != words[1]) continue;

            for (var i = 2; i < words.Length; i++)
            {
                if (Array.IndexOf(quest, words[i], 2) >= 0) return true;
            }
        }

        return false;
    }

    /// <summary>Words of each quest in progress, leading chapter number dropped. Rebuilt per keypress.</summary>
    private static readonly List<string[]> QuestTokens = new();

    private static readonly HashSet<string> QuestNoise = new(StringComparer.Ordinal)
    {
        "with", "without", "the", "a", "to", "no", "and", "removed", "find", "take", "go", "leave",
    };

    private static void RefreshQuestTokens()
    {
        QuestTokens.Clear();
        try
        {
            var quests = QuestReader.ActiveQuests();
            if (quests == null) return;

            foreach (var quest in quests)
            {
                if (quest == null || quest.status != QuestStatus.InProgress || string.IsNullOrEmpty(quest.id)) continue;

                var words = quest.id.ToLowerInvariant().Split('_').ToList();
                if (words.Count > 0 && words[0].All(char.IsDigit)) words.RemoveAt(0);
                if (words.Count < 3) continue;

                // The chapter prefix is kept whatever it is; only the words after it are filtered.
                var kept = words.Take(2).Concat(words.Skip(2).Where(w => !QuestNoise.Contains(w))).ToArray();
                if (kept.Length >= 3) QuestTokens.Add(kept);
            }
        }
        catch (Exception ex)
        {
            _log?.LogWarning($"[Nav] Could not read quests for story steps: {ex.Message}");
        }
    }

    /// <summary>True when the game has a name for this id, rather than just the id itself.</summary>
    private static bool HasRealName(string id)
    {
        if (string.IsNullOrEmpty(id)) return false;
        var name = LLBase.L(id);
        return !string.IsNullOrEmpty(name) && name != id;
    }

    /// <summary>
    /// Which list an object belongs in: what it <i>is</i>, where that can be told, and the game's
    /// own interaction type otherwise.
    ///
    /// <para>
    /// <b>The game's type says how an object is used, not what it is</b>, and the two diverge badly
    /// for a player choosing where to walk. Read out of <c>GameBalance</c> (2026-09-25): <c>Work</c>
    /// holds every tree, bush, mushroom, stone, ore vein, blockage and looter's chest - anything
    /// hit with a tool - so "work stations" was a heap of mushrooms. Doors are spread over four
    /// types: broken and locked ones are <c>Craft</c> (they are repaired), the cellar door is
    /// <c>Script</c>, and ordinary doorways (<c>tp_home_main_entrance_inside</c>) are teleports
    /// typed <c>CustomInteraction</c>. People are <c>Script</c>, so "story objects" was mostly NPCs.
    /// </para>
    ///
    /// <para>
    /// The developers' ids are consistent words joined by underscores, so whole words are matched
    /// - "stone" must not catch "millstone". Rules for what an object is apply to any type; the
    /// rules that split up <c>Work</c> apply only to it, because "stone_container" is a chest and
    /// "millstone" is a workstation.
    /// </para>
    /// </summary>
    private static string CategoryOf(string id, WGODef def)
    {
        var type = def.interactionType;
        var words = string.IsNullOrEmpty(id) ? Array.Empty<string>() : id.ToLowerInvariant().Split('_');
        bool Has(params string[] any) => any.Any(w => Array.IndexOf(words, w) >= 0);

        if (Has("mob")) return "enemies";

        // Workbench add-ons (the hardening bucket, the bellows) have no action of their own, so
        // their type put them among landmarks; they belong with the workbenches they extend.
        if (IsExtension(id)) return "wgo." + WGODef.InteractionType.Craft;

        // Blueprint desks are typed Builder, like every "*_place" build spot, and were lost among them
        // under "building spots".
        if (ObjectNames.IsBuilderDesk(id)) return "desks";
        if (words.Length > 0 && words[0] == "npc") return "people";

        // Fast travel milestones keep their own list; every other teleport is a doorway between
        // two rooms or areas.
        if (type != WGODef.InteractionType.TeleportMilestone
            && !Has("milestone")
            && ((words.Length > 0 && words[0] == "tp") || Has("door", "doors", "gate", "hatch", "entrance", "teleport")))
            return "doors";

        if (type == WGODef.InteractionType.Ladder || Has("ladder", "stairs", "staircase")) return "ladders";
        // Fight barricades are typed Barricade and hold squad flags; only the story's ones are in the way.
        if (type != WGODef.InteractionType.Barricade && Has("blockage", "barricade", "debris", "blocked", "rubble", "junk")) return "obstacles";

        if (type == WGODef.InteractionType.Work)
        {
            // "wine_barrel_place" is where a wine barrel will be built, not a barrel to smash.
            if (words.Length > 0 && words[words.Length - 1] == "place") return "wgo." + WGODef.InteractionType.Builder;

            // Junk to smash open before the repair rule: "broken_barrel" is loot, not a repair.
            if (Has("looters", "loot", "trash", "barrel", "shelf")) return "loot";
            if (Has("garden", "vineyard") || id.StartsWith("seed_generator", StringComparison.OrdinalIgnoreCase))
                return "wgo." + WGODef.InteractionType.Garden;
        }

        if (type == WGODef.InteractionType.Work || type == WGODef.InteractionType.Craft)
        {
            if (Has("broken", "repairing")) return "repairs";
        }

        if (type == WGODef.InteractionType.Work)
        {
            if (Has("bush", "mushroom", "berry", "berries", "flower", "herb", "honey", "apple", "beeswax"))
                return "plants";

            var tool = def.toolAction == null ? ItemType.None : def.toolAction.actionableTool;
            if (tool == ItemType.Axe || Has("tree", "stump", "driftwood")) return "trees";
            if (tool == ItemType.Pickaxe
                || Has("stone", "stones", "ore", "ores", "vein", "marble", "clay", "sand", "coal", "copper",
                    "iron", "lead", "rock", "boulder", "nugget"))
                return "rocks";
        }

        return type == WGODef.InteractionType.None ? "named" : "wgo." + type;
    }

    /// <summary>
    /// The categories that currently have something in them, in a stable order - a list that
    /// reshuffles as the player walks would make the category keys unusable.
    /// </summary>
    private static void RebuildCategories(Vector3 from)
    {
        RebuildTargets(from);

        var previous = _categoryIndex >= 0 && _categoryIndex < Categories.Count ? Categories[_categoryIndex] : null;

        var present = new HashSet<string>();
        foreach (var target in AllTargets) present.Add(target.CategoryKey);

        Categories.Clear();
        Categories.AddRange(present);
        Categories.Sort(CompareCategories);
        _log?.LogInfo($"[Nav] {AllTargets.Count} objects in {Categories.Count} categories ({string.Join(", ", Categories)}); {_hiddenCount} left out as unusable or out of reach.");

        // Keep pointing at the same category across a rebuild, so walking around does not silently
        // move the selection to a different kind of thing.
        if (previous != null)
            _categoryIndex = Categories.IndexOf(previous);
    }

    /// <summary>Priority categories first, in <see cref="CategoryPriority"/> order; the rest alphabetically.</summary>
    private static int CompareCategories(string a, string b)
    {
        var pa = Array.IndexOf(CategoryPriority, a);
        var pb = Array.IndexOf(CategoryPriority, b);
        if (pa < 0) pa = int.MaxValue;
        if (pb < 0) pb = int.MaxValue;
        return pa != pb ? pa.CompareTo(pb) : StringComparer.Ordinal.Compare(a, b);
    }

    private static void RebuildObjects(Vector3 from)
    {
        var previous = Selected();
        var hadSelection = previous.IsValid;

        Objects.Clear();
        if (_categoryIndex < 0 || _categoryIndex >= Categories.Count) return;

        var wanted = Categories[_categoryIndex];
        foreach (var target in AllTargets)
        {
            if (target.CategoryKey == wanted) Objects.Add(target);
        }

        Objects.Sort((a, b) =>
            Flat(a.Position - from).sqrMagnitude.CompareTo(Flat(b.Position - from).sqrMagnitude));

        // Same reasoning as the category: hold the selection steady across a rebuild where we can.
        if (!hadSelection) return;

        for (var i = 0; i < Objects.Count; i++)
        {
            if (!Objects[i].SameAs(previous)) continue;
            _objectIndex = i;
            return;
        }
    }

    /// <summary>
    /// The closest interactable thing, or an invalid target when there is nothing. Used when
    /// something needs a target and the player has not chosen one.
    /// </summary>
    internal static NavTarget NearestTarget()
    {
        try
        {
            if (!TryGetPlayer(out var from, out _, quiet: true)) return default;

            RebuildTargets(from);

            var best = default(NavTarget);
            var bestDistance = float.MaxValue;

            foreach (var target in AllTargets)
            {
                var distance = Flat(target.Position - from).magnitude;
                if (distance >= bestDistance) continue;

                bestDistance = distance;
                best = target;
            }

            return best;
        }
        catch (Exception ex)
        {
            _log?.LogError($"[Nav] Nearest lookup failed: {ex.GetType().Name}: {ex.Message}");
            return default;
        }
    }

    // ---- wording ---------------------------------------------------------------------------

    /// <summary>"Gravestone, 12 metres: 9 north, 8 west".</summary>
    private static string Describe(string name, Vector3 to, Vector3 from, Vector2 facing)
    {
        var offset = Flat(to - from);

        return Loc.Fmt("nav.object", name, Mathf.RoundToInt(offset.magnitude), KeyDirections(offset));
    }

    /// <summary>
    /// Where something is, as compass directions: "9 north, 8 west". North is W, west is A - the
    /// user asked for compass words rather than up/left.
    ///
    /// <para>
    /// This replaced a clock face relative to the way the character faces. Facing changes with
    /// every step, so "2 o'clock" pointed somewhere new each time the player moved to follow it -
    /// the player reported walking by it and never getting closer. GK2's movement is fixed to the
    /// screen instead: <c>PlayerPhysicalBody.MoveByDirection</c> maps the input (x, y) straight to
    /// world (x, z), so W is always +z and D always +x. Saying the offset in those terms stays true
    /// however the player turns.
    /// </para>
    /// </summary>
    internal static string KeyDirections(Vector2 offset)
    {
        var parts = new List<string>(2);
        var up = Mathf.RoundToInt(offset.y);
        var right = Mathf.RoundToInt(offset.x);

        // The bigger axis first - it is the one to start walking along.
        void AddUp() { if (up != 0) parts.Add(Loc.Fmt(up > 0 ? "nav.dir.up" : "nav.dir.down", Math.Abs(up))); }
        void AddRight() { if (right != 0) parts.Add(Loc.Fmt(right > 0 ? "nav.dir.right" : "nav.dir.left", Math.Abs(right))); }

        if (Math.Abs(up) >= Math.Abs(right)) { AddUp(); AddRight(); }
        else { AddRight(); AddUp(); }

        return parts.Count == 0 ? Loc.Get("nav.dir.here") : string.Join(", ", parts);
    }

    /// <summary>
    /// A spoken name for a category. Falls back to the enum name with its words separated, so a
    /// category the mod has no wording for is still usable rather than missing.
    /// </summary>
    private static string CategoryName(string key)
    {
        // The mod's own keys are lower case ("doors"); their wording is under "nav.cat.Doors".
        var raw = key.StartsWith("wgo.", StringComparison.Ordinal)
            ? key.Substring(4)
            : char.ToUpperInvariant(key[0]) + key.Substring(1);
        var known = Loc.Find("nav.cat." + raw);
        if (known != null) return known;

        var sb = new System.Text.StringBuilder(raw.Length + 4);
        for (var i = 0; i < raw.Length; i++)
        {
            if (i > 0 && char.IsUpper(raw[i]) && !char.IsUpper(raw[i - 1])) sb.Append(' ');
            sb.Append(raw[i]);
        }
        return sb.ToString().ToLowerInvariant();
    }

    private static Vector2 Flat(Vector3 v) => new(v.x, v.z);

    private static int Wrap(int index, int count) => count <= 0 ? -1 : ((index % count) + count) % count;

    private static bool TryGetPlayer(out Vector3 position, out Vector2 facing, bool quiet = false)
    {
        position = default;
        facing = default;

        var player = MainGame.PlayerController;
        if (player == null)
        {
            if (!quiet) ScreenReader.Say(Loc.Get("nav.not_in_game"));
            return false;
        }

        position = player.MovablePosition;
        facing = player.MovableDirection;
        return true;
    }

    /// <summary>
    /// What the game's tutorial arrow is attached to, or null when it is not showing. The target
    /// is a private field on the singleton, so it is read reflectively rather than recomputed -
    /// the arrow and the spoken objective must never disagree about where to go.
    /// </summary>
    internal static NavTarget ObjectiveTarget()
    {
        try
        {
            var arrow = LazySingleton<UITutorialArrow>.Instance;
            var data = arrow != null && arrow.gameObject.activeSelf ? ArrowTarget(arrow) : null;

            // The arrow's target also persists on the save, which survives the arrow object being
            // switched off between scripted beats - so a marker set a moment ago is still readable.
            if (data == null)
            {
                var id = MainGame.PlayerData == null ? null : MainGame.PlayerData.tutorialArrowWgoId;
                if (id != null) data = MainGame.WorldData?.GetWgoData(id);
            }

            // An arrow left on something the story has since parked off the map is no objective.
            if (data != null && Parked(data)) data = null;

            // No arrow: the object the story is waiting for is the next best thing, and in practice
            // the arrow is missing far more often than it is shown.
            if (data == null) return NearestStoryTarget();

            return new NavTarget(data.id, data.Position, data.WorldId, "objective", data: data);
        }
        catch (Exception ex)
        {
            _log?.LogWarning($"[Nav] Could not read the tutorial arrow: {ex.Message}");
            return default;
        }
    }

    /// <summary>The closest object a flowscript is waiting for the player to use.</summary>
    private static NavTarget NearestStoryTarget()
    {
        if (!TryGetPlayer(out var from, out _, quiet: true)) return default;

        RefreshQuestTokens();
        WgoData best = null;
        var bestDistance = float.MaxValue;

        foreach (var wgo in Spawned)
        {
            if (wgo == null) continue;
            var data = wgo.Data;
            if (data == null || !IsStoryStep(data) || Parked(data)) continue;

            var distance = Flat(data.Position - from).magnitude;
            if (distance > NavRange || distance >= bestDistance) continue;

            bestDistance = distance;
            best = data;
        }

        // Nothing near: the nearest story object anywhere in the loaded world.
        if (best == null)
        {
            foreach (var data in FarStorySteps())
            {
                var distance = Flat(data.Position - from).magnitude;
                if (distance >= bestDistance) continue;
                bestDistance = distance;
                best = data;
            }
        }

        var target = best == null ? default : new NavTarget(best.id, best.Position, best.WorldId, "objective", data: best);

        // A zone the story is listening for counts too - in the intro it is the next step more
        // often than any object.
        try
        {
            foreach (var zone in UnityEngine.Object.FindObjectsByType<GDZone>(FindObjectsSortMode.None))
            {
                if (zone == null || !zone.isActiveAndEnabled || !StoryListeners.AwaitsZone(zone)) continue;

                var collider = zone.GetComponent<Collider>();
                var position = collider != null ? collider.bounds.center : zone.transform.position;
                var distance = Flat(position - from).magnitude;
                if (distance > NavRange || distance >= bestDistance) continue;

                bestDistance = distance;
                target = new NavTarget(Humanise(zone.customTag), position, null, "objective", zone: zone);
            }
        }
        catch (Exception ex)
        {
            _log?.LogWarning($"[Nav] Could not read story zones: {ex.Message}");
        }

        return target;
    }

    private static readonly AccessTools.FieldRef<UITutorialArrow, WgoData> ArrowTarget =
        AccessTools.FieldRefAccess<UITutorialArrow, WgoData>("wgoData");
}
