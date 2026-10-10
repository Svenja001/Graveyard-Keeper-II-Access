namespace GraveyardKeeper2Accessibility;

/// <summary>
/// Walks the player to something, instead of only telling them where it is.
///
/// <para>
/// A bearing and a distance turn out not to be enough in a 3D world with elevation, fences and
/// buildings: "12 metres, 2 o'clock" is a straight line, and the way there usually is not. That is
/// what the plan's §4 Phase 3 is for, and it was supposed to be a multi-session job.
/// </para>
///
/// <para>
/// <b>It is not, because the game already does it.</b> <c>Flow_GoTo</c> - the flowscript node that
/// walks the player around during scripted sequences - does it with
/// <c>MainGame.PlayerController.MovementComponent.StartPath(...)</c>, passing the player's own
/// <c>PlayerLocalAreaMovement.Seeker</c>. So the mod does not need a pathfinder, a follower, or a
/// stuck-detector: it calls the same method the game's own cutscenes call and lets the game walk.
/// </para>
///
/// <para>
/// This retires the entire worst-bug category from the GK1 mod in one go. A route the game
/// considers valid cannot clip a wall, cannot strand the player off the navmesh, and does not need
/// breadcrumbs to recover - because it is the game moving its own player, not the mod dragging a
/// transform around.
/// </para>
/// </summary>
internal static class AutoWalk
{
    private static ManualLogSource _log;
    private static ConfigEntry<KeyboardShortcut> _walkObjectiveKey;
    private static ConfigEntry<KeyboardShortcut> _releaseControlKey;
    private static ConfigEntry<bool> _allowLongDistance;

    /// <summary>
    /// The ways the game takes the player away from the player. While any of them holds, the game
    /// is moving the player itself, and the mod must neither start a walk nor stop one.
    ///
    /// <para>
    /// <b>Why this matters: it soft-locked a whole session.</b> The forest guards' flowscript
    /// (<c>Event_31_Forest_Guards</c>) takes control, stops the player, and walks them back out of
    /// the zone with <c>Flow_GoTo</c>. It hands control back only in that GoTo's finish callback.
    /// The player pressed autowalk during the scene, and the mod's <c>StartPath</c> replaced the
    /// flow's path along with its callback. The flow waited forever, <c>ByFlow</c> never came back,
    /// and nothing but the mod's own keys worked until the game was quit, unsaved.
    /// </para>
    /// </summary>
    private static readonly TakenControlType[] SceneControl =
    {
        TakenControlType.ByFlow, TakenControlType.ByCinematics, TakenControlType.ByTeleport,
        TakenControlType.BySleep, TakenControlType.ByDeath,
    };

    /// <summary>When the release key was first pressed, for the press-twice confirmation.</summary>
    private static float _releaseArmedAt = -100f;
    private const float ReleaseConfirmWindow = 5f;

    /// <summary>Where we are walking, for the arrival announcement and to answer "again?".</summary>
    private static string _destinationName;

    /// <summary>
    /// Where the thing itself is, when the walk goes to a spot beside it instead. The player is
    /// turned towards it on arrival, because the game's interaction box sits in front of the
    /// player in the direction they face.
    /// </summary>
    private static Vector3? _faceTowards;
    private static bool _walking;

    /// <summary>
    /// The object the walk is meant to end in front of, so the arrival can be checked against what
    /// the game actually picked (see <see cref="InteractionSpot"/>). Null for ground items, zones
    /// and landmarks.
    /// </summary>
    private static WgoData _interactWith;
    private static NavTarget _interactTarget;

    /// <summary>Stand points from which the game picks <see cref="_interactWith"/>, best first.</summary>
    private static List<InteractionSpot.Spot> _spots;
    private static int _spotIndex;
    private static bool _replanned;

    /// <summary>The cardinal direction to face at the stand point; null to face the thing itself.</summary>
    private static Vector2? _faceDirection;

    /// <summary>
    /// When to check what the game is targeting after arriving. The component updates its target in
    /// its own <c>Update</c>, and the facing set on arrival needs a frame to settle. Negative when
    /// nothing is waiting.
    /// </summary>
    private static float _verifyAt = -1f;
    private static bool _faceAgain;
    private static Vector3 _arrivedAt;
    private const float VerifyDelay = 0.35f;
    private const int MaxSpotTries = 3;

    /// <summary>True while walking to a second stand point: the player already heard "walking to".</summary>
    private static bool _quiet;
    private static string _lastPicked;

    /// <summary>
    /// A walk that goes over a bridge the navmesh does not cross (see <see cref="BridgeCrossing"/>)
    /// runs in three legs: to the near end, straight over, then on to the target. The next leg is
    /// started from <see cref="Update"/>, never from the arrival callback: the game calls
    /// <c>OnPathComplete</c> right after that callback, which would undo a path started inside it.
    /// </summary>
    private enum Leg { None, ToBridge, Crossing, ToLadder, AtLadder }
    private static Leg _leg;
    private static bool _nextLegDue;
    private static NavTarget _finalTarget;
    private static BridgeCrossing.Span _span;
    private static int _bridgesCrossed;
    private const int MaxBridges = 3;

    /// <summary>
    /// A walk to another level runs in legs too: to the foot (or head) of the ladder, then a wait
    /// while the player climbs - E, then Up or Down, which the mod leaves to them - and on from the
    /// other end once the climb is over. See <see cref="LadderRoute"/>.
    /// </summary>
    private static LadderRoute.Link _ladder;
    private static int _laddersClimbed;
    private static bool _climbSeen;
    private static float _ladderSince;
    private static float _ladderCheckAt = -1f;

