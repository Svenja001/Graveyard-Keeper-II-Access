namespace GraveyardKeeper2Accessibility;

/// <summary>
/// The two trees on the character window: the tech tree (spend red, green and blue points on
/// technologies) and the inspiration page (finish inspirations, buy them with faith for talent
/// experience, spend talent points on perks).
///
/// <para>
/// Focus already moved through both - the game's own navigation covers the tree nodes - but every
/// node was read from its labels, which said a name and a price as sprite names and nothing else:
/// not whether it was learned, whether it could be learned, what it needed first, or what it
/// unlocked. The perk nodes have no text at all. Everything here is read from the definitions and
/// the save instead (<see cref="TechDef"/>, <see cref="TalentData"/>), the same way the station
/// readers work.
/// </para>
///
/// <para>
/// <b>The detail window could not unlock anything.</b> <c>UITechTreeElementWindow</c>'s Unlock
/// button listens for <c>GameKey.Select</c> (Space) only while a gamepad is active, and is not a
/// navigation item - focus only visits the unlock list. Enter presses it here, and unlocks directly
/// when the press does nothing.
/// </para>
///
/// <para>
/// Page and branch keys: the window's own tab keys (<c>NextTab</c>, <c>NextSubTab</c>) are
/// controller buttons, so Ctrl+up/down switch pages and Ctrl+left/right switch the tech branch or
/// the talent.
/// </para>
/// </summary>
internal static class TreesReader
{
    internal static bool Ready { get; private set; }

    private static class G
    {
        internal static readonly AccessTools.FieldRef<LazyWidget<CharacterWindowData>, CharacterWindowData> CharData =
            AccessTools.FieldRefAccess<LazyWidget<CharacterWindowData>, CharacterWindowData>("data");

        internal static readonly AccessTools.FieldRef<TechTreePageWidget, TechTreeTab> CurrentTab =
            AccessTools.FieldRefAccess<TechTreePageWidget, TechTreeTab>("currentTab");

        internal static readonly AccessTools.FieldRef<LazyWidget<UITechTreeElementWindowData>, UITechTreeElementWindowData> ElementData =
            AccessTools.FieldRefAccess<LazyWidget<UITechTreeElementWindowData>, UITechTreeElementWindowData>("data");

        internal static readonly AccessTools.FieldRef<UITechTreeElementWindow, List<UIDialogWindowButton>> ElementButtons =
            AccessTools.FieldRefAccess<UITechTreeElementWindow, List<UIDialogWindowButton>>("activeButtons");

        // What the tech tree's own Unlock does after TechDef.Unlock, for when its button cannot be pressed.
        internal static readonly MethodInfo UnlockReputationTechs =
            AccessTools.Method(typeof(TechTreePageWidget), "UnlockAvailableReputationTechs") ?? throw new MissingMethodException("UnlockAvailableReputationTechs");
        internal static readonly MethodInfo RedrawSpheres =
            AccessTools.Method(typeof(TechTreePageWidget), "RedrawSpheres") ?? throw new MissingMethodException("RedrawSpheres");
        internal static readonly MethodInfo UpdateConnectors =
            AccessTools.Method(typeof(TechTreePageWidget), "UpdateConnectors") ?? throw new MissingMethodException("UpdateConnectors");

        internal static readonly AccessTools.FieldRef<LazyWidget<InspirationWidgetData>, InspirationWidgetData> FinishedData =
            AccessTools.FieldRefAccess<LazyWidget<InspirationWidgetData>, InspirationWidgetData>("data");
    }

    private static ConfigEntry<KeyboardShortcut> _nextUnlockableKey;
    private static ConfigEntry<KeyboardShortcut> _prevUnlockableKey;

    internal static void Init(ConfigFile config)
    {
        _nextUnlockableKey = ModKeys.Bind(config,
            "Trees", "NextUnlockable", new KeyboardShortcut(KeyCode.C),
            "On the inspiration page: jumps to the next thing you can buy right now - a finished " +
            "inspiration you have the faith for, or a perk you have the talent points for, in any talent. On the tech " +
            "tree: jumps to the next technology your points can learn, in any open branch.");
        _prevUnlockableKey = ModKeys.Bind(config,
            "Trees", "PreviousUnlockable", new KeyboardShortcut(KeyCode.C, KeyCode.LeftShift),
            "The same, backwards.");

        try
        {
            System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(typeof(G).TypeHandle);
            Ready = true;
        }
        catch (Exception ex)
        {
            Plugin.Log?.LogError($"[Trees] The tech and inspiration trees are not supported by this game version: " +
                                 $"{(ex.InnerException ?? ex).GetType().Name}: {(ex.InnerException ?? ex).Message}");
        }
    }

    private static CharacterWindowData.CharPage _lastPage = CharacterWindowData.CharPage.Undefined;
    private static string _lastSummary;

    internal static void Reset()
    {
        _lastPage = CharacterWindowData.CharPage.Undefined;
        _lastSummary = null;
        _switchingPage = false;
    }

    /// <summary>Windows whose opening this reader announces itself, by page or in full.</summary>
    internal static bool AnnouncesOpening(LazyWidgetBase window) =>
        Ready && (window is CharacterWindow || window is UITechTreeElementWindow);

    /// <summary>Windows this reader speaks in full, so their closing is not announced either.</summary>
    internal static bool SpeaksForItself(LazyWidgetBase window) => Ready && window is UITechTreeElementWindow;

    // ---- pages and branches -----------------------------------------------------------------

    /// <summary>True while a page is being drawn, when a tech tab change is part of that and not news.</summary>
    private static bool _switchingPage;

