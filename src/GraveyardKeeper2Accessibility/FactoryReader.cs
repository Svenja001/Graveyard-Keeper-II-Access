using System.Runtime.CompilerServices;

namespace GraveyardKeeper2Accessibility;

/// <summary>
/// The cellar factory: conveyor belts, the workbenches zombies run on them, chests, splitters and
/// the zombie carousel that powers it all (user, 2026-10-10: "the förderbänder and the other things
/// need to be attached properly to each other to get the produce one wants"). Built from the code
/// before anyone has reached it in play; see PLAN.md, Phase 4b.
///
/// <para>
/// <b>It is one graph, and the save holds it.</b> Every piece is a <c>ConveyorWgoData</c> whose
/// <c>ConveyorComponent</c> lists what it hands items on to: belts, splitters and chests in
/// <c>ConnectedWgoData</c>, a pallet in its single link, a workbench in
/// <c>connectedOutWgoDataUniqueId</c>. A workbench also lists the belts it takes from
/// (<c>connectedInWgoDataUniqueId</c>) - the only link kept on the receiving end, so it is turned
/// round here into an edge from the belt to the bench. Items move from a piece to the pieces it
/// lists, one step per <c>ConveyorSystem</c> tick.
/// </para>
///
/// <para>
/// <b>G</b> adds the factory to the zone line while the player is in a scene that has any of it:
/// power against what the belts use (the whole factory stands still below it -
/// <c>ConveyorSystem.HasEnoughPower</c>), the carousels and their zombies, and every workbench with
/// its worker, what it makes and what is stopping it. <b>Shift+G / Ctrl+G</b> step through the
/// lines: from each chest, bench, splitter or loose belt end, the belt runs in compass steps to
/// where they lead, with dead ends and unfed belts called out - the mistakes a sighted player sees
/// as a belt arrow pointing at nothing.
/// </para>
///
/// <para>
/// <b>Building.</b> A piece joins its neighbours when it is built, by collider overlap
/// (<c>ConveyorBuildPointer.MakeConnections</c>): each of its connector boxes (layer 19 hits) and
/// each neighbour's connector boxes over its body (layer 27 hits), filtered by a per-connector rule
/// on the neighbour's type and facing. The placement pointer is a real, temporary copy of the piece
/// standing where it would go, so the same tests are run on it here - read-only - and the cursor
/// line says which way the piece runs and what it would join before Enter is pressed.
/// </para>
/// </summary>
internal static class FactoryReader
{
    private static ManualLogSource _log;

    private static ConfigEntry<KeyboardShortcut> _nextLineKey;
    private static ConfigEntry<KeyboardShortcut> _prevLineKey;

    private static int _lineIndex = -1;

    private const int BodyLayerMask = 1 << 19;
    private const int ConnectorLayerMask = 1 << 27;

    internal static void Init(ManualLogSource log, ConfigFile config)
    {
        _log = log;
        _nextLineKey = ModKeys.Bind(config, "Keys", "FactoryNextLine",
            new KeyboardShortcut(KeyCode.G, KeyCode.LeftShift),
            "In the factory: reads the next conveyor line, where it starts and where it leads.");
        _prevLineKey = ModKeys.Bind(config, "Keys", "FactoryPreviousLine",
            new KeyboardShortcut(KeyCode.G, KeyCode.LeftControl),
            "In the factory: reads the previous conveyor line.");
    }

