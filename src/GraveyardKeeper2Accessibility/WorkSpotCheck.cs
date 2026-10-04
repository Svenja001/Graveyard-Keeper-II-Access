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
            // MainGame.PlayerController dereferences Instance, which is null until a game loads.
            var player = MainGame.Instance == null ? null : MainGame.PlayerController;
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
            _log?.LogInfo($"[Work] Holding F at '{wgo.Data.id}' started nothing: {reason}");
            _log?.LogInfo($"[Work] State: {StateDump(wgo, player)}");
            ScreenReader.Say(Loc.Fmt("work.refused", name, reason));
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

    /// <summary>
    /// The first reason the game will refuse, in the order it checks them: what the player carries
    /// (<c>WorkPlayerState.CanWork</c>), whether the station takes a worker at all
    /// (<c>PlayerWorkComponent.CanWorkOn</c>), the tool, the work spots, then what the queued craft
    /// itself needs (<c>ToolComponent.CanProceedWork</c>). Every one of these refuses in silence.
    /// Never null: when none of them fits, the station's own state is described instead, so the
    /// player is not left with "reason unknown" again (2026-10-03, stone and wood benches).
    /// </summary>
    private static string Diagnose(Wgo wgo, PlayerController player, bool logAll)
    {
        var data = wgo.Data;
        var craft = data.CraftComponent;
        var playerData = player.PlayerData;

        // Several things stacked overhead also hide the "[F]" hint, so the station sounded like
        // it had no hand work at all.
        if (playerData != null && playerData.HasMultipleOverheadItems)
            return Loc.Get("work.reason_carrying");
        if (playerData != null && playerData.HasOverheadItem &&
            playerData.overheadItem?.Definition?.itemGroupIds?.Contains("zombie") == true)
            return Loc.Get("work.reason_carrying_zombie");

        var bigDrop = player.PlayerInteractionComponent?.BigDropUnderInteraction;
        if (bigDrop != null && bigDrop.InteractionHandler != null && bigDrop.InteractionHandler.HasInteraction2())
            return Loc.Fmt("work.reason_big_drop", ObjectNames.Of(bigDrop.Data?.Id));

        if (!data.IsInteractable || wgo.MainWgoPart?.InteractableColliders == null ||
            wgo.MainWgoPart.InteractableColliders.Count == 0)
            return Loc.Get("work.reason_not_interactable");

        // The game takes no station that has any worker - the player included. The craft window
        // sets the player as worker and clears it on close; one left behind locks the station.
        if (data.Worker != null)
            return Loc.Get(ReferenceEquals(data.Worker, player) ? "work.reason_stuck_worker" : "work.reason_busy");

        if (craft?.CraftableObject != null && craft.CraftableObject.CraftableType == CraftableType.ConveyorWorkbench &&
            !craft.IsDestroyingCraftActive)
            return Loc.Get("work.reason_conveyor");

        var tool = wgo.InteractionHandler?.GetRequiredInteractionToolType() ?? ItemType.None;
        var item = playerData?.toolBeltInventory?.Data?.GetItemByType(tool);
        if (item == null || item.IsEmpty)
            return Loc.Fmt("work.reason_tool", ObjectStatus.ToolName(tool));

        var docks = Docks(wgo);
        if (docks.Count == 0) return Loc.Get("work.reason_no_spot");

        var movement = player.PlayerLocalAreaMovement;
        movement?.RescanPlayerGraph();

        var notes = new List<string>();
        var anyFree = false;
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
            if (note == null) anyFree = true;
            else notes.Add(note);
        }
        if (!anyFree) return string.Join("; ", notes);

        return CraftReason(data, craft, player, item, tool) ?? StateReason(craft);
    }

    /// <summary>What the queued craft needs that the player lacks - <c>ToolComponent.CanProceedWork</c>.</summary>
    private static string CraftReason(WgoData data, CraftComponent craft, PlayerController player, Item item, ItemType tool)
    {
        if (craft == null) return null;
        var element = craft.CurrentCraftElement;

        // PlayerCraftActivity.CanUseTool
        if (element == null || craft.IsQueueDelayed)
        {
            if (!craft.IsQueueDelayed) return Loc.Get("work.reason_nothing_current");
            if (craft.CraftElementsQueue.Find(x => x.CraftStatus == CraftStatus.OK) == null)
                return Loc.Fmt("work.reason_queue_blocked", QueueHeadReason(data, craft));
        }
        else if (craft.IsFinishDelayed)
        {
            return Loc.Get("work.reason_finishing");
        }

        var def = element?.Def;
        if (def == null || craft.IsQueueDelayed) return null;

        // PlayerCraftActivity.IsEnoughMastery
        if (!def.isStarCraft && !def.isAutopsyCraft && !(def is SurveyDef) && !(element is CraftElementMix))
        {
            var talent = data.Definition?.talent;
            var have = player.GetMasteryLevelForTalentBranch(talent, def);
            if (have < def.talentLock)
                return Loc.Fmt("work.reason_mastery", CraftReader.TalentName(talent), def.talentLock, have);
        }

        // Energy and insanity count only once the craft runs, or when nothing in the queue can start.
        var running = craft.IsStarted || craft.CraftElementsQueue.Find(x => x.CraftStatus == CraftStatus.OK) == null;
        if (running)
        {
            var energy = def.energyPerTick.EvaluateFloat() + player.GetPerksEnergyBonusValue(def) -
                         item.Definition.GetGameResOnUse("energy");
            if (!PlayerEnergyGameResSystem.GetSystem().IsEnoughValue(energy))
                return Loc.Get("work.reason_energy");

            var insanity = def.insanityPerTick.EvaluateFloat() + player.GetPerksInsanityBonusValue(def) -
                           item.Definition.GetGameResOnUse("insanity");
            if (!PlayerInsanityGameResSystem.GetSystem().CanChangeInsanity(insanity))
                return Loc.Get(insanity > 0 ? "work.reason_insanity_full" : "work.reason_insanity_low");
        }

        // PlayerCraftActivity.IsEnoughDurability
        if (item.TryGetProperty<DurabilitySerializedItemProperty>(out var durability) &&
            durability.Durability <= item.Definition.durDecreaseOnUse)
            return Loc.Fmt("work.reason_worn", ObjectStatus.ToolName(tool));

        return null;
    }

    /// <summary>"Steinbausatz: nicht genug Zutaten" for the first job in the queue.</summary>
    private static string QueueHeadReason(WgoData data, CraftComponent craft)
    {
        var head = craft.CraftElementsQueue.Count > 0 ? craft.CraftElementsQueue[0] : null;
        if (head == null) return CraftReader.StatusText(CraftStatus.Other);
        var status = head.CraftStatus;
        if (status == CraftStatus.OK || status == CraftStatus.Other)
            status = craft.GetStartCraftStatus(head);
        return $"{CraftReader.RecipeName(head.Def, data)}: {CraftReader.StatusText(status)}";
    }

    /// <summary>
    /// Last resort: none of the game's checks explains it, so say what state the station is in.
    /// Still something to act on or report, where "reason unknown" was neither.
    /// </summary>
    private static string StateReason(CraftComponent craft)
    {
        if (craft == null) return Loc.Fmt("work.reason_state", "-", "-");
        var head = craft.CurrentCraftElement ?? (craft.CraftElementsQueue.Count > 0 ? craft.CraftElementsQueue[0] : null);
        var headText = head == null ? "-" : CraftReader.StatusText(head.CraftStatus);
        return Loc.Fmt("work.reason_state", Navigator.Humanise(craft.Status.ToString()), headText);
    }

    private static readonly FieldInfo MagnetismField =
        AccessTools.Field(typeof(PlayerWorkComponent), "isMagnetismDelayed");

    /// <summary>Everything the checks above read, for the log, so a refusal can be traced afterwards.</summary>
    private static string StateDump(Wgo wgo, PlayerController player)
    {
        try
        {
            var data = wgo.Data;
            var craft = data.CraftComponent;
            var queue = craft == null
                ? "-"
                : string.Join(", ", craft.CraftElementsQueue.Select(e => $"{e.Def?.id}x{e.Count}={e.CraftStatus}"));
            var work = player.PlayerWorkComponent;
            return $"craft status {craft?.Status}, started {craft?.IsStarted}, current '{craft?.CurrentCraftElement?.Def?.id}', " +
                   $"queue [{queue}], worker {data.Worker?.GetType().Name ?? "none"}, interactable {data.IsInteractable}, " +
                   $"overhead {player.PlayerData?.OverheadCount}, big drop '{player.PlayerInteractionComponent?.BigDropUnderInteraction?.Data?.Id}', " +
                   $"under interaction '{player.PlayerInteractionComponent?.WgoUnderInteraction?.Data?.id}', " +
                   $"work wgo '{work?.Wgo?.Data?.id}', work active {work?.IsActive}, magnetism delayed {MagnetismField?.GetValue(work)}, " +
                   $"energy {player.PlayerData?.GetRes("energy")}, insanity {player.PlayerData?.GetRes("insanity")}.";
        }
        catch (Exception ex)
        {
            return $"(dump failed: {ex.Message})";
        }
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
