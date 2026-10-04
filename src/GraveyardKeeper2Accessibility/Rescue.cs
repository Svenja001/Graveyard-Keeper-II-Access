namespace GraveyardKeeper2Accessibility;

/// <summary>
/// The way back when the player has ended up somewhere they cannot get out of: fallen through the
/// world, wedged in a wall, on a ledge with no map.
///
/// <para>
/// <b>Asked for (2026-10-04)</b> after a walk carried the player off the warehouse platform and out
/// of the world. Their only way out was to quit unsaved and lose everything since the last night's
/// sleep. A sighted player in the same spot would have quit too, but would also see it coming.
/// </para>
///
/// <para>
/// Once a second the mod notes where the player stands, if it is safe: controls free, no scene
/// running, not on a ladder, not in a fight, solid floor under the feet and the map under them
/// (<see cref="AutoWalk.OffMesh"/>). The rescue key goes back to the newest such spot that is not
/// where the player is now, or to the home travel stone if there is none. It moves the player
/// with the game's own <c>PlayerController.Teleport</c>, the call travel stones and story scenes
/// use: fade out, the scene loaded if needed, position set, fade in. The player is never moved
/// directly. First press says where it would go; a second press within five seconds goes.
/// </para>
/// </summary>
internal static class Rescue
{
    private static ManualLogSource _log;
    private static ConfigEntry<KeyboardShortcut> _key;

    private readonly struct SafeSpot
    {
        internal readonly Vector3 Position;
        internal readonly string SceneId;
        internal readonly string Preset;
        internal readonly float Time;

        internal SafeSpot(Vector3 position, string sceneId, string preset, float time)
        {
            Position = position;
            SceneId = sceneId;
            Preset = preset;
            Time = time;
        }
    }

    private static readonly List<SafeSpot> Spots = new();
    private const int MaxSpots = 60;
    private static float _nextRecordAt;

    private static float _armedAt = -100f;
    private const float ConfirmWindow = 5f;

    private const string HomeStone = "teleport_milestone_6_home";

    /// <summary>Things lying about and the player's own colliders are no floor.</summary>
    private static readonly int FloorMask = Physics.DefaultRaycastLayers & ~(
        (1 << LazyConsts.Layers.PLAYER) | (1 << LazyConsts.Layers.DROP) | (1 << LazyConsts.Layers.PLAYER_DROP) |
        (1 << LazyConsts.Layers.FIGHTER));

    internal static void Init(ManualLogSource log, ConfigFile config)
    {
        _log = log;
        _key = ModKeys.Bind(config, "Keys", "Rescue", new KeyboardShortcut(KeyCode.F5, KeyCode.LeftControl, KeyCode.LeftShift),
            "Emergency: takes you back to the last safe place you stood (or the home travel stone) when you are stuck, " +
            "fell out of the world or cannot get out. First press says where; a second press within 5 seconds goes.");
    }

    /// <summary>Notes safe spots; runs every frame, keys or not.</summary>
    internal static void Record()
    {
        if (Time.unscaledTime < _nextRecordAt) return;
        _nextRecordAt = Time.unscaledTime + 1f;

        try
        {
            var player = MainGame.PlayerController;
            if (!IsSafeNow(player)) return;

            var at = player.MovablePosition;
            var scene = MainGame.PlayerData.currentGameSceneId;
            if (Spots.Count > 0)
            {
                var last = Spots[Spots.Count - 1];
                // A new spot only after moving a little, so the list reaches back further than a
                // minute of standing still.
                if (last.SceneId == scene && Vector3.Distance(last.Position, at) < 1f) return;
            }

            Spots.Add(new SafeSpot(at, scene, EnvironmentEngine.Instance?.Data?.timeOfDayPresetName, Time.unscaledTime));
            if (Spots.Count > MaxSpots) Spots.RemoveAt(0);
        }
        catch
        {
            // Noting a spot is never worth an error in the log every second.
        }
    }

    internal static void Update()
    {
        if (_key == null || !_key.Value.IsDown()) return;

        try
        {
            Press();
        }
        catch (Exception ex)
        {
            _log?.LogError($"[Rescue] Failed: {ex.GetType().Name}: {ex.Message}");
            ScreenReader.Say(Loc.Get("rescue.failed"));
        }
    }