    [HarmonyPatch(typeof(CharacterWindow), "SwitchPage")]
    [HarmonyPrefix]
    private static void CharacterWindow_SwitchPage_Prefix() => _switchingPage = true;

    [HarmonyPatch(typeof(CharacterWindow), "SwitchPage")]
    [HarmonyPostfix]
    private static void CharacterWindow_SwitchPage(CharacterWindow __instance, CharacterWindowData.CharPage page)
    {
        _switchingPage = false;
        Guard("character window page", () =>
        {
            var summary = PageSummary(__instance, page);
            var changed = page != _lastPage;
            if (!changed && summary == _lastSummary) return;

            var text = changed ? Join(PageName(page), summary) : summary;
            _lastPage = page;
            _lastSummary = summary;
            Speak(__instance, text);
        });
    }

    /// <summary>A new tech branch shown by its own key or button, outside a page switch.</summary>
    [HarmonyPatch(typeof(TechTreePageWidget), nameof(TechTreePageWidget.DisplayTab))]
    [HarmonyPostfix]
    private static void TechTreePageWidget_DisplayTab(TechTreePageWidget __instance) =>
        Guard("tech branch", () =>
        {
            if (_switchingPage) return;
            var summary = TechTabSummary(__instance);
            if (summary == _lastSummary) return;
            _lastSummary = summary;
            Speak(LazyUI.GetWindow<CharacterWindow>(), summary);
        });

    private static string PageName(CharacterWindowData.CharPage page)
    {
        var key = page switch
        {
            CharacterWindowData.CharPage.Main => "CharMainPage",
            CharacterWindowData.CharPage.TechTree => "TechTreePage",
            CharacterWindowData.CharPage.Inspiration => "CharInspirationPage",
            CharacterWindowData.CharPage.QuestTree => "QuestTreePage",
            CharacterWindowData.CharPage.Map => "MapPage",
            _ => null,
        };
        return key == null ? null : TmpText.Clean(LLBase.L(key));
    }

    private static string PageSummary(CharacterWindow window, CharacterWindowData.CharPage page) =>
        page switch
        {
            CharacterWindowData.CharPage.TechTree => TechTabSummary(window.TechTreePageWidget),
            CharacterWindowData.CharPage.Inspiration => TalentSummary(G.CharData(window)?.InspirationPageWidgetData?.TalentData),
            _ => null,
        };

    // ---- the tech tree ----------------------------------------------------------------------

    private static string TechTabSummary(TechTreePageWidget page)
    {
        if (page == null) return null;
        var tab = G.CurrentTab(page);
        var techs = GameBalance.Me.techDefs.Where(t => t.tab == tab && t.techDefType == TechDefType.Common).ToList();
        var learned = techs.Count(t => t.TechState == TechState.Unlocked);
        var ready = techs.Count(t => t.TechState == TechState.Available && t.EnoughResources);

        return Join(
            Loc.Fmt("tree.tech_tab", TmpText.Clean(LLBase.L($"tech_tab_{tab}")), techs.Count, learned, ready),
            Loc.Fmt("tree.points", Res("tech_red"), Res("tech_green"), Res("tech_blue")),
            Loc.Get("tree.tech_hint"));
    }

    /// <summary>A tech node as focus lands on it: name, state, cost, what it needs first, what it unlocks.</summary>
    private static string DescribeTech(TechDef def)
    {
        if (def == null) return null;
        var state = def.TechState;
        if (state == TechState.Hidden) return Loc.Get("tree.hidden");

        if (def.techDefType != TechDefType.Common) return DescribeReputationGate(def, state);

        var parts = new List<string> { TechName(def) };
        switch (state)
        {
            case TechState.Unlocked:
                parts.Add(Loc.Get("tree.learned"));
                break;
            case TechState.Visible:
                parts.Add(Loc.Fmt("tree.needs_first", MissingParents(def)));
                parts.Add(Cost(def));
                break;
            case TechState.Available:
                parts.Add(def.EnoughResources ? Loc.Get("tree.can_learn") : TmpText.Clean(LLBase.L("tech_not_enough_resources")));
                parts.Add(Cost(def));
                break;
        }
        parts.Add(Unlocks(def));
        return Join(parts.ToArray());
    }

    /// <summary>
    /// A reputation node - a gate in the tree that opens by itself once a person or district thinks
    /// well enough of you. On screen it is a portrait and a number.
    /// </summary>
    private static string DescribeReputationGate(TechDef def, TechState state)
    {
        GameResAtom need;
        string who;
        if (def.techDefType == TechDefType.CharRep)
        {
            var wgoId = def.wgoRepLock?.List?.FirstOrDefault()?.type;
            need = def.CharReputationLock?.List?.FirstOrDefault();
            who = string.IsNullOrEmpty(wgoId) ? null : TmpText.Clean(LLBase.L(wgoId));
        }
        else
        {
            need = def.districtReputationLock?.List?.FirstOrDefault();
            who = need == null ? null : TmpText.Clean(LLBase.L(need.type));
        }
        if (need == null) return TechName(def);

        var gate = Loc.Fmt("tree.rep_gate", who ?? need.type, Mathf.FloorToInt(MainGame.PlayerData.GetRes(need.type)), Mathf.RoundToInt(need.value));
        return state == TechState.Unlocked ? Join(gate, Loc.Get("tree.learned")) : gate;
    }

    private static string TechName(TechDef def) => TmpText.Clean(LLBase.L(def.id));

