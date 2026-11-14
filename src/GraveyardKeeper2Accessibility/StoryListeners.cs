namespace GraveyardKeeper2Accessibility;

/// <summary>
/// What the story is listening for right now, read from <c>GlobalEventsSystem.checkingEvents</c>.
///
/// <para>
/// The only listeners in that list are quest checks - the start check of each awaiting quest and
/// the finish check of each quest in progress - so it is an exact answer to "which zone, which
/// object does the story need next". A zone's <c>customTag</c> does nothing at all unless a check
/// is waiting for it: in the intro the player walked to the bridge zone before
/// <c>2_intro_scout_road_to_bridge</c> had been finished by the corner zone, and nothing happened,
/// because at that moment nobody was listening for the bridge.
/// </para>
/// </summary>
internal static class StoryListeners
{
    /// <summary>Zone tags a quest waits to see entered or left.</summary>
    internal static readonly HashSet<string> ZoneTags = new(StringComparer.Ordinal);

    /// <summary>Object ids a quest waits on - used, destroyed, handed something.</summary>
    internal static readonly HashSet<string> WgoIds = new(StringComparer.Ordinal);

    /// <summary>Object custom tags a quest waits on.</summary>
    internal static readonly HashSet<string> WgoTags = new(StringComparer.Ordinal);

    /// <summary>
    /// Talk events a quest waits for the player to use, by object id: <c>npc_donkey</c> →
    /// <c>21_base_donkey_1</c>. Kept apart from <see cref="WgoIds"/> because such a check can only
    /// ever fire while the object holds that event - see <see cref="AwaitsWgo"/>.
    /// </summary>
    private static readonly Dictionary<string, HashSet<string>> WgoEvents = new(StringComparer.Ordinal);

    private static int _frame = -1;

    /// <summary>Rereads the listeners, at most once a frame.</summary>
    internal static void Refresh()
    {
        if (_frame == Time.frameCount) return;
        _frame = Time.frameCount;

        ZoneTags.Clear();
        WgoIds.Clear();
        WgoTags.Clear();
        WgoEvents.Clear();

        var events = MainGame.Instance?.GameSave?.globalEventsSystem?.checkingEvents;
        if (events == null) return;

        foreach (var ev in events)
        {
            if (ev == null || string.IsNullOrEmpty(ev.id) || ev.trigerrables.Count == 0) continue;

            switch (ev.type)
            {
                case GlobalEventsSystem.Event.Type.PlayerEnterGDZone:
                case GlobalEventsSystem.Event.Type.PlayerExitGDZone:
                    ZoneTags.Add(ev.id);
                    break;

                case GlobalEventsSystem.Event.Type.WgoDead:
                case GlobalEventsSystem.Event.Type.RemoveWgoDataFromScene:
                case GlobalEventsSystem.Event.Type.CustomInteraction:
                    if (!IsMainStory(ev)) break;
                    var colon = ev.id.IndexOf(':');
                    if (colon > 0 && colon < ev.id.Length - 1)
                    {
                        var wgo = ev.id.Substring(0, colon);
                        if (!WgoEvents.TryGetValue(wgo, out var names)) WgoEvents[wgo] = names = new HashSet<string>(StringComparer.Ordinal);
                        names.Add(ev.id.Substring(colon + 1));
                    }
                    else WgoIds.Add(BeforeColon(ev.id));
                    break;

                case GlobalEventsSystem.Event.Type.Interaction:
                case GlobalEventsSystem.Event.Type.PlayerInsertOverheadToWgoAnItem:
                case GlobalEventsSystem.Event.Type.PlayerTakeFromWgoTheItem:
                    // Objects are everywhere, so only the main story's own checks mark one - a side
                    // quest waiting to be offered at any chest would otherwise flag every chest.
                    if (IsMainStory(ev)) WgoIds.Add(BeforeColon(ev.id));
                    break;

                case GlobalEventsSystem.Event.Type.WgoCustomTagDead:
                case GlobalEventsSystem.Event.Type.WgoCustomTagHpValueReached:
                    if (IsMainStory(ev)) WgoTags.Add(BeforeColon(ev.id));
                    break;
            }
        }
    }

    internal static bool AwaitsZone(GDZone zone)
    {
        Refresh();
        return zone != null && !string.IsNullOrEmpty(zone.customTag) && ZoneTags.Contains(zone.customTag);
    }

