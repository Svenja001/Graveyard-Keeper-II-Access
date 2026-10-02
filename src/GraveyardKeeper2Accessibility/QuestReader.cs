namespace GraveyardKeeper2Accessibility;

/// <summary>
/// What you are supposed to be doing, and where.
///
/// <para>
/// A sighted player told "let's get out of here" is not reading the level design - they are
/// reading the game's own signposting: the tutorial arrow floating over the way out, and the quest
/// entry naming the task. Both of those are data the mod can read; neither of them was being read.
/// </para>
///
/// <para>
/// The arrow (<c>UITutorialArrow</c>) is attached and detached by flowscript at scripted moments,
/// so it is authoritative when it exists and absent the rest of the time. The quest list is the
/// fallback that is always there, and is the answer to "what now" when nothing is pointing
/// anywhere.
/// </para>
/// </summary>
internal static class QuestReader
{
    private static ManualLogSource _log;
    private static ConfigEntry<KeyboardShortcut> _questsKey;

    internal static void Init(ManualLogSource log, ConfigFile config)
    {
        _log = log;

        // J is the key the Graveyard Keeper mod used for the quest list.
        _questsKey = ModKeys.Bind(config, "Keys", "Quests", new KeyboardShortcut(KeyCode.J),
            "Reads the quests you currently have.");
    }