    private static string MissingParents(TechDef def)
    {
        var known = MainGame.Instance.GameSave.knowledgeSystem;
        var missing = def.parentDefinitionList.Where(p => !known.IsTechUnlocked(p.id)).Select(TechName).ToList();
        if (missing.Count == 0) return TmpText.Clean(LLBase.L("tech_not_all_techs_unlocked"));
        var any = def.techLockType == TechLockType.Any && missing.Count > 1;
        return string.Join(any ? Loc.Get("tree.or") : ", ", missing);
    }

    /// <summary>"25 red points, you have 10" - need, then have, for each colour the node costs.</summary>
    private static string Cost(TechDef def)
    {
        var parts = def.PriceRes.List.Where(a => a.value > 0)
            .Select(a => Loc.Fmt("tooltip.need_have", Res(a.type), Mathf.RoundToInt(a.value), Loc.Get("icon." + a.type)))
            .ToList();
        return parts.Count == 0 ? null : Loc.Fmt("tree.cost", string.Join("; ", parts));
    }

    private static string Unlocks(TechDef def)
    {
        var names = new List<string>();
        foreach (var linked in def.linkedEntityWidgetDatas)
        {
            string name;
            try
            {
                var header = TmpText.Clean(linked.GetLinkedEntityHeader());
                var prefix = TmpText.Clean(linked.GetLinkedEntityPrefix());
                name = string.IsNullOrWhiteSpace(prefix) ? header : prefix + ": " + header;
            }
            catch
            {
                continue;
            }
            if (!string.IsNullOrWhiteSpace(name) && !names.Contains(name)) names.Add(name);
        }
        return names.Count == 0 ? null : Loc.Fmt("tree.unlocks", string.Join(", ", names));
    }

    private static int Res(string type) => Mathf.FloorToInt(MainGame.PlayerData.GetRes(type));

    // ---- the tech detail window -------------------------------------------------------------

    [HarmonyPatch(typeof(UITechTreeElementWindow), nameof(UITechTreeElementWindow.Open))]
    [HarmonyPostfix]
    private static void UITechTreeElementWindow_Open(UITechTreeElementWindow __instance) =>
        Guard("technology", () => Speak(__instance, DescribeElementWindow(__instance)));

    private static string DescribeElementWindow(UITechTreeElementWindow window)
    {
        var data = G.ElementData(window);
        var def = data?.TechDef;
        if (def == null) return null;

        var parts = new List<string> { TmpText.Clean(data.HeaderText) ?? TechName(def), TmpText.Clean(data.TopText) };
        var desc = LLBase.HasL(def.id + "_d") ? TmpText.Clean(LLBase.L(def.id + "_d")) : null;
        parts.Add(desc);

        if (def.TechState == TechState.Unlocked)
        {
            parts.Add(Loc.Get("tree.learned"));
            parts.Add(Unlocks(def));
            parts.Add(Loc.Get("tree.detail_close"));
            return Join(parts.ToArray());
        }

        parts.Add(Cost(def));
        parts.Add(Unlocks(def));
        parts.Add(WhyNot(def) ?? Loc.Get("tree.detail_unlock"));
        return Join(parts.ToArray());
    }

    /// <summary>Why the Unlock button is greyed out, or null when it is not.</summary>
    private static string WhyNot(TechDef def)
    {
        if (!def.ParentsUnlocked) return Loc.Fmt("tree.needs_first", MissingParents(def));
        if (!def.EnoughResources) return TmpText.Clean(LLBase.L("tech_not_enough_resources"));
        return def.TechState == TechState.Available ? null : Loc.Get("craft.cannot_start");
    }

    /// <summary>
    /// Enter in the detail window presses Unlock (or OK). Returns false when that window is not on top.
    /// </summary>
    internal static bool TryConfirm()
    {
        if (!Ready || !(LazyWindowsStackController.ActiveWindow is UITechTreeElementWindow window)) return false;
        try
        {
            var def = G.ElementData(window)?.TechDef;
            // The game always adds Unlock (or OK, for a learned tech) first and Cancel second.
            // Matching on KeyToReplace == Select found neither of the two buttons on 2026-09-25,
            // although Space - the game's own Select path - pressed Unlock fine; so go by position.
            var buttons = G.ElementButtons(window);
            var button = buttons?.FirstOrDefault(b => b != null);
            if (button == null || def == null)
            {
                Plugin.Log?.LogInfo($"[Trees] Enter in the tech window found no button " +
                                    $"({buttons?.Count ?? -1} buttons, def {(def == null ? "missing" : def.id)}); closing.");
                window.Close();
                return true;
            }

            if (!button.LazyButton.interactable)
            {
                Say(WhyNot(def) ?? Loc.Get("craft.cannot_start"));
                return true;
            }

            Plugin.Log?.LogInfo($"[Trees] Enter presses '{TmpText.Clean(button.GetComponentInChildren<TMPro.TMP_Text>(true)?.text)}' " +
                                $"(key {(button.KeyToReplace == null ? "none" : button.KeyToReplace.value.ToString())}).");
            var wasLearned = def.TechState == TechState.Unlocked;
            button.LazyButton.onClick?.Invoke();

            // The button's listener can be gone: a pooled button that wakes up after Draw has
            // LazyButton.Awake swap its onClick for an empty event, and the press does nothing.
            // Seen on 2026-09-25 ("Hacken", 8 of 8 red). Do what the game's Unlock does instead.
            if (!wasLearned && def.TechState != TechState.Unlocked)
            {
                Plugin.Log?.LogInfo($"[Trees] Unlock button did nothing for '{def.id}'; unlocking directly.");
                def.Unlock();
                G.UnlockReputationTechs.Invoke(null, null);
                var page = LazyUI.GetWindow<CharacterWindow>()?.TechTreePageWidget;
                if (page != null)
                {
                    G.RedrawSpheres.Invoke(page, null);
                    page.UpdateElements();
                    G.UpdateConnectors.Invoke(page, null);
                }
                LazyAudio.PlayAndForget("unlock");
            }

            // Same for OK and for Unlock: the window closes, whether or not the listener survived.
            if (LazyWindowsStackController.ActiveWindow == window) window.Close();

            if (!wasLearned && def.TechState == TechState.Unlocked)
                Say(Join(Loc.Fmt("tree.tech_learned", TechName(def)),
                         Loc.Fmt("tree.points", Res("tech_red"), Res("tech_green"), Res("tech_blue"))), holdFocus: true);
        }
        catch (Exception ex)
        {
            Plugin.Log?.LogError($"[Trees] Unlocking failed: {ex.GetType().Name}: {ex.Message}");
        }
        return true;
    }

