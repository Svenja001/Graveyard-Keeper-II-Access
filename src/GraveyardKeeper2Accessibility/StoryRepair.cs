namespace GraveyardKeeper2Accessibility;

/// <summary>
/// Catches the story up when it waits for something that already happened.
///
/// <para>
/// Quest steps listen through <c>GlobalEventsSystem</c>, which is edge-triggered: a listener
/// registered <i>after</i> the event fired waits forever. The intro barricade showed it. The
/// player was already standing in <c>GDZone_Intro_Scout_Road_Barricade</c> when "clear the path"
/// began listening for an <i>entry</i> into it, so that step never ran; they smashed the barricade
/// anyway, and when a later walk finally re-entered the zone the story moved on to "destroy the
/// barricade" - listening for a <c>WgoDead</c> that had fired minutes before. A sighted player
/// crosses the zone on the way; a player walked point to point can stand in it the whole time.
/// </para>
///
/// <para>
/// So pending listeners are checked against the world as it is now, and fired when the thing they
/// wait for is already true and has stayed true for a few seconds (long enough that a flowscript
/// about to spawn the object has done so): the object is gone, the tagged object is gone or down
/// to the awaited HP, or the player is already inside the awaited zone. Only listeners that belong
/// to a quest are considered, and each is fired at most once per session.
/// </para>
/// </summary>
internal static class StoryRepair
{
    private const float ScanInterval = 1f;
    private const float SettleSeconds = 3f;

    private static ManualLogSource _log;
    private static ConfigEntry<bool> _enabled;
    private static float _nextScan;

    /// <summary>When each pending listener was first seen already satisfied.</summary>
    private static readonly Dictionary<string, float> SatisfiedSince = new Dictionary<string, float>();

    private static readonly HashSet<string> Fired = new HashSet<string>();

    /// <summary>
    /// Quests the game itself has sent a zone entry to during the player's current stay in that
    /// zone, by event key. Cleared when the player is seen outside the zone.
    ///
    /// <para>
    /// A quest that re-arms itself is listening again the moment it has handled the entry - the
    /// forest guards (<c>31_forest_guards_block</c>) turn the player back on every entry and wait
    /// for the next. To the scan that looked exactly like the barricade: a listener for a zone the
    /// player is already in. It was fired a second time and the guards said their lines twice
    /// (log of 2026-09-25). A quest that already got this entry is not missing it.
    /// </para>
    /// </summary>
    private static readonly Dictionary<string, HashSet<string>> Received = new Dictionary<string, HashSet<string>>();

    private static bool _repairing;

    internal static void Init(ManualLogSource log, ConfigFile config)
    {
        _log = log;
        _enabled = config.Bind("World", "RepairMissedStoryEvents", true,
            "When the story waits for something that already happened - a barricade already " +
            "destroyed, a zone you are already standing in - sends the game that event again.");
    }

    internal static void Update()
    {
        if (!_enabled.Value || Time.unscaledTime < _nextScan) return;
        _nextScan = Time.unscaledTime + ScanInterval;

        try
        {
            Scan();
        }
        catch (Exception ex)
        {
            _log?.LogError($"[Story] Repair scan failed: {ex.GetType().Name}: {ex.Message}");
            _nextScan = Time.unscaledTime + 30f;
        }
    }

