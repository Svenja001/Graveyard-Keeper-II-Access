namespace GraveyardKeeper2Accessibility;

/// <summary>
/// What is in front of you as you walk around: the thing you can interact with, the item lying on
/// the ground, and what you just picked up.
///
/// The GK1 mod had to sweep the scene for this and paid for it in frames. GK2 hands it over as
/// events: <c>PlayerInteractionComponent</c> already tracks what the player is standing in front
/// of, because it has to draw the "press E" hint, and it raises an event whenever that changes.
/// The mod listens. Nothing is scanned, nothing is polled.
///
/// Those events are instance fields rather than statics, so the subscription is made from a patch
/// on the component's own <c>Init</c> - which is also exactly the right moment, with no waiting on
/// the game to finish loading.
/// </summary>
internal static class WorldAnnouncer
{
    private static ManualLogSource _log;

    /// <summary>
    /// The thing most recently announced, so standing still does not repeat it. The interaction
    /// component re-raises its events more than once for the same target as the player shifts
    /// about within range.
    /// </summary>
    private static string _lastAnnounced;

    internal static void Init(ManualLogSource log, ConfigFile config)
    {
        _log = log;
        _announceZones = config.Bind("World", "AnnounceStoryTriggers", true,
            "Says when you cross one of the story's invisible trigger volumes while the story is waiting for it.");
    }