    // ---- the inspiration page ---------------------------------------------------------------

    internal static string TalentName(string talentId)
    {
        if (string.IsNullOrEmpty(talentId)) return null;
        return Loc.Find("talent." + talentId) ?? Loc.Find("icon." + talentId) ?? talentId;
    }

    private static string TalentSummary(TalentData talent)
    {
        if (talent == null) return null;

        var parts = new List<string>
        {
            Loc.Fmt("tree.talent_summary", TalentName(talent.id), talent.curTalentLevel, talent.curExp,
                talent.talentExpLevelBalanceData?.GetExpForLevel(talent.curTalentLevel) ?? 0,
                talent.talentExpPoints, Mastery(talent)),
            Loc.Fmt("tree.faith", Faith()),
        };

        var visible = (talent.activeInspirations ?? new List<InspirationData>())
            .Where(i => !i.IsHidden && !i.isAllLevelsBought).ToList();
        var complete = visible.Count(i => i.IsCompleted);
        if (complete > 0) parts.Add(Loc.Fmt("tree.insp_ready", complete));
        parts.Add(Loc.Fmt("tree.insp_open", visible.Count - complete));

        var perks = GameBalance.Me.talentLevelUpDefs.Count(d =>
            !d.isZombiePerk && d.talentId == talent.id && talent.GetLevelUpState(d) == TalentLevelUpDef.State.Available);
        if (perks > 0) parts.Add(Loc.Fmt("tree.perks_ready", perks));

        parts.Add(Loc.Get("tree.talent_hint"));
        return Join(parts.ToArray());
    }

    /// <summary>
    /// The number the world's "needs building 4" locks compare against: mastery (raised only by the
    /// mastery perks) plus tool and perk bonuses. Not the talent level, which experience raises and
    /// which only pays out talent points - reading the level made the tree say 4 where a lock said 3.
    /// </summary>
    private static int Mastery(TalentData talent) =>
        MainGame.PlayerController?.GetMasteryLevelForTalentBranch(talent.id) ?? talent.curTalentValue;

    private static int Faith() =>
        MainGame.PlayerController.PlayerData.Inventory.Data.GetTotalCountInInventory("faith");

    /// <summary>An inspiration still being worked on or waiting to be bought.</summary>
    private static string DescribeInspiration(InspirationWidget widget)
    {
        var insp = widget.InspirationData;
        if (insp == null) return null;

        var level = insp.curLevel;
        var parts = new List<string> { InspirationName(insp.id, level), InspirationDescription(insp.id, level) };
        var def = InspirationDef.GetDataForLevel(insp.id, level);

        if (!insp.IsCompleted)
        {
            parts.Add(Loc.Fmt("tree.insp_progress", Math.Min(insp.curProgressValue, insp.completionGoalValue), insp.completionGoalValue));
        }
        else
        {
            var price = def?.completionPrice ?? 0;
            var faith = Faith();
            parts.Add(price <= 0 ? Loc.Get("tree.insp_free")
                : faith >= price ? Loc.Fmt("tree.insp_buy", price, faith)
                : Loc.Fmt("tree.insp_poor", price, faith));
        }

        if (def != null && def.completionExp > 0) parts.Add(Loc.Fmt("tree.insp_reward", def.completionExp));
        return Join(parts.ToArray());
    }

    private static string DescribeFinished(InspirationWidgetFinished widget)
    {
        var def = G.FinishedData(widget)?.InspirationDef;
        if (def == null) return null;
        return Join(TmpText.Clean(LLBase.L(def.id)), Loc.Get("tree.insp_done"), InspirationDescription(def.idWithoutLvl, def.lvl));
    }

    private static string InspirationName(string id, int level) => TmpText.Clean(LLBase.L($"{id}_{level}"));

    /// <summary>
    /// What the inspiration asks for. The game only writes a description for some levels
    /// ("insp_gardener_1_d", "_2_d") and shows the raw key for the rest, so fall back to the nearest
    /// lower level that has one.
    /// </summary>
    private static string InspirationDescription(string id, int level)
    {
        if (string.IsNullOrEmpty(id)) return null;
        for (var l = level; l >= 1; l--)
        {
            var key = $"{id}_{l}_d";
            if (LLBase.HasL(key)) return TmpText.Clean(LLBase.L(key));
        }
        return null;
    }

