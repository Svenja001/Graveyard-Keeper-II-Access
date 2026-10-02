namespace GraveyardKeeper2Accessibility;

/// <summary>
/// Speaks dialogue: what characters say, the player's own thoughts, and the choices offered back.
///
/// <para>
/// <b>Another method the plan named that nothing calls.</b> §3.3 hook 3 says
/// <c>UISpeechBubble.ShowMessage</c>. Like <c>UITooltip.Show</c>, it has no callers in the game.
/// Every spoken line in Graveyard Keeper II goes through <c>Bubble.Talk(PhraseData)</c> - NPC
/// speech, player speech and player thoughts alike - and every set of choices through
/// <c>Bubble.ShowMultiAnswer</c>. Those two are the real funnels, with dozens of call sites each.
/// </para>
///
/// <para>
/// <c>PhraseData.text</c> is sometimes a locale key and sometimes already-resolved text, depending
/// on the caller. <c>LLBase.L</c> returns an unknown key unchanged, so running everything through
/// it handles both without having to guess which is which.
/// </para>
/// </summary>
internal static class DialogueReader
{
    /// <summary>
    /// Who spoke last, so the speaker is named only when it changes. Naming them on every line is
    /// correct but unbearable across a long conversation; naming them never leaves a two-hander
    /// impossible to follow.
    /// </summary>
    private static string _lastSpeaker;

    /// <summary>
    /// Until when focus announcements should be ignored (unscaled time).
    ///
    /// When a set of answers appears, the widget puts focus on the first option itself. That focus
    /// is not the player moving - it is the list arriving - and letting it speak would cut off the
    /// question and the option list that were queued a moment earlier, which is the single worst
    /// thing that can happen in a conversation. Player-driven focus moves land well outside this
    /// window and are unaffected.
    /// </summary>
    internal static float SuppressFocusUntil { get; private set; }

    private const float AnswerFocusSettleTime = 0.35f;