    internal static void Update()
    {
        if (StatusKeys.IsTyping()) return;

        try
        {
            if (_nextLineKey.Value.IsDown()) StepLine(1);
            else if (_prevLineKey.Value.IsDown()) StepLine(-1);
        }
        catch (Exception ex)
        {
            _log?.LogError($"[Factory] Line key failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    // ---- the graph ----------------------------------------------------------------------------

    private sealed class Node
    {
        internal ConveyorWgoData Data;
        internal ConveyorElementType Type;
        internal readonly List<Node> Out = new();
        internal readonly List<Node> In = new();

        internal bool IsBelt => Type == ConveyorElementType.Cell || Type == ConveyorElementType.UndergroundCell;
        internal Vector3 Position => Data.Position;
    }

    private sealed class ByReference : IEqualityComparer<object>
    {
        internal static readonly ByReference Instance = new();
        bool IEqualityComparer<object>.Equals(object a, object b) => ReferenceEquals(a, b);
        int IEqualityComparer<object>.GetHashCode(object o) => RuntimeHelpers.GetHashCode(o);
    }

    private static string CurrentSceneId()
    {
        var player = MainGame.PlayerController;
        if (player == null) return null;
        try
        {
            return player.CurrentGameScene == null ? null : player.CurrentGameScene.Id;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Every factory piece in a scene, linked in the direction items move.</summary>
    private static List<Node> Graph(string sceneId)
    {
        var nodes = new Dictionary<object, Node>(ByReference.Instance);
        var scene = string.IsNullOrEmpty(sceneId) ? null : MainGame.Instance?.GameSave?.worldData?.GetGameSceneDataById(sceneId);
        if (scene?.wgoDataList == null) return new List<Node>();

        foreach (var wgoData in scene.wgoDataList)
        {
            if (wgoData is not ConveyorWgoData data || data.ConveyorComponent == null || data.Definition == null) continue;
            var type = data.Definition.conveyorType;
            if (type == ConveyorElementType.None) continue;
            nodes[data] = new Node { Data = data, Type = type };
        }

        void Link(WgoData from, WgoData to)
        {
            if (from == null || to == null || ReferenceEquals(from, to)) return;
            if (!nodes.TryGetValue(from, out var a) || !nodes.TryGetValue(to, out var b)) return;
            if (a.Out.Contains(b)) return;
            a.Out.Add(b);
            b.In.Add(a);
        }

        foreach (var node in nodes.Values.ToList())
        {
            try
            {
                foreach (var next in Downstream(node.Data.ConveyorComponent)) Link(node.Data, next);
                if (node.Data.ConveyorComponent is ConveyorWorkbenchComponent bench)
                    foreach (var id in bench.connectedInWgoDataUniqueId) Link(Resolve(id), node.Data);
            }
            catch (Exception ex)
            {
                _log?.LogWarning($"[Factory] Could not read the links of '{node.Data.id}': {ex.Message}");
            }
        }

        return nodes.Values.ToList();
    }

    private static WgoData Resolve(SGuid id) =>
        SGuid.IsNullOrEmpty(id) ? null : MainGame.Instance?.GameSave?.worldData?.GetWgoData(id);

    /// <summary>What a piece hands its items on to, read from the field each kind keeps it in.</summary>
    private static IEnumerable<WgoData> Downstream(ConveyorComponent component)
    {
        switch (component)
        {
            case ConveyorCellComponent c: return c.ConnectedWgoData;
            case ConveyorCellUndergroundComponent c: return c.ConnectedWgoData;
            case ConveyorCellStationComponent c: return c.ConnectedWgoData;
            case ConveyorSplitterComponent c: return c.ConnectedWgoData;
            case ConveyorChestComponent c: return c.ConnectedWgoData;
            case ConveyorChestOutComponent c: return c.ConnectedWgoData;
            case ConveyorPalletComponent c: return c.ConnectedWgoData == null ? Array.Empty<WgoData>() : new WgoData[] { c.ConnectedWgoData };
            case ConveyorWorkbenchComponent c: return c.connectedOutWgoDataUniqueId.Select(Resolve);
            default: return Array.Empty<WgoData>();
        }
    }

    /// <summary>True while the player is somewhere with factory pieces in it.</summary>
    internal static bool InFactory() => Graph(CurrentSceneId()).Count > 0;

    /// <summary>A belt is listed by the line reader, not one by one in the object list.</summary>
    internal static bool IsBeltPiece(WgoData data)
    {
        if (data is not ConveyorWgoData || data.Definition == null) return false;
        var type = data.Definition.conveyorType;
        return type == ConveyorElementType.Cell || type == ConveyorElementType.UndergroundCell;
    }

    // ---- G: the factory's state ---------------------------------------------------------------

    /// <summary>The factory part of G, or null when there is no factory here.</summary>
    internal static string Status()
    {
        var nodes = Graph(CurrentSceneId());
        if (nodes.Count == 0) return null;

        var parts = new List<string>();
        var system = MainGame.Instance?.conveyorSystem;

        if (system != null)
        {
            var zone = system.ConveyorWorldZone;
            var have = zone == null ? 0 : Mathf.RoundToInt(zone.GetTotalQuality(WorldZoneWgoQualityType.ConveyorPowerSource));
            var need = zone == null ? 0 : Mathf.RoundToInt(zone.GetTotalQuality(WorldZoneWgoQualityType.ConveyorCells));

            if (system.IsPaused) parts.Add(Loc.Get("factory.paused"));
            else if (!system.HasEnoughPower) parts.Add(Loc.Fmt("factory.no_power", have, need));
            else parts.Add(zone == null ? Loc.Get("factory.running") : Loc.Fmt("factory.power", have, need));
        }

        foreach (var node in nodes.Where(n => n.Type == ConveyorElementType.PowerSource))
        {
            var part = node.Data.MainWgoPartData;
            var busy = part?.GetDockPoints(DockPointData.Availability.OnlyOccupied, DockPointData.Filter.OnlyZombie).Count ?? 0;
            var free = part?.GetDockPoints(DockPointData.Availability.OnlyNotOccupied, DockPointData.Filter.OnlyZombie).Count ?? 0;
            parts.Add(Loc.Fmt("factory.carousel", ObjectNames.Of(node.Data.id), busy, free));
        }

        var belts = nodes.Count(n => n.IsBelt);
        var lines = Lines(nodes, PlayerPosition(), out var problems);
        if (lines.Count > 0)
            parts.Add(problems > 0
                ? Loc.Fmt("factory.lines_problems", belts, lines.Count, problems)
                : Loc.Fmt("factory.lines", belts, lines.Count));

        foreach (var node in nodes.Where(n => n.Type == ConveyorElementType.Workbench).OrderBy(n => Flat(n.Position - PlayerPosition()).sqrMagnitude))
            parts.Add(Workbench(node));

        return string.Join(". ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));
    }

    private static string Workbench(Node node)
    {
        var data = node.Data;
        var parts = new List<string> { ObjectNames.Of(data.id) };

        if (data.Worker is ZombieWgoData zombie)
            parts.Add(Loc.Fmt("factory.worker", string.IsNullOrWhiteSpace(zombie.Name) ? ObjectNames.Of(zombie.id) : zombie.Name));
        else if (data.Worker == null && !data.Definition.isAutoCrafter)
            parts.Add(Loc.Get("factory.no_worker"));

        var craft = data.CraftComponent;
        string state = null;
        try
        {
            state = ObjectStatus.Of(data);
        }
        catch (Exception ex)
        {
            _log?.LogWarning($"[Factory] Could not read the craft of '{data.id}': {ex.Message}");
        }
        if (state != null) parts.Add(state);
        else if (craft != null && !craft.HasCraftsInQueue) parts.Add(Loc.Get("factory.nothing_queued"));

        if (craft != null)
        {
            if (craft.Status == CraftComponentStatus.WaitingForOutputDrop) parts.Add(Loc.Get("factory.output_blocked"));

            var missing = Missing(data);
            if (missing != null) parts.Add(Loc.Fmt("factory.missing", missing));
        }

        if (node.In.Count == 0) parts.Add(Loc.Get("factory.no_belt_in"));
        if (node.Out.Count == 0) parts.Add(Loc.Get("factory.no_belt_out"));

        var open = OpenSides(data);
        if (open != null) parts.Add(open);

        return string.Join(", ", parts);
    }

    /// <summary>What the first order still lacks, the way <c>ConveyorWorkbenchComponent.GetItemFromConveyor</c> checks it.</summary>
    private static string Missing(WgoData data)
    {
        var craft = data.CraftComponent;
        if (craft == null || !craft.HasCraftsInQueue || craft.IsStarted) return null;
        var element = craft.CraftElementsQueue[0];
        if (element?.Requirements == null || element.CraftStatus != CraftStatus.NotEnoughResources) return null;

        var inventory = data.CraftableObjectCraftInventory?.Data;
        var names = new List<string>();
        foreach (var need in element.Requirements)
        {
            if (need == null || need.IsEmpty || need.IsGroup) continue;
            var count = need.GetCount(data);
            if (inventory != null && inventory.HasItemQuantityInInventory(need.id, count)) continue;
            names.Add(count > 1 ? $"{count} {ItemText.Name(need.id)}" : ItemText.Name(need.id));
        }
        return names.Count == 0 ? null : string.Join(", ", names);
    }

    private static readonly AccessTools.FieldRef<ConveyorWorkbenchBuildConnector, Direction> BenchConnectorDirection =
        AccessTools.FieldRefAccess<ConveyorWorkbenchBuildConnector, Direction>("direction");

    /// <summary>
    /// "free: in west, out east" - a bench's connectors with no belt on them, the arrows the game
    /// draws round a workbench while building. The bench records a link under the connector's own
    /// direction field, so that is what is compared.
    /// </summary>
    private static string OpenSides(WgoData data)
    {
        var wgo = GameScene.GetWgoViewGlobal(data.UniqueId);
        if (wgo == null || data is not ConveyorWgoData conveyor || conveyor.ConveyorComponent == null) return null;

        var taken = conveyor.ConveyorComponent.occupiedConnectorsDirections;
        var free = new List<string>();
        foreach (var connector in wgo.GetComponentsInChildren<ConveyorWorkbenchBuildConnector>())
        {
            if (!connector.gameObject.activeInHierarchy) continue;
            if (taken != null && taken.Contains(BenchConnectorDirection(connector))) continue;
            free.Add(Loc.Fmt(KindKey(connector.ConnectionType), Side(connector.transform.position - wgo.transform.position)));
        }
        return free.Count == 0 ? null : Loc.Fmt("factory.free_sides", string.Join(", ", free));
    }

    // ---- Shift+G / Ctrl+G: the lines ----------------------------------------------------------

    private static void StepLine(int delta)
    {
        if (ResourceAnnouncer.CurrentPlayer() == null) { ScreenReader.Say(Loc.Get("nav.not_in_game")); return; }

        var nodes = Graph(CurrentSceneId());
        if (nodes.Count == 0) { Say(Loc.Get("factory.none_here")); return; }

        var lines = Lines(nodes, PlayerPosition(), out _);
        if (lines.Count == 0) { Say(Loc.Get("factory.no_lines")); return; }

        _lineIndex = _lineIndex < 0 && delta < 0 ? lines.Count - 1 : ((_lineIndex + delta) % lines.Count + lines.Count) % lines.Count;
        Say(Loc.Fmt("factory.line", _lineIndex + 1, lines.Count, lines[_lineIndex]));
    }

    /// <summary>
    /// The factory as spoken lines, nearest start first. A line starts at every link out of a
    /// chest, bench, splitter or pallet, at a belt nothing feeds, and where two belts join; it runs
    /// along single belts and ends at the next of those, or at a belt that leads nowhere.
    /// </summary>
    private static List<string> Lines(List<Node> nodes, Vector3 from, out int problems)
    {
        var found = new List<(float Distance, string Text)>();
        var walked = new HashSet<object>(ByReference.Instance);
        var count = 0;

        void Add(Node start, Node first)
        {
            var text = Walk(start, first, walked, from, out var problem);
            if (problem) count++;
            found.Add((Flat((start ?? first).Position - from).sqrMagnitude, text));
        }

        foreach (var node in nodes)
        {
            if (!node.IsBelt)
                foreach (var next in node.Out) Add(node, next);
            else if (node.In.Count == 0 || node.In.Count > 1)
                Add(null, node);
        }

        // Belts that only ever feed each other round in a ring, with nothing joining from outside.
        foreach (var node in nodes)
            if (node.IsBelt && !walked.Contains(node))
                Add(null, node);

        problems = count;
        return found.OrderBy(f => f.Distance).Select(f => f.Text).ToList();
    }

    private static string Walk(Node start, Node first, HashSet<object> walked, Vector3 from, out bool problem)
    {
        problem = false;
        var origin = start ?? first;
        var where = Navigator.KeyDirections(Flat(origin.Position - from));

        string head;
        if (start != null)
        {
            head = Loc.Fmt("factory.from", ObjectNames.Of(start.Data.id), where);
            if (start.Type == ConveyorElementType.Splitter && start.Out.Count > 1) head += ", " + Loc.Get("factory.splitter_turns");
        }
        else if (first.In.Count > 1) head = Loc.Fmt("factory.from_join", where);
        else if (first.In.Count == 0)
        {
            head = Loc.Fmt("factory.from_unfed", where);
            problem = true;
        }
        else head = Loc.Fmt("factory.from_ring", where);

        var runs = new List<(int Dir, int Count)>();
        void Step(int dir)
        {
            if (dir < 0) return;
            if (runs.Count > 0 && runs[runs.Count - 1].Dir == dir) runs[runs.Count - 1] = (dir, runs[runs.Count - 1].Count + 1);
            else runs.Add((dir, 1));
        }

        var seen = new HashSet<object>(ByReference.Instance);
        var cur = first;
        string end;
        while (true)
        {
            if (!cur.IsBelt)
            {
                end = Loc.Fmt("factory.into", ObjectNames.Of(cur.Data.id));
                break;
            }
            // A belt where lines join starts a line of its own; only that line walks on from it.
            if (cur.In.Count > 1 && !(start == null && cur == first))
            {
                end = Loc.Get("factory.joins_line");
                break;
            }
            if (!seen.Add(cur))
            {
                end = Loc.Get("factory.loops");
                break;
            }
            walked.Add(cur);

            if (cur.Out.Count == 0)
            {
                var facing = BeltFacing(cur.Data);
                if (facing == null && runs.Count > 0) facing = DirVector(runs[runs.Count - 1].Dir);
                Step(facing == null ? -1 : Dir(facing.Value));
                end = DeadEnd(cur, facing, from);
                problem = true;
                break;
            }

            var next = cur.Out[0];
            Step(Dir(next.Position - cur.Position));
            if (cur.Out.Count > 1)
            {
                end = Loc.Fmt("factory.branches", string.Join(", ", cur.Out.Select(o => ObjectNames.Of(o.Data.id))));
                break;
            }
            cur = next;
        }

        var body = runs.Count == 0 ? null : Loc.Fmt("factory.runs", string.Join(", ", runs.Select(r => Loc.Fmt("factory.run", r.Count, DirName(r.Dir)))));
        return body == null ? $"{head}: {end}" : $"{head}: {body}, {end}";
    }

    /// <summary>"dead end, points at Sawbench but is not joined to it" - the commonest building mistake.</summary>
    private static string DeadEnd(Node belt, Vector3? facing, Vector3 from)
    {
        var where = Navigator.KeyDirections(Flat(belt.Position - from));
        if (facing != null)
        {
            var ahead = Flat(facing.Value).normalized;
            ConveyorWgoData best = null;
            var bestDistance = 3f;
            foreach (var other in AllInScene(belt))
            {
                if (ReferenceEquals(other, belt.Data)) continue;
                var offset = Flat(other.Position - belt.Position);
                var distance = offset.magnitude;
                if (distance < 0.05f || distance >= bestDistance) continue;
                if (Vector2.Dot(offset / distance, ahead) < 0.7f) continue;
                bestDistance = distance;
                best = other;
            }
            if (best != null) return Loc.Fmt("factory.dead_end_at", ObjectNames.Of(best.id), where);
        }
        return Loc.Fmt("factory.dead_end", where);
    }

    private static IEnumerable<ConveyorWgoData> AllInScene(Node near)
    {
        var scene = MainGame.Instance?.GameSave?.worldData?.GetGameSceneDataById(near.Data.WorldId);
        if (scene?.wgoDataList == null) yield break;
        foreach (var w in scene.wgoDataList)
            if (w is ConveyorWgoData c && c.Definition != null && c.Definition.conveyorType != ConveyorElementType.None)
                yield return c;
    }

    /// <summary>Which way a belt carries items: from its centre to its one connector, which sits at the outgoing end.</summary>
    private static Vector3? BeltFacing(WgoData data)
    {
        var wgo = GameScene.GetWgoViewGlobal(data.UniqueId);
        return wgo == null ? null : BeltFacing(wgo);
    }

    private static Vector3? BeltFacing(Wgo wgo)
    {
        foreach (var connector in wgo.GetComponentsInChildren<BuildConnector>())
            if (connector.gameObject.activeInHierarchy && connector.ConnectionType != ConveyorConnectionType.In)
                return connector.transform.position - wgo.transform.position;
        return null;
    }

    // ---- building -----------------------------------------------------------------------------

    /// <summary>True when the piece being placed is part of the factory.</summary>
    internal static bool IsConveyor(Wgo wgo) =>
        wgo != null && wgo.Data?.Definition != null && wgo.Data.Definition.conveyorType != ConveyorElementType.None;

    /// <summary>
    /// "runs east, joins: sends east into Sawbench, takes from Chest to the west" - for the piece on
    /// the build pointer, before it is built. Null when it is not a factory piece.
    /// </summary>
    internal static string Placement(Wgo pointer)
    {
        if (!IsConveyor(pointer)) return null;
        try
        {
            var parts = new List<string>();
            var facing = Facing(pointer);
            if (facing != null) parts.Add(facing);

            var links = PredictLinks(pointer);
            parts.Add(links.Count == 0 ? Loc.Get("factory.joins_nothing") : Loc.Fmt("factory.joins", string.Join(", ", links)));
            _log?.LogInfo($"[Factory] Placing '{pointer.Data.id}' at {pointer.transform.position}: " +
                          $"{pointer.GetComponentsInChildren<BuildConnector>().Count(c => c.gameObject.activeInHierarchy)} active connector(s), " +
                          $"{links.Count} predicted link(s).");
            return string.Join(", ", parts);
        }
        catch (Exception ex)
        {
            _log?.LogWarning($"[Factory] Could not work out what the piece would join: {ex.Message}");
            return null;
        }
    }

    /// <summary>"runs east" for a belt; "in west, out east" for a piece with several connectors.</summary>
    private static string Facing(Wgo wgo)
    {
        var type = wgo.Data.Definition.conveyorType;
        if (type == ConveyorElementType.Cell || type == ConveyorElementType.UndergroundCell)
        {
            var facing = BeltFacing(wgo);
            return facing == null ? null : Loc.Fmt("factory.runs_to", Side(facing.Value));
        }

        var sides = new List<string>();
        foreach (var connector in wgo.GetComponentsInChildren<BuildConnector>())
        {
            if (!connector.gameObject.activeInHierarchy) continue;
            var text = Loc.Fmt(KindKey(connector.ConnectionType), Side(connector.transform.position - wgo.transform.position));
            if (!sides.Contains(text)) sides.Add(text);
        }
        return sides.Count == 0 ? null : string.Join(", ", sides);
    }

    private static string KindKey(ConveyorConnectionType type) => type switch
    {
        ConveyorConnectionType.In => "factory.side_in",
        ConveyorConnectionType.Out => "factory.side_out",
        _ => "factory.side_inout",
    };

    /// <summary>
    /// The links <c>ConveyorBuildPointer.MakeConnections</c> would make for this piece where it
    /// stands - the same overlaps and the same per-connector rules, without connecting anything.
    /// </summary>
    private static List<string> PredictLinks(Wgo me)
    {
        Physics.SyncTransforms();
        var links = new List<string>();
        var myType = me.Data.Definition.conveyorType;
        var restriction = Restriction(me);

        // My connectors over a neighbour's body.
        var hits = new Collider[10];
        foreach (var connector in me.GetComponentsInChildren<BuildConnector>())
        {
            if (!connector.gameObject.activeInHierarchy || connector.BoxCollider == null) continue;
            var bounds = connector.BoxCollider.bounds;
            var count = Physics.OverlapBoxNonAlloc(bounds.center, bounds.extents / 2f, hits, Quaternion.identity, BodyLayerMask);
            for (var i = 0; i < count; i++)
            {
                var hit = hits[i];
                var other = Neighbour(hit, me);
                if (other == null) continue;
                var otherType = other.Data.Definition.conveyorType;
                if (otherType == ConveyorElementType.UndergroundCell)
                {
                    var allowed = myType == ConveyorElementType.UndergroundCell || restriction == null || !restriction.disabledTypes.Contains(otherType);
                    if (!allowed || !hit.TryGetComponent<ConveyorCellSequenceIdentifier>(out var sequence) || !sequence.isStartElement) continue;
                }

                // The game tries the first conveyor piece it finds under each connector, and only that one.
                if (WouldConnect(connector, me, other))
                    links.Add(LinkText(me, other, inward: connector is ConveyorWorkbenchBuildConnector && connector.ConnectionType == ConveyorConnectionType.In));
                break;
            }
        }

        // A neighbour's connectors over my body.
        var underground = myType == ConveyorElementType.UndergroundCell;
        var near = new Collider[20];
        foreach (var body in me.GetComponentsInChildren<BoxCollider>())
        {
            if (body.gameObject.layer != 19) continue;
            if (underground && (!body.TryGetComponent<ConveyorCellSequenceIdentifier>(out var sequence) || !sequence.isStartElement)) continue;

            var half = body.bounds.extents / 2f;
            half.y = 0.2f;
            var count = Physics.OverlapBoxNonAlloc(body.bounds.center, half, near, Quaternion.identity, ConnectorLayerMask);
            for (var i = 0; i < count; i++)
            {
                var connector = near[i] == null ? null : near[i].GetComponent<BuildConnector>();
                if (connector == null) continue;
                var other = Neighbour(near[i], me);
                if (other == null || !WouldConnect(connector, other, me)) continue;

                // Their connector makes the link; a bench's input connector means items go to it.
                var toThem = connector is ConveyorWorkbenchBuildConnector && connector.ConnectionType == ConveyorConnectionType.In;
                var text = LinkText(me, other, inward: !toThem);
                if (!links.Contains(text)) links.Add(text);
            }
        }

        return links;
    }

    /// <summary>A built factory piece the collider belongs to - not the pointer, and no other placement ghost.</summary>
    private static Wgo Neighbour(Collider hit, Wgo me)
    {
        if (hit == null) return null;
        var other = hit.GetComponentInParent<Wgo>();
        if (other == null || other == me || other.Data == null || other.Data.isTempObject) return null;
        return other.Data is ConveyorWgoData && other.Data.Definition != null ? other : null;
    }

    private static ConnectivityRestriction Restriction(Wgo me)
    {
        try
        {
            var extents = me.MainWgoPart.WgoPartData.BakedData.ChunkBounds.withoutShadows.extents / 4f;
            var hits = new Collider[10];
            var count = Physics.OverlapBoxNonAlloc(me.transform.position, extents, hits, Quaternion.identity);
            for (var i = 0; i < count; i++)
            {
                var hit = hits[i];
                if (hit == null) continue;
                var owner = hit.GetComponentInParent<Wgo>();
                if (owner != null && (owner.Data == null || owner.Data.isTempObject || owner == me)) continue;
                if (hit.TryGetComponent<ConnectivityRestriction>(out var restriction)) return restriction;
            }
        }
        catch
        {
            // No baked bounds on a ghost: no restriction found, as with no collider there.
        }
        return null;
    }

    /// <summary>
    /// Mirrors each <c>BuildConnector.TryConnect</c> override and the receiving component's
    /// <c>Connect</c> type check, for a connector on <paramref name="owner"/> touching <paramref name="other"/>.
    /// </summary>
    private static bool WouldConnect(BuildConnector connector, Wgo owner, Wgo other)
    {
        var ownerType = owner.Data.Definition.conveyorType;
        var otherType = other.Data.Definition.conveyorType;
        if (!Accepts(ownerType, otherType)) return false;

        var to = connector.transform.position - owner.transform.position;

        float AngleToFirst()
        {
            var first = other.GetComponentInChildren<BuildConnector>();
            return first == null ? 0f : Mathf.Abs(Vector3.Angle(first.transform.position - other.transform.position, to));
        }

        bool SplitterInput()
        {
            foreach (var c in other.GetComponentsInChildren<BuildConnector>())
            {
                if (!c.gameObject.activeSelf || c.ConnectionType != ConveyorConnectionType.In) continue;
                var angle = Mathf.Abs(Vector3.Angle(c.transform.position - other.transform.position, to));
                if (Mathf.Abs(angle - 180f) < 1E-05f || Mathf.Abs(angle) < 1E-05f) return true;
            }
            return false;
        }

        switch (connector)
        {
            case ConveyorCellBuildConnector:
                return otherType switch
                {
                    ConveyorElementType.Cell or ConveyorElementType.UndergroundCell => AngleToFirst() < 180f,
                    ConveyorElementType.Chest => true,
                    ConveyorElementType.Splitter => SplitterInput(),
                    ConveyorElementType.StationCell => AngleToFirst().Equals(180f),
                    _ => false,
                };
            case ConveyorCellUndergroundBuildConnector:
            case ConveyorChestBuildConnector:
                return otherType switch
                {
                    ConveyorElementType.Cell or ConveyorElementType.UndergroundCell => AngleToFirst() < 180f,
                    ConveyorElementType.Splitter => SplitterInput(),
                    _ => false,
                };
            case ConveyorSplitterBuildConnector:
                if (connector.ConnectionType != ConveyorConnectionType.Out) return false;
                return otherType switch
                {
                    ConveyorElementType.Cell or ConveyorElementType.UndergroundCell => AngleToFirst() < 180f,
                    ConveyorElementType.Chest => true,
                    ConveyorElementType.Splitter => SplitterInput(),
                    _ => false,
                };
            case ConveyorPalletBuildConnector:
            case ConveyorWorkbenchBuildConnector:
                return otherType is ConveyorElementType.Cell or ConveyorElementType.UndergroundCell or ConveyorElementType.Splitter;
            case ConveyorCellStationBuildConnector:
                return false;
            default:
                return true;
        }
    }

    /// <summary>The type check at the top of each component's <c>Connect</c>.</summary>
    private static bool Accepts(ConveyorElementType owner, ConveyorElementType other) => owner switch
    {
        ConveyorElementType.Cell => other is ConveyorElementType.Cell or ConveyorElementType.Chest or ConveyorElementType.Splitter
            or ConveyorElementType.UndergroundCell or ConveyorElementType.StationCell,
        ConveyorElementType.UndergroundCell or ConveyorElementType.Splitter => other is ConveyorElementType.Cell
            or ConveyorElementType.Chest or ConveyorElementType.Splitter or ConveyorElementType.UndergroundCell,
        ConveyorElementType.Chest or ConveyorElementType.ChestOut => other is ConveyorElementType.Cell
            or ConveyorElementType.Splitter or ConveyorElementType.UndergroundCell,
        ConveyorElementType.Pallet => other == ConveyorElementType.Cell,
        ConveyorElementType.Workbench => other is ConveyorElementType.Cell or ConveyorElementType.Splitter,
        _ => false,
    };

    private static string LinkText(Wgo me, Wgo other, bool inward)
    {
        var side = Side(other.transform.position - me.transform.position);
        var name = ObjectNames.Of(other.Data.id);
        return Loc.Fmt(inward ? "factory.link_in" : "factory.link_out", side, name);
    }

    // ---- after building -----------------------------------------------------------------------

    private static Wgo _built;

    /// <summary>Forget the last piece; the next build fills it in.</summary>
    internal static void BeginBuild() => _built = null;

    /// <summary>"joins: sends east into Sawbench" for the piece just built, from the links the game made.</summary>
    internal static string BuiltLinks()
    {
        var built = _built;
        _built = null;
        if (built == null || built.Data is not ConveyorWgoData data) return null;

        try
        {
            var node = Graph(data.WorldId).FirstOrDefault(n => ReferenceEquals(n.Data, data));
            if (node == null) return null;

            var links = new List<string>();
            foreach (var next in node.Out)
                links.Add(Loc.Fmt("factory.link_out", Side(next.Position - node.Position), ObjectNames.Of(next.Data.id)));
            foreach (var prev in node.In)
                links.Add(Loc.Fmt("factory.link_in", Side(prev.Position - node.Position), ObjectNames.Of(prev.Data.id)));

            _log?.LogInfo($"[Factory] Built '{data.id}' at {data.Position}: {links.Count} link(s).");
            return links.Count == 0 ? Loc.Get("factory.joined_nothing") : Loc.Fmt("factory.joins", string.Join(", ", links));
        }
        catch (Exception ex)
        {
            _log?.LogWarning($"[Factory] Could not read the links of the new piece: {ex.Message}");
            return null;
        }
    }

    /// <summary>The first piece a build connects - the one placed; the belt some pieces add on their own comes after.</summary>
    [HarmonyPatch(typeof(ConveyorBuildPointer), nameof(ConveyorBuildPointer.MakeConnections))]
    [HarmonyPostfix]
    private static void ConveyorBuildPointer_MakeConnections(Wgo builtWgo)
    {
        if (_built == null) _built = builtWgo;
    }

    // ---- shared -------------------------------------------------------------------------------

    private static Vector3 PlayerPosition()
    {
        var player = MainGame.PlayerController;
        return player == null ? Vector3.zero : player.MovablePosition;
    }

    private static Vector2 Flat(Vector3 v) => new(v.x, v.z);

    /// <summary>0 north, 1 east, 2 south, 3 west - the bigger axis wins. North is W, as everywhere in the mod.</summary>
    private static int Dir(Vector3 offset)
    {
        if (Mathf.Abs(offset.x) < 0.01f && Mathf.Abs(offset.z) < 0.01f) return -1;
        if (Mathf.Abs(offset.x) > Mathf.Abs(offset.z)) return offset.x > 0 ? 1 : 3;
        return offset.z > 0 ? 0 : 2;
    }

    private static Vector3? DirVector(int dir) => dir switch
    {
        0 => Vector3.forward,
        1 => Vector3.right,
        2 => Vector3.back,
        3 => Vector3.left,
        _ => null,
    };

    private static string DirName(int dir) => Loc.Get(dir switch
    {
        0 => "factory.north",
        1 => "factory.east",
        2 => "factory.south",
        _ => "factory.west",
    });

    private static string Side(Vector3 offset) => DirName(Math.Max(0, Dir(offset)));

    private static void Say(string line)
    {
        _log?.LogInfo($"[Factory] {line}");
        ScreenReader.Say(line);
    }
}