    /// <summary>A perk node in the talent tree. On screen these are icons only.</summary>
    private static string DescribeLevelUp(TalentLevelUpWidget widget)
    {
        var def = widget.Data?.Def;
        if (def == null || def.isZombiePerk) return null;

        var system = MainGame.Instance.GameSave.talentSystemData;
        var talent = system.GetTalentBranch(def.talentId);
        if (talent == null) return null;

        var state = talent.GetLevelUpState(def);
        if (state == TalentLevelUpDef.State.Unknown) return Loc.Get("tree.perk_unknown");

        var parts = new List<string> { LevelUpName(def) };
        switch (state)
        {
            case TalentLevelUpDef.State.Unlocked:
                parts.Add(Loc.Get("tree.learned"));
                break;
            case TalentLevelUpDef.State.Available:
                parts.Add(Loc.Fmt("tree.perk_can_learn", def.talentExpPointsPrice, talent.talentExpPoints));
                break;
            default:
                if (!def.ParentsUnlocked)
                {
                    var missing = def.parentDefinitionList.Where(p => !talent.studiedLevelUps.Contains(p.id)).Select(LevelUpName).ToList();
                    var any = def.lockType == TalentLevelUpDef.LockType.Any && missing.Count > 1;
                    parts.Add(Loc.Fmt("tree.needs_first", missing.Count == 0
                        ? TmpText.Clean(LLBase.L("ui_previous_perk_locked"))
                        : string.Join(any ? Loc.Get("tree.or") : ", ", missing)));
                }
                parts.Add(Loc.Fmt("tree.perk_cost", def.talentExpPointsPrice, talent.talentExpPoints));
                break;
        }

        if (!string.IsNullOrEmpty(def.linkedPerk) && LLBase.HasL(def.linkedPerk + "_d"))
            parts.Add(TmpText.Clean(LLBase.L(def.linkedPerk + "_d")));
        return Join(parts.ToArray());
    }

    private static string LevelUpName(TalentLevelUpDef def)
    {
        if (!string.IsNullOrEmpty(def.linkedPerk))
        {
            var perk = GameBalance.Me.GetData<PerkDef>(def.linkedPerk);
            if (perk != null) return TmpText.Clean(perk.GetHeader());
        }
        if (def.talentValueAdd > 0)
            return Loc.Fmt("tree.mastery_add", TmpText.Clean(LLBase.L("ui_add_mastery")), def.talentValueAdd);
        return TmpText.Clean(LLBase.L(def.id));
    }

    private static string DescribeTalentTab(TalentTabButton button)
    {
        var talent = MainGame.Instance.GameSave.talentSystemData.GetTalentBranch(button.TalentId);
        if (talent == null) return TalentName(button.TalentId);
        return Join(Loc.Fmt("tree.talent_tab", TalentName(talent.id), talent.curTalentLevel, Mastery(talent)),
                    talent.HasCompletedInspirationToBuy() ? Loc.Get("tree.something_to_buy") : null);
    }

    /// <summary>The perk nodes' own tooltip repeats the name and description the focus line already gave.</summary>
    [HarmonyPatch(typeof(UITooltip), nameof(UITooltip.ShowTalentLevelUpWidget))]
    [HarmonyPrefix]
    private static void UITooltip_ShowTalentLevelUpWidget(TalentLevelUpWidget widget)
    {
        try
        {
            if (widget != null && widget.Data?.Def != null && !widget.Data.Def.isZombiePerk)
                TooltipReader.SuppressNext = true;
        }
        catch { /* the tooltip is read normally then */ }
    }

    // ---- purchases --------------------------------------------------------------------------

    [HarmonyPatch(typeof(TalentSystemData), nameof(TalentSystemData.PurchaseInspiration))]
    [HarmonyPrefix]
    private static void TalentSystemData_PurchaseInspiration_Prefix(TalentSystemData __instance, string inspirationId,
        out (string name, int exp, int level) __state)
    {
        __state = default;
        try
        {
            if (!GameBalance.Me.inspirationLevelsCache.TryGetValue(inspirationId, out var levels)) return;
            if (!TalentSystemCache.Instance.inspirations.TryGetValue(inspirationId, out var insp)) return;
            var talent = __instance.GetTalentBranch(levels.talentId);
            __state = (InspirationName(inspirationId, insp.curLevel),
                InspirationDef.GetDataForLevel(inspirationId, insp.curLevel)?.completionExp ?? 0,
                talent?.curTalentLevel ?? 0);
        }
        catch { /* no announcement then */ }
    }

    [HarmonyPatch(typeof(TalentSystemData), nameof(TalentSystemData.PurchaseInspiration))]
    [HarmonyPostfix]
    private static void TalentSystemData_PurchaseInspiration(TalentSystemData __instance, string inspirationId,
        (string name, int exp, int level) __state) =>
        Guard("inspiration purchase", () =>
        {
            if (__state.name == null) return;
            var talent = __instance.GetTalentBranch(GameBalance.Me.inspirationLevelsCache[inspirationId].talentId);
            var parts = new List<string> { Loc.Fmt("tree.insp_bought", __state.name, __state.exp) };
            if (talent != null && talent.curTalentLevel > __state.level)
                parts.Add(Loc.Fmt("tree.talent_level_up", talent.curTalentLevel, talent.talentExpPoints));
            Say(Join(parts.ToArray()), holdFocus: true);
        });

    [HarmonyPatch(typeof(TalentSystemData), nameof(TalentSystemData.PurchaseLevel))]
    [HarmonyPrefix]
    private static void TalentSystemData_PurchaseLevel_Prefix(TalentSystemData __instance, string talentLevelId, out bool __state)
    {
        __state = false;
        try
        {
            __state = __instance.CanPurchaseLevel(talentLevelId, out _);
        }
        catch { /* no announcement then */ }
    }