    /// <summary>
    /// Every line of dialogue in the game. Interrupts, because a new bubble has replaced the old
    /// one on screen and the previous line is no longer readable by anyone.
    /// </summary>
    [HarmonyPatch(typeof(Bubble), nameof(Bubble.Talk))]
    [HarmonyPostfix]
    private static void Bubble_Talk(PhraseData data)
    {
        try
        {
            var text = TmpText.Clean(LLBase.L(data.text));
            if (string.IsNullOrWhiteSpace(text)) return;

            var speaker = SpeakerName(data);

            // A thought is the player's own voice and never needs attributing - it is always the
            // same speaker - but it does need distinguishing from speech, because "I should look
            // for a shovel" means something different said aloud to someone.
            string line;
            if (data.speechType == SpeechBubbleType.Think)
            {
                line = Loc.Fmt("dialogue.thought", text);
                _lastSpeaker = null;
            }
            else if (speaker != null && speaker != _lastSpeaker)
            {
                line = Loc.Fmt("dialogue.line", speaker, text);
                _lastSpeaker = speaker;
            }
            else
            {
                line = text;
            }

            ScreenReader.Say(line);
        }
        catch (Exception ex)
        {
            Plugin.Log?.LogError($"[Dialogue] Failed to read a line: {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>
    /// The choices offered back to the player.
    ///
    /// Read as one list rather than left to the focus narration alone, so the player hears what is
    /// on offer before deciding rather than having to arrow through the options to discover them.
    /// Queued rather than interrupting, so it follows the question it answers; arrowing between
    /// the options afterwards reads them one at a time as usual.
    /// </summary>
    [HarmonyPatch(typeof(Bubble), nameof(Bubble.ShowMultiAnswer))]
    [HarmonyPostfix]
    private static void Bubble_ShowMultiAnswer(List<AnswerVisualData> answers)
    {
        try
        {
            SuppressFocusUntil = Time.unscaledTime + AnswerFocusSettleTime;

            if (answers == null || answers.Count == 0) return;

            var options = new List<string>();
            foreach (var answer in answers)
            {
                if (answer == null || answer.hiddenByDefault) continue;

                var text = AnswerText(answer, full: false);
                if (!string.IsNullOrWhiteSpace(text)) options.Add(text);
            }

            if (options.Count == 0) return;

            ScreenReader.Say(
                Loc.Fmt("dialogue.options", options.Count, string.Join(", ", options)),
                interrupt: false);
        }
        catch (Exception ex)
        {
            Plugin.Log?.LogError($"[Dialogue] Failed to read the answers: {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>
    /// On-screen text during a cutscene - titles, captions, the narration over an establishing
    /// shot. It does not go through <c>Bubble.Talk</c>: cutscenes are Timeline sequences, and their
    /// text is driven by a playable that pushes the finished string into the widget once per
    /// block. Without this, the opening of the game is silent in exactly the stretch that explains
    /// what is going on.
    ///
    /// Interrupts, because a caption replacing another caption means the first one is gone. Note
    /// this is the one place where speech may fall behind what is on screen: a cutscene advances on
    /// its own timing, not the player's. If that turns out to lose text, the answer is to slow the
    /// cutscene rather than to speak faster - see the GK1 note about a keypress mid-scene wedging
    /// the dialogue flow permanently, which is why nothing here touches the scene's input.
    /// </summary>
    [HarmonyPatch(typeof(CinematicsTextWidget), "OnTextChanged")]
    [HarmonyPostfix]
    private static void CinematicsTextWidget_OnTextChanged(string text)
    {
        try
        {
            var clean = TmpText.Clean(text);
            if (string.IsNullOrWhiteSpace(clean) || clean == _lastCinematicText) return;

            _lastCinematicText = clean;
            Plugin.Log?.LogInfo($"[Cutscene] text: \"{clean}\"");
            ScreenReader.Say(clean);
        }
        catch (Exception ex)
        {
            Plugin.Log?.LogError($"[Dialogue] Failed to read cutscene text: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static string _lastCinematicText;

    /// <summary>
    /// A conversation is over; the next speaker should be named again even if it is the same
    /// character, because the context around them has gone.
    /// </summary>
    internal static void ForgetSpeaker()
    {
        _lastSpeaker = null;
        _lastCinematicText = null;
    }

    private static string SpeakerName(PhraseData data)
    {
        if (data.isPlayer) return Loc.Get("dialogue.player");

        var wgo = data.npcWgoData;
        if (wgo == null || string.IsNullOrEmpty(wgo.id)) return null;

        // The def id is also the locale key - uniform across every definition type in GK2 - so
        // this is the character's name in the player's language, and falls back to the raw id
        // rather than to nothing.
        return TmpText.Clean(LLBase.L(wgo.id));
    }

    private static readonly AccessTools.FieldRef<UIMultiAnswerOption, AnswerVisualData> OptionData =
        AccessTools.FieldRefAccess<UIMultiAnswerOption, AnswerVisualData>("visualData");

    /// <summary>
    /// A focused answer in a conversation, with what it needs, costs and gives - or null when the
    /// focused control is not an answer. Without this the focus narration reads only the label
    /// ("Überreiche Töpferei."), so an answer the game will refuse sounds exactly like one it will
    /// take, and nothing says what to go and get.
    /// </summary>
    internal static string DescribeWidget(GamepadNavigationItem item)
    {
        try
        {
            var option = item.GetComponentInParent<UIMultiAnswerOption>();
            if (option == null) return null;

            var data = OptionData(option);
            return data == null ? null : AnswerText(data, full: true);
        }
        catch (Exception ex)
        {
            Plugin.Log?.LogWarning($"[Dialogue] Could not read an answer: {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// The wording of one answer. The id <i>is</i> the phrase - <c>UIMultiAnswerOption</c> builds
    /// its label by translating <c>visualData.id</c> and nothing else - so there is no separate
    /// text field to read.
    ///
    /// <para>
    /// <b>Availability is worked out here, the way <c>UIMultiAnswerOption.ShowIcons</c> does it</b>,
    /// not read from the widget: an answer is greyed out when any lock or cost item or resource is
    /// short, when it is tied to another day of the week, or to a trader's order not yet delivered.
    /// <c>notAvailable</c> counts only when none of those are present - the game ignores it
    /// otherwise. The widget's own flag is set in <c>ShowIcons</c>, which runs after the answer
    /// list is announced, so it cannot be relied on here.
    /// </para>
    /// </summary>
    private static string AnswerText(AnswerVisualData answer, bool full)
    {
        if (string.IsNullOrEmpty(answer.id)) return null;

        var text = TmpText.Clean(LLBase.L(answer.id));
        if (string.IsNullOrWhiteSpace(text)) return null;

        var data = answer.answerData;
        if (data == null) return text;

        var available = true;
        var needs = new List<string>();
        var costs = new List<string>();

        AddRes(data.lockRes, needs, ref available);
        AddRes(data.costRes, costs, ref available);

        if (!string.IsNullOrEmpty(data.dayNumber))
        {
            var today = false;
            try { today = MainGame.Instance.GameSave.environmentData.CurrentDayNumber == ConstDef.Get(data.dayNumber).IntValue; }
            catch { }
            if (!today) available = false;
            needs.Add(Loc.Fmt("dialogue.need_day", Loc.Find("icon." + data.dayNumber) ?? data.dayNumber));
        }

        if (!string.IsNullOrEmpty(data.order))
        {
            var done = false;
            try { done = MainGame.Instance.GameSave.vendorSystem.IsOrderFinished(data.order); }
            catch { }
            if (!done) available = false;
            needs.Add(Loc.Fmt(done ? "dialogue.order_done" : "dialogue.order_open", OrderName(data.order)));
        }

        var gated = data.lockRes != null || data.costRes != null || !string.IsNullOrEmpty(data.dayNumber) || !string.IsNullOrEmpty(data.order);
        if (!gated && data.notAvailable) available = false;

        // The opening list keeps each answer short - the full reasons for every option at once
        // would bury the question. Arrowing onto an answer gives them.
        if (!full) return available ? text : Loc.Fmt("dialogue.unavailable", text);

        var parts = new List<string> { text };

        // An answer the player cannot pick is still shown, greyed out. Without a word for that
        // state it reads as a live option, and picking it is the obvious next move. Pressing it
        // does nothing at all, so the reason is the only useful thing to say next.
        if (!available) parts.Add(Loc.Get("dialogue.not_available"));
        if (needs.Count > 0) parts.Add(Loc.Fmt("dialogue.needs", string.Join("; ", needs)));
        if (costs.Count > 0) parts.Add(Loc.Fmt("dialogue.costs", string.Join("; ", costs)));

        var rewards = new List<string>();
        AddRewards(data.rewardRes, rewards);
        AddRewards(data.fakeRewardRes, rewards);
        if (rewards.Count > 0) parts.Add(Loc.Fmt("dialogue.rewards", string.Join(", ", rewards)));

        return string.Join(". ", parts);
    }

    /// <summary>"0 of 1 pottery" for each item and resource, marking the answer unavailable when short.</summary>
    private static void AddRes(SmartRes res, List<string> into, ref bool available)
    {
        if (res == null) return;
        var player = MainGame.PlayerData;

        if (res.gameRes?.List != null)
        {
            foreach (var atom in res.gameRes.List)
            {
                if (atom == null || string.IsNullOrEmpty(atom.type)) continue;
                if (player != null && !player.IsEnoughRes(atom)) available = false;

                var have = player == null ? 0f : player.GetRes(atom.type);
                into.Add(atom.type == "money"
                    ? Loc.Fmt("dialogue.money_have", Money.ToSpeech(atom.value), Money.ToSpeech(have))
                    : Loc.Fmt("tooltip.need_have", Mathf.FloorToInt(have), Mathf.RoundToInt(atom.value), ResName(atom.type)));
            }
        }

        if (res.items != null)
        {
            foreach (var item in res.items)
            {
                if (item == null || string.IsNullOrEmpty(item.itemId)) continue;
                try
                {
                    if (player != null && !player.Inventory.Data.HasItemQuantityInInventory(item.itemId, item.count)) available = false;
                }
                catch { }

                into.Add(Loc.Fmt("tooltip.need_have", Math.Max(0, ItemText.Held(item.itemId)), item.count, ItemText.Name(item.itemId)));
            }
        }
    }

    private static void AddRewards(SmartRes res, List<string> into)
    {
        if (res == null) return;

        if (res.gameRes?.List != null)
            foreach (var atom in res.gameRes.List)
                if (atom != null && !string.IsNullOrEmpty(atom.type))
                    into.Add(atom.type == "money"
                        ? Money.ToSpeech(atom.value)
                        : $"{Mathf.RoundToInt(atom.value)} {ResName(atom.type)}");

        if (res.items != null)
            foreach (var item in res.items)
                if (item != null && !string.IsNullOrEmpty(item.itemId))
                    into.Add($"{item.count} {ItemText.Name(item.itemId)}");
    }

    /// <summary>A resource by the mod's own word for its icon, else the game's name for the id.</summary>
    private static string ResName(string type) =>
        Loc.Find("icon." + type) ?? TmpText.Clean(LLBase.L(type));

    /// <summary>A trader's order, named by what it asks for: "3 Töpferei".</summary>
    private static string OrderName(string order)
    {
        try
        {
            var def = GameBalance.Me.GetData<VendorOrderDef>(order);
            if (def != null) return $"{def.count} {ItemText.Name(def.itemId)}";
        }
        catch { }
        return TmpText.Clean(LLBase.L(order));
    }
}