    /// <summary>
    /// Subscribes to the live interaction component. Unsubscribing first because <c>Init</c> runs
    /// again on a reload or a new game, and a doubled delegate would say everything twice.
    /// </summary>
    [HarmonyPatch(typeof(PlayerInteractionComponent), nameof(PlayerInteractionComponent.Init))]
    [HarmonyPostfix]
    private static void PlayerInteractionComponent_Init(PlayerInteractionComponent __instance)
    {
        try
        {
            __instance.OnInteractionTargetEnter -= OnTargetEnter;
            __instance.OnInteractionTargetEnter += OnTargetEnter;

            __instance.OnInteractionTargetChanged -= OnTargetEnter;
            __instance.OnInteractionTargetChanged += OnTargetEnter;

            __instance.OnInteractionTargetExit -= OnTargetExit;
            __instance.OnInteractionTargetExit += OnTargetExit;

            __instance.OnInteractionBigDropTargetEnter -= OnDropEnter;
            __instance.OnInteractionBigDropTargetEnter += OnDropEnter;

            __instance.OnInteractionBigDropTargetChanged -= OnDropEnter;
            __instance.OnInteractionBigDropTargetChanged += OnDropEnter;

            __instance.OnInteractionBigDropTargetExit -= OnTargetExit;
            __instance.OnInteractionBigDropTargetExit += OnTargetExit;

            _log?.LogInfo("[World] Subscribed to player interaction events.");
        }
        catch (Exception ex)
        {
            _log?.LogError($"[World] Could not subscribe to interaction events: {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>
    /// Something you could interact with is now in front of you.
    ///
    /// Announced without interrupting: walking is not a request for information the way pressing a
    /// key is, and cutting off a line of dialogue or a quest notification because the player
    /// wandered past a bush would be worse than saying nothing.
    /// </summary>
    private static void OnTargetEnter(Wgo wgo)
    {
        try
        {
            if (wgo == null || wgo.Data == null) return;

            var name = ObjectName(wgo.Data.id);
            if (string.IsNullOrWhiteSpace(name)) return;

            var hint = InteractionHint(wgo);
            var line = string.IsNullOrWhiteSpace(hint) ? name : Loc.Fmt("world.target_with_hint", name, hint);

            // A workbench add-on has no action of its own; say what it is for and whether it works.
            var extension = ObjectStatus.Extension(wgo.Data);
            if (extension != null) line = $"{line}, {extension}";

            // A story step here that will not count yet - see StoryListeners.MissingFor.
            var missing = StoryListeners.MissingFor(wgo.Data.id);
            if (missing != null)
            {
                _log?.LogInfo($"[World] '{wgo.Data.id}' story step still needs: {missing}");
                line = Loc.Fmt("story.still_needs", line, missing);
            }

            Announce(line);
        }
        catch (Exception ex)
        {
            _log?.LogError($"[World] Interaction announce failed for '{(wgo == null || wgo.Data == null ? "?" : wgo.Data.id)}': {ex}");
        }
    }

    /// <summary>An item lying on the ground, close enough to pick up.</summary>
    private static void OnDropEnter(DropView drop)
    {
        try
        {
            var data = drop == null ? null : drop.Data;
            if (data == null) return;

            var name = ItemText.Name(data.Id);
            if (string.IsNullOrWhiteSpace(name)) return;

            Announce(data.Count > 1
                ? Loc.Fmt("world.drop_many", data.Count, name)
                : Loc.Fmt("world.drop", name));
        }
        catch (Exception ex)
        {
            _log?.LogError($"[World] Drop announce failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>
    /// Nothing in range any more. Deliberately silent - walking past a row of gravestones would
    /// otherwise produce an "exit" for every one of them - but the memory is cleared so that
    /// coming back to the same object announces it again.
    /// </summary>
    private static void OnTargetExit() => _lastAnnounced = null;

    private static void Announce(string line)
    {
        if (string.IsNullOrWhiteSpace(line) || line == _lastAnnounced) return;
        _lastAnnounced = line;

        // Logged as well as spoken: when a player reports that pressing the interaction key does
        // nothing, the hint text is the first thing worth seeing, because it often already says
        // why - a missing tool, an unmet requirement, or no action at all.
        _log?.LogInfo($"[World] {line}");
        ScreenReader.Say(line, interrupt: false);
    }

    /// <summary>
    /// What the player can do with this object, and when they cannot, why not.
    ///
    /// The reason is the valuable half. On screen an unavailable action is drawn greyed out with a
    /// tool icon or a little lock beside it, so a sighted player sees "I need an axe" without a
    /// word being written. Spoken as just the action name, it sounds available, and the player is
    /// left pressing a key that does nothing.
    /// </summary>
    private static string InteractionHint(Wgo wgo)
    {
        var handler = wgo.InteractionHandler;
        if (handler == null) return null;

        // Craft objects (a broken table, oven, ladder, bridge) throw here when they first come into
        // reach: CraftInteractionHandler reads its assignedCraftComponent before the game has set
        // it. Losing the whole line to that left the player hearing nothing at the object, so the
        // name is said without the action instead.
        InteractionInfos infos;
        try
        {
            infos = handler.GetInteractionInfos();
        }
        catch (NullReferenceException)
        {
            return null;
        }
        if (infos == null || infos.IsEmpty) return null;

        var parts = new List<string>();

        foreach (var info in infos.list)
        {
            if (info == null) continue;

            var text = string.IsNullOrEmpty(info.text) ? null : TmpText.Clean(LLBase.L(info.text));
            if (string.IsNullOrWhiteSpace(text)) continue;

            if (KeyOnly.IsMatch(text))
                text = WorkHint(wgo, text, info);

            if (!info.isItemEquipped)
                text = Loc.Fmt("world.needs_tool", text);
            else if (!info.isEnoughMastery)
                text = MasteryHint(text, info);

            if (!parts.Contains(text)) parts.Add(text);
        }

        return parts.Count == 0 ? null : string.Join(", ", parts);
    }

    /// <summary>A hint that is nothing but a key, such as "[F]".</summary>
    private static readonly System.Text.RegularExpressions.Regex KeyOnly =
        new System.Text.RegularExpressions.Regex(@"^\[[^\]]+\]$");

    /// <summary>
    /// "[F] hold: demolish" in place of a bare "[F]".
    ///
    /// <para>
    /// Working an object by hand - demolishing a building marked for removal, turning a queued
    /// craft at a station like the potter's wheel - is hinted by the key icon alone, next to a
    /// hand or tool picture (<c>GetInteractionInfoByUsingTool</c>). Spoken, that was "Töpferscheibe,
    /// [F]": the user marked the wheel for removal, stood at it, and never learned that holding F
    /// is how it comes down (2026-09-27). Marking also takes away E, so the wheel seemed dead.
    /// </para>
    /// </summary>
    private static string WorkHint(Wgo wgo, string key, InteractionInfo info)
    {
        var craft = wgo.Data?.CraftComponent;
        string what;
        if (craft != null && craft.IsDestroyingCraftActive)
            what = Loc.Get("world.work_demolish");
        else if (craft?.CurrentCraftElement?.Def != null)
            what = Loc.Fmt("world.work_craft", CraftReader.RecipeName(craft.CurrentCraftElement.Def, wgo.Data));
        else
            what = Loc.Get("world.work_plain");

        var line = Loc.Fmt("world.hold_key", key, what);
        var tool = info.equippedItemType;
        if (tool != ItemType.None && tool != ItemType.Hand)
            line = Loc.Fmt("world.work_with", line, ObjectStatus.ToolName(tool));

        // Every work spot covered: F will do nothing here (WorkSpotCheck).
        var blocked = WorkSpotCheck.BlockedNote(wgo);
        if (blocked != null) line = Loc.Fmt("world.work_blocked", line, blocked);
        return line;
    }

    /// <summary>
    /// "needs Smithing mastery 4, you have 2". A bare "mastery 4" did not say which of the five
    /// talents, nor how far off the player is - on screen the icon's colour says the first.
    /// </summary>
    private static string MasteryHint(string text, InteractionInfo info)
    {
        var talent = info.assignedTalent?.id;
        var player = MainGame.PlayerController;
        if (string.IsNullOrEmpty(talent) || player == null)
            return Loc.Fmt("world.needs_mastery", text, info.masteryLock);

        return Loc.Fmt("world.needs_mastery_of", text, info.masteryLock, CraftReader.TalentName(talent),
            player.GetMasteryLevelForTalentBranch(talent));
    }

    /// <summary>
    /// The display name for a definition id. In GK2 the id <i>is</i> the locale key, uniformly
    /// across every definition type, and <c>LLBase.L</c> hands back the id itself when there is no
    /// entry - so an unnamed object says something recognisable rather than nothing.
    /// </summary>
    private static string ObjectName(string id)
    {
        return string.IsNullOrEmpty(id) ? null : ObjectNames.Of(id);
    }

    /// <summary>
    /// Crossing into one of the story's invisible trigger volumes.
    ///
    /// Said out loud because it is the only confirmation that the world responded at all. A sighted
    /// player gets the same confirmation from whatever the trigger sets off on screen; if the
    /// flowscript behind it produces nothing audible, a player who deliberately walked in there has
    /// no way to tell whether it worked, and will keep trying.
    /// </summary>
    /// <remarks>
    /// Unity calls <c>OnTriggerEnter</c> for every collider the player carries, and again as the
    /// player shifts about inside; the game counts only the first, by setting its private
    /// <c>isPlayerInside</c>. Announcing every call said "story trigger" over and over while the
    /// story saw nothing - so the prefix notes whether the player was already inside, and the
    /// postfix speaks only when that flipped.
    ///
    /// <para>
    /// <b>And only when the story was waiting for this zone</b> (user, 2026-09-26). Walking again
    /// through a zone that has already done its job said "story trigger" every time, and a player
    /// then stands there waiting for something that will never happen. Whether a quest listens for
    /// the zone's tag is checked <i>before</i> the game handles the entry, since handling it is
    /// what removes the listener. Zones with only a flowscript are not announced either: whether
    /// the script still does anything cannot be told from outside, and when it does, it speaks
    /// for itself.
    /// </para>
    /// </remarks>
    [HarmonyPatch(typeof(GDZone), "OnTriggerEnter")]
    [HarmonyPrefix]
    private static void GDZone_OnTriggerEnter_Prefix(GDZone __instance, out ZoneEntry __state)
    {
        __state = __instance == null
            ? new ZoneEntry(true, false)
            : new ZoneEntry(ZoneInside(__instance), StoryListeners.AwaitsZone(__instance));
    }

    internal readonly struct ZoneEntry
    {
        internal readonly bool WasInside;
        internal readonly bool Awaited;

        internal ZoneEntry(bool wasInside, bool awaited)
        {
            WasInside = wasInside;
            Awaited = awaited;
        }
    }

    internal static readonly AccessTools.FieldRef<GDZone, bool> ZoneInside =
        AccessTools.FieldRefAccess<GDZone, bool>("isPlayerInside");

    private const int GDZoneLayerMask = 1 << 23;
    private static readonly RaycastHit[] ZoneHits = new RaycastHit[8];

    /// <summary>
    /// Whether the player is standing in the zone right now. <c>isPlayerInside</c> alone is not
    /// enough: it is cleared only by <c>OnTriggerExit</c>, and a door teleport never produces one.
    /// The player entered <c>GDZone_Intro_Graveyard_Morgue</c>, went home through the door and
    /// slept; the flag still said "inside", so StoryRepair fired the donkey-and-nun scene in the
    /// bedroom, its walk had no route, and control never came back (log of 2026-09-26). This is the
    /// game's own exit test: a vertical ray through the player against the zone layer.
    /// </summary>
    internal static bool PlayerInZone(GDZone zone) => zone != null && ZoneInside(zone) && PlayerStandsIn(zone);

    /// <summary>
    /// The ray test alone, whatever the game's flag says - for finding an entry the game dropped
    /// (see <see cref="ZoneEntry"/>).
    /// </summary>
    internal static bool PlayerStandsIn(GDZone zone)
    {
        if (zone == null || !zone.isActiveAndEnabled) return false;

        var player = MainGame.PlayerController;
        if (player == null) return false;

        var from = player.MovablePosition + Vector3.down * 100f;
        var count = Physics.RaycastNonAlloc(new Ray(from, Vector3.up), ZoneHits, 200f, GDZoneLayerMask, QueryTriggerInteraction.Collide);
        for (var i = 0; i < count; i++)
            if (ZoneHits[i].collider != null && ZoneHits[i].collider.TryGetComponent<GDZone>(out var hit) && hit == zone)
                return true;
        return false;
    }

    [HarmonyPatch(typeof(GDZone), "OnTriggerEnter")]
    [HarmonyPostfix]
    private static void GDZone_OnTriggerEnter(GDZone __instance, ZoneEntry __state)
    {
        try
        {
            if (__instance == null || __state.WasInside || !ZoneInside(__instance)) return;

            var tag = string.IsNullOrEmpty(__instance.customTag) ? __instance.name : __instance.customTag;
            var script = __instance.onEnter != null && __instance.onEnter.flowScript != null ? __instance.onEnter.flowScript.name : "-";
            _log?.LogInfo($"[World] Entered story zone '{tag}' (script {script}, {(__state.Awaited ? "awaited" : "not awaited")}).");

            if (!__state.Awaited) return;
            if (_announceZones != null && !_announceZones.Value) return;
            ScreenReader.Say(Loc.Get("world.zone_entered"), interrupt: false);
        }
        catch (Exception ex)
        {
            _log?.LogError($"[World] Zone announce failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static ConfigEntry<bool> _announceZones;

    private static readonly AccessTools.FieldRef<WorkPlayerState, Wgo> WorkTarget =
        AccessTools.FieldRefAccess<WorkPlayerState, Wgo>("targetWgo");

    /// <summary>
    /// Work stopped on something that is still standing - out of energy, key released, or
    /// interrupted. Says how much is left.
    ///
    /// <para>
    /// On screen that is a health bar over the object and the object visibly still there. Without
    /// it, a barricade that took one round of blows sounds exactly like one that was cleared: the
    /// red point is awarded either way, and the story simply waits, with nothing to say why.
    /// </para>
    /// </summary>
    [HarmonyPatch(typeof(WorkPlayerState), nameof(WorkPlayerState.OnExit))]
    [HarmonyPrefix]
    private static void WorkPlayerState_OnExit(WorkPlayerState __instance)
    {
        try
        {
            var wgo = WorkTarget(__instance);
            if (wgo == null || wgo.IsDespawning || wgo.Data == null) return;

            var hp = wgo.Data.HpComponent;
            if (hp == null || !hp.WasDamagedAtLeastOnce || hp.MaxHpValue <= 0) return;
            if (hp.Hp <= 0 || hp.HasFullHp) return;

            var percent = Mathf.Clamp(Mathf.RoundToInt(100f * hp.Hp / hp.MaxHpValue), 1, 99);
            var name = ObjectName(wgo.Data.id) ?? wgo.Data.id;
            _log?.LogInfo($"[World] Work stopped on '{wgo.Data.id}' at {hp.Hp}/{hp.MaxHpValue}.");
            ScreenReader.Say(Loc.Fmt("world.work_left", name, percent), interrupt: false);
        }
        catch (Exception ex)
        {
            _log?.LogError($"[World] Work-stop announce failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>
    /// Items arriving in the inventory - picked up, harvested, bought, or handed over at the end
    /// of a quest. This is the notification popup's own feed, so it covers every route in without
    /// needing to know about any of them.
    /// </summary>
    [HarmonyPatch(typeof(UINotificator), "HandleAddItems")]
    [HarmonyPostfix]
    private static void UINotificator_HandleAddItems(List<Item> items)
    {
        try
        {
            if (items == null || items.Count == 0) return;

            var parts = new List<string>();
            foreach (var item in items)
            {
                if (item == null) continue;

                var name = ItemText.Name(item.id);
                if (string.IsNullOrWhiteSpace(name)) continue;

                parts.Add(item.Count > 1 ? $"{item.Count} {name}" : name);
            }

            if (parts.Count == 0) return;

            ScreenReader.Say(Loc.Fmt("world.picked_up", string.Join(", ", parts)), interrupt: false);
        }
        catch (Exception ex)
        {
            _log?.LogError($"[World] Pickup announce failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>
    /// A full inventory silently stops pickups working, which is maddening when the only sign of
    /// it is a popup.
    /// </summary>
    [HarmonyPatch(typeof(UINotificator), nameof(UINotificator.HandleInventoryFull))]
    [HarmonyPostfix]
    private static void UINotificator_HandleInventoryFull()
    {
        try
        {
            ScreenReader.Say(Loc.Get("world.inventory_full"));
        }
        catch (Exception ex)
        {
            _log?.LogError($"[World] Inventory-full announce failed: {ex.GetType().Name}: {ex.Message}");
        }
    }
}