    [HarmonyPatch(typeof(TalentSystemData), nameof(TalentSystemData.PurchaseLevel))]
    [HarmonyPostfix]
    private static void TalentSystemData_PurchaseLevel(TalentSystemData __instance, string talentLevelId, bool free, bool __state) =>
        Guard("perk purchase", () =>
        {
            // Free purchases are the story granting a perk, not the player buying one here.
            if (!__state || free || !(LazyWindowsStackController.ActiveWindow is CharacterWindow)) return;
            var def = GameBalance.Me.GetData<TalentLevelUpDef>(talentLevelId);
            var talent = def == null ? null : __instance.GetTalentBranch(def.talentId);
            if (talent == null || !talent.studiedLevelUps.Contains(talentLevelId)) return;
            Say(Loc.Fmt("tree.perk_learned", LevelUpName(def), talent.talentExpPoints), holdFocus: true);
        });

    // ---- focus ------------------------------------------------------------------------------

    /// <summary>What to say for a focused control on either tree, or null for the generic reading.</summary>
    internal static string DescribeWidget(GamepadNavigationItem item)
    {
        if (item == null || !Ready) return null;
        try
        {
            var element = item.GetComponent<LazyScrollableElement>() ?? item.GetComponentInParent<LazyScrollableElement>();
            if (element != null && element.Data is TechTreeElementBaseWidgetData tech && tech.techDef != null)
                return DescribeTech(tech.techDef);

            var levelUp = item.GetComponent<TalentLevelUpWidget>() ?? item.GetComponentInParent<TalentLevelUpWidget>();
            if (levelUp != null) return DescribeLevelUp(levelUp);

            var inspiration = item.GetComponent<InspirationWidget>() ?? item.GetComponentInParent<InspirationWidget>();
            if (inspiration != null) return DescribeInspiration(inspiration);

            var finished = item.GetComponent<InspirationWidgetFinished>() ?? item.GetComponentInParent<InspirationWidgetFinished>();
            if (finished != null) return DescribeFinished(finished);

            var tab = item.GetComponentInParent<TalentTabButton>();
            if (tab != null) return DescribeTalentTab(tab);
        }
        catch (Exception ex)
        {
            Plugin.Log?.LogWarning($"[Trees] Could not describe '{item.name}': {ex.GetType().Name}: {ex.Message}");
        }
        return null;
    }

    // ---- keys -------------------------------------------------------------------------------

    /// <summary>Ctrl+left/right: tech branch or talent. Ctrl+up/down: page. True when handled.</summary>
    internal static bool TryKeys()
    {
        if (!Ready || !(LazyWindowsStackController.ActiveWindow is CharacterWindow window)) return false;

        if (_nextUnlockableKey.Value.IsDown()) return CycleUnlockable(window, 1);
        if (_prevUnlockableKey.Value.IsDown()) return CycleUnlockable(window, -1);

        if (!Input.GetKey(KeyCode.LeftControl) && !Input.GetKey(KeyCode.RightControl)) return false;

        try
        {
            if (Input.GetKeyDown(KeyCode.RightArrow) || Input.GetKeyDown(KeyCode.LeftArrow))
            {
                var page = window.LastOpenedPage;
                if (page != CharacterWindowData.CharPage.TechTree && page != CharacterWindowData.CharPage.Inspiration)
                {
                    Say(Loc.Get("tree.no_branches"));
                    return true;
                }
                ScreenReader.ClearMenuContext();
                if (Input.GetKeyDown(KeyCode.RightArrow)) window.OnPressedNextSubTab();
                else window.OnPressedPrevSubTab();
                return true;
            }

            if (Input.GetKeyDown(KeyCode.DownArrow) || Input.GetKeyDown(KeyCode.UpArrow))
            {
                ScreenReader.ClearMenuContext();
                if (Input.GetKeyDown(KeyCode.DownArrow)) window.OnPressedNextTab();
                else window.OnPressedPrevTab();
                return true;
            }
        }
        catch (Exception ex)
        {
            Plugin.Log?.LogError($"[Trees] Key failed: {ex.GetType().Name}: {ex.Message}");
            return true;
        }
        return false;
    }

    /// <summary>
    /// C / Shift+C on the inspiration page: moves focus to the next (previous) thing that can be
    /// bought right now, in reading order - top to bottom, left to right. On the tech tree see
    /// <see cref="CycleLearnableTech"/>.
    /// </summary>
    private static bool CycleUnlockable(CharacterWindow window, int step)
    {
        if (window.LastOpenedPage == CharacterWindowData.CharPage.TechTree) return CycleLearnableTech(window, step);
        if (window.LastOpenedPage != CharacterWindowData.CharPage.Inspiration) return false;

        try
        {
            var candidates = BuyableWidgets(window);
            var focused = UiNarrator.FocusedItem;
            var at = focused == null ? -1 : candidates.IndexOf(focused);

            // Past the last one here (or nothing here): on to the next talent that has something.
            if (candidates.Count == 0 || (at >= 0 && (at + step < 0 || at + step >= candidates.Count)))
            {
                var current = G.CharData(window)?.InspirationPageWidgetData?.TalentData?.id;
                var next = NextTalentWithPurchase(current, step);
                if (next != null) return SwitchTalentAndFocus(window, next, step);
                if (candidates.Count == 0)
                {
                    Say(Loc.Get("tree.nothing_unlockable"));
                    return true;
                }
            }

            var target = at < 0
                ? candidates[step > 0 ? 0 : candidates.Count - 1]
                : candidates[(at + step + candidates.Count) % candidates.Count];

            // Pressing again on the only one: focus stays put and says nothing, so read it here.
            if (target == focused)
            {
                Say(Join(DescribeWidget(target), Loc.Get("tree.only_unlockable")));
                return true;
            }

            var controller = focused != null && focused.Controller != null
                ? focused.Controller
                : window.GetComponentInChildren<GamepadNavigationController>();
            if (controller == null) return true;

            // SetFocusedItem only knows items from the controller's last scan (see ChestSections).
            if (!Registered(controller).Contains(target)) controller.ReinitItems(focusOnFirstActive: false);
            controller.SetFocusedItem(target);
            Plugin.Log?.LogInfo($"[Trees] Next unlockable: '{target.name}' ({candidates.Count} in all).");
        }
        catch (Exception ex)
        {
            Plugin.Log?.LogError($"[Trees] Finding the next unlockable failed: {ex.GetType().Name}: {ex.Message}");
        }
        return true;
    }

