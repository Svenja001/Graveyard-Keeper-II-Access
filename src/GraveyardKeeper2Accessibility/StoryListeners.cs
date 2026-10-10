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

    private static int _frame = -1;

    /// <summary>Rereads the listeners, at most once a frame.</summary>
    internal static void Refresh()
    {
        if (_frame == Time.frameCount) return;
        _frame = Time.frameCount;

        ZoneTags.Clear();
        WgoIds.Clear();
        WgoTags.Clear();

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
                case GlobalEventsSystem.Event.Type.Interaction:
                case GlobalEventsSystem.Event.Type.CustomInteraction:
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

    internal static bool AwaitsWgo(WgoData data)
    {
        Refresh();
        if (data == null) return false;
        return (data.id != null && WgoIds.Contains(data.id))
            || (!string.IsNullOrEmpty(data.CustomTag) && WgoTags.Contains(data.CustomTag));
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
