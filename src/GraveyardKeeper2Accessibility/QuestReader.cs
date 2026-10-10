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
            "Opens the list of quests in progress. Up and Down step through it, J closes it.");
    }

    private static List<string> _items;
    private static int _index;

    internal static bool IsOpen => _items != null;

    /// <summary>
    /// The quest list, opened with J. While it is open it owns the arrow keys: Up and Down step
    /// through one quest at a time, Enter reads the current one again, and J closes it. Escape is left alone - the game opens its pause menu on it, and any
    /// game window opening closes the list anyway.
    ///
    /// <para>
    /// <b>Reported (2026-10-06):</b> J read every quest as one long sentence, finished ones among
    /// them. A list the player can step through is what the GK1 mod did, and only quests in
    /// progress are in it (see <see cref="ActiveQuests"/>).
    /// </para>
    /// <para>Returns true when the quest list used this frame's keys.</para>
    /// </summary>
    internal static bool Update()
    {
        try
        {
            if (_items != null && (LazyWindowsStackController.ActiveWindow != null || ActiveQuests() == null))
            {
                Close(silent: true);
                return false;
            }

            if (_questsKey != null && _questsKey.Value.IsDown())
            {
                if (_items != null) Close(silent: false);
                else Open();
                return true;
            }

            if (_items == null) return false;

            var count = _items.Count;
            if (Input.GetKeyDown(KeyCode.DownArrow)) Move((_index + 1) % count);
            else if (Input.GetKeyDown(KeyCode.UpArrow)) Move((_index - 1 + count) % count);
            else if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)) Move(_index);
            else return false;
            return true;
        }
        catch (Exception ex)
        {
            _log?.LogError($"[Quest] Key handling failed: {ex.GetType().Name}: {ex.Message}");
            _items = null;
            return false;
        }
    }

    private static void Open()
    {
        var quests = ActiveQuests();
        if (quests == null)
        {
            ScreenReader.Say(Loc.Get("nav.not_in_game"));
            return;
        }

        LogBreakdown();

        var parts = Describe(quests.Where(Shown));
        var running = parts.Count;
        parts.AddRange(Startable());

        if (parts.Count == 0)
        {
            ScreenReader.Say(Loc.Get("quest.none"));
            return;
        }

        _log?.LogInfo($"[Quest] {running} in progress, {parts.Count - running} to start: {string.Join(" | ", parts)}");
        _items = parts;
        _index = 0;
        ScreenReader.Say(Loc.Fmt("quest.opened", running, parts.Count - running, Entry(0)), interrupt: true);
    }

    private static void Close(bool silent)
    {
        _items = null;
        _index = 0;
        if (!silent) ScreenReader.Say(Loc.Get("quest.closed"), interrupt: true);
    }

    private static void Move(int index)
    {
        _index = index;
        ScreenReader.Say(Entry(index), interrupt: true);
    }

    private static string Entry(int index) => Loc.Fmt("quest.entry", index + 1, _items.Count, _items[index]);

    /// <summary>
    /// One line per quest: what the quest log says, and what the game is actually waiting for.
    /// </summary>
    private static List<string> Describe(IEnumerable<QuestData> quests)
    {
        var parts = new List<string>();
        foreach (var quest in quests)
        {
            var text = Text(quest);

            // What the game is actually waiting for. Many story steps have no text at all, and
            // the ones that do describe the goal ("clear the path"), not the action that counts.
            var step = NextStep(quest);
            if (step != null) text = text == null ? Loc.Fmt("quest.now", step) : Loc.Fmt("quest.with_step", text, step);

            if (string.IsNullOrWhiteSpace(text) || parts.Contains(text)) continue;
            parts.Add(text);
        }

        return parts;
    }

    /// <summary>
    /// A quest the quest screen shows. <b>Reported (2026-10-06):</b> most of J was
    /// "quest_open_new_game_d" and the like - 31 of the 44 quests in progress in that save were the
    /// game's own bookkeeping (<c>new_game</c>, <c>unlock_quarry_cave</c>, <c>repair_1104</c>),
    /// flagged hidden and without any text. <c>QuestTreePageWidget.Display</c> skips
    /// <c>isHidden</c>, and so does J. The navigator still reads them (see <see cref="ActiveQuests"/>):
    /// a hidden repair quest is how it knows which ruin the story means.
    /// </summary>
    private static bool Shown(QuestData quest) => !quest.isHidden;

    /// <summary>
    /// Quests waiting to be begun: <c>Awaiting</c> means the game has registered the quest's start
    /// check and starts it the moment that happens - "talk to the donkey". Hidden ones are kept,
    /// since in that save every quest not begun was hidden; only those whose start can be said in
    /// words are listed.
    /// </summary>
    private static List<string> Startable()
    {
        var parts = new List<string>();
        try
        {
            var all = MainGame.Instance?.GameSave?.questSystemData?.questCollection?.quests;
            if (all == null) return parts;

            foreach (var quest in all)
            {
                if (quest == null || quest.status != QuestStatus.Awaiting || quest.Definition == null) continue;

                var step = Step(quest.Definition.startCheck, quest.id);
                if (step == null) continue;

                var text = Text(quest);
                var line = text == null ? Loc.Fmt("quest.to_start", step) : Loc.Fmt("quest.to_start_with_text", text, step);
                if (!parts.Contains(line)) parts.Add(line);
            }
        }
        catch (Exception ex)
        {
            _log?.LogWarning($"[Quest] Could not read the quests waiting to start: {ex.Message}");
        }

        return parts;
    }

    /// <summary>
    /// The quest log's text for the quest, or null when it has none. <c>LLBase.L</c> hands the key
    /// back when a string is missing, which is how "quest_open_new_game_d" came to be read out.
    /// </summary>
    private static string Text(QuestData quest)
    {
        var text = TmpText.Clean(quest.Description);
        if (string.IsNullOrWhiteSpace(text) || text == quest.id) return null;
        if (text.StartsWith("quest_open_", StringComparison.Ordinal) || text.StartsWith("quest_closed_", StringComparison.Ordinal))
            return null;
        return text;
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
    internal static string NextStep(QuestData quest) => Step(quest?.Definition?.finishCheck, quest?.id);

    /// <summary>A quest check - its finish or its start - in words; see <see cref="NextStep"/>.</summary>
    private static string Step(QuestCheck check, string questId)
    {
        try
        {
            if (check == null) return null;

            // A check can be a bare condition with no event: 166_base_donkey2_speak starts once
            // PPar("wz_town")>=50, the town's quality. Then the conditions are the whole answer.
            var id = check.triggerId ?? "";
            var target = StoryListeners.BeforeColon(id);
            string step = null;
            if (check.hasTrigger)
            switch (check.triggerType)
            {
                case GlobalEventsSystem.Event.Type.PlayerEnterGDZone:
                    step = Loc.Fmt("quest.step.zone", Navigator.Humanise(id));
                    break;
                case GlobalEventsSystem.Event.Type.PlayerExitGDZone:
                    step = Loc.Fmt("quest.step.leave_zone", Navigator.Humanise(id));
                    break;
                // "object id:event": the event the story put on the object, whose last word picks
                // its bubble (InteractionEvent) - and a speech bubble means talking, not "using".
                case GlobalEventsSystem.Event.Type.CustomInteraction
                    when id.IndexOf(':') > 0 && new InteractionEvent(id.Substring(id.IndexOf(':') + 1)).type == InteractionEvent.Type.Talk:
                    step = Loc.Fmt("quest.step.talk", Name(target));
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
                // Fired by the donkey's flowscripts when he sets off on his body run and when he
                // reaches the morgue; the donkey's talks (165-167_base_donkey*_speak) start on them.
                case GlobalEventsSystem.Event.Type.DonkeyStart:
                    step = Loc.Get("quest.step.donkey_start");
                    break;
                case GlobalEventsSystem.Event.Type.DonkeyMorgue:
                    step = Loc.Get("quest.step.donkey_morgue");
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

            if (step == null) return unmet.Count == 0 ? null : string.Join(", ", unmet);
            return unmet.Count == 0 ? step : Loc.Fmt("quest.step_needs", step, string.Join(", ", unmet));
        }
        catch (Exception ex)
        {
            _log?.LogWarning($"[Quest] Could not describe the step of '{questId}': {ex.Message}");
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

    /// <summary>
    /// Every quest in progress, the game's hidden bookkeeping ones included, or null when not in a
    /// game. J reads only the <see cref="Shown"/> ones.
    /// </summary>
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

                // Only quests being done now. IsActiveQuest also passes Available and Awaiting -
                // every quest not begun - and J read those out as open tasks (reported
                // 2026-10-06). Awaiting ones are listed separately, by their start (Startable).
                if (quest.status == QuestStatus.InProgress) result.Add(quest);
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
            if (!Shown(quests[i])) continue;
            var text = Text(quests[i]);
            if (text != null) return text;
        }

        return null;
    }
}