    /// <summary>
    /// True when entering a zone cannot move the story: its quests are all over, or no quest
    /// belongs to it at all. False when a quest listens for it or one of its quests is still open.
    ///
    /// <para>
    /// <b>Reported (2026-11-09):</b> "Doctor Sister" and a forest zone stayed in the story triggers
    /// after their quests were done, and every walk warned about story zones "that are also already
    /// done". A zone with a flowscript was listed for the script alone, and the script never goes
    /// away: <c>GDZone_Doctor_Sister</c> runs <c>Event_71_Doctor_Sister</c>, whose only quest,
    /// <c>71_doctor_sister_corpse</c>, was completed. The story scripts are named
    /// <c>Event_&lt;quest number&gt;_…</c>, so the quests of that number are the zone's quests.
    /// Without a number, quests whose words begin with the zone's first two words
    /// (<c>GDZone_Forest_Guards</c> → <c>31_forest_guards_*</c>) are used. A quest not begun yet
    /// (Available) still counts as open - the forest guards' block starts that way, when you walk in.
    /// </para>
    /// </summary>
    internal static bool ZoneDone(GDZone zone)
    {
        if (zone == null || AwaitsZone(zone)) return false;
        RefreshQuests();
        if (QuestStatusById.Count == 0) return false;

        var number = ScriptNumber(zone.onEnter) ?? ScriptNumber(zone.onExit) ?? ScriptNumber(zone.onCrossedToLeft)
            ?? ScriptNumber(zone.onCrossedToRight) ?? ScriptNumber(zone.onCrossedToUp) ?? ScriptNumber(zone.onCrossedToDown);

        string prefix = null;
        if (number == null)
        {
            var tag = Navigator.Humanise(string.IsNullOrEmpty(zone.customTag) ? zone.name : zone.customTag);
            var words = tag.ToLowerInvariant().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (words.Length < 2) return false;
            prefix = words[0] + "_" + words[1];
        }

                foreach (var pair in QuestStatusById)
        {
            var id = pair.Key;
            var underscore = id.IndexOf('_');
            if (underscore <= 0) continue;
            var lead = id.Substring(0, underscore);
            if (!lead.All(char.IsDigit)) continue;

            var hit = number != null
                ? lead == number
                : id.Substring(underscore + 1).StartsWith(prefix + "_", StringComparison.Ordinal) || id.Substring(underscore + 1) == prefix;
            if (!hit) continue;

            if (pair.Value != QuestStatus.Completed && pair.Value != QuestStatus.Canceled) return false;
        }

        // No quest belongs to it at all: a flavour scene that plays every time and never moves
        // the story on. GDZone_Bridge_To_Sawmill_Forest howls a wolf and warns about the forest on
        // every visit, with no quest of its name and nothing that ever switches it off (user,
        // 2026-11-09: "I can't seem to find a way to let it disappear").
        return true;
    }

