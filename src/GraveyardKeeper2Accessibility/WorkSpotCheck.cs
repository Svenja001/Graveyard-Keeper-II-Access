namespace GraveyardKeeper2Accessibility;

/// <summary>
/// Why holding F at a station does nothing.
///
/// <para>
/// Working a station by hand - turning a queued craft, demolishing a building marked for removal -
/// needs a free <c>DockPoint</c>: the spot the character is walked onto to work. The game takes
/// one only if the player's body fits there (<c>PlayerColliderTester.IsPositionReachable</c>: no
/// building overlapping by more than 5 cm) and a path to it exists on the small grid around the
/// player (<c>PlayerLocalAreaMovement.IsReachable</c>). When none qualifies, holding F is simply
/// ignored - no sound, no message, nothing in the log. A sighted player sees the wheel jammed
/// against the sawhorse; the user heard nothing and was stuck at the potter's wheel, unable to
/// craft or demolish it (2026-09-27).
/// </para>
/// </summary>
internal static class WorkSpotCheck
{
    private static ManualLogSource _log;

    /// <summary>Seconds of holding F before silence counts as refusal. The walk to the spot starts sooner.</summary>
    private const float Patience = 0.6f;

    private static Wgo _pending;
    private static float _pressedAt;

    private static readonly FieldInfo PlayerColliderField =
        AccessTools.Field(typeof(PlayerColliderTester), "thisCollider");

    private static readonly Collider[] Hits = new Collider[16];

    internal static void Init(ManualLogSource log) => _log = log;

    internal static void Update()
    {
        try
        {
            var player = MainGame.PlayerController;
            if (player == null) { _pending = null; return; }

            if (LazyInput.GetKeyDown(GameKey.Action) && player.IsControlsEnabled)
            {
                var target = player.PlayerInteractionComponent?.WgoUnderInteraction;
                _pending = target != null && WantsHandWork(target) ? target : null;
                _pressedAt = Time.time;
                return;
            }

            if (_pending == null || Time.time - _pressedAt < Patience) return;

            var wgo = _pending;
            _pending = null;

            // Let go early: a tap, not a hold - nothing to explain.
            if (!LazyInput.GetKey(GameKey.Action)) return;

            var work = player.PlayerWorkComponent;
            if (work == null || work.WorkInProgress || work.IsMoving || work.Wgo == wgo) return;
            if (wgo == null || wgo.IsDespawning || wgo.Data == null) return;

            var reason = Diagnose(wgo, player, logAll: true);
            var name = ObjectNames.Of(wgo.Data.id);
            _log?.LogInfo($"[Work] Holding F at '{wgo.Data.id}' started nothing: {reason ?? "no reason found"}");
            ScreenReader.Say(Loc.Fmt("work.refused", name, reason ?? Loc.Get("work.reason_unknown")));
        }
        catch (Exception ex)
        {
            _pending = null;
            _log?.LogError($"[Work] Work check failed: {ex}");
        }
    }

    /// <summary>A station with manual work waiting: a queued hand craft, or a demolition.</summary>
    internal static bool WantsHandWork(Wgo wgo)
    {
        var craft = wgo?.Data?.CraftComponent;
        return craft != null && craft.HasCraftsInQueue && (craft.IsManualActualCraftable || craft.IsDestroyingCraftActive);
    }

    /// <summary>
    /// "the work spot south is covered by Sawhorse", or null if a work spot looks usable. Only the
    /// cheap collider test, for the line spoken when the station comes into reach.
    /// </summary>
    internal static string BlockedNote(Wgo wgo)
    {
        var player = MainGame.PlayerController;
        if (player == null || !WantsHandWork(wgo)) return null;
        var docks = Docks(wgo);
        if (docks.Count == 0) return null;

        var notes = new List<string>();
        foreach (var dock in docks)
        {
            var blocker = Blocker(dock.transform.position, wgo);
            if (blocker == null) return null;
            notes.Add(Loc.Fmt("work.spot_blocked", Side(wgo, dock), blocker));
        }
        return string.Join("; ", notes);
    }