    private static void Scan()
    {
        var save = MainGame.Instance?.GameSave;
        var events = save?.globalEventsSystem?.checkingEvents;
        var world = save?.WorldData;
        if (events == null || world == null || !world.HasCache)
        {
            SatisfiedSince.Clear();
            MissedSince.Clear();
            return;
        }

        // Never start a story scene on top of another one, a sleep or a teleport fade: the settle
        // time starts over once the player has control again.
        if (AutoWalk.SceneHasControl() != null)
        {
            SatisfiedSince.Clear();
            MissedSince.Clear();
            return;
        }

        var now = Time.unscaledTime;
        var seen = new HashSet<string>();
        GDZone[] zones = null;

        // Firing edits checkingEvents, so decide first and fire afterwards.
        List<GlobalEventsSystem.Event> due = null;
        List<GDZone> missed = null;

        foreach (var ev in events)
        {
            if (ev == null || !ev.hasId || string.IsNullOrEmpty(ev.id) || ev.trigerrables.Count == 0) continue;
            if (!ev.trigerrables.Any(t => t is QuestCheck)) continue;

            bool satisfied;
            switch (ev.type)
            {
                case GlobalEventsSystem.Event.Type.WgoDead:
                case GlobalEventsSystem.Event.Type.RemoveWgoDataFromScene:
                    satisfied = !Exists(world.Cache.wgoDataByIdsCache, ev.id);
                    break;
                case GlobalEventsSystem.Event.Type.WgoCustomTagDead:
                    satisfied = !Exists(world.Cache.wgoDataByCustomTagsCache, ev.id);
                    break;
                case GlobalEventsSystem.Event.Type.WgoCustomTagHpValueReached:
                    satisfied = HpReached(world.Cache, ev.id);
                    break;
                case GlobalEventsSystem.Event.Type.PlayerEnterGDZone:
                    zones ??= UnityEngine.Object.FindObjectsByType<GDZone>(FindObjectsSortMode.None);
                    foreach (var z in zones)
                        if (z != null && z.customTag == ev.id && !WorldAnnouncer.ZoneInside(z) && WorldAnnouncer.PlayerStandsIn(z))
                            (missed ??= new List<GDZone>()).Add(z);
                    satisfied = zones.Any(z => z != null && z.customTag == ev.id && WorldAnnouncer.PlayerInZone(z));
                    break;
                default:
                    continue;
            }

            var key = $"{ev.type}:{ev.id}";
            if (!satisfied)
            {
                Received.Remove(key);
                continue;
            }
            if (Fired.Contains(key)) continue;
            if (Received.TryGetValue(key, out var got) && ev.trigerrables.All(t => QuestOf(t) == null || got.Contains(QuestOf(t))))
                continue;

            seen.Add(key);
            if (!SatisfiedSince.TryGetValue(key, out var since))
            {
                SatisfiedSince[key] = now;
                continue;
            }

            if (now - since >= SettleSeconds) (due ??= new List<GlobalEventsSystem.Event>()).Add(ev);
        }

        foreach (var key in SatisfiedSince.Keys.Where(k => !seen.Contains(k)).ToList())
            SatisfiedSince.Remove(key);

        EnterMissed(missed, now);

        if (due == null) return;

        foreach (var ev in due)
        {
            var key = $"{ev.type}:{ev.id}";
            Fired.Add(key);
            SatisfiedSince.Remove(key);

            var quests = string.Join(", ", ev.trigerrables.Select(QuestOf).Where(q => q != null));
            _log?.LogWarning($"[Story] Quest {quests} was waiting for {ev.type} '{ev.id}', which already happened. Firing it again.");

            _repairing = true;
            try { GlobalEventsSystem.FireTrigger(ev.type, ev.id); }
            finally { _repairing = false; }
            ScreenReader.Say(Loc.Get("story.caught_up"), interrupt: false);
        }
    }

    /// <summary>Notes which quests receive a zone entry the game fires itself - see <see cref="Received"/>.</summary>
    [HarmonyPatch(typeof(GlobalEventsSystem), nameof(GlobalEventsSystem.FireTrigger))]
    [HarmonyPrefix]
    private static void GlobalEventsSystem_FireTrigger(GlobalEventsSystem.Event.Type type, string id)
    {
        try
        {
            if (_repairing || type != GlobalEventsSystem.Event.Type.PlayerEnterGDZone || string.IsNullOrEmpty(id)) return;

            var events = MainGame.Instance?.GameSave?.globalEventsSystem?.checkingEvents;
            if (events == null) return;

            var key = $"{type}:{id}";
            foreach (var ev in events)
            {
                if (ev == null || ev.type != type || ev.id != id) continue;
                foreach (var t in ev.trigerrables)
                {
                    var quest = QuestOf(t);
                    if (quest == null) continue;
                    if (!Received.TryGetValue(key, out var set)) Received[key] = set = new HashSet<string>();
                    set.Add(quest);
                }
            }
        }
        catch (Exception ex)
        {
            _log?.LogWarning($"[Story] Could not note a zone entry: {ex.Message}");
        }
    }

    /// <summary>When the player was first seen standing in an awaited zone the game thinks they are not in.</summary>
    private static readonly Dictionary<int, float> MissedSince = new Dictionary<int, float>();

    private const float MissedEntrySeconds = 1f;