    /// <summary>What can be bought on the talent shown, in reading order - top to bottom, left to right.</summary>
    private static List<GamepadNavigationItem> BuyableWidgets(CharacterWindow window) =>
        window.GetComponentsInChildren<GamepadNavigationItem>(false)
            .Where(n => n != null && n.Active && CanBuy(n))
            .OrderByDescending(n => Mathf.Round(n.transform.position.y))
            .ThenBy(n => n.transform.position.x)
            .ToList();

    /// <summary>
    /// The next open talent after <paramref name="current"/> (before it, for a negative step) that has
    /// something to buy, read from the save since only the shown talent has widgets. Null when none.
    /// </summary>
    private static string NextTalentWithPurchase(string current, int step)
    {
        var talents = GameBalance.Me.talentDefs.Select(d => d.id).ToList();
        var from = talents.IndexOf(current);
        for (var i = 1; i < talents.Count; i++)
        {
            var id = talents[((from + step * i) % talents.Count + talents.Count) % talents.Count];
            if (id == current || !MainGame.Instance.GameSave.knowledgeSystem.IsTalentBranchUnlocked(id)) continue;
            if (HasPurchase(MainGame.Instance.GameSave.talentSystemData.GetTalentBranch(id))) return id;
        }
        return null;
    }

    /// <summary>The data side of <see cref="CanBuy"/>, for a talent that is not on screen.</summary>
    private static bool HasPurchase(TalentData talent)
    {
        if (talent == null) return false;
        var faith = Faith();
        var inspiration = (talent.activeInspirations ?? new List<InspirationData>()).Any(i =>
        {
            if (i.IsHidden || i.isAllLevelsBought || !i.IsCompleted) return false;
            var price = InspirationDef.GetDataForLevel(i.id, i.curLevel)?.completionPrice ?? 0;
            return price <= 0 || faith >= price;
        });
        return inspiration || GameBalance.Me.talentLevelUpDefs.Any(d =>
            !d.isZombiePerk && d.talentId == talent.id && talent.GetLevelUpState(d) == TalentLevelUpDef.State.Available);
    }

    /// <summary>
    /// Shows another talent the way its tab button does, then focuses its first (or, going
    /// backwards, last) buyable thing. The page summary names the talent.
    /// </summary>
    private static bool SwitchTalentAndFocus(CharacterWindow window, string talentId, int step)
    {
        ScreenReader.ClearMenuContext();
        _skipFocusEcho = true;
        try
        {
            window.SetInspirationPageWithSpecificTalent(talentId);
        }
        finally
        {
            _skipFocusEcho = false;
        }

        // The new widgets are laid out at the next canvas update; reading order needs their places now.
        Canvas.ForceUpdateCanvases();
        var candidates = BuyableWidgets(window);
        var controller = window.GetComponentInChildren<GamepadNavigationController>();
        if (candidates.Count == 0 || controller == null)
        {
            Plugin.Log?.LogWarning($"[Trees] Switched to talent '{talentId}' but found nothing to buy on screen.");
            return true;
        }

        var target = candidates[step > 0 ? 0 : candidates.Count - 1];
        controller.ReinitItems(focusOnFirstActive: false);
        controller.SetFocusedItem(target);
        Plugin.Log?.LogInfo($"[Trees] Next unlockable: '{target.name}' in talent '{talentId}' ({candidates.Count} there).");
        return true;
    }

    /// <summary>True while C switches talent: the focus Speak would repeat is still on the old talent.</summary>
    private static bool _skipFocusEcho;