    internal static void Update()
    {
        try
        {
            if (_questsKey != null && _questsKey.Value.IsDown()) SayQuests();
        }
        catch (Exception ex)
        {
            _log?.LogError($"[Quest] Key handling failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>
    /// Every quest currently in play, described. <c>QuestData.Description</c> resolves the right
    /// locale key for the quest's state, so a finished one reads as finished rather than as a
    /// standing instruction.
    /// </summary>
    private static void SayQuests()
    {
        var quests = ActiveQuests();
        if (quests == null)
        {
            ScreenReader.Say(Loc.Get("nav.not_in_game"));
            return;
        }

        LogBreakdown();

        if (quests.Count == 0)
        {
            ScreenReader.Say(Loc.Get("quest.none"));
            return;
        }

        var parts = new List<string>();
        foreach (var quest in quests)
        {
            var text = TmpText.Clean(quest.Description);
            if (string.IsNullOrWhiteSpace(text) || text == quest.id) text = null;

            // What the game is actually waiting for. Many story steps have no text at all, and
            // the ones that do describe the goal ("clear the path"), not the action that counts.
            var step = quest.status == QuestStatus.InProgress ? NextStep(quest) : null;
            if (step != null) text = text == null ? Loc.Fmt("quest.now", step) : Loc.Fmt("quest.with_step", text, step);

            if (string.IsNullOrWhiteSpace(text) || parts.Contains(text)) continue;
            parts.Add(text);
        }

        if (parts.Count == 0)
        {
            ScreenReader.Say(Loc.Get("quest.none"));
            return;
        }

        _log?.LogInfo($"[Quest] {parts.Count} active: {string.Join(" | ", parts)}");
        ScreenReader.Say(Loc.Fmt("quest.list", parts.Count, string.Join(". ", parts)));
    }

    /// <summary>
    /// The action the quest's finish check waits for, in words - "go to Intro Scout Bridge",
    /// "use Käse", "destroy Barrikade" - plus any condition that is not met yet. Null when the
    /// check is something the player does not do (a line of dialogue ending, control returning).
    ///
    /// <para>
    /// This is the same data the game decides with (<c>QuestDef.finishCheck</c>: a
    /// <c>GlobalEventsSystem</c> event type, an id and optional conditions), so it cannot drift from
    /// what actually completes the quest.
    /// </para>
    /// </summary>
    internal static string NextStep(QuestData quest)
    {
        try
        {
            var check = quest?.Definition?.finishCheck;
            if (check == null || !check.hasTrigger) return null;

            var id = check.triggerId ?? "";
            var target = StoryListeners.BeforeColon(id);
            string step;
            switch (check.triggerType)
            {
                case GlobalEventsSystem.Event.Type.PlayerEnterGDZone:
                    step = Loc.Fmt("quest.step.zone", Navigator.Humanise(id));
                    break;
                case GlobalEventsSystem.Event.Type.PlayerExitGDZone:
                    step = Loc.Fmt("quest.step.leave_zone", Navigator.Humanise(id));
                    break;
                case GlobalEventsSystem.Event.Type.Interaction:
                case GlobalEventsSystem.Event.Type.CustomInteraction:
                case GlobalEventsSystem.Event.Type.PlayerFindWgoToWork:
                    step = Loc.Fmt("quest.step.use", Name(target));
                    break;
                case GlobalEventsSystem.Event.Type.PlayerTeleport:
                case GlobalEventsSystem.Event.Type.PlayerTeleportAfterFadeOut:
                    step = Loc.Fmt("quest.step.go_through", Name(target));
                    break;
                case GlobalEventsSystem.Event.Type.WgoDead:
                case GlobalEventsSystem.Event.Type.RemoveWgoDataFromScene:
                case GlobalEventsSystem.Event.Type.WgoCustomTagDead:
                    step = Loc.Fmt("quest.step.destroy", Name(target));
                    break;
                case GlobalEventsSystem.Event.Type.WgoCustomTagHpValueReached:
                    step = Loc.Fmt("quest.step.fight_over", Name(target));
                    break;
                case GlobalEventsSystem.Event.Type.PlayerUseItem:
                    step = Loc.Fmt("quest.step.use_item", Name(target));
                    break;
                case GlobalEventsSystem.Event.Type.CraftFinish:
                case GlobalEventsSystem.Event.Type.CraftStart:
                    step = Loc.Fmt("quest.step.craft", Name(target));
                    break;
                case GlobalEventsSystem.Event.Type.BuildBuilding:
                    step = Loc.Fmt("quest.step.build", Name(target));
                    break;
                case GlobalEventsSystem.Event.Type.ConveyorChestItemAdded:
                    step = Loc.Fmt("quest.step.wait_for_item", Name(id.Substring(id.IndexOf(':') + 1)));
                    break;
                case GlobalEventsSystem.Event.Type.PlayerInsertBodyToAutopsy:
                    step = Loc.Get("quest.step.autopsy");
                    break;
                case GlobalEventsSystem.Event.Type.AfterSleep:
                    step = Loc.Get("quest.step.sleep");
                    break;
                case GlobalEventsSystem.Event.Type.FightWon:
                    step = Loc.Get("quest.step.win_fight");
                    break;
                case GlobalEventsSystem.Event.Type.CloseUIWindow:
                    step = Loc.Get("quest.step.close_window");
                    break;
                default:
                    return null;
            }

            var unmet = new List<string>();
            foreach (var expression in check.condExpressions)
            {
                if (expression == null || !expression.HasExpression || expression.EvaluateBool()) continue;
                foreach (var part in expression.ToUnparsedString().Split(new[] { "&&" }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var text = StoryListeners.DescribeUnmet(part.Trim());
                    if (text != null && !unmet.Contains(text)) unmet.Add(text);
                }
            }

            return unmet.Count == 0 ? step : Loc.Fmt("quest.step_needs", step, string.Join(", ", unmet));
        }
        catch (Exception ex)
        {
            _log?.LogWarning($"[Quest] Could not describe the step of '{quest?.id}': {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// A readable name for an object or item id. Story objects often have no text of their own
    /// (<c>LLBase.L</c> hands the id back), and "tp_RT_workshop_exit" is better said as
    /// "workshop exit" than spelled out.
    /// </summary>
    private static string Name(string id)
    {
        if (string.IsNullOrEmpty(id)) return id;
        var name = ItemText.Name(id);
        if (!string.IsNullOrWhiteSpace(name) && name != id) return name;

        var raw = id.StartsWith("tp_RT_", StringComparison.Ordinal) ? id.Substring(6) : id;
        return Navigator.Humanise(raw);
    }

    /// <summary>
    /// Logs every quest the save holds, with its status and visibility flags.
    ///
    /// "No active quests" has two very different causes - the game genuinely has none yet because
    /// this stretch is driven entirely by flowscript, or it has several that the visibility filter
    /// is discarding - and from the spoken output they are identical. This is the only way to tell
    /// them apart without guessing.
    /// </summary>
    private static void LogBreakdown()
    {
        try
        {
            var collection = MainGame.Instance?.GameSave?.questSystemData?.questCollection;
            var all = collection?.quests;
            if (all == null)
            {
                _log?.LogInfo("[Quest] No quest collection on the save.");
                return;
            }

            var interesting = new List<string>();
            foreach (var quest in all)
            {
                if (quest == null) continue;
                if (quest.status == QuestStatus.Completed || quest.status == QuestStatus.Canceled) continue;

                interesting.Add($"{quest.id} [{quest.status}/{quest.ViewStatus}" +
                                $"{(quest.isHidden ? " hidden" : "")}{(quest.isUnknown ? " unknown" : "")}]");
            }

            _log?.LogInfo($"[Quest] {all.Count} total, {interesting.Count} open: {string.Join(", ", interesting)}");
        }
        catch (Exception ex)
        {
            _log?.LogWarning($"[Quest] Could not log the quest breakdown: {ex.Message}");
        }
    }

    /// <summary>The quests worth telling the player about, or null when not in a game.</summary>
    internal static List<QuestData> ActiveQuests()
    {
        try
        {
            var save = MainGame.Instance == null ? null : MainGame.Instance.GameSave;
            var collection = save?.questSystemData?.questCollection;
            if (collection?.quests == null) return null;

            var result = new List<QuestData>();
            foreach (var quest in collection.quests)
            {
                if (quest == null) continue;

                // IsActiveQuest is the quest screen's own test, and it hides anything flagged
                // unknown. A quest already in progress is one the player is doing right now, so it
                // is included regardless - being told what you are in the middle of is not a
                // spoiler.
                if (quest.IsActiveQuest || quest.status == QuestStatus.InProgress) result.Add(quest);
            }

            return result;
        }
        catch (Exception ex)
        {
            _log?.LogError($"[Quest] Could not read the quest list: {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// A one-line summary of the current task, for when the objective key has no arrow to read.
    /// Returns null when there is nothing to say.
    /// </summary>
    internal static string CurrentTask()
    {
        var quests = ActiveQuests();
        if (quests == null || quests.Count == 0) return null;

        // The most recently started quest is very nearly always the one being worked on, and the
        // collection keeps them in the order they were added.
        for (var i = quests.Count - 1; i >= 0; i--)
        {
            var text = TmpText.Clean(quests[i].Description);
            if (!string.IsNullOrWhiteSpace(text)) return text;
        }

        return null;
    }
}