    /// <summary>The first reason the game will refuse, in the order it checks them.</summary>
    private static string Diagnose(Wgo wgo, PlayerController player, bool logAll)
    {
        var data = wgo.Data;

        if (data.Worker != null && !ReferenceEquals(data.Worker, player))
            return Loc.Get("work.reason_busy");

        var tool = wgo.InteractionHandler?.GetRequiredInteractionToolType() ?? ItemType.None;
        var item = player.PlayerData?.toolBeltInventory?.Data?.GetItemByType(tool);
        if (item == null || item.IsEmpty)
            return Loc.Fmt("work.reason_tool", ObjectStatus.ToolName(tool));

        var docks = Docks(wgo);
        if (docks.Count == 0) return Loc.Get("work.reason_no_spot");

        var movement = player.PlayerLocalAreaMovement;
        movement?.RescanPlayerGraph();

        var notes = new List<string>();
        foreach (var dock in docks)
        {
            var pos = dock.transform.position;
            var side = Side(wgo, dock);
            var blocker = Blocker(pos, wgo);
            string note;
            if (blocker != null) note = Loc.Fmt("work.spot_blocked", side, blocker);
            else if (movement != null && !movement.IsReachable(pos)) note = Loc.Fmt("work.spot_unreachable", side);
            else note = null;

            if (logAll)
                _log?.LogInfo($"[Work] '{data.id}' dock {dock.name} at {pos} ({side}): {note ?? "free"}.");
            if (note == null) return null; // a usable spot: the refusal is something else
            notes.Add(note);
        }
        return string.Join("; ", notes);
    }

    private static List<DockPoint> Docks(Wgo wgo)
    {
        var list = new List<DockPoint>();
        var docks = wgo.DockPoints;
        if (docks == null) return list;
        foreach (var dock in docks)
            if (dock != null && dock.gameObject.activeInHierarchy) list.Add(dock);
        return list;
    }

    /// <summary>
    /// What covers this work spot, by the game's own test: the player's collider placed there,
    /// overlapping a building (layer 8) by more than 5 cm. Named after its object where it has one.
    /// </summary>
    private static string Blocker(Vector3 position, Wgo owner)
    {
        var body = PlayerColliderField?.GetValue(null) as Collider;
        if (body == null) return null;

        position = new Vector3(position.x, body.transform.position.y, position.z);
        var count = Physics.OverlapSphereNonAlloc(position, 0.5f, Hits, 256);
        for (var i = 0; i < count; i++)
        {
            var hit = Hits[i];
            if (hit == null || hit == body) continue;
            if (!Physics.ComputePenetration(body, position, body.transform.rotation, hit, hit.transform.position,
                    hit.transform.rotation, out _, out var distance) || distance <= 0.05f) continue;

            var wgo = hit.GetComponentInParent<Wgo>();
            if (wgo == null || wgo.Data == null) return Loc.Get("work.blocker_wall");
            return wgo == owner ? Loc.Get("work.blocker_itself") : ObjectNames.Of(wgo.Data.id);
        }
        return null;
    }

    /// <summary>Which side of the station the spot is on, in movement-key compass terms (north = W).</summary>
    private static string Side(Wgo wgo, DockPoint dock)
    {
        var offset = dock.transform.position - wgo.Data.Position;
        if (Mathf.Abs(offset.x) < 0.05f && Mathf.Abs(offset.z) < 0.05f) return Loc.Get("work.side.center");
        if (Mathf.Abs(offset.z) >= Mathf.Abs(offset.x))
            return Loc.Get(offset.z > 0 ? "work.side.north" : "work.side.south");
        return Loc.Get(offset.x > 0 ? "work.side.east" : "work.side.west");
    }
}