    /// <summary>
    /// C / Shift+C on the tech tree: the next (previous) technology the points on hand can learn,
    /// across every open branch - this branch first, then the next ones. Read from the definitions,
    /// not the nodes on screen, which only exist while scrolled into view; the game's own
    /// <c>DisplayTab(tab, focusOnTech)</c> then switches branch if needed, scrolls and focuses.
    /// </summary>
    private static bool CycleLearnableTech(CharacterWindow window, int step)
    {
        try
        {
            var page = window.TechTreePageWidget;
            if (page == null) return true;
            var known = MainGame.Instance.GameSave.knowledgeSystem;
            var current = G.CurrentTab(page);
            var tabCount = Enum.GetValues(typeof(TechTreeTab)).Length;

            // Reading order starting at this branch: branch, then column, then top to bottom.
            int TabRank(TechTreeTab t) => ((int)t - (int)current + tabCount) % tabCount;
            (int, float, float) Key(TechDef d) => (TabRank(d.tab), d.TreePos.x, -d.TreePos.y);

            var candidates = GameBalance.Me.techDefs
                .Where(d => d.techDefType == TechDefType.Common && !known.IsTechTabLocked(d.tab) &&
                            d.TechState == TechState.Available && d.EnoughResources)
                .OrderBy(Key)
                .ToList();

            if (candidates.Count == 0)
            {
                Say(Join(Loc.Get("tree.nothing_learnable"),
                         Loc.Fmt("tree.points", Res("tech_red"), Res("tech_green"), Res("tech_blue"))));
                return true;
            }

            var focusedItem = UiNarrator.FocusedItem;
            var focusedElement = focusedItem == null ? null
                : focusedItem.GetComponent<LazyScrollableElement>() ?? focusedItem.GetComponentInParent<LazyScrollableElement>();
            var focused = (focusedElement?.Data as TechTreeElementBaseWidgetData)?.techDef;

            TechDef target;
            if (focused == null || focused.tab != current)
            {
                target = candidates[step > 0 ? 0 : candidates.Count - 1];
            }
            else
            {
                // From where focus is, so it works whether or not focus is on a learnable one.
                var at = Key(focused);
                target = step > 0
                    ? candidates.FirstOrDefault(d => Key(d).CompareTo(at) > 0) ?? candidates[0]
                    : candidates.LastOrDefault(d => Key(d).CompareTo(at) < 0) ?? candidates[candidates.Count - 1];
            }

            if (target == focused)
            {
                Say(Join(DescribeTech(target), Loc.Get("tree.only_learnable")));
                return true;
            }

            if (target.tab != current) ScreenReader.ClearMenuContext();
            page.DisplayTab(target.tab, target.id);
            Plugin.Log?.LogInfo($"[Trees] Next learnable tech: '{target.id}' in {target.tab} ({candidates.Count} in all).");
        }
        catch (Exception ex)
        {
            Plugin.Log?.LogError($"[Trees] Finding the next learnable tech failed: {ex.GetType().Name}: {ex.Message}");
        }
        return true;
    }

    /// <summary>A finished inspiration the player has the faith for, or a perk they have the points for.</summary>
    private static bool CanBuy(GamepadNavigationItem item)
    {
        var levelUp = item.GetComponent<TalentLevelUpWidget>() ?? item.GetComponentInParent<TalentLevelUpWidget>();
        if (levelUp != null)
        {
            var def = levelUp.Data?.Def;
            if (def == null || def.isZombiePerk) return false;
            var talent = MainGame.Instance.GameSave.talentSystemData.GetTalentBranch(def.talentId);
            return talent != null && talent.GetLevelUpState(def) == TalentLevelUpDef.State.Available;
        }

        var inspiration = item.GetComponent<InspirationWidget>() ?? item.GetComponentInParent<InspirationWidget>();
        var insp = inspiration?.InspirationData;
        if (insp == null || insp.IsHidden || insp.isAllLevelsBought || !insp.IsCompleted) return false;
        var price = InspirationDef.GetDataForLevel(insp.id, insp.curLevel)?.completionPrice ?? 0;
        return price <= 0 || Faith() >= price;
    }

    private static readonly AccessTools.FieldRef<GamepadNavigationController, List<GamepadNavigationItem>> Registered =
        AccessTools.FieldRefAccess<GamepadNavigationController, List<GamepadNavigationItem>>("selectableItems");

    internal static bool TryRepeat()
    {
        if (!Ready) return false;
        string text = null;
        try
        {
            text = LazyWindowsStackController.ActiveWindow switch
            {
                UITechTreeElementWindow w => DescribeElementWindow(w),
                CharacterWindow w => Join(PageName(w.LastOpenedPage), PageSummary(w, w.LastOpenedPage)),
                _ => null,
            };
        }
        catch (Exception ex)
        {
            Plugin.Log?.LogError($"[Trees] Could not read the page again: {ex.Message}");
        }
        if (string.IsNullOrEmpty(text)) return false;
        ScreenReader.Say(text);
        if (!string.IsNullOrEmpty(UiNarrator.LastFocusLabel)) ScreenReader.Say(UiNarrator.LastFocusLabel, interrupt: false);
        return true;
    }

    // ---- shared -----------------------------------------------------------------------------

    /// <summary>
    /// Says a page or window summary, then the focused control again: the game focuses the first node
    /// while drawing the page, a moment before this runs, so the summary would otherwise cut it off.
    /// </summary>
    private static void Speak(LazyWidgetBase window, string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        Plugin.Log?.LogInfo($"[Trees] \"{text}\"");
        ScreenReader.Say(text);

        var focused = UiNarrator.FocusedItem;
        if (!_skipFocusEcho && focused != null && window != null && focused.transform.IsChildOf(window.transform) &&
            !string.IsNullOrEmpty(UiNarrator.LastFocusLabel))
            ScreenReader.Say(UiNarrator.LastFocusLabel, interrupt: false);
        UiNarrator.QueueFocusUntil = Time.unscaledTime + 0.6f;
    }

    private static string Join(params string[] parts) =>
        string.Join(". ", parts.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p.Trim().TrimEnd('.')));

    private static void Say(string text, bool holdFocus = false)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        Plugin.Log?.LogInfo($"[Trees] \"{text}\"");
        ScreenReader.Say(text);
        if (holdFocus) UiNarrator.QueueFocusUntil = Time.unscaledTime + 1.5f;
    }

    private static void Guard(string what, Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            Plugin.Log?.LogError($"[Trees] Reading the {what} failed: {ex.GetType().Name}: {ex.Message}");
        }
    }
}