    /// <summary>
    /// Awaited zones the player stands in while the game's flag says they never entered: the game
    /// dropped the entry (see <see cref="ZoneEntry"/>). After a second of that - an entry the game
    /// does see arrives within a physics step - the entry is handed over. Once it is taken, the
    /// flag is set and the zone no longer qualifies, so it happens once per stay.
    /// </summary>
    private static void EnterMissed(List<GDZone> missed, float now)
    {
        var current = new HashSet<int>();
        if (missed != null)
        {
            foreach (var zone in missed.Distinct())
            {
                var id = zone.GetInstanceID();
                current.Add(id);
                if (!MissedSince.TryGetValue(id, out var since))
                {
                    MissedSince[id] = now;
                    continue;
                }
                if (now - since < MissedEntrySeconds) continue;

                var entered = ZoneEntry.Enter(zone);
                _log?.LogWarning($"[Story] Standing in '{ZoneEntry.Name(zone)}', which a quest awaits, but the game never registered the entry. " +
                    (entered ? "Entered it now." : "Could not enter it yet; trying again."));
                if (entered) MissedSince.Remove(id);
            }
        }

        foreach (var id in MissedSince.Keys.Where(k => !current.Contains(k)).ToList())
            MissedSince.Remove(id);
    }

    private static bool Exists(Dictionary<string, List<WgoData>> cache, string key)
    {
        return cache.TryGetValue(key, out var list) && list != null && list.Any(w => w != null);
    }

    /// <summary><c>tag:hp</c> - every object with that tag is gone or at or below that HP.</summary>
    private static bool HpReached(WgoDataCache cache, string key)
    {
        var colon = key.IndexOf(':');
        if (colon <= 0 || !int.TryParse(key.Substring(colon + 1), out var hp)) return false;
        if (!cache.wgoDataByCustomTagsCache.TryGetValue(key.Substring(0, colon), out var list) || list == null)
            return true;

        return list.All(w => w == null || w.HpComponent == null || w.HpComponent.Hp <= hp);
    }

    private static string QuestOf(IEventTrigerrable t) => (t as QuestCheck)?.questId;

    private const float ReachMetres = 2.5f;

    /// <summary>
    /// The interaction key pressed with nothing in front of the player, while an object the story
    /// waits for is within reach but switched off: use it for them.
    ///
    /// <para>
    /// The game only offers objects whose <c>IsInteractable</c> is set, and some turn that off on
    /// their own. The intro's cheese plate is a container: take the cheese before Larry asks for
    /// it and the plate empties and goes dead - while "eat the cheese" still waits for the player
    /// to use the plate (<c>AddInteractionEvent("intro_scout_chees", "work")</c>), with the doors
    /// locked until they do. A sighted player sees Larry point at a plate and does it in order.
    /// Pressing the key there does exactly what the game does for a usable object with a story
    /// event: <c>FireInteractionEvent</c>.
    /// </para>
    /// </summary>
    [HarmonyPatch(typeof(PlayerInputHandler), nameof(PlayerInputHandler.UpdateInput))]
    [HarmonyPostfix]
    private static void PlayerInputHandler_UpdateInput()
    {
        try
        {
            if (!_enabled.Value) return;
            if (!LazyInput.GetKeyDown(GameKey.Interaction) && !LazyInput.GetKeyDown(GameKey.Action)) return;

            var player = MainGame.PlayerController;
            var interaction = player == null ? null : player.PlayerInteractionComponent;
            if (interaction == null || interaction.WgoUnderInteraction != null || interaction.BigDropUnderInteraction != null) return;

            var from = player.MovablePosition;
            WgoData best = null;
            var bestDistance = ReachMetres;
            foreach (var wgo in Navigator.SpawnedWgos)
            {
                if (wgo == null || wgo.IsDespawning) continue;
                var data = wgo.Data;
                if (data == null || data.IsInteractable || data.Events == null || data.Events.Count == 0) continue;

                var offset = data.Position - from;
                var distance = new Vector2(offset.x, offset.z).magnitude;
                if (distance > bestDistance) continue;

                bestDistance = distance;
                best = data;
            }

            if (best == null) return;

            _log?.LogWarning($"[Story] '{best.id}' awaits use but is not interactable; firing its interaction event.");
            best.FireInteractionEvent();
            ScreenReader.Say(Loc.Fmt("story.used_for_you", TmpText.Clean(LLBase.L(best.id))), interrupt: false);
        }
        catch (Exception ex)
        {
            _log?.LogError($"[Story] Using a dead story object failed: {ex.GetType().Name}: {ex.Message}");
        }
    }
}
