namespace GraveyardKeeper2Accessibility;

/// <summary>
/// What a fight looks like from the outside: that it started and ended, how the player and the
/// thing being defended are holding up, and where the enemies are.
///
/// <para>
/// The first fight (chapter 3, <c>fighting_level_ev_3</c>) cannot be lost - the player turns
/// damage-immune at 12 HP and the story moves on when the barricade drops to 6 - so what a blind
/// player lacks there is not skill but information: whether the fight is running, whether anything
/// they do matters, and when it is over. On screen that is health bars, a crowd of zombies and the
/// camera; here it is announcements at thresholds and a status key.
/// </para>
///
/// <para>
/// "The thing being defended" is not hard-coded. The story listens for
/// <c>WgoCustomTagHpValueReached barricade_ev_3_2:6</c>, so objects whose custom tag a quest
/// watches (<see cref="StoryListeners.WgoTags"/>) are the ones worth reporting.
/// </para>
/// </summary>
internal static class FightAnnouncer
{
    private const float EnemyRange = 30f;

    private static ManualLogSource _log;
    private static ConfigEntry<KeyboardShortcut> _statusKey;
    private static FightingGameController _controller;
    private static FightState _state;
    private static float _nextWatch;

    // Last threshold announced, in quarters: 4 = full, 3 = 75 %, ... so each is said once per fall.
    private static int _playerQuarter = 4;
    private static readonly Dictionary<string, int> WatchedQuarter = new();

    internal static void Init(ManualLogSource log, ConfigFile config)
    {
        _log = log;
        _statusKey = ModKeys.Bind(config, "Keys", "FightStatus", new KeyboardShortcut(KeyCode.F4),
            "In a fight: your health, the health of what you defend, and how many enemies are near.");
    }