    /// <summary>
    /// Stand points at the ladder, best first, and the one in use. As at any object, the game may
    /// pick nothing from the first: the last step of the path decides the facing. Reported
    /// (2026-10-04): a walk straight to the ladder worked on its second or third spot, but the
    /// ladder leg of a walk to the order board stopped at the first, where E reached nothing.
    /// </summary>
    private static List<InteractionSpot.Spot> _ladderSpots;
    private static int _ladderSpotIndex;
    private const int MaxLadders = 3;
    private const float LadderPatience = 180f;

    /// <summary>
    /// The walk we have asked for but which has not started moving yet, and when we asked.
    ///
    /// <para>
    /// <b>This is the whole reason the first version silently did nothing.</b>
    /// <c>StartPath</c> returns <c>Started</c> as soon as it has <i>requested</i> a path, not when
    /// it has one - the search is asynchronous. Worse, when the destination fails its reachability
    /// test <c>FindPathRecastGraph</c> logs to the Unity console and returns without calling the
    /// finish callback or changing status. So "Started" meant nothing, the fallback to the
    /// long-distance graph never ran because the result was never anything else, and the player
    /// heard "walking to X" followed by silence and no movement, indefinitely.
    /// </para>
    /// </summary>
    private static NavTarget _pending;
    private static float _pendingSince;
    private static bool _triedFallback;

    /// <summary>
    /// How long to let a path search run before calling it a failure. Generous enough for a long
    /// route on a loaded scene, short enough that a player who is going nowhere finds out quickly.
    /// </summary>
    private const float PathTimeout = 3f;

    private static readonly AccessTools.FieldRef<MovementComponent, MovementComponent.Status> MovementStatus =
        AccessTools.FieldRefAccess<MovementComponent, MovementComponent.Status>("status");

    /// <summary>The route the game has just planned, read to name the zones it passes through.</summary>
    private static readonly AccessTools.FieldRef<MovementComponent, Pathfinding.ABPath> WholePath =
        AccessTools.FieldRefAccess<MovementComponent, Pathfinding.ABPath>("wholePath");

    /// <summary>
    /// Length of the planned route, from the game's own <c>SetOnPathLengthReady</c> callback (the
    /// one <c>Flow_GoTo</c> uses). Negative until the path is known.
    /// </summary>
    private static float _routeLength = -1f;

    /// <summary>Where the walk started, to compare the route with the straight line.</summary>
    private static Vector3 _walkFrom;

    /// <summary>
    /// Matches the speed <c>Flow_GoTo</c> uses by default. Deliberately the game's own walking
    /// pace rather than something faster: arriving is not the only point, and a player who is
    /// moved faster than the game expects can outrun its own streaming and triggers.
    /// </summary>
    private const float WalkSpeed = 1.5f;

    /// <summary>Keys that mean "stop, I will take it from here".</summary>
    private static readonly KeyCode[] CancelKeys =
    {
        KeyCode.W, KeyCode.A, KeyCode.S, KeyCode.D,
        KeyCode.UpArrow, KeyCode.DownArrow, KeyCode.LeftArrow, KeyCode.RightArrow,
        KeyCode.Space, KeyCode.Escape,
    };

    internal static void Init(ManualLogSource log, ConfigFile config)
    {
        _log = log;

        _walkObjectiveKey = ModKeys.Bind(config,
            "Keys", "WalkToObjective", new KeyboardShortcut(KeyCode.F5),
            "Walks to the current objective - whatever the game's tutorial arrow points at. " +
            "Press again, or any movement key, to stop.");

        _releaseControlKey = ModKeys.Bind(config,
            "Keys", "ReleaseControl", new KeyboardShortcut(KeyCode.F5, KeyCode.LeftControl),
            "Emergency key for when the game has taken your controls and never gives them back. " +
            "First press says what is holding control; a second press within 5 seconds takes it back.");

        _allowLongDistance = config.Bind(
            "Walking", "AllowLongDistanceRoutes", false,
            "When the local route fails, try the game's long-distance route network instead. " +
            "Off by default: that network is what NPCs use to cross the map and it ignores " +
            "story blockages and walls, so it can carry you out of an area the story has not " +
            "opened yet - in the prison intro it walked straight through the rubble and out.");
    }

    internal static void Update()
    {
        try
        {
            if (_walkObjectiveKey != null && _walkObjectiveKey.Value.IsDown())
                WalkTo(Navigator.ObjectiveTarget(), Loc.Get("nav.no_objective"));

            if (_releaseControlKey != null && _releaseControlKey.Value.IsDown())
                ReleaseControl();

            if (_nextLegDue && SceneHasControl() == null)
            {
                _nextLegDue = false;
                NextLeg();
                return;
            }

            if ((_pending.IsValid || _walking || _nextLegDue || _verifyAt >= 0f) && SceneHasControl() != null)
            {
                // The game has taken over - usually it has already stopped our walk and is about to
                // move the player itself. Let go without touching the movement: stopping it now
                // would stop the scene's walk, not ours.
                _log?.LogInfo($"[Walk] The game took control ({SceneHasControl()}); abandoning the walk to '{_destinationName}'.");
                _pending = default;
                _walking = false;
                ResetLegs();
                ResetInteraction();
                return;
            }

            if (_faceAgain)
            {
                // Once more a frame after arriving: the game's own end-of-path handling runs after
                // the arrival callback and may leave the walking direction in place.
                _faceAgain = false;
                Face();
            }

            if (_leg == Leg.AtLadder)
            {
                WatchLadder();
                return;
            }

            // A menu opened meanwhile clears the game's target; check once it is closed again.
            if (_verifyAt >= 0f && MainGame.PlayerController != null && !MainGame.PlayerController.IsControlsEnabled)
                _verifyAt = Mathf.Max(_verifyAt, Time.unscaledTime + VerifyDelay);

            if (_verifyAt >= 0f && Time.unscaledTime >= _verifyAt)
            {
                Verify();
                return;
            }

            if (_pending.IsValid) WatchPending();
            else if (_walking) WatchProgress();
        }
        catch (Exception ex)
        {
            _log?.LogError($"[Walk] Update failed: {ex.GetType().Name}: {ex.Message}");
            _walking = false;
            ResetLegs();
        }
    }