    private static void Press()
    {
        var player = MainGame.PlayerController;
        if (player == null || MainGame.PlayerData == null)
        {
            ScreenReader.Say(Loc.Get("nav.not_in_game"));
            return;
        }

        if (FightAnnouncer.State != FightState.Disabled)
        {
            ScreenReader.Say(Loc.Get("rescue.in_fight"));
            return;
        }

        var held = AutoWalk.SceneHasControl();
        if (held != null)
        {
            ScreenReader.Say(Loc.Fmt("rescue.scene", AutoWalk.ControlName(held.Value)));
            return;
        }

        var target = Choose(player.MovablePosition, MainGame.PlayerData.currentGameSceneId);
        var where = target.HasValue ? Describe(player, target.Value) : Loc.Fmt("rescue.to_home", ObjectNames.Of(HomeStone));

        var armed = Time.unscaledTime - _armedAt < ConfirmWindow;
        if (!armed)
        {
            _armedAt = Time.unscaledTime;
            _log?.LogInfo($"[Rescue] Armed at {player.MovablePosition}; would go to {(target.HasValue ? target.Value.Position.ToString() : HomeStone)}.");
            ScreenReader.Say(Loc.Fmt("rescue.confirm", where));
            return;
        }
        _armedAt = -100f;

        // A walk or a ladder leg must not carry on from the new place.
        if (AutoWalk.IsBusy) AutoWalk.Cancel();

        bool started;
        if (target.HasValue)
        {
            var spot = target.Value;
            started = PlayerController.Teleport(new SpotTeleportData(spot.Position, spot.SceneId, string.IsNullOrEmpty(spot.Preset) ? "outdoor" : spot.Preset));
            _log?.LogWarning($"[Rescue] Teleport from {player.MovablePosition} to safe spot {spot.Position} in '{spot.SceneId}' (preset {spot.Preset}): {started}.");
        }
        else
        {
            started = TeleportHome();
        }

        ScreenReader.Say(started ? Loc.Fmt("rescue.going", where) : Loc.Get("rescue.failed"));
    }

    /// <summary>The newest safe spot that is not where the player stands now.</summary>
    private static SafeSpot? Choose(Vector3 at, string scene)
    {
        for (var i = Spots.Count - 1; i >= 0; i--)
        {
            var spot = Spots[i];
            if (spot.SceneId == scene && Vector3.Distance(spot.Position, at) < 2f) continue;
            if (MainGame.Instance.GameSave.worldData.GetGameSceneDataById(spot.SceneId) == null) continue;
            return spot;
        }
        return null;
    }

    private static string Describe(PlayerController player, SafeSpot spot)
    {
        var ago = Mathf.Max(1, Mathf.RoundToInt(Time.unscaledTime - spot.Time));
        var offset = spot.Position - player.MovablePosition;
        var flat = new Vector2(offset.x, offset.z);
        if (spot.SceneId != MainGame.PlayerData.currentGameSceneId || flat.magnitude > 500f)
            return Loc.Fmt("rescue.to_spot_far", ago);
        return Loc.Fmt("rescue.to_spot", ago, Mathf.RoundToInt(flat.magnitude), Navigator.KeyDirections(flat));
    }

    /// <summary>The home travel stone, where the travel map puts the player.</summary>
    private static bool TeleportHome()
    {
        var world = MainGame.Instance.GameSave.worldData;
        if (!world.TryGetWgoData(HomeStone, out var stone, out _) || stone == null)
        {
            _log?.LogWarning("[Rescue] The home travel stone was not found.");
            return false;
        }

        var point = stone.GetGDPointData("milestone_teleport_point");
        var started = point != null
            ? PlayerController.Teleport(new GDPointTeleportData(point))
            : PlayerController.Teleport(new WgoTeleportData(HomeStone, teleportToDockPoint: true));
        _log?.LogWarning($"[Rescue] Teleport from {MainGame.PlayerController.MovablePosition} to the home travel stone: {started}.");
        return started;
    }

    private static bool IsSafeNow(PlayerController player)
    {
        if (player == null || MainGame.PlayerData == null || !player.IsControlsEnabled) return false;
        if (AutoWalk.SceneHasControl() != null || LadderRoute.Climbing(player)) return false;
        if (FightAnnouncer.State != FightState.Disabled) return false;
        if (string.IsNullOrEmpty(MainGame.PlayerData.currentGameSceneId)) return false;

        var at = player.MovablePosition;
        if (!Physics.Raycast(at + Vector3.up * 0.3f, Vector3.down, 0.7f, FloorMask, QueryTriggerInteraction.Ignore)) return false;
        return Reachability.FloorGraph(player) != null && !AutoWalk.OffMesh(player);
    }

    /// <summary>A place and the scene it is in, for the game's own teleport.</summary>
    private sealed class SpotTeleportData : TeleportDataBase
    {
        private readonly Vector3 _position;
        private readonly string _sceneId;

        internal SpotTeleportData(Vector3 position, string sceneId, string preset) : base(preset)
        {
            _position = position;
            _sceneId = sceneId;
        }

        public override string GetDestinationId() => "accessibility_rescue";

        public override GameSceneData GetDestinationSceneData() =>
            MainGame.Instance.GameSave.worldData.GetGameSceneDataById(_sceneId);

        public override Vector3 GetPosition() => _position;
    }
}