    internal static void Update()
    {
        try
        {
            Subscribe();

            if (_statusKey.Value.IsDown()) SayStatus();
            if (_state != FightState.Disabled) WatchHotBarUse();

            if (_state != FightState.Disabled && Time.unscaledTime >= _nextWatch)
            {
                _nextWatch = Time.unscaledTime + 0.5f;
                WatchFlag();
                if (_state == FightState.ActiveFight)
                {
                    WatchThresholds();
                    WatchBattlefield();
                }
            }
        }
        catch (Exception ex)
        {
            _log?.LogError($"[Fight] Update failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>The fight state as last reported by the game; Disabled outside fights.</summary>
    internal static FightState State => _controller == null ? FightState.Disabled : _state;

    /// <summary>The level being fought or prepared, or null.</summary>
    internal static FightingLevel Level => _controller == null || _state == FightState.Disabled ? null : _controller.CurrentLevel;

    private static readonly FieldInfo ExistingController =
        AccessTools.Field(typeof(LazySingleton<FightingGameController>), "instance");

    private static void Subscribe()
    {
        // Not LazySingleton.Instance: that searches the scene when empty and, failing, creates a
        // fresh controller - every frame in the main menu. The field is only read.
        var controller = ExistingController?.GetValue(null) as FightingGameController;
        if (ReferenceEquals(controller, _controller)) return;

        if (_controller != null) _controller.OnFightStateChanged -= OnStateChanged;
        _controller = controller;
        if (_controller == null) return;

        _controller.OnFightStateChanged += OnStateChanged;
        _state = _controller.CurrentFightState;
    }

    private static void OnStateChanged(FightState state)
    {
        try
        {
            var previous = _state;
            _state = state;
            _log?.LogInfo($"[Fight] {previous} -> {state}");

            switch (state)
            {
                case FightState.InPreFight:
                    _carried = null;
                    Announce(Loc.Get("fight.prefight_long"));
                    break;
                case FightState.ActiveFight:
                    LossReason = null;
                    _playerQuarter = 4;
                    WatchedQuarter.Clear();
                    ResetBattlefield();
                    Announce(Loc.Get("fight.started_long"));
                    break;
                case FightState.Disabled when previous == FightState.ActiveFight:
                    Announce(Loc.Get("fight.over"));
                    break;
            }
        }
        catch (Exception ex)
        {
            _log?.LogError($"[Fight] State announce failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static void WatchThresholds()
    {
        var hp = MainGame.PlayerData == null ? null : MainGame.PlayerData.hpComponent;
        if (hp != null && hp.MaxHpValue > 0)
        {
            var quarter = Quarter(hp);
            if (quarter < _playerQuarter)
            {
                var potion = PotionHint();
                var text = Loc.Fmt("fight.player_hp", Percent(hp));
                Announce(potion == null || quarter >= 3 ? text : $"{text}. {potion}");
            }
            _playerQuarter = quarter;
        }

        foreach (var (name, component) in Watched())
        {
            var quarter = Quarter(component);
            if (WatchedQuarter.TryGetValue(name, out var last) && quarter < last)
                Announce(Loc.Fmt("fight.object_hp", name, Percent(component)));
            WatchedQuarter[name] = quarter;
        }
    }

    private static void SayStatus()
    {
        var player = MainGame.PlayerController;
        if (player == null)
        {
            ScreenReader.Say(Loc.Get("nav.not_in_game"));
            return;
        }

        var parts = new List<string>();
        parts.Add(Loc.Get(_state == FightState.ActiveFight ? "fight.state_active"
            : _state == FightState.InPreFight ? "fight.state_prefight" : "fight.state_none"));

        var hp = MainGame.PlayerData == null ? null : MainGame.PlayerData.hpComponent;
        if (hp != null && hp.MaxHpValue > 0) parts.Add(Loc.Fmt("fight.player_hp", Percent(hp)));

        foreach (var (name, component) in Watched())
            parts.Add(Loc.Fmt("fight.object_hp", name, Percent(component)));

        try
        {
            AddBattlefieldStatus(parts);
        }
        catch (Exception ex)
        {
            _log?.LogWarning($"[Fight] Battlefield status failed: {ex.GetType().Name}: {ex.Message}");
        }

        var from = player.MovablePosition;
        var enemies = Navigator.SpawnedWgos
            .Where(IsLiveEnemy)
            .Select(w => (wgo: w, offset: new Vector2(w.Data.Position.x - from.x, w.Data.Position.z - from.z)))
            .Where(e => e.offset.magnitude <= EnemyRange)
            .OrderBy(e => e.offset.sqrMagnitude)
            .ToList();

        if (enemies.Count == 0)
        {
            parts.Add(Loc.Get("fight.no_enemies"));
        }
        else
        {
            var nearest = enemies[0].offset;
            parts.Add(Loc.Fmt("fight.enemies", enemies.Count, Mathf.RoundToInt(nearest.magnitude), Navigator.KeyDirections(nearest)));
        }

        var text = string.Join(". ", parts);
        _log?.LogInfo($"[Fight] Status{BaseDistance()}: \"{text}\"");
        ScreenReader.Say(text);
    }

    /// <summary>
    /// Fight lines are spoken unasked and queued; they are logged too, with how far the player is
    /// from the middle of the base, so a lost fight can be read back afterwards.
    /// </summary>
    private static void Announce(string text)
    {
        _log?.LogInfo($"[Fight] Said{BaseDistance()}: \"{text}\"");
        ScreenReader.Say(text, interrupt: false);
    }

    private static string BaseDistance()
    {
        var level = Level;
        var player = MainGame.PlayerController;
        if (level == null || level.BaseCapturePoint == null || player == null) return "";
        var offset = player.MovablePosition - level.BaseCapturePoint.transform.position;
        return $" (player {new Vector2(offset.x, offset.z).magnitude:0.0} m from base middle, on it: {PlayerOnBase()})";
    }

    /// <summary>The game's own team test: a zombie id that is not an ally.</summary>
    internal static bool IsLiveEnemy(Wgo wgo)
    {
        if (wgo == null || wgo.IsDespawning || wgo.Data == null || wgo.Data.id == null) return false;
        if (wgo.TeamType != LazyConsts.Fighting.TeamType.WildZombie) return false;

        var hp = wgo.Data.HpComponent;
        return hp == null || hp.Hp > 0;
    }

    /// <summary>Objects with health whose custom tag a quest is watching.</summary>
    private static IEnumerable<(string name, HPComponent hp)> Watched()
    {
        StoryListeners.Refresh();
        if (StoryListeners.WgoTags.Count == 0) yield break;

        foreach (var wgo in Navigator.SpawnedWgos)
        {
            var data = wgo == null || wgo.IsDespawning ? null : wgo.Data;
            if (data == null || string.IsNullOrEmpty(data.CustomTag) || !StoryListeners.WgoTags.Contains(data.CustomTag)) continue;

            var hp = data.HpComponent;
            if (hp == null || hp.MaxHpValue <= 0) continue;

            yield return (ItemText.Name(data.id), hp);
        }
    }

    // ---- the battlefield ----------------------------------------------------------------------

    /// <summary>
    /// A squad fight is won when no wild zombie is left and lost when they take the base capture
    /// point (<c>FightingGameController.WereWeLost</c>). So what the player needs said, unasked, is
    /// how that race is going: a point changing hands, a line breached, the base being entered,
    /// and the enemy count falling. Polled twice a second from the game's own state.
    /// </summary>
    private static readonly Dictionary<FightingCapturePoint, LazyConsts.Fighting.TeamType> Owners = new();
    private static int _breached;
    private static int _lastEnemyStep = -1;
    private static float _baseWarnedAt = -100f;
    private static Wgo _carried;

    private static void ResetBattlefield()
    {
        Owners.Clear();
        _breached = 0;
        _lastEnemyStep = -1;
        _baseWarnedAt = -100f;
        _goalSaid = false;
        KnownEnemies.Clear();
        NewEnemies.Clear();
        _lastStep = null;
        _lastTimeStep = -1;
        _onBase = false;
    }

    // ---- the clock ----------------------------------------------------------------------------

    /// <summary>
    /// <b>A squad fight is won by holding out, not by killing.</b> Read from
    /// <c>FightingGameController.OnPresetFinished</c> (2026-10-03, after two lost fights): when the
    /// wave timeline runs out and the base point is still ours, the fight is won and the leftover
    /// enemies are killed by the game. Lost the moment the base is theirs. The timeline is the
    /// controller's private <c>presetProcessor</c>; its own times are public.
    /// </summary>
    private static readonly AccessTools.FieldRef<FightingGameController, FightingLevelPresetProcessor> Processor =
        AccessTools.FieldRefAccess<FightingGameController, FightingLevelPresetProcessor>("presetProcessor");

    private static bool _goalSaid;
    private static int _lastTimeStep = -1;

    /// <summary>Seconds until the waves end, or -1 when no timeline is running.</summary>
    internal static int SecondsLeft()
    {
        if (_controller == null || _state != FightState.ActiveFight) return -1;
        var processor = Processor(_controller);
        if (processor == null || !processor.IsPlaying || processor.TotalDuration <= 0f) return -1;
        return Mathf.Max(0, Mathf.CeilToInt(processor.TotalDuration - processor.CurrentProgress));
    }

    /// <summary>"2 Minuten 10 Sekunden".</summary>
    private static string Duration(int seconds)
    {
        var minutes = seconds / 60;
        var rest = seconds % 60;
        if (minutes == 0) return Loc.Plural("fight.seconds", rest, rest);
        var m = Loc.Plural("fight.minutes", minutes, minutes);
        return rest == 0 ? m : $"{m} {Loc.Plural("fight.seconds", rest, rest)}";
    }

    /// <summary>
    /// Says the goal once the timeline is known (it is measured a few frames after the start), then
    /// each full minute left and the last 30 seconds.
    /// </summary>
    private static void WatchClock()
    {
        var left = SecondsLeft();
        if (left < 0) return;

        if (!_goalSaid)
        {
            _goalSaid = true;
            _lastTimeStep = left / 60;
            LogPoints("at the start");
            var targets = ToTake(Level).Select(MilitaryReader.PointName).ToList();
            Announce(targets.Count == 0
                ? Loc.Fmt("fight.goal", Duration(left))
                : Loc.Fmt("fight.goal_capture", Duration(left), string.Join(Loc.Get("fight.then"), targets)));
            AnnounceNextStep();
            return;
        }

        // Steps: whole minutes left, then 0 for "30 seconds".
        var step = left > 60 ? (left - 1) / 60 : left > 30 ? 1 : 0;
        if (step < _lastTimeStep && left > 0)
            Announce(Loc.Fmt("fight.time_left", Duration(step == 0 ? 30 : step * 60)));
        _lastTimeStep = step;
    }

    // ---- standing on the base -----------------------------------------------------------------

    /// <summary>
    /// The base circle is only about 2 m across, and the player counts as a defender on it: while
    /// anyone of ours stands inside, enemies on it cannot take it (<c>FightingCapturePoint.CustomUpdate</c>
    /// freezes a contested point). Both lost fights ended with "0 allies, 13 enemies" on the base,
    /// so stepping on and off it is said.
    /// </summary>
    private static bool _onBase;

    internal static bool PlayerOnBase()
    {
        var level = Level;
        var player = MainGame.PlayerController;
        return level != null && level.BaseCapturePoint != null && player != null
            && level.BaseCapturePoint.IsOnCapturePoint(player.MovablePosition);
    }

    private static void WatchOnBase()
    {
        var on = PlayerOnBase();
        if (on != _onBase) Announce(Loc.Get(on ? "fight.on_base" : "fight.off_base"));
        _onBase = on;
    }

    private static IEnumerable<FightingCapturePoint> CapturePoints(FightingLevel level)
    {
        if (level == null) yield break;
        if (level.BaseCapturePoint != null) yield return level.BaseCapturePoint;
        if (level.FightingLines == null) yield break;
        foreach (var line in level.FightingLines)
        {
            if (line == null || line.sectors == null) continue;
            foreach (var sector in line.sectors)
                if (sector != null && sector.point != null && !ReferenceEquals(sector.point, level.BaseCapturePoint)
                    && (sector.point.isActiveAndEnabled || sector.point.isBasePoint)) yield return sector.point;
        }
    }

    /// <summary>
    /// Line points the game also marks as a base. <c>OnPresetFinished</c> calls the fight lost when
    /// the timeline ends with any of them not ours - so they are targets to take, not only the base
    /// to hold. Found 2026-10-03: the third fight was lost with the player standing on the base all
    /// along (the base cannot fall then), and "fight_A1_1" has two points named "Capture Point Base".
    /// </summary>
    internal static IEnumerable<FightingCapturePoint> Objectives(FightingLevel level) =>
        CapturePoints(level).Where(p => p.isBasePoint && !ReferenceEquals(p, level.BaseCapturePoint));

    private static IEnumerable<FightingCapturePoint> OpenObjectives(FightingLevel level) =>
        Objectives(level).Where(p => p.OwnedByTeam != LazyConsts.Fighting.TeamType.Player);

    /// <summary>Every capture point with its flags, for reading a fight back from the log.</summary>
    private static void LogPoints(string when)
    {
        var level = _controller == null ? null : _controller.CurrentLevel;
        if (level == null) return;
        foreach (var p in CapturePoints(level))
        {
            if (p == null) continue;
            var pos = p.transform.position;
            _log?.LogInfo($"[Fight] Point {when}: '{p.name}' ({MilitaryReader.PointName(p)}) main base: {ReferenceEquals(p, level.BaseCapturePoint)}, isBasePoint: {p.isBasePoint}, active: {p.isActiveAndEnabled}, owner {p.OwnedByTeam}, {Mathf.RoundToInt(100f * p.CurrentProgress)} %, allies {p.allies.Count}, enemies {p.enemies.Count}, at ({pos.x:0.0}, {pos.z:0.0}).");
            // What taking or losing it switches (spawners, zombie fog): whether capturing the target
            // point stops the waves that kill the player there is level data, not code.
            if (when == "at the start")
            {
                foreach (var a in p.OnCaptureActions) _log?.LogInfo($"[Fight]   on capture: {Describe(a)}");
                foreach (var a in p.OnLostActions) _log?.LogInfo($"[Fight]   on loss: {Describe(a)}");
            }
        }
        if (when == "at the start" && level.FightingLines != null)
            foreach (var line in level.FightingLines)
            {
                if (line == null || line.spawnZones == null) continue;
                foreach (var zone in line.spawnZones)
                    if (zone != null)
                        _log?.LogInfo($"[Fight] Line {line.lineIdx + 1} spawn zone '{zone.name}' active {zone.gameObject.activeInHierarchy} at ({zone.transform.position.x:0.0}, {zone.transform.position.z:0.0}).");
            }
    }

    // ---- waves -------------------------------------------------------------------------------

    /// <summary>
    /// "Wave: 6 enemies, 25 m east of the base, near line 2, point 1." Reported 2026-10-03: the
    /// waves arrive unannounced and the first a blind player knows of one is the base warning.
    /// Enemies are spawned a few at a time, so new ones are gathered until none has appeared for
    /// 1.5 s and then said as one wave, placed by their middle relative to the base.
    /// </summary>
    private static readonly HashSet<Wgo> KnownEnemies = new();
    private static readonly List<Wgo> NewEnemies = new();
    private static float _lastSpawnAt;

    private static void WatchWaves(FightingLevel level)
    {
        foreach (var wgo in Navigator.SpawnedWgos)
        {
            if (!IsLiveEnemy(wgo) || !KnownEnemies.Add(wgo)) continue;
            NewEnemies.Add(wgo);
            _lastSpawnAt = Time.unscaledTime;
        }
        KnownEnemies.RemoveWhere(w => w == null || w.IsDespawning);

        if (NewEnemies.Count == 0 || Time.unscaledTime - _lastSpawnAt < 3f) return;

        var alive = NewEnemies.Where(w => w != null && w.Data != null).ToList();
        NewEnemies.Clear();
        if (alive.Count == 0 || level.BaseCapturePoint == null) return;

        var middle = Vector3.zero;
        foreach (var w in alive) middle += w.Data.Position;
        middle /= alive.Count;

        var from = level.BaseCapturePoint.transform.position;
        var offset = new Vector2(middle.x - from.x, middle.z - from.z);
        var line = Loc.Fmt("fight.wave", alive.Count, Mathf.RoundToInt(offset.magnitude), Navigator.KeyDirections(offset));

        var near = CapturePoints(level).Where(p => p != null)
            .OrderBy(p => Flat(p.transform.position - middle)).FirstOrDefault();
        if (near != null && Flat(near.transform.position - middle) < 10f)
            line = $"{line}, {Loc.Fmt("fight.wave_near", MilitaryReader.PointName(near))}";
        Announce(line);
    }

    // ---- staying alive ---------------------------------------------------------------------

    private static readonly GameKey[] HotBarKeys =
        { GameKey.UseHotBarItem1, GameKey.UseHotBarItem2, GameKey.UseHotBarItem3, GameKey.UseHotBarItem4 };

    private static int _usedSlot = -1;
    private static string _usedId;
    private static int _usedHeld;
    private static int _usedHp;
    private static float _usedCheckAt;

    /// <summary>
    /// Says what a hot-bar key did in a fight. The game is silent when it refuses - it skips the
    /// keys while it holds the player's controls - and the user could not tell whether a potion
    /// was drunk (2026-10-03). Compares the item count a moment after the press.
    /// </summary>
    private static void WatchHotBarUse()
    {
        if (_usedSlot < 0)
        {
            for (var i = 0; i < HotBarKeys.Length; i++)
            {
                if (!LazyInput.GetKeyDown(HotBarKeys[i])) continue;
                var pinned = MainGame.PlayerData?.pinnedItems;
                _usedSlot = i;
                _usedId = pinned != null && i < pinned.Length ? pinned[i] : null;
                _usedHeld = string.IsNullOrEmpty(_usedId) ? 0 : ItemText.Held(_usedId);
                _usedHp = MainGame.PlayerData?.hpComponent?.Hp ?? 0;
                _usedCheckAt = Time.unscaledTime + 0.6f;
                return;
            }
            return;
        }

        if (Time.unscaledTime < _usedCheckAt) return;
        var slot = _usedSlot + 1;
        _usedSlot = -1;

        string text;
        if (string.IsNullOrEmpty(_usedId)) text = Loc.Fmt("fight.slot_empty", slot);
        else
        {
            var name = ItemText.Name(_usedId);
            var held = ItemText.Held(_usedId);
            var hp = MainGame.PlayerData == null ? null : MainGame.PlayerData.hpComponent;
            var blocked = AutoWalk.SceneHasControl();
            // Health rising counts as used too: in the won fight (2026-10-03) the potion healed -
            // the 72 % warning came twice - while the count read the same 0.4 s later.
            if (held < _usedHeld || (hp != null && hp.Hp > _usedHp))
                text = hp != null && hp.MaxHpValue > 0 ? Loc.Fmt("fight.item_used_hp", name, Percent(hp), held) : Loc.Fmt("fight.item_used", name, held);
            else if (hp != null && hp.Hp <= 0) text = Loc.Fmt("fight.item_dead", name);
            else if (blocked != null) text = Loc.Fmt("fight.item_blocked", name, AutoWalk.ControlName(blocked.Value));
            else if (_usedHeld == 0) text = Loc.Fmt("fight.item_none_left", name);
            else text = Loc.Fmt("fight.item_not_used", name);
        }
        _log?.LogInfo($"[Fight] Hot bar {slot} ({_usedId ?? "empty"}): \"{text}\"");
        ScreenReader.Say(text);
    }

    /// <summary>
    /// "Healing potion: key 3, 15 left" when one is on the hot bar. The sixth fight (2026-10-03)
    /// ended with the player dead at the target point with 15 potions on slot 3, never drunk.
    /// </summary>
    private static string PotionHint()
    {
        var pinned = MainGame.PlayerData?.pinnedItems;
        if (pinned == null) return null;
        for (var i = 0; i < pinned.Length; i++)
        {
            var id = pinned[i];
            if (string.IsNullOrEmpty(id) || id.IndexOf("heal", StringComparison.OrdinalIgnoreCase) < 0) continue;
            var held = ItemText.Held(id);
            if (held == 0) continue;
            return Loc.Fmt("fight.potion", ItemText.Name(id), i + 1, held);
        }
        return null;
    }

    /// <summary>
    /// Enemies within reach of the player: said when they arrive and every 6 s while they stay. A
    /// sighted player sees the crowd closing in; here the first sign was the health falling.
    /// Space attacks, and <see cref="CombatAim"/> turns the swing towards the nearest of them.
    /// </summary>
    private const float CloseRange = 3.5f;
    private static int _closeCount;
    private static float _closeSaidAt = -100f;

    private static void WatchCloseEnemies()
    {
        var player = MainGame.PlayerController;
        if (player == null) return;
        var from = player.MovablePosition;
        var close = Navigator.SpawnedWgos.Where(IsLiveEnemy)
            .Select(w => new Vector2(w.Data.Position.x - from.x, w.Data.Position.z - from.z))
            .Where(o => o.magnitude <= CloseRange).OrderBy(o => o.sqrMagnitude).ToList();

        if (close.Count > 0 && (close.Count > _closeCount || Time.unscaledTime - _closeSaidAt > 6f))
        {
            _closeSaidAt = Time.unscaledTime;
            Announce(Loc.Fmt("fight.close_enemies", close.Count, Navigator.KeyDirections(close[0])));
        }
        _closeCount = close.Count;
    }

    // ---- what to do next -----------------------------------------------------------------------

    /// <summary>
    /// <b>Line points are taken in order.</b> <c>FightingLine.UpdateSectorsCapturability</c> locks a
    /// point while the one before it on its line has the same owner, and a locked point ignores who
    /// stands on it. Found 2026-10-03 (fourth loss): the mercenaries stood on the target point for
    /// two minutes with no enemy near and it never moved, because "line 2, point 1" before it was
    /// still the enemy's. Only meaningful once the waves run; before that every point says locked.
    /// </summary>
    internal static bool IsLocked(FightingCapturePoint point) =>
        _state == FightState.ActiveFight && point.LockedForCapture && point.OwnedByTeam != LazyConsts.Fighting.TeamType.Player;

    /// <summary>
    /// The points still to take, in the order the game lets them be taken: on every line that ends in
    /// a target point, each point of that line not yet ours, first to last.
    /// </summary>
    internal static List<FightingCapturePoint> ToTake(FightingLevel level)
    {
        var result = new List<FightingCapturePoint>();
        if (level == null || level.FightingLines == null || !OpenObjectives(level).Any()) return result;

        foreach (var line in level.FightingLines)
        {
            if (line == null || line.sectors == null) continue;
            if (!line.sectors.Any(s => s != null && s.point != null && s.point.isBasePoint && !ReferenceEquals(s.point, level.BaseCapturePoint))) continue;
            foreach (var sector in line.sectors)
            {
                var point = sector == null ? null : sector.point;
                if (point == null || ReferenceEquals(point, level.BaseCapturePoint)) continue;
                if (point.OwnedByTeam != LazyConsts.Fighting.TeamType.Player) result.Add(point);
            }
        }
        return result;
    }

    /// <summary>
    /// The flag stands on the battlefield, read from the world objects. Not from
    /// <c>FightingGameController.FlagStandComponents</c> and <c>IsLinkedFlagStand</c>: in the fifth
    /// fight (2026-10-03) that found no stand for either point, so F5 sent the player onto the point
    /// alone instead of to the banner, and they fell there.
    /// </summary>
    private static List<WgoData> Stands() =>
        Navigator.SpawnedWgos
            .Where(w => w != null && !w.IsDespawning && w.Data?.Definition != null && !w.Data.IsHidden
                && w.Data.Definition.interactionType == WGODef.InteractionType.FlagStand)
            .Select(w => w.Data).ToList();

    /// <summary>The stand beside a point: the nearest within 3.5 m of its middle (they sit 0.5-2 m off).</summary>
    private static WgoData StandFor(FightingCapturePoint point, List<WgoData> stands)
    {
        if (point == null) return null;
        var middle = point.transform.position;
        return stands.Where(s => Flat(s.Position - middle) < 3.5f).OrderBy(s => Flat(s.Position - middle)).FirstOrDefault();
    }

    private static bool HoldsFlag(WgoData stand)
    {
        var held = stand.GameResStr.Get("flag_stand_sguid");
        return !string.IsNullOrEmpty(held) && MainGame.WorldData.GetWgoData(SGuid.Parse(held)) != null;
    }

    private static float Flat(Vector3 v) => new Vector2(v.x, v.z).magnitude;

    private static NavTarget StandTarget(WgoData stand, string label) =>
        new(stand.id, stand.Position, stand.WorldId, "objective", label, stand);

    private static NavTarget BaseTarget(FightingLevel level, string label) =>
        new("capture_point_base", level.BaseCapturePoint.transform.position, null, "objective", label);

    /// <summary>
    /// What the player should do next in this fight, as a place to walk to with a label that says
    /// why - F3 says it, F5 walks there. A fight is too fast to page through lists: the fourth fight
    /// had 3 minutes 50 for two points in order, a banner to move twice, and the base to stand on
    /// whenever enemies reach it. Invalid outside a fight.
    /// </summary>
    internal static NavTarget NextStep()
    {
        try
        {
            var level = Level;
            if (level == null || level.BaseCapturePoint == null) return default;

            var basePoint = level.BaseCapturePoint;
            var carrying = MilitaryReader.CarriedFlag() != null;
            var baseThreatened = _state == FightState.ActiveFight && basePoint.enemies.Count > 0 && !PlayerOnBase();

            if (baseThreatened && !carrying) return BaseTarget(level, Loc.Get("fight.step_defend"));

            var stands = Stands();

            var target = ToTake(level).FirstOrDefault();
            if (target != null)
            {
                var name = MilitaryReader.PointName(target);
                var stand = StandFor(target, stands);
                if (stand == null) return new NavTarget("capture_point", target.transform.position, null, "objective", Loc.Fmt("fight.step_take_point", name));
                if (carrying) return StandTarget(stand, Loc.Fmt("fight.step_plant", name));
                if (HoldsFlag(stand)) return BaseTarget(level, Loc.Fmt("fight.step_wait", name));

                var bannerAt = stands.Where(HoldsFlag).OrderBy(st => Flat(st.Position - basePoint.transform.position)).FirstOrDefault();
                if (bannerAt != null) return StandTarget(bannerAt, Loc.Fmt("fight.step_fetch", name));
                _log?.LogInfo($"[Fight] No stand holds a banner ({stands.Count} stands); the banner may be on a barricade.");
            }

            if (carrying)
            {
                var home = stands.OrderBy(st => Flat(st.Position - basePoint.transform.position)).FirstOrDefault();
                if (home != null) return StandTarget(home, Loc.Get("fight.step_plant_home"));
            }

            return BaseTarget(level, Loc.Get("fight.step_hold"));
        }
        catch (Exception ex)
        {
            _log?.LogWarning($"[Fight] Next step failed: {ex.GetType().Name}: {ex.Message}");
            return default;
        }
    }

    private static string _lastStep;

    /// <summary>Says the next step when it has changed, after a point changes hands or the banner moves.</summary>
    private static void AnnounceNextStep()
    {
        var step = NextStep();
        if (!step.IsValid || step.Name == _lastStep) return;
        _lastStep = step.Name;
        Announce(Loc.Fmt("fight.next_f5", step.Name));
    }

    private static string Describe(CapturePointAction action) => action switch
    {
        null => "none",
        CPA_SetSpawnerActive sp => $"{(sp.teamType)} -> spawner line {sp.lineId} '{sp.spawnZoneName}' active={sp.isActive}",
        CPA_SetZombieFogActive fog => $"{fog.teamType} -> zombie fog active={fog.isActive}",
        _ => $"{action.teamType} -> {action.GetType().Name}",
    };

    // ---- why a fight was lost -----------------------------------------------------------------

    /// <summary>Why the last fight was lost, worked out the moment the game decided it; null if unknown.</summary>
    internal static string LossReason { get; private set; }

    /// <summary>Called first thing in <c>FightingGameController.FinishAsLost</c>, before anything is torn down.</summary>
    internal static void RecordLoss()
    {
        try
        {
            if (LossReason != null) return;
            var level = _controller == null ? null : _controller.CurrentLevel;
            if (level == null) return;
            LogPoints("at the loss");

            var hp = MainGame.PlayerData == null ? null : MainGame.PlayerData.hpComponent;
            var open = OpenObjectives(level).FirstOrDefault();
            if (level.BaseCapturePoint != null && level.BaseCapturePoint.OwnedByTeam == LazyConsts.Fighting.TeamType.WildZombie)
                LossReason = Loc.Get("fight.lost_base");
            else if (open != null)
                LossReason = Loc.Fmt("fight.lost_point", MilitaryReader.PointName(open));
            else if (hp != null && hp.Hp <= 0)
                LossReason = Loc.Get("fight.lost_dead");

            _log?.LogInfo($"[Fight] Lost{BaseDistance()}: {LossReason ?? "reason unknown"}; time left {SecondsLeft()} s.");
        }
        catch (Exception ex)
        {
            _log?.LogWarning($"[Fight] Could not work out the loss: {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>Every active capture point, base first, for the object list.</summary>
    internal static IEnumerable<FightingCapturePoint> ActiveCapturePoints() => CapturePoints(Level);

    private static string LineList() =>
        string.Join(", ", _controller.BreachedLines.Where(l => l != null).Select(l => l.lineIdx + 1).OrderBy(i => i));

    private static void WatchBattlefield()
    {
        try
        {
            var level = Level;
            if (level == null || _controller == null) return;

            WatchClock();
            WatchOnBase();
            WatchWaves(level);
            WatchCloseEnemies();

            foreach (var point in CapturePoints(level))
            {
                var owner = point.OwnedByTeam;
                if (Owners.TryGetValue(point, out var before) && before != owner)
                {
                    var key = owner == LazyConsts.Fighting.TeamType.Player ? "fight.point_ours" : "fight.point_theirs";
                    Announce(Loc.Fmt(key, MilitaryReader.PointName(point)));
                    if (!ReferenceEquals(point, level.BaseCapturePoint)) AnnounceNextStep();
                }
                Owners[point] = owner;
            }

            var breached = _controller.BreachedLines.Count;
            if (breached > _breached) Announce(Loc.Fmt("fight.line_breached", LineList()));
            _breached = breached;

            var basePoint = level.BaseCapturePoint;
            if (basePoint != null && basePoint.enemies.Count > 0 && Time.unscaledTime - _baseWarnedAt > 10f)
            {
                _baseWarnedAt = Time.unscaledTime;
                Announce(BaseAttacked(basePoint));
            }

            // 20, 10, 5, 3, 2, 1: the end of the fight coming, without a running count.
            var left = _controller.TargetsDatabase.GetTargetCountByTeam(LazyConsts.Fighting.TeamType.WildZombie);
            var step = left <= 3 ? left : left <= 5 ? 5 : left <= 10 ? 10 : left <= 20 ? 20 : 99;
            if (_lastEnemyStep >= 0 && step < _lastEnemyStep && left > 0)
                Announce(Loc.Plural("fight.enemies_left", left, left));
            _lastEnemyStep = step;
        }
        catch (Exception ex)
        {
            _log?.LogWarning($"[Fight] Battlefield watch failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>Picking up or planting a flag is the only way to give a squad an order: say both.</summary>
    private static void WatchFlag()
    {
        try
        {
            var flag = MilitaryReader.CarriedFlag();
            if (ReferenceEquals(flag, _carried)) return;
            if (flag != null)
            {
                Announce(Loc.Fmt("fight.flag_taken", MilitaryReader.CarriedFlagText()));
            }
            else if (_carried != null && _carried.Data != null)
            {
                var where = MilitaryReader.PointAt(_carried.Data.Position);
                Announce(where == null ? Loc.Get("fight.flag_planted") : Loc.Fmt("fight.flag_planted_at", where));
            }
            _carried = flag;
            AnnounceNextStep();
        }
        catch (Exception ex)
        {
            _log?.LogWarning($"[Fight] Flag watch failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static void AddBattlefieldStatus(List<string> parts)
    {
        var level = Level;
        if (level == null || _controller == null) return;

        var carried = MilitaryReader.CarriedFlagText();
        if (carried != null) parts.Add(Loc.Fmt("fight.carrying", carried));

        if (_state != FightState.ActiveFight) return;

        var seconds = SecondsLeft();
        if (seconds >= 0) parts.Add(Loc.Fmt("fight.time_left", Duration(seconds)));

        var left = _controller.TargetsDatabase.GetTargetCountByTeam(LazyConsts.Fighting.TeamType.WildZombie);
        parts.Add(Loc.Plural("fight.enemies_left", left, left));

        var basePoint = level.BaseCapturePoint;
        if (basePoint != null)
            parts.Add(basePoint.enemies.Count > 0 ? BaseAttacked(basePoint) : Loc.Get("fight.base_safe"));
        if (PlayerOnBase()) parts.Add(Loc.Get("fight.on_base"));
        foreach (var point in Objectives(level))
        {
            var text = Loc.Fmt(point.OwnedByTeam == LazyConsts.Fighting.TeamType.Player ? "fight.objective_ours" : "fight.objective_theirs",
                MilitaryReader.PointName(point), Mathf.RoundToInt(100f * point.CurrentProgress), point.allies.Count, point.enemies.Count);
            parts.Add(IsLocked(point) ? $"{text}, {Loc.Get("mil.locked")}" : text);
        }
        var next = NextStep();
        if (next.IsValid) parts.Add(Loc.Fmt("fight.next", next.Name));

        if (_controller.BreachedLines.Count > 0) parts.Add(Loc.Fmt("fight.line_breached", LineList()));

        if (level.AlliesSpawns != null)
            foreach (var spawn in level.AlliesSpawns)
            {
                if (spawn == null || spawn.Fighters.Count == 0) continue;
                var alive = spawn.Fighters.Count(f => f != null && (f.HpComponent == null || f.HpComponent.Hp > 0));
                var name = spawn.SquadSlotIndex == 0 ? Loc.Get("mil.mercs") : Loc.Fmt("mil.squad", spawn.SquadSlotIndex);
                parts.Add(Loc.Fmt("fight.squad_alive", name, alive, spawn.Fighters.Count));
            }
    }

    /// <summary>
    /// The base warning. With nobody of ours on the circle it drains unopposed, so the warning
    /// says the one thing that stops it.
    /// </summary>
    private static string BaseAttacked(FightingCapturePoint basePoint)
    {
        var held = Mathf.RoundToInt(100f * basePoint.CurrentProgress);
        return basePoint.allies.Count == 0
            ? Loc.Fmt("fight.base_attacked_empty", basePoint.enemies.Count, held)
            : Loc.Fmt("fight.base_attacked", basePoint.enemies.Count, held);
    }

    /// <summary>
    /// Our fighters, one entry per squad, at the middle of its living members - for the object
    /// list. Squad members are not interactable, so the ordinary object list never had them.
    /// </summary>
    internal static IEnumerable<(string label, Vector3 position)> Squads()
    {
        var level = Level;
        if (level == null || level.AlliesSpawns == null) yield break;

        foreach (var spawn in level.AlliesSpawns)
        {
            if (spawn == null || spawn.Fighters.Count == 0) continue;
            var alive = spawn.Fighters.Where(f => f != null && (f.HpComponent == null || f.HpComponent.Hp > 0)).ToList();
            if (alive.Count == 0) continue;

            var middle = Vector3.zero;
            foreach (var f in alive) middle += f.Position;
            middle /= alive.Count;

            var name = spawn.SquadSlotIndex == 0 ? Loc.Get("mil.mercs") : Loc.Fmt("mil.squad", spawn.SquadSlotIndex);
            var label = Loc.Fmt("fight.squad_alive", name, alive.Count, spawn.Fighters.Count);
            var where = MilitaryReader.PointAt(middle);
            if (where != null) label = Loc.Fmt("fight.squad_at", label, where);
            yield return (label, middle);
        }
    }

    private static int Percent(HPComponent hp) => Mathf.Clamp(Mathf.RoundToInt(100f * hp.Hp / hp.MaxHpValue), 0, 100);

    private static int Quarter(HPComponent hp) => Mathf.Clamp(Mathf.CeilToInt(4f * hp.Hp / hp.MaxHpValue), 0, 4);
}