    /// <summary>
    /// Starts walking to <paramref name="target"/>, or stops if a walk is already under way.
    ///
    /// The same key stops as starts, so a player who set off towards the wrong thing, or who
    /// simply wants to stand still, never has to remember a second key.
    /// </summary>
    internal static void WalkTo(NavTarget target, string nothingMessage = null)
    {
        if (SceneHasControl() != null)
        {
            ScreenReader.Say(Loc.Get("walk.scene_busy"));
            return;
        }

        if (_walking || _pending.IsValid || _nextLegDue || _leg == Leg.AtLadder)
        {
            Stop(Loc.Get("walk.stopped"));
            return;
        }

        // A route still being worked out for the list would be spoken over the walk's own start.
        RouteInfo.Cancel();

        if (!target.IsValid)
        {
            ScreenReader.Say(nothingMessage ?? Loc.Get("nav.nothing_selected"));
            return;
        }

        ResetLegs();
        ResetInteraction();
        FocusLock.Release("a new walk");
        _bridgesCrossed = 0;
        _laddersClimbed = 0;
        _interactTarget = target;
        _interactWith = target.Data != null && target.Data.IsInteractable ? target.Data : null;
        Start(target);
    }

    private static void Start(NavTarget target)
    {
        var player = MainGame.PlayerController;
        if (player == null)
        {
            ScreenReader.Say(Loc.Get("nav.not_in_game"));
            return;
        }

        var movement = player.MovementComponent;
        if (movement == null)
        {
            ScreenReader.Say(Loc.Get("walk.unavailable"));
            return;
        }

        // No map under the player's feet - a platform the room's map does not cover, the map a
        // storey below. A walk planned from here is a straight push towards that map, and on
        // 2026-10-04 it pushed the player off the warehouse platform and out of the world.
        if (OffMesh(player))
        {
            _log?.LogWarning($"[Walk] Not walking to '{target.Id}': no floor on the map under the player at {player.MovablePosition}.");
            ResetLegs();
            ResetInteraction();
            ScreenReader.Say(Loc.Get("walk.off_mesh_refused"));
            return;
        }

        _destinationName = target.Name;
        _triedFallback = false;
        _faceTowards = target.Position;
        _faceDirection = null;

        // Something to use: stand where the game will pick it, not just somewhere near it. See
        // InteractionSpot for why "near" was not enough.
        if (_interactWith != null)
        {
            _spots = InteractionSpot.Find(player, _interactWith);
            _spotIndex = 0;
            if (_spots.Count > 0)
            {
                _log?.LogInfo($"[Walk] '{target.Id}': {_spots.Count} spot(s) the game picks it from; using {_spots[0].Position} facing {_spots[0].Facing}.");
                WalkToSpot(target);
                return;
            }
            _log?.LogInfo($"[Walk] '{target.Id}': no spot found yet that the game picks it from.");
        }

        // On another level, joined to this one by a ladder: go to the ladder first.
        if (_laddersClimbed < MaxLadders)
        {
            var link = LadderRoute.Find(player, target.Position);
            if (link.HasValue)
            {
                StartLadderLeg(player, target, link.Value);
                return;
            }
        }

        // A story zone: stop on floor inside it, by the game's own test. Its middle is a point in
        // mid-air, and the approach search below could end beside a small zone rather than in it -
        // "arrived", and nothing happened (user, 2026-09-27). None reachable from this side (a
        // bridge first, say): fall through, and the next leg asks again.
        if (target.Zone != null)
        {
            var inside = ZoneEntry.Spot(player, target.Zone);
            if (inside.HasValue)
            {
                _log?.LogInfo($"[Walk] Zone '{ZoneEntry.Name(target.Zone)}': walking to {inside.Value} inside it.");
                _faceTowards = null;
                RequestPath(new NavTarget(target.Id, inside.Value, target.WorldId, target.CategoryKey, target.Label, zone: target.Zone), MovementType.Recast);
                return;
            }
            _log?.LogInfo($"[Walk] Zone '{ZoneEntry.Name(target.Zone)}': no reachable floor inside it from here.");
        }

        // An obstacle's own position is inside the obstacle - rubble, a boulder, a wall - so the
        // navmesh has no floor there, and the nearest floor may well be on the far side of it. The
        // prison rubble is exactly that: four metres from the player, "no route" every time. So
        // when the thing itself cannot be reached, walk to the nearest spot beside it that can.
        // Across a bridge or an open door the navmesh does not cover: walk to its near end first,
        // cross, and plan the rest from the far side. Tried before the approach below: from inside
        // the resurrection room the morgue exit is 4 m away through a wall, and the approach
        // walked to the wall instead of out through the door (2026-09-27). Find only returns a
        // span when the target's floor is in another navmesh area, so rubble is unaffected.
        if (!Reachable(player, target.Position) && _bridgesCrossed < MaxBridges)
        {
            var span = BridgeCrossing.Find(player, target.Position);
            if (span.HasValue)
            {
                _finalTarget = target;
                _span = span.Value;
                _leg = Leg.ToBridge;
                RequestPath(new NavTarget(target.Id + " (bridge)", _span.NearEnd, target.WorldId, target.CategoryKey, target.Label), MovementType.Recast);
                return;
            }
        }

        var approach = Approach(player, target.Position);
        if (approach.HasValue)
        {
            _log?.LogInfo($"[Walk] '{target.Id}' is not reachable itself; approaching it at {approach.Value}.");
            target = new NavTarget(target.Id, approach.Value, target.WorldId, target.CategoryKey, target.Label);
        }

        // Recast is the scene navmesh and is what short, local journeys want. The GD-point graph
        // is the game's coarse long-distance network, tried only if the local search fails.
        RequestPath(target, MovementType.Recast);
    }