    private static readonly Regex EventNumber = new(@"^Event_(\d+)_", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static string ScriptNumber(GDZone.GDZoneEvent e)
    {
        var name = e?.flowScript == null ? null : e.flowScript.name;
        if (string.IsNullOrEmpty(name)) return null;
        var m = EventNumber.Match(name);
        return m.Success ? m.Groups[1].Value.TrimStart('0').PadLeft(1, '0') : null;
    }

    private static readonly Dictionary<string, QuestStatus> QuestStatusById = new(StringComparer.Ordinal);
    private static int _questFrame = -1;

    private static void RefreshQuests()
    {
        if (_questFrame == Time.frameCount) return;
        _questFrame = Time.frameCount;
        QuestStatusById.Clear();
        try
        {
            var quests = MainGame.Instance?.GameSave?.questSystemData?.questCollection?.quests;
            if (quests == null) return;
            foreach (var quest in quests)
            {
                if (quest != null && !string.IsNullOrEmpty(quest.id)) QuestStatusById[quest.id] = quest.status;
            }
        }
        catch
        {
            QuestStatusById.Clear();
        }
    }

    /// <summary>
    /// True when a main-story check waits on this object right now.
    ///
    /// <para>
    /// <b>A talk check counts only while the talk is there</b> (user, 2026-11-09: the donkey kept
    /// turning up as a story step after every quest was done, "though he only delivers corpses and
    /// can't be talked to"). <c>CustomInteraction 'npc_donkey:21_base_donkey_1'</c> is fired by
    /// <c>WgoData</c> only when the player uses the interaction event of that name - it is
    /// <c>id + ":" + event.str</c> as the event is popped. The check can linger in the save after
    /// the event is gone, or wait for a flowscript to add it later; either way, until the object
    /// holds that event, using it cannot count.
    /// </para>
    /// </summary>
    internal static bool AwaitsWgo(WgoData data)
    {
        Refresh();
        if (data == null) return false;
        if (data.id != null && WgoIds.Contains(data.id)) return true;
        if (!string.IsNullOrEmpty(data.CustomTag) && WgoTags.Contains(data.CustomTag)) return true;

        if (data.id == null || !WgoEvents.TryGetValue(data.id, out var names)) return false;
        var events = data.Events;
        if (events == null) return false;
        foreach (var e in events)
        {
            if (e != null && e.str != null && names.Contains(e.str)) return true;
        }
        return false;
    }

    /// <summary>
    /// What the story still needs before using this object will count - "Rostige Rüstung not
    /// equipped" - or null when nothing is missing or nothing waits here.
    ///
    /// <para>
    /// Some steps are a listener <i>plus a condition</i>: leaving the chapter 3 workshop is
    /// <c>Interaction tp_RT_workshop_exit</c> with <c>HasPlayerItemEquip("armor_0", 1) &amp;&amp;
    /// HasPlayerItemEquip("sword_0", 1)</c>. Fail it and the game puts the player back inside with
    /// one line of dialogue that names neither item. The condition is split at <c>&amp;&amp;</c>
    /// and each part evaluated on its own, so the mod can say which part fails; parts it has no
    /// wording for are left out rather than guessed at.
    /// </para>
    /// </summary>
    internal static string MissingFor(string wgoId)
    {
        if (string.IsNullOrEmpty(wgoId)) return null;
        var events = MainGame.Instance?.GameSave?.globalEventsSystem?.checkingEvents;
        if (events == null) return null;

        var missing = new List<string>();
        foreach (var ev in events)
        {
            if (ev == null || string.IsNullOrEmpty(ev.id) || BeforeColon(ev.id) != wgoId) continue;
            if (ev.type != GlobalEventsSystem.Event.Type.Interaction
                && ev.type != GlobalEventsSystem.Event.Type.CustomInteraction
                && ev.type != GlobalEventsSystem.Event.Type.PlayerTeleport
                && ev.type != GlobalEventsSystem.Event.Type.PlayerTeleportAfterFadeOut
                && ev.type != GlobalEventsSystem.Event.Type.PlayerFindWgoToWork) continue;

            foreach (var check in ev.trigerrables.OfType<QuestCheck>())
            {
                foreach (var expression in check.condExpressions)
                {
                    if (expression == null || !expression.HasExpression || expression.EvaluateBool()) continue;

                    foreach (var part in expression.ToUnparsedString().Split(new[] { "&&" }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        var text = DescribeUnmet(part.Trim());
                        if (text != null && !missing.Contains(text)) missing.Add(text);
                    }
                }
            }
        }

        return missing.Count == 0 ? null : string.Join(", ", missing);
    }

    private static readonly Regex Call = new Regex(@"^(\w+)\(\s*""([^""]+)""", RegexOptions.Compiled);
    private static readonly Regex Threshold = new Regex(@"\)\s*>=?\s*(\d+)", RegexOptions.Compiled);

    internal static string DescribeUnmet(string part)
    {
        var m = Call.Match(part);
        if (!m.Success) return null;

        try
        {
            if (new LazyExpression(part).EvaluateBool()) return null;
        }
        catch
        {
            return null;
        }

        // PPar("wz_town")>=50: a zone's quality - the sum over everything built and repaired in it
        // (WorldZoneData.GetTotalQuality). The donkey's second talk waits on the town's.
        if (m.Groups[1].Value == "PPar" && m.Groups[2].Value.StartsWith("wz_", StringComparison.Ordinal))
        {
            var at = Threshold.Match(part);
            if (!at.Success) return null;
            var zone = TmpText.Clean(LLBase.L(m.Groups[2].Value));
            if (string.IsNullOrWhiteSpace(zone) || zone == m.Groups[2].Value) zone = Navigator.Humanise(m.Groups[2].Value.Substring(3));
            float now;
            try { now = MainGame.PlayerData.GetRes(m.Groups[2].Value); }
            catch { return null; }
            return Loc.Fmt("story.need_zone_quality", zone, at.Groups[1].Value, Mathf.FloorToInt(now));
        }

        var name = ItemText.Name(m.Groups[2].Value);
        return m.Groups[1].Value switch
        {
            "HasPlayerItemEquip" => Loc.Fmt("story.need_equipped", name),
            "HasPlayerItemInInv" => Loc.Fmt("story.need_in_bag", name),
            "HasPlayerOvrhdItem" => Loc.Fmt("story.need_carried", name),
            _ => null,
        };
    }

    /// <summary>
    /// A finish check (the quest is already under way), or a check on a chapter quest - those are
    /// numbered, "2_intro_scout_bridge".
    /// </summary>
    private static bool IsMainStory(GlobalEventsSystem.Event ev)
    {
        foreach (var t in ev.trigerrables)
        {
            if (t is QuestFinishCheck) return true;
            if (t is QuestCheck c && !string.IsNullOrEmpty(c.questId) && char.IsDigit(c.questId[0])) return true;
        }
        return false;
    }

    internal static string BeforeColon(string id)
    {
        var colon = id.IndexOf(':');
        return colon > 0 ? id.Substring(0, colon) : id;
    }
}
