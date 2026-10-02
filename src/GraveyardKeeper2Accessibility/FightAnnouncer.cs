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
                    ScreenReader.Say(Loc.Get("fight.prefight_long"), interrupt: false);
                    break;
                case FightState.ActiveFight:
                    _playerQuarter = 4;
                    WatchedQuarter.Clear();
                    ResetBattlefield();
                    ScreenReader.Say(Loc.Get("fight.started_long"), interrupt: false);
                    break;
                case FightState.Disabled when previous == FightState.ActiveFight:
                    ScreenReader.Say(Loc.Get("fight.over"), interrupt: false);
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
                ScreenReader.Say(Loc.Fmt("fight.player_hp", Percent(hp)), interrupt: false);
            _playerQuarter = quarter;
        }

        foreach (var (name, component) in Watched())
        {
            var quarter = Quarter(component);
            if (WatchedQuarter.TryGetValue(name, out var last) && quarter < last)
                ScreenReader.Say(Loc.Fmt("fight.object_hp", name, Percent(component)), interrupt: false);
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

        ScreenReader.Say(string.Join(". ", parts));
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
                if (sector != null && sector.point != null && sector.point.isActiveAndEnabled) yield return sector.point;
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

            foreach (var point in CapturePoints(level))
            {
                var owner = point.OwnedByTeam;
                if (Owners.TryGetValue(point, out var before) && before != owner)
                {
                    var key = owner == LazyConsts.Fighting.TeamType.Player ? "fight.point_ours" : "fight.point_theirs";
                    ScreenReader.Say(Loc.Fmt(key, MilitaryReader.PointName(point)), interrupt: false);
                }
                Owners[point] = owner;
            }

            var breached = _controller.BreachedLines.Count;
            if (breached > _breached) ScreenReader.Say(Loc.Fmt("fight.line_breached", LineList()), interrupt: false);
            _breached = breached;

            var basePoint = level.BaseCapturePoint;
            if (basePoint != null && basePoint.enemies.Count > 0 && Time.unscaledTime - _baseWarnedAt > 10f)
            {
                _baseWarnedAt = Time.unscaledTime;
                ScreenReader.Say(Loc.Fmt("fight.base_attacked", basePoint.enemies.Count, Mathf.RoundToInt(100f * basePoint.CurrentProgress)), interrupt: false);
            }

            // 20, 10, 5, 3, 2, 1: the end of the fight coming, without a running count.
            var left = _controller.TargetsDatabase.GetTargetCountByTeam(LazyConsts.Fighting.TeamType.WildZombie);
            var step = left <= 3 ? left : left <= 5 ? 5 : left <= 10 ? 10 : left <= 20 ? 20 : 99;
            if (_lastEnemyStep >= 0 && step < _lastEnemyStep && left > 0)
                ScreenReader.Say(Loc.Plural("fight.enemies_left", left, left), interrupt: false);
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
                ScreenReader.Say(Loc.Fmt("fight.flag_taken", MilitaryReader.CarriedFlagText()), interrupt: false);
            }
            else if (_carried != null && _carried.Data != null)
            {
                var where = MilitaryReader.PointAt(_carried.Data.Position);
                ScreenReader.Say(where == null ? Loc.Get("fight.flag_planted") : Loc.Fmt("fight.flag_planted_at", where), interrupt: false);
            }
            _carried = flag;
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

        var left = _controller.TargetsDatabase.GetTargetCountByTeam(LazyConsts.Fighting.TeamType.WildZombie);
        parts.Add(Loc.Plural("fight.enemies_left", left, left));

        var basePoint = level.BaseCapturePoint;
        if (basePoint != null)
            parts.Add(basePoint.enemies.Count > 0
                ? Loc.Fmt("fight.base_attacked", basePoint.enemies.Count, Mathf.RoundToInt(100f * basePoint.CurrentProgress))
                : Loc.Get("fight.base_safe"));

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

    private static int Percent(HPComponent hp) => Mathf.Clamp(Mathf.RoundToInt(100f * hp.Hp / hp.MaxHpValue), 0, 100);

    private static int Quarter(HPComponent hp) => Mathf.Clamp(Mathf.CeilToInt(4f * hp.Hp / hp.MaxHpValue), 0, 4);
}