    private static void RequestPath(NavTarget target, MovementType type)
    {
        var player = MainGame.PlayerController;
        var movement = player == null ? null : player.MovementComponent;
        if (movement == null) return;

        _routeLength = -1f;
        _walkFrom = player.MovablePosition;
        try
        {
            movement.SetOnPathLengthReady(length => _routeLength = length);
        }
        catch (Exception ex)
        {
            _log?.LogWarning($"[Walk] Could not ask for the route length: {ex.Message}");
        }

        var result = TryStart(movement, player, target, type);

        if (result == MovementComponent.StartPathResult.AlreadyAtDestinationPoint)
        {
            // StartPath has already called OnArrived for this case, which did the rest.
            _pending = default;
            return;
        }

        if (result != MovementComponent.StartPathResult.Started)
        {
            FailOrFallback(target);
            return;
        }

        // Nothing is announced yet. Saying "walking to X" here would be a promise the game has not
        // made: the path search has only been requested, and it may quietly turn out there is no
        // route at all.
        _pending = target;
        _pendingSince = Time.unscaledTime;
        _log?.LogInfo(
            $"[Walk] Requested {type} path to '{target.Id}' at {target.Position} " +
            $"from {player.MovablePosition}.");
    }

    /// <summary>
    /// Watches a requested path until it starts moving, fails, or runs out of patience - the
    /// asynchronous half of starting a walk.
    /// </summary>
    private static void WatchPending()
    {
        var movement = MainGame.PlayerController == null ? null : MainGame.PlayerController.MovementComponent;
        if (movement == null)
        {
            _pending = default;
            return;
        }

        if (movement.IsMoving)
        {
            _walking = true;
            var target = _pending;
            _pending = default;
            var line = _leg == Leg.ToBridge
                ? Loc.Fmt("walk.started_via_bridge", _destinationName, _span.Name)
                : _leg == Leg.ToLadder
                    ? Loc.Fmt(_ladder.Up ? "walk.started_via_ladder_up" : "walk.started_via_ladder_down", _destinationName, _ladder.Name)
                    : Loc.Fmt("walk.started", _destinationName);
            if (!_quiet)
            {
                var note = _leg == Leg.None ? RouteNote(movement, _faceTowards ?? target.Position) : null;
                ScreenReader.Say(note == null ? line : $"{line}. {note}");
            }
            _quiet = false;
            _log?.LogInfo($"[Walk] Moving to '{target.Id}' (route {_routeLength:0} m).");
            return;
        }

        // Still searching is fine; anything else means the search ended without a route, which the
        // component reports by simply going quiet.
        var stillSearching = Status(movement) == MovementComponent.Status.PathBuilding;
        if (stillSearching && Time.unscaledTime - _pendingSince < PathTimeout) return;
        if (!stillSearching && Time.unscaledTime - _pendingSince < 0.1f) return;

        FailOrFallback(_pending);
    }

    /// <summary>
    /// A detour or a story zone on the way, said as the walk starts; null for an ordinary walk,
    /// which stays as short to hear as before. See <see cref="RouteInfo"/>.
    /// </summary>
    private static string RouteNote(MovementComponent movement, Vector3 thing)
    {
        try
        {
            if (_routeLength <= 0f) return null;
            var path = WholePath(movement)?.vectorPath;
            return RouteInfo.Describe(_routeLength, Flat(thing - _walkFrom), path, _walkFrom, full: false);
        }
        catch (Exception ex)
        {
            _log?.LogWarning($"[Walk] Could not describe the route: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// The spot a walk to <paramref name="target"/> would end at: the thing itself, or the nearest
    /// reachable spot beside it. Shared with <see cref="RouteInfo"/> so the length it reports is the
    /// length of the walk that would really happen.
    /// </summary>
    internal static Vector3 WalkPoint(PlayerController player, Vector3 target) => Approach(player, target) ?? target;

    private static MovementComponent.Status Status(MovementComponent movement)
    {
        try { return MovementStatus(movement); }
        catch { return MovementComponent.Status.None; }
    }

    /// <summary>
    /// The local navmesh could not get there. <b>That answer is trustworthy</b>: the game snaps both
    /// ends to the nearest walkable point before testing, so a failure means the way is really shut
    /// - usually by something the story wants cleared first. Optionally try the long-distance graph
    /// once (see <c>AllowLongDistanceRoutes</c>), then say so -
    /// out loud, because a walk that silently does not happen is indistinguishable from a mod
    /// that has stopped working.
    /// </summary>
    private static void FailOrFallback(NavTarget target)
    {
        _pending = default;

        if (target.IsValid && !_triedFallback && _allowLongDistance != null && _allowLongDistance.Value)
        {
            _triedFallback = true;
            _log?.LogInfo($"[Walk] No local route to '{target.Id}', trying the long-distance graph.");
            RequestPath(target, MovementType.GDGraph);
            return;
        }

        _walking = false;
        ResetLegs();
        var wasRetry = _quiet;
        _quiet = false;

        // StartPath has already called PlayerController.OnPathStart, which makes the body kinematic
        // for the path, and a search that fails quietly never calls OnPathComplete to undo it - so
        // the player's own movement keys stopped working until the next walk that arrived.
        // ForceStop is what calls OnPathComplete.
        try
        {
            var movement = MainGame.PlayerController == null ? null : MainGame.PlayerController.MovementComponent;
            if (movement != null && !movement.IsMoving) movement.ForceStop();
        }
        catch (Exception ex)
        {
            _log?.LogWarning($"[Walk] Could not release the failed path: {ex.Message}");
        }

        // A second stand point that turned out unreachable: the player is already beside the thing,
        // so say what is in front of them rather than "no route".
        if (wasRetry)
        {
            _log?.LogWarning($"[Walk] Could not reach the next spot for '{target.Id}'.");
            ReportArrival(_lastPicked);
            return;
        }

        ResetInteraction();

        // Standing where the navmesh has no floor, every route fails - and the game's own
        // reachability test throws. Seen 2026-09-27 after a pause during a walk: every walk said
        // "no route" until the player quit. Say what is really wrong.
        var player = MainGame.PlayerController;
        if (OffMesh(player))
        {
            _log?.LogWarning($"[Walk] The player at {player.MovablePosition} is off the navmesh.");
            ScreenReader.Say(Loc.Get("walk.off_mesh"));
            return;
        }

        // Still say where it is. Walking by hand needs a direction, and a far target may simply be
        // beyond what one pathfinding request covers.
        if (target.IsValid && player != null)
        {
            var offset = target.Position - player.MovablePosition;
            var flat = new Vector2(offset.x, offset.z);
            ScreenReader.Say(Loc.Fmt("walk.no_route_where", _destinationName,
                Mathf.RoundToInt(flat.magnitude), Navigator.KeyDirections(flat)));
        }
        else
        {
            ScreenReader.Say(Loc.Fmt("walk.no_route", _destinationName));
        }
        _log?.LogWarning($"[Walk] No route to '{(target.IsValid ? target.Id : "?")}'.");
    }

    private static MovementComponent.StartPathResult TryStart(
        MovementComponent movement, PlayerController player, NavTarget target, MovementType type)
    {
        try
        {
            // A Recast walk searches the scene's graph only. Where the player's floor is on another
            // one - a room the game gave no navmesh, see InteriorNavmesh - search that graph
            // instead, through the overload NPCs use to walk inside world zones.
            var floor = type == MovementType.Recast ? Reachability.FloorGraph(player) : null;
            if (floor != null && floor != player.SceneRecastGraph)
            {
                _log?.LogInfo($"[Walk] Walking on graph {floor.graphIndex}, not the scene's.");
                return movement.StartPath(
                    target.Position,
                    Pathfinding.GraphMask.FromGraphIndex(floor.graphIndex),
                    MainGame.PlayerData.currentGameSceneId,
                    WalkSpeed,
                    string.Empty,
                    OnArrived);
            }

            return movement.StartPath(
                target.Position,
                MainGame.PlayerData.currentGameSceneId,
                string.IsNullOrEmpty(target.WorldId) ? MainGame.PlayerData.currentGameSceneId : target.WorldId,
                type,
                WalkSpeed,
                string.Empty,
                OnArrived,
                player.PlayerLocalAreaMovement == null ? null : player.PlayerLocalAreaMovement.Seeker);
        }
        catch (Exception ex)
        {
            _log?.LogWarning($"[Walk] {type} path failed from {player.MovablePosition}: {ex}");
            return MovementComponent.StartPathResult.IncorrectMovementType;
        }
    }

    /// <summary>
    /// Watches for the player taking over, and for the walk ending without the callback - which
    /// happens when the game stops the movement itself, for a cutscene or a conversation.
    /// </summary>
    private static void WatchProgress()
    {
        foreach (var key in CancelKeys)
        {
            if (!Input.GetKeyDown(key)) continue;
            Stop(Loc.Get("walk.stopped"));
            return;
        }

        var movement = MainGame.PlayerController == null ? null : MainGame.PlayerController.MovementComponent;
        if (movement == null || !movement.IsMoving)
        {
            // Silent: either OnArrived already spoke, or the game stopped the walk for a reason it
            // is about to explain itself. Announcing "stopped" over the start of a cutscene would
            // be worse than saying nothing.
            _walking = false;
        }
    }

    private static void OnArrived()
    {
        _walking = false;

        if (_leg != Leg.None)
        {
            _nextLegDue = true;
            return;
        }

        Face();

        // Something to use: say "arrived" only once it is certain what E will do here.
        if (_interactWith != null)
        {
            _faceAgain = true;
            _verifyAt = Time.unscaledTime + VerifyDelay;
            var player = MainGame.PlayerController;
            _arrivedAt = player == null ? Vector3.zero : player.MovablePosition;
            return;
        }

        _faceTowards = null;
        ScreenReader.Say(Loc.Fmt("walk.arrived", _destinationName));
    }

    /// <summary>Starts, or restarts, the walk to the current stand point in <see cref="_spots"/>.</summary>
    private static void WalkToSpot(NavTarget target)
    {
        var spot = _spots[_spotIndex];
        _faceDirection = spot.Facing;
        RequestPath(new NavTarget(target.Id, spot.Position, target.WorldId, target.CategoryKey, target.Label), MovementType.Recast);
    }

    /// <summary>
    /// After arriving at something to use: is it what the game picked? If not, the next stand
    /// point is tried quietly, and only when none works is the player told what is in front of them
    /// instead - never just "arrived" in front of the wrong thing, which is how the millstone
    /// opened a chest.
    /// </summary>
    private static void Verify()
    {
        _verifyAt = -1f;
        var player = MainGame.PlayerController;
        if (player == null || _interactWith == null)
        {
            ResetInteraction();
            return;
        }

        // The player walked off in the meantime: what is in front of them now is their business.
        if (Flat(player.MovablePosition - _arrivedAt) > 0.3f)
        {
            ResetInteraction();
            return;
        }

        var picked = InteractionSpot.CurrentTarget(player, out var wgo);
        _lastPicked = picked;
        if (wgo == _interactWith)
        {
            _log?.LogInfo($"[Walk] Arrived at '{_interactWith.id}'; the game targets it.");
            // Held as well, so the facing settling a frame later cannot lose it again.
            FocusLock.Hold(_interactWith);
            ReportArrival(picked);
            return;
        }

        _log?.LogInfo(
            $"[Walk] Arrived for '{_interactWith.id}' at {player.MovablePosition} facing {player.MovableDirection}, " +
            $"but the game targets '{picked ?? "nothing"}'.");

        // Close enough: make it the target, as GK1's mod did, rather than walking on to another
        // spot or saying "nothing in front of you". See FocusLock.
        if (FocusLock.Hold(_interactWith))
        {
            ReportArrival(picked);
            return;
        }

        // Planned from far away the thing may not have existed yet; it does now.
        if ((_spots == null || _spots.Count == 0) && !_replanned)
        {
            _replanned = true;
            _spots = InteractionSpot.Find(player, _interactWith);
            _spotIndex = -1;
        }

        if (_spots != null && _spotIndex + 1 < _spots.Count && _spotIndex + 1 < MaxSpotTries)
        {
            _spotIndex++;
            _log?.LogInfo($"[Walk] Trying spot {_spotIndex + 1} of {_spots.Count} at {_spots[_spotIndex].Position} facing {_spots[_spotIndex].Facing}.");
            _quiet = true;
            WalkToSpot(_interactTarget);
            return;
        }

        ReportArrival(picked);
    }

    /// <summary>Says the arrival, and what E will reach if it is not the thing walked to.</summary>
    private static void ReportArrival(string picked)
    {
        var player = MainGame.PlayerController;
        InteractionSpot.CurrentTarget(player, out var wgo);
        var name = _destinationName;
        var wanted = _interactWith;
        ResetInteraction();

        if (wanted == null || wgo == wanted)
            ScreenReader.Say(Loc.Fmt("walk.arrived", name));
        else if (string.IsNullOrEmpty(picked))
            ScreenReader.Say(Loc.Fmt("walk.arrived_nothing", name));
        else
            ScreenReader.Say(Loc.Fmt("walk.arrived_other", name, ItemText.Name(picked)));
    }

    private static void ResetInteraction()
    {
        _verifyAt = -1f;
        _faceAgain = false;
        _quiet = false;
        _spots = null;
        _spotIndex = 0;
        _replanned = false;
        _faceDirection = null;
        _faceTowards = null;
        _interactWith = null;
        _interactTarget = default;
        _lastPicked = null;
    }

    /// <summary>
    /// True when there is no floor under the player: the navmesh's nearest point is far away or
    /// missing, so no route can start here.
    /// </summary>
    internal static bool OffMesh(PlayerController player)
    {
        try
        {
            var graph = Reachability.FloorGraph(player);
            if (graph == null) return false;
            var nearest = graph.GetNearest(player.MovablePosition);
            return nearest.node == null || Flat(nearest.position - player.MovablePosition) > 1f ||
                   Mathf.Abs(nearest.position.y - player.MovablePosition.y) > Reachability.MaxStandGap;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// A reachable spot next to <paramref name="target"/>, or null when the target itself is
    /// reachable (walk straight there) or nothing near it is.
    ///
    /// <para>
    /// Uses the same test the game does - <c>PathUtilities.IsPathPossible</c> between the nearest
    /// navmesh nodes - so a spot chosen here is one <c>StartPath</c> will accept. Rings are tried
    /// from the inside out, so the chosen spot is as close to the thing as the floor allows; within
    /// a ring the spot nearest the player wins, so the approach is from the player's own side.
    /// </para>
    /// </summary>
    private static Vector3? Approach(PlayerController player, Vector3 target)
    {
        try
        {
            var graph = Reachability.FloorGraph(player);
            if (graph == null) return null;

            var from = player.MovablePosition;
            var start = graph.GetNearest(from).node;
            if (start == null) return null;

            var direct = graph.GetNearest(target);
            if (direct.node != null && Pathfinding.PathUtilities.IsPathPossible(start, direct.node)
                && Flat(direct.position - target) < 1.5f)
                return null;

            foreach (var radius in ApproachRadii)
            {
                Vector3? best = null;
                var bestDistance = float.MaxValue;

                for (var i = 0; i < ApproachSteps; i++)
                {
                    var angle = i * Mathf.PI * 2f / ApproachSteps;
                    var probe = target + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radius;

                    var nearest = graph.GetNearest(probe);
                    if (nearest.node == null || !nearest.node.Walkable) continue;

                    // The snapped point must still be beside the target, not somewhere the
                    // snapping carried it off to.
                    if (Flat(nearest.position - target) > radius + 0.75f) continue;
                    if (!Pathfinding.PathUtilities.IsPathPossible(start, nearest.node)) continue;

                    var distance = Flat(nearest.position - from);
                    if (distance >= bestDistance) continue;
                    bestDistance = distance;
                    best = nearest.position;
                }

                if (best.HasValue) return best;
            }
        }
        catch (Exception ex)
        {
            _log?.LogWarning($"[Walk] Could not look for a way to approach the target: {ex.Message}");
        }

        return null;
    }

    private static readonly float[] ApproachRadii = { 0.75f, 1.25f, 1.75f, 2.5f, 3.5f, 5f };
    private const int ApproachSteps = 16;

    private static float Flat(Vector3 v) => new Vector2(v.x, v.z).magnitude;

    private static void ResetLegs()
    {
        _leg = Leg.None;
        _nextLegDue = false;
        _finalTarget = default;
        _ladder = default;
        _climbSeen = false;
        _ladderCheckAt = -1f;
        _ladderSpots = null;
        _ladderSpotIndex = 0;
    }

    /// <summary>Walks to a spot where the game picks the ladder, at the end on the player's level.</summary>
    private static void StartLadderLeg(PlayerController player, NavTarget target, LadderRoute.Link link)
    {
        _finalTarget = target;
        _ladder = link;
        _leg = Leg.ToLadder;
        _faceTowards = link.NearEnd;
        _faceDirection = null;

        var spots = InteractionSpot.Find(player, link.Data).FindAll(s => Flat(s.Position - link.NearEnd) < 2.5f);
        _ladderSpots = spots;
        _ladderSpotIndex = 0;
        var stand = link.NearEnd;
        if (spots.Count > 0)
        {
            stand = spots[0].Position;
            _faceDirection = spots[0].Facing;
        }

        _log?.LogInfo($"[Walk] '{target.Id}' is on another level; going by ladder '{link.Data.id}' from {stand} ({spots.Count} spot(s) the game picks it from).");
        RequestPath(new NavTarget(link.Data.id + " (ladder)", stand, target.WorldId, target.CategoryKey, target.Label), MovementType.Recast);
    }

    /// <summary>The next stand point at the ladder, quietly; false when there is none left to try.</summary>
    private static bool TryNextLadderSpot()
    {
        if (_ladderSpots == null || _ladderSpotIndex + 1 >= _ladderSpots.Count || _ladderSpotIndex + 1 >= MaxSpotTries) return false;

        _ladderSpotIndex++;
        var spot = _ladderSpots[_ladderSpotIndex];
        _log?.LogInfo($"[Walk] Trying ladder spot {_ladderSpotIndex + 1} of {_ladderSpots.Count} at {spot.Position} facing {spot.Facing}.");
        _leg = Leg.ToLadder;
        _faceDirection = spot.Facing;
        _quiet = true;
        RequestPath(new NavTarget(_ladder.Data.id + " (ladder)", spot.Position, _finalTarget.WorldId, _finalTarget.CategoryKey, _finalTarget.Label), MovementType.Recast);
        return true;
    }

    /// <summary>
    /// At the ladder: say how to climb, wait for the climb, and walk on from the other end. Moving
    /// keys do not cancel here - Up and Down are how the player climbs.
    /// </summary>
    private static void WatchLadder()
    {
        var player = MainGame.PlayerController;
        if (player == null)
        {
            ResetLegs();
            return;
        }

        if (_ladderCheckAt >= 0f && Time.unscaledTime >= _ladderCheckAt)
        {
            _ladderCheckAt = -1f;
            var picked = InteractionSpot.CurrentTarget(player, out var wgo);
            if (wgo != _ladder.Data)
            {
                _log?.LogInfo($"[Walk] At ladder '{_ladder.Data?.id}' spot {_ladderSpotIndex + 1}, but the game targets '{picked ?? "nothing"}'.");
                if (TryNextLadderSpot()) return;
            }
            var line = Loc.Fmt(_ladder.Up ? "walk.at_ladder_up" : "walk.at_ladder_down",
                _ladder.Name, GameKeys.Name(GameKey.Interaction) ?? "E", GameKeys.Name(_ladder.Up ? GameKey.Up : GameKey.Down) ?? (_ladder.Up ? "W" : "S"),
                _destinationName);
            if (wgo != _ladder.Data)
                line += ". " + (string.IsNullOrEmpty(picked) ? Loc.Get("walk.ladder_not_targeted") : Loc.Fmt("walk.ladder_other_targeted", ItemText.Name(picked)));
            _log?.LogInfo($"[Walk] At ladder '{_ladder.Data?.id}'; the game targets '{picked ?? "nothing"}'.");
            ScreenReader.Say(line);
            return;
        }

        if (LadderRoute.Climbing(player))
        {
            _climbSeen = true;
            return;
        }

        if (_climbSeen)
        {
            var final = _finalTarget;
            var arrivedUp = Mathf.Abs(player.MovablePosition.y - _ladder.FarEnd.y) < 1.2f;
            _log?.LogInfo($"[Walk] Climb over at {player.MovablePosition}; {(arrivedUp ? "on the target's level" : "not on the target's level")}.");
            ResetLegs();
            if (!arrivedUp || !final.IsValid) return;

            _laddersClimbed++;
            Start(final);
            return;
        }

        if (Flat(player.MovablePosition - _ladder.NearEnd) > 4f || Time.unscaledTime - _ladderSince > LadderPatience)
        {
            _log?.LogInfo($"[Walk] Gave up waiting at ladder '{_ladder.Data?.id}': the player walked away or took too long.");
            ResetLegs();
        }
    }

    /// <summary>
    /// Starts the leg after the one that just arrived: the straight walk over the bridge, or the
    /// rest of the route from its far end.
    /// </summary>
    private static void NextLeg()
    {
        var player = MainGame.PlayerController;
        var movement = player == null ? null : player.MovementComponent;
        if (movement == null)
        {
            ResetLegs();
            return;
        }

        if (_leg == Leg.ToLadder)
        {
            _leg = Leg.AtLadder;
            _ladderSince = Time.unscaledTime;
            _ladderCheckAt = Time.unscaledTime + VerifyDelay;
            _climbSeen = false;
            Face();
            _faceAgain = true;
            return;
        }

        if (_leg == Leg.ToBridge)
        {
            _leg = Leg.Crossing;
            var sceneId = MainGame.PlayerData.currentGameSceneId;
            MovementComponent.StartPathResult result;
            try
            {
                result = movement.StartPath(_span.FarEnd, sceneId, sceneId, MovementType.Direct, WalkSpeed, string.Empty, OnArrived, null);
            }
            catch (Exception ex)
            {
                _log?.LogWarning($"[Walk] Could not start the bridge crossing: {ex.Message}");
                result = MovementComponent.StartPathResult.IncorrectMovementType;
            }

            if (result == MovementComponent.StartPathResult.AlreadyAtDestinationPoint)
            {
                _nextLegDue = true;
                return;
            }
            if (result != MovementComponent.StartPathResult.Started)
            {
                var target = _finalTarget;
                ResetLegs();
                FailOrFallback(target);
                return;
            }

            _walking = true;
            _log?.LogInfo($"[Walk] Crossing '{_span.Name}' to {_span.FarEnd}.");
            ScreenReader.Say(Loc.Fmt(_span.IsDoor ? "walk.through_door" : "walk.crossing", _span.Name));
            return;
        }

        // Over. Plan the rest from here; another bridge on the way is found the same way.
        var final = _finalTarget;
        ResetLegs();
        _bridgesCrossed++;
        if (final.IsValid) Start(final);
    }

    /// <summary>True when the navmesh connects the player to the spot nearest the target.</summary>
    private static bool Reachable(PlayerController player, Vector3 target)
    {
        try
        {
            var graph = Reachability.FloorGraph(player);
            if (graph == null) return true;
            var start = graph.GetNearest(player.MovablePosition).node;
            var goal = graph.GetNearest(target).node;
            return start == null || goal == null || Pathfinding.PathUtilities.IsPathPossible(start, goal);
        }
        catch
        {
            return true;
        }
    }

    /// <summary>
    /// Turns the player to face the thing after a walk: along the planned direction when the walk
    /// went to a stand point, otherwise towards the thing.
    ///
    /// <para>
    /// Through <c>PlayerPhysicalBody.SetFacingDirection</c>, not <c>MovableDirection</c>. Setting
    /// only the data left the animator facing the old way, and <c>UpdatePlayerData</c> copies the
    /// animator's direction back whenever it changes, which undid the turn at the next step.
    /// </para>
    /// </summary>
    private static void Face()
    {
        try
        {
            var player = MainGame.PlayerController;
            if (player == null) return;

            Vector2 direction;
            if (_faceDirection.HasValue) direction = _faceDirection.Value;
            else if (_faceTowards.HasValue)
            {
                var offset = _faceTowards.Value - player.MovablePosition;
                direction = new Vector2(offset.x, offset.z);
            }
            else return;

            if (direction.sqrMagnitude < 0.0001f) return;
            direction = direction.normalized;

            if (player.PhysicalBody != null) player.PhysicalBody.SetFacingDirection(direction);
            else player.MovableDirection = direction;
        }
        catch (Exception ex)
        {
            _log?.LogWarning($"[Walk] Could not face the target: {ex.Message}");
        }
    }

    /// <summary>The first way the game is holding the player's controls, or null when it is not.</summary>
    internal static TakenControlType? SceneHasControl()
    {
        var player = MainGame.PlayerController;
        if (player == null) return null;

        foreach (var type in SceneControl)
            if (!player.IsControlEnabledByType(type)) return type;
        return null;
    }

    /// <summary>
    /// The way out of a scene that never ends. Asks first, because a scene that is merely long - a
    /// conversation, a teleport fade - also holds control, and taking it back mid-scene can leave
    /// the story half-played.
    /// </summary>
    private static void ReleaseControl()
    {
        var player = MainGame.PlayerController;
        if (player == null)
        {
            ScreenReader.Say(Loc.Get("nav.not_in_game"));
            return;
        }

        var held = SceneControl.Where(t => !player.IsControlEnabledByType(t)).ToList();
        var armed = Time.unscaledTime - _releaseArmedAt < ReleaseConfirmWindow;

        if (!armed)
        {
            _releaseArmedAt = Time.unscaledTime;
            _log?.LogInfo($"[Walk] Release key: control held by {(held.Count == 0 ? "nothing" : string.Join(", ", held))}.");
            ScreenReader.Say(held.Count == 0
                ? Loc.Get("control.free")
                : Loc.Fmt("control.held", string.Join(", ", held.Select(ControlName))));
            return;
        }

        _releaseArmedAt = -100f;
        _pending = default;
        _walking = false;
        ResetLegs();

        try
        {
            // Stopping the movement also clears the kinematic flag a stranded path leaves behind.
            player.MovementComponent?.ForceStop();
            foreach (var type in held) player.SetControlTakenType(type, isEnabled: true);
            _log?.LogWarning($"[Walk] Released control by force: {(held.Count == 0 ? "movement only" : string.Join(", ", held))}.");
            ScreenReader.Say(Loc.Get("control.released"));
        }
        catch (Exception ex)
        {
            _log?.LogError($"[Walk] Could not release control: {ex.Message}");
            ScreenReader.Say(Loc.Get("walk.unavailable"));
        }
    }

    internal static string ControlName(TakenControlType type) => type switch
    {
        TakenControlType.ByFlow => Loc.Get("control.by_flow"),
        TakenControlType.ByCinematics => Loc.Get("control.by_cinematics"),
        TakenControlType.ByTeleport => Loc.Get("control.by_teleport"),
        TakenControlType.BySleep => Loc.Get("control.by_sleep"),
        TakenControlType.ByDeath => Loc.Get("control.by_death"),
        _ => type.ToString(),
    };

    /// <summary>True while a walk, or one of its legs, is under way.</summary>
    internal static bool IsBusy => _walking || _pending.IsValid || _nextLegDue || _leg != Leg.None || _verifyAt >= 0f;

    /// <summary>Drops the walk without a word, for the rescue key.</summary>
    internal static void Cancel() => Stop(null);

    private static void Stop(string message)
    {
        _walking = false;
        _pending = default;
        ResetLegs();
        ResetInteraction();
        FocusLock.Release("the walk was stopped");

        try
        {
            var movement = MainGame.PlayerController == null ? null : MainGame.PlayerController.MovementComponent;
            movement?.ForceStop();
        }
        catch (Exception ex)
        {
            _log?.LogWarning($"[Walk] Could not stop cleanly: {ex.Message}");
        }

        if (!string.IsNullOrEmpty(message)) ScreenReader.Say(message);
    }
}
