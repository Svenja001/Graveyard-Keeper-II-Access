using TMPro;

namespace GraveyardKeeper2Accessibility;

/// <summary>
/// The stations that are not crafting in the recipe sense, each with a window of its own: the
/// resurrection table, the prayer stand and its sermon report, porter stations, a zombie's own
/// window, fishing (bait choice and the reeling minigame) and the town building desk.
///
/// <para>
/// Same pattern as <see cref="CraftReader"/>: each window is read from its data when it is drawn,
/// changes under a stationary focus get their own hooks, and actions whose keyboard key is unknown
/// are offered on mod keys that stand down when <see cref="GameKeys"/> says the game already binds
/// the same key. The one "do it" key is shared with the crafting stations (F).
/// </para>
///
/// <para>
/// <b>Fishing is a timing game.</b> Hold E to reel; let go while the fish fights, or the line snaps.
/// The game gives sound cues for the bite and the fight, but line tension and how close the fish is
/// exist only as a rope colour and a bobber position. <see cref="UpdateFishing"/> polls the minigame
/// each frame and says each change in one or two words, interrupting, because a late cue is useless.
/// </para>
/// </summary>
internal static class StationsReader
{
    private static ConfigEntry<KeyboardShortcut> _rollNameKey;
    private static ConfigEntry<bool> _fishingCues;

    private static string _current;

    /// <summary>False when a game field could not be found; the reader then stays out of the way.</summary>
    internal static bool Ready { get; private set; }

    private static class G
    {
        internal static readonly AccessTools.FieldRef<LazyWidget<UIResurrectionWindowData>, UIResurrectionWindowData> ResurrectionData =
            AccessTools.FieldRefAccess<LazyWidget<UIResurrectionWindowData>, UIResurrectionWindowData>("data");

        internal static readonly AccessTools.FieldRef<UIResurrectionWindowData, string> ResurrectionReason =
            AccessTools.FieldRefAccess<UIResurrectionWindowData, string>("cantStartResurrectionReason");

        internal static readonly AccessTools.FieldRef<UIResurrectionWindow, UIItemCell> ResurrectionCollar =
            AccessTools.FieldRefAccess<UIResurrectionWindow, UIItemCell>("collarCell");

        internal static readonly AccessTools.FieldRef<LazyWidget<UIPrayWindowData>, UIPrayWindowData> PrayData =
            AccessTools.FieldRefAccess<LazyWidget<UIPrayWindowData>, UIPrayWindowData>("data");

        internal static readonly AccessTools.FieldRef<UIPrayWindow, LazyButton> PrayStartButton =
            AccessTools.FieldRefAccess<UIPrayWindow, LazyButton>("startButton");

        internal static readonly AccessTools.FieldRef<UIPrayWindow, UIFixedTypeItemCell> PraySlot =
            AccessTools.FieldRefAccess<UIPrayWindow, UIFixedTypeItemCell>("prayInsertionCell");

        internal static readonly AccessTools.FieldRef<LazyWidget<UIPrayReportWindowData>, UIPrayReportWindowData> ReportData =
            AccessTools.FieldRefAccess<LazyWidget<UIPrayReportWindowData>, UIPrayReportWindowData>("data");

        internal static readonly AccessTools.FieldRef<LazyWidget<UIPorterStationWindowData>, UIPorterStationWindowData> PorterData =
            AccessTools.FieldRefAccess<LazyWidget<UIPorterStationWindowData>, UIPorterStationWindowData>("data");

        internal static readonly AccessTools.FieldRef<UIPorterStationItemCell, bool> PorterListed =
            AccessTools.FieldRefAccess<UIPorterStationItemCell, bool>("isListed");

        internal static readonly AccessTools.FieldRef<UIPorterStationItemCell, Item> PorterItem =
            AccessTools.FieldRefAccess<UIPorterStationItemCell, Item>("item");

        internal static readonly AccessTools.FieldRef<LazyWidget<UIZombieWorkerWindowData>, UIZombieWorkerWindowData> ZombieData =
            AccessTools.FieldRefAccess<LazyWidget<UIZombieWorkerWindowData>, UIZombieWorkerWindowData>("data");

        internal static readonly AccessTools.FieldRef<UIZombieWorkerWindow, List<string>> ZombieTalents =
            AccessTools.FieldRefAccess<UIZombieWorkerWindow, List<string>>("talents");

        internal static readonly AccessTools.FieldRef<UIZombieWorkerWindow, ZombieProgressionWidget> ZombieProgression =
            AccessTools.FieldRefAccess<UIZombieWorkerWindow, ZombieProgressionWidget>("zombieProgressionWidget");

        internal static readonly AccessTools.FieldRef<LazyWidget<UIFishingWindowData>, UIFishingWindowData> FishingData =
            AccessTools.FieldRefAccess<LazyWidget<UIFishingWindowData>, UIFishingWindowData>("data");

        internal static readonly AccessTools.FieldRef<UIFishingWindow, int> FishingPointer =
            AccessTools.FieldRefAccess<UIFishingWindow, int>("selectionPointer");

        internal static readonly AccessTools.FieldRef<UIFishingWindow, UIDialogWindowButton> FishingSubmit =
            AccessTools.FieldRefAccess<UIFishingWindow, UIDialogWindowButton>("submutButton");

        internal static readonly AccessTools.FieldRef<PlayerFishingComponent, FishingMiniGame> MiniGame =
            AccessTools.FieldRefAccess<PlayerFishingComponent, FishingMiniGame>("miniGame");

        internal static readonly AccessTools.FieldRef<LazyWidget<UITownBuildingWindowData>, UITownBuildingWindowData> TownData =
            AccessTools.FieldRefAccess<LazyWidget<UITownBuildingWindowData>, UITownBuildingWindowData>("data");

        internal static readonly AccessTools.FieldRef<LazyWidget<UITownBuildingWidgetData>, UITownBuildingWidgetData> TownWidgetData =
            AccessTools.FieldRefAccess<LazyWidget<UITownBuildingWidgetData>, UITownBuildingWidgetData>("data");

        internal static readonly MethodInfo ResurrectionStart = AccessTools.Method(typeof(UIResurrectionWindow), "StartResurrectionButtonPressed");
        internal static readonly MethodInfo ResurrectionRoll = AccessTools.Method(typeof(UIResurrectionWindow), "OnDiceButtonPressed");
        internal static readonly MethodInfo PrayStart = AccessTools.Method(typeof(UIPrayWindow), "OnPrayStartPress");
        internal static readonly MethodInfo ZombieNextTab = AccessTools.Method(typeof(UIZombieWorkerWindow), "OnPressedNextTab");
        internal static readonly MethodInfo ZombiePrevTab = AccessTools.Method(typeof(UIZombieWorkerWindow), "OnPressedPrevTab");
        internal static readonly MethodInfo FishingSelect = AccessTools.Method(typeof(UIFishingWindow), "SelectNextItem");
        internal static readonly MethodInfo FishingCast = AccessTools.Method(typeof(UIFishingWindow), "OnSubmitBait");
    }

    internal static void Init(ConfigFile config)
    {
        try
        {
            System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(typeof(G).TypeHandle);
            Ready = true;
        }
        catch (Exception ex)
        {
            Plugin.Log?.LogError($"[Station] Station windows are not supported by this game version: " +
                                 $"{(ex.InnerException ?? ex).GetType().Name}: {(ex.InnerException ?? ex).Message}");
        }

        _rollNameKey = ModKeys.Bind(config,
            "Crafting", "RollZombieName", new KeyboardShortcut(KeyCode.N),
            "At the resurrection table: roll a new name for the zombie. Only acts if the game does not " +
            "already use this key for that.");
        _fishingCues = config.Bind(
            "Crafting", "FishingCues", true,
            "While reeling in a fish: say when it bites, when it fights (let go of E), when to reel " +
            "again, when the line is getting tight, and how close the catch is.");
    }

    internal static void Reset() => _current = null;

    internal static bool IsStationWindow(LazyWidgetBase window) =>
        Ready && (window is UIResurrectionWindow || window is UIPrayWindow || window is UIPrayReportWindow ||
                  window is UIPorterStationWindow || window is UIZombieWorkerWindow || window is UIFishingWindow ||
                  window is UITownBuildingWindow);

    // ---- windows ----------------------------------------------------------------------------

    private static void SpeakSummary(LazyWidgetBase window, string text)
    {
        if (string.IsNullOrEmpty(text) || text == _current) return;
        _current = text;
        Plugin.Log?.LogInfo($"[Station] \"{text}\"");
        ScreenReader.Say(text);

        var focused = UiNarrator.FocusedItem;
        if (focused != null && window != null && focused.transform.IsChildOf(window.transform) &&
            !string.IsNullOrEmpty(UiNarrator.LastFocusLabel))
            ScreenReader.Say(UiNarrator.LastFocusLabel, interrupt: false);
        UiNarrator.QueueFocusUntil = Time.unscaledTime + 0.6f;
    }

    /// <summary>True while the resurrection table draws in full, when RedrawLite is part of that and not a change.</summary>
    private static bool _resurrectionRedrawing;

    [HarmonyPatch(typeof(UIResurrectionWindow), nameof(UIResurrectionWindow.Redraw))]
    [HarmonyPrefix]
    private static void UIResurrectionWindow_Redraw_Prefix() => _resurrectionRedrawing = true;

    [HarmonyPatch(typeof(UIResurrectionWindow), nameof(UIResurrectionWindow.Redraw))]
    [HarmonyPostfix]
    private static void UIResurrectionWindow_Redraw(UIResurrectionWindow __instance)
    {
        _resurrectionRedrawing = false;
        Guard("resurrection table", () => SpeakSummary(__instance, DescribeResurrection(__instance)));
    }

    /// <summary>A collar was put in: say what that changed, not the whole table again.</summary>
    [HarmonyPatch(typeof(UIResurrectionWindow), "RedrawLite")]
    [HarmonyPostfix]
    private static void UIResurrectionWindow_RedrawLite(UIResurrectionWindow __instance) =>
        Guard("resurrection table", () =>
        {
            if (_resurrectionRedrawing) return;
            var data = G.ResurrectionData(__instance);
            if (data == null || data.IsEmpty) return;
            Say(Join(CollarLine(data), ResurrectionReadiness(data)), holdFocus: true);
        });

    [HarmonyPatch(typeof(UIResurrectionWindow), "OnDiceButtonPressed")]
    [HarmonyPostfix]
    private static void UIResurrectionWindow_OnDiceButtonPressed(UIResurrectionWindow __instance) =>
        Guard("zombie name", () =>
        {
            var data = G.ResurrectionData(__instance);
            if (data != null) Say(Loc.Fmt("station.zombie_name", TmpText.Clean(LLBase.L(data.ZombieName))));
        });

    [HarmonyPatch(typeof(UIPrayWindow), nameof(UIPrayWindow.Redraw))]
    [HarmonyPostfix]
    private static void UIPrayWindow_Redraw(UIPrayWindow __instance) =>
        Guard("prayer stand", () => SpeakSummary(__instance, DescribePray(__instance)));

    [HarmonyPatch(typeof(UIPrayReportWindow), nameof(UIPrayReportWindow.Redraw))]
    [HarmonyPostfix]
    private static void UIPrayReportWindow_Redraw(UIPrayReportWindow __instance) =>
        Guard("sermon report", () => SpeakSummary(__instance, DescribeReport(__instance)));

    [HarmonyPatch(typeof(UIPorterStationWindow), nameof(UIPorterStationWindow.Redraw))]
    [HarmonyPostfix]
    private static void UIPorterStationWindow_Redraw(UIPorterStationWindow __instance) =>
        Guard("porter station", () => SpeakSummary(__instance, DescribePorter(__instance)));

    [HarmonyPatch(typeof(UIPorterStationItemCell), "OnPressed")]
    [HarmonyPostfix]
    private static void UIPorterStationItemCell_OnPressed(UIPorterStationItemCell __instance) =>
        Guard("porter item", () => Say(DescribePorterCell(__instance)));

    [HarmonyPatch(typeof(UIZombieWorkerWindow), nameof(UIZombieWorkerWindow.Redraw))]
    [HarmonyPostfix]
    private static void UIZombieWorkerWindow_Redraw(UIZombieWorkerWindow __instance) =>
        Guard("zombie", () => SpeakSummary(__instance, DescribeZombie(__instance)));

    [HarmonyPatch(typeof(UIFishingWindow), "RedrawInternal")]
    [HarmonyPostfix]
    private static void UIFishingWindow_RedrawInternal(UIFishingWindow __instance) =>
        Guard("bait", () => SpeakSummary(__instance, DescribeBait(__instance)));

    [HarmonyPatch(typeof(UITownBuildingWindow), nameof(UITownBuildingWindow.Redraw))]
    [HarmonyPostfix]
    private static void UITownBuildingWindow_Redraw(UITownBuildingWindow __instance) =>
        Guard("town building desk", () => SpeakSummary(__instance, DescribeTown(__instance)));

    // ---- descriptions -----------------------------------------------------------------------

    private static string DescribeResurrection(UIResurrectionWindow window)
    {
        var data = G.ResurrectionData(window);
        if (data == null) return null;

        var parts = new List<string> { TmpText.Clean(data.InfoWidgetData?.Header) ?? Header(data.Wgo) };
        if (data.IsEmpty)
        {
            parts.Add(Loc.Get("body.table_empty"));
            return Join(parts.ToArray());
        }

        parts.Add(Loc.Fmt("station.zombie_name", TmpText.Clean(LLBase.L(data.ZombieName))));
        parts.Add(Loc.Fmt("body.skulls", data.WhiteSkulls, Loc.Fmt("body.red_skulls", data.RedSkulls)));
        parts.Add(BodyWindowsReader.OrgansLine(data.CorpseWidgetData?.Body));

        var needs = data.NeedItemsWidgetData;
        if (needs?.NeedItems != null && needs.NeedItems.Count > 0)
        {
            var list = needs.NeedItems.Select(n => Loc.Fmt("tooltip.need_have",
                needs.MultiInventory?.GetTotalCount(n.Id) ?? 0, n.GetCount(needs.WgoData), ItemText.Name(n.Id)));
            parts.Add(Loc.Fmt("tooltip.needs", string.Join("; ", list)));
        }

        parts.Add(CollarLine(data));
        parts.Add(ResurrectionReadiness(data));
        parts.Add(Loc.Fmt("station.resurrection_keys", GameKeys.Name(GameKey.ZombieRollName) ?? GameKeys.Name(_rollNameKey.Value),
            GameKeys.Name(GameKey.ExtractBody) ?? "T"));
        return Join(parts.ToArray());
    }

    private static string CollarLine(UIResurrectionWindowData data) =>
        data.Collar == null || data.Collar.IsEmpty
            ? Loc.Get("station.collar_empty")
            : Loc.Fmt("station.collar", ItemText.Name(data.Collar.id));

    private static string ResurrectionReadiness(UIResurrectionWindowData data)
    {
        if (data.CanStartResurrection != null && data.CanStartResurrection())
            return Loc.Fmt("craft.key_action", StartKeyName(GameKey.StartResurrection), TmpText.Clean(LLBase.L("ui_prepare")));

        // CanStartResurrection writes its reason into a private field as it runs.
        var reason = G.ResurrectionReason(data);
        return string.IsNullOrEmpty(reason)
            ? Loc.Get("craft.cannot_start")
            : Loc.Fmt("craft.cannot_because", TmpText.Clean(LLBase.L(reason)));
    }

    private static string DescribePray(UIPrayWindow window)
    {
        var data = G.PrayData(window);
        if (data == null) return null;

        var parts = new List<string>
        {
            Header(data.PrayingStand),
            Loc.Fmt("station.pray_stats", data.ChurchQuality, data.Happiness, data.ResultVisitors),
        };

        var sermon = data.SermonDef;
        if (sermon == null)
        {
            parts.Add(Loc.Get("station.pray_no_sermon"));
            return Join(parts.ToArray());
        }

        parts.Add(Loc.Fmt("station.pray_sermon", ItemText.Name(sermon.id)));
        if (!data.EnoughParishioners())
        {
            parts.Add(Loc.Fmt("station.pray_too_few", sermon.minParishioners));
        }
        else
        {
            var chance = Math.Clamp((int)(data.ResultVisitors * 100f / Math.Max(1, sermon.sermonDifficulty)), 0, 100);
            parts.Add(Loc.Fmt("station.pray_chance", chance));
        }

        var start = G.PrayStartButton(window);
        parts.Add(start != null && start.interactable
            ? Loc.Fmt("craft.key_action", StartKeyName(GameKey.PrayStart), TmpText.Clean(LLBase.L("ui_pray_start")))
            : Loc.Get("craft.cannot_start"));
        return Join(parts.ToArray());
    }

    private static string DescribeReport(UIPrayReportWindow window)
    {
        var result = G.ReportData(window)?.SermonResultData;
        if (result == null) return null;

        var parts = new List<string>
        {
            TmpText.Clean(LLBase.L(result.success ? "ui_pray_success_text" : "ui_pray_fail_text")),
            Loc.Fmt("station.report_visitors", result.parishionersCount),
        };
        if (result.FaithOnlyParishioners > 0) parts.Add(Loc.Fmt("station.report_faith", result.FaithOnlyParishioners));
        if (result.MoneyOnlyParishioners > 0) parts.Add(Loc.Fmt("station.report_money", Money.ToSpeech(result.MoneyOnlyParishioners)));

        if (result.success)
        {
            if (result.FaithOnlyBonus > 0) parts.Add(Loc.Fmt("station.report_faith_bonus", result.FaithOnlyBonus));
            if (result.MoneyOnlyBonus > 0) parts.Add(Loc.Fmt("station.report_money_bonus", Money.ToSpeech(result.MoneyOnlyBonus)));
            var buff = result.Definition?.successRewardBuff;
            if (!string.IsNullOrEmpty(buff)) parts.Add(Loc.Fmt("station.report_buff", TmpText.Clean(LLBase.L(buff))));
        }

        parts.Add(Loc.Get("station.report_close"));
        return Join(parts.ToArray());
    }

    private static string DescribePorter(UIPorterStationWindow window)
    {
        var data = G.PorterData(window);
        if (data == null) return null;

        var items = data.PorterStationDef?.items ?? new List<NeedItemData>();
        var parts = new List<string> { Header(data.Station) };
        if (items.Count == 0)
        {
            parts.Add(Loc.Get("station.porter_nothing"));
            return Join(parts.ToArray());
        }

        var carried = items.Where(i => data.Station.GetGameResInt(i.id) == 1).Select(i => ItemText.Name(i.id)).ToList();
        parts.Add(carried.Count == 0
            ? Loc.Get("station.porter_none_carried")
            : Loc.Fmt("station.porter_carried", string.Join(", ", carried)));
        parts.Add(Loc.Get("station.porter_hint"));
        return Join(parts.ToArray());
    }

    private static string DescribePorterCell(UIPorterStationItemCell cell)
    {
        var item = G.PorterItem(cell);
        if (item == null) return null;
        return Loc.Fmt(G.PorterListed(cell) ? "station.porter_on" : "station.porter_off", ItemText.Name(item.id));
    }

    private static string DescribeZombie(UIZombieWorkerWindow window)
    {
        var zombie = G.ZombieData(window)?.ZombieWgoData;
        if (zombie == null) return null;

        var parts = new List<string>
        {
            TmpText.Clean(LLBase.L(zombie.Name)),
            Loc.Fmt("body.skulls", zombie.WhiteSkulls, Loc.Fmt("body.red_skulls", zombie.RedSkulls)),
        };

        var talents = G.ZombieTalents(window);
        if (talents != null && talents.Count > 0)
        {
            var levels = talents.Select(t => Loc.Fmt("station.talent_level", TmpText.Clean(LLBase.L(t)), zombie.GetMasteryLevelForTalentBranch(t)));
            parts.Add(Loc.Fmt("station.zombie_talents", string.Join(", ", levels)));
        }

        parts.Add(Loc.Fmt("station.zombie_points", zombie.techRed, zombie.techGreen, zombie.techBlue));
        parts.Add(Loc.Fmt("station.zombie_perks", zombie.GetUsedPerksCount(), zombie.RedSkulls));
        parts.Add(Loc.Fmt("station.zombie_tab", ZombieTabName(window)));
        parts.Add(Loc.Get("station.zombie_hint"));
        return Join(parts.ToArray());
    }

    private static string ZombieTabName(UIZombieWorkerWindow window)
    {
        var progression = G.ZombieProgression(window);
        var perks = progression != null && progression.gameObject.activeSelf;
        return TmpText.Clean(LLBase.L(perks ? "ui_zombie_wndw_perks" : "ui_zombie_wndw_char"));
    }

    private static string DescribeBait(UIFishingWindow window)
    {
        var data = G.FishingData(window);
        if (data?.BaitItems == null || data.BaitItems.Count == 0) return null;

        var pointer = Mathf.Clamp(G.FishingPointer(window), 0, data.BaitItems.Count - 1);
        var bait = data.BaitItems[pointer];
        var parts = new List<string>
        {
            bait.id == "no_bait"
                ? Loc.Fmt("station.bait", TmpText.Clean(LLBase.L(bait.id)), pointer + 1, data.BaitItems.Count)
                : Loc.Fmt("station.bait_count", ItemText.Name(bait.id), bait.Count, pointer + 1, data.BaitItems.Count),
        };

        var reservoir = data.Reservoir?.Data;
        if (reservoir != null && data.ReservoirFishings != null)
        {
            var fish = new List<string>();
            foreach (var def in data.ReservoirFishings)
            {
                var bites = def.baitMod.List.Any(m => m.type == bait.id) && reservoir.GetGameRes(def.fishId) > 0f;
                if (!bites) continue;
                fish.Add(reservoir.GetGameRes(def.fishId + "_caught") > 0f ? ItemText.Name(def.fishId) : Loc.Get("station.fish_unknown"));
            }
            parts.Add(fish.Count == 0 ? Loc.Get("station.fish_none") : Loc.Fmt("station.fish_list", string.Join(", ", fish)));
        }

        var submit = G.FishingSubmit(window);
        if (submit?.LazyButton != null && submit.LazyButton.interactable)
            parts.Add(Loc.Fmt("craft.key_action", StartKeyName(GameKey.SubmitBait), TmpText.Clean(LLBase.L("ui_submit_bait"))));
        if (data.BaitItems.Count > 1) parts.Add(Loc.Get("station.bait_hint"));
        return Join(parts.ToArray());
    }

    private static string DescribeTown(UITownBuildingWindow window)
    {
        var data = G.TownData(window);
        if (data == null) return null;

        var parts = new List<string> { Header(data.AssignedWgo?.Data) };
        var count = data.BuildsToDisplay?.Count ?? 0;
        parts.Add(count == 0
            ? TmpText.Clean(LLBase.L("ui_builddesk_town_is_empty"))
            : Loc.Fmt("station.town_count", count));
        return Join(parts.ToArray());
    }

    /// <summary>What to say for a focused control in one of these windows, or null for the generic reading.</summary>
    internal static string DescribeWidget(GamepadNavigationItem item)
    {
        if (item == null || !Ready) return null;
        try
        {
            var porter = item.GetComponentInParent<UIPorterStationItemCell>();
            if (porter != null) return DescribePorterCell(porter);

            var building = item.GetComponent<UITownBuildingWidget>();
            if (building != null) return DescribeTownBuilding(building);

            var window = LazyWindowsStackController.ActiveWindow;
            var cell = item.GetComponent<UIItemCell>() ?? item.GetComponentInParent<UIItemCell>();
            switch (window)
            {
                case UIResurrectionWindow resurrection when cell != null && cell == G.ResurrectionCollar(resurrection):
                {
                    var data = G.ResurrectionData(resurrection);
                    return data == null ? null : CollarLine(data);
                }
                case UIPrayWindow pray when cell != null && cell == G.PraySlot(pray)?.UIItemCell:
                {
                    var sermon = G.PrayData(pray)?.SermonDef;
                    return sermon == null ? Loc.Get("station.pray_no_sermon") : Loc.Fmt("station.pray_sermon", ItemText.Name(sermon.id));
                }
            }
        }
        catch (Exception ex)
        {
            Plugin.Log?.LogWarning($"[Station] Could not describe '{item.name}': {ex.GetType().Name}: {ex.Message}");
        }
        return null;
    }

    /// <summary>A building on the town desk: name, description, and every material as "have of need".</summary>
    private static string DescribeTownBuilding(UITownBuildingWidget widget)
    {
        var data = G.TownWidgetData(widget);
        if (data == null) return null;

        var parts = new List<string> { TmpText.Clean(LLBase.L(data.Name)), TmpText.Clean(data.Description) };
        var needs = new List<string>();
        var enough = true;
        foreach (var cell in data.CraftItemCellsData ?? new List<UICraftItemCellData>())
        {
            var need = cell?.currentItem;
            if (need == null) continue;
            var count = need.GetCount(cell.WgoData);
            var have = cell.MultiInventory?.GetTotalCount(need.Id) ?? 0;
            if (have < count) enough = false;
            needs.Add(Loc.Fmt("tooltip.need_have", have, count, ItemText.Name(need.Id)));
        }
        if (needs.Count > 0) parts.Add(Loc.Fmt("tooltip.needs", string.Join("; ", needs)));
        if (!enough) parts.Add(Loc.Get("craft.status.NotEnoughResources"));
        return Join(parts.ToArray());
    }

    // ---- keys -------------------------------------------------------------------------------

    /// <summary>Enter on the sermon report: its OK button listens for GameKey.Select, which has no keyboard key.</summary>
    internal static bool TryConfirm()
    {
        if (!Ready || !(LazyWindowsStackController.ActiveWindow is UIPrayReportWindow report)) return false;
        try
        {
            report.Close();
        }
        catch (Exception ex)
        {
            Plugin.Log?.LogError($"[Station] Closing the sermon report failed: {ex.Message}");
        }
        return true;
    }

    /// <summary>The start key, the name roll, and the zombie window's tabs. True when handled.</summary>
    internal static bool TryKeys()
    {
        if (!Ready) return false;
        var window = LazyWindowsStackController.ActiveWindow;
        if (!IsStationWindow(window)) return false;

        try
        {
            var ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);

            if (CraftReader.StartKey.IsDown())
            {
                switch (window)
                {
                    case UIResurrectionWindow resurrection:
                    {
                        var data = G.ResurrectionData(resurrection);
                        if (data == null || data.IsEmpty || data.CanStartResurrection == null || !data.CanStartResurrection())
                        {
                            Say(data == null || data.IsEmpty ? Loc.Get("body.table_empty") : ResurrectionReadiness(data));
                            return true;
                        }
                        if (GameKeys.GameHandles(GameKey.StartResurrection, CraftReader.StartKey)) return true;
                        Say(Loc.Fmt("station.resurrecting", TmpText.Clean(LLBase.L(data.ZombieName))), holdFocus: true);
                        Invoke(G.ResurrectionStart, resurrection);
                        return true;
                    }
                    case UIPrayWindow pray:
                    {
                        var start = G.PrayStartButton(pray);
                        if (start == null || !start.interactable)
                        {
                            Say(DescribePray(pray));
                            return true;
                        }
                        if (GameKeys.GameHandles(GameKey.PrayStart, CraftReader.StartKey)) return true;
                        Say(TmpText.Clean(LLBase.L("ui_pray_start")), holdFocus: true);
                        Invoke(G.PrayStart, pray);
                        return true;
                    }
                    case UIFishingWindow fishing when fishing.IsBaitSelectionVisible:
                    {
                        var submit = G.FishingSubmit(fishing);
                        if (submit?.LazyButton == null || !submit.LazyButton.interactable)
                        {
                            Say(Loc.Get("station.cannot_cast"));
                            return true;
                        }
                        if (GameKeys.GameHandles(GameKey.SubmitBait, CraftReader.StartKey)) return true;
                        Invoke(G.FishingCast, fishing);
                        return true;
                    }
                }
                return false;
            }

            if (_rollNameKey.Value.IsDown() && window is UIResurrectionWindow roll)
            {
                var data = G.ResurrectionData(roll);
                if (data == null || data.IsEmpty) return true;
                if (GameKeys.GameHandles(GameKey.ZombieRollName, _rollNameKey.Value)) return true;
                Invoke(G.ResurrectionRoll, roll);
                return true;
            }

            if (ctrl && window is UIZombieWorkerWindow zombie &&
                (Input.GetKeyDown(KeyCode.LeftArrow) || Input.GetKeyDown(KeyCode.RightArrow)))
            {
                Invoke(Input.GetKeyDown(KeyCode.RightArrow) ? G.ZombieNextTab : G.ZombiePrevTab, zombie);
                ScreenReader.ClearMenuContext();
                Say(ZombieTabName(zombie));
                UiNarrator.QueueFocusUntil = Time.unscaledTime + 0.6f;
                return true;
            }
        }
        catch (Exception ex)
        {
            Plugin.Log?.LogError($"[Station] Key failed: {ex.GetType().Name}: {ex.Message}");
            return true;
        }
        return false;
    }

    /// <summary>Left and right choose the bait, as the game's tab keys do there.</summary>
    internal static bool TryArrow(GUIDirection direction)
    {
        if (!Ready || !(LazyWindowsStackController.ActiveWindow is UIFishingWindow fishing)) return false;
        if (!fishing.IsBaitSelectionVisible) return false;
        if (direction != GUIDirection.Left && direction != GUIDirection.Right) return false;

        try
        {
            var moved = G.FishingSelect != null &&
                        (bool)G.FishingSelect.Invoke(fishing, new object[] { direction == GUIDirection.Left });
            if (!moved) Say(Loc.Get("station.bait_end"));
        }
        catch (Exception ex)
        {
            Plugin.Log?.LogError($"[Station] Choosing bait failed: {ex.Message}");
        }
        return true;
    }

    internal static bool TryRepeat()
    {
        if (!Ready) return false;
        var window = LazyWindowsStackController.ActiveWindow;
        string text = null;
        try
        {
            text = window switch
            {
                UIResurrectionWindow w => DescribeResurrection(w),
                UIPrayWindow w => DescribePray(w),
                UIPrayReportWindow w => DescribeReport(w),
                UIPorterStationWindow w => DescribePorter(w),
                UIZombieWorkerWindow w => DescribeZombie(w),
                UIFishingWindow w when w.IsBaitSelectionVisible => DescribeBait(w),
                UIFishingWindow => FishingStatus(),
                UITownBuildingWindow w => DescribeTown(w),
                _ => null,
            };
        }
        catch (Exception ex)
        {
            Plugin.Log?.LogError($"[Station] Could not read the window again: {ex.Message}");
        }
        if (string.IsNullOrEmpty(text)) return false;
        ScreenReader.Say(text);
        if (!string.IsNullOrEmpty(UiNarrator.LastFocusLabel)) ScreenReader.Say(UiNarrator.LastFocusLabel, interrupt: false);
        return true;
    }

    // ---- the fishing minigame ---------------------------------------------------------------

    private static FishingMiniGame.Stage _lastStage = FishingMiniGame.Stage.Finish;
    private static bool _wasActive;
    private static bool _wasResisting;
    private static bool _tightWarned;
    private static int _lastProgressStep;
    private static float _lastProgress;
    private static float _lastTension;

    /// <summary>Polled every frame from the plugin. Returns at once when no one is fishing.</summary>
    internal static void UpdateFishing()
    {
        if (!Ready || _fishingCues == null || !_fishingCues.Value) return;

        FishingMiniGame game;
        try
        {
            var player = MainGame.PlayerController;
            var component = player == null ? null : player.FishingComponent;
            game = component == null ? null : G.MiniGame(component);
        }
        catch
        {
            return;
        }

        var active = game != null && game.IsActive;
        if (!active)
        {
            if (_wasActive) FishingOver();
            _wasActive = false;
            return;
        }

        if (!_wasActive)
        {
            _wasActive = true;
            _lastStage = FishingMiniGame.Stage.Start;
            _wasResisting = false;
            _tightWarned = false;
            _lastProgressStep = 0;
        }

        var stage = game.CurrentStage;
        if (stage != _lastStage)
        {
            _lastStage = stage;
            switch (stage)
            {
                case FishingMiniGame.Stage.WaitingForBite:
                    Say(Loc.Get("station.fish_wait"));
                    break;
                case FishingMiniGame.Stage.Biting:
                    Say(Loc.Get("station.fish_bite"));
                    break;
                case FishingMiniGame.Stage.PlayingWithFish:
                    Say(Loc.Get("station.fish_hooked"));
                    break;
            }
        }

        if (stage != FishingMiniGame.Stage.PlayingWithFish) return;

        _lastProgress = game.progress;
        _lastTension = game.tension;

        // The fish fighting is the one thing that must be acted on at once: keep reeling through it
        // and the line snaps.
        var resisting = game.IsFishResisting;
        if (resisting != _wasResisting)
        {
            _wasResisting = resisting;
            Say(Loc.Get(resisting ? "station.fish_fights" : "station.fish_reel"));
            return;
        }

        if (!_tightWarned && game.tension >= 70f)
        {
            _tightWarned = true;
            Say(Loc.Get("station.fish_tight"));
        }
        else if (_tightWarned && game.tension < 45f)
        {
            _tightWarned = false;
        }

        // Progress runs from -100 (gone) to 100 (caught); say each quarter towards the catch, and
        // warn once when it is slipping away.
        var step = game.progress >= 75f ? 3 : game.progress >= 50f ? 2 : game.progress >= 25f ? 1 : game.progress <= -50f ? -1 : 0;
        if (step != _lastProgressStep)
        {
            var rising = step > _lastProgressStep;
            _lastProgressStep = step;
            if (step > 0 && rising) ScreenReader.Say(Loc.Fmt("station.fish_progress", step * 25), interrupt: false);
            else if (step < 0) ScreenReader.Say(Loc.Get("station.fish_slipping"), interrupt: false);
        }
    }

    private static void FishingOver()
    {
        if (_lastStage != FishingMiniGame.Stage.PlayingWithFish) return;
        if (_lastProgress >= 95f) Say(Loc.Get("station.fish_caught"));
        else if (_lastTension >= 95f) Say(Loc.Get("station.fish_line_broke"));

        // The bait window comes back after a catch; read it again even though nothing in it changed.
        _current = null;
    }

    private static string FishingStatus()
    {
        var game = G.MiniGame(MainGame.PlayerController.FishingComponent);
        if (game == null || !game.IsActive) return null;
        return Loc.Fmt("station.fish_status", Mathf.RoundToInt(game.progress), Mathf.RoundToInt(game.tension));
    }

    // ---- shared -----------------------------------------------------------------------------

    private static string StartKeyName(GameKey key) => GameKeys.Name(key) ?? GameKeys.Name(CraftReader.StartKey);

    private static string Header(WgoData wgo)
    {
        if (wgo == null) return null;
        var name = TmpText.Clean(LLBase.L(wgo.id));
        return string.IsNullOrWhiteSpace(name) || name == wgo.id ? Navigator.Humanise(wgo.id) : name;
    }

    private static string Join(params string[] parts) =>
        string.Join(". ", parts.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p.Trim().TrimEnd('.')));

    private static void Say(string text, bool holdFocus = false)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        Plugin.Log?.LogInfo($"[Station] \"{text}\"");
        ScreenReader.Say(text);
        if (holdFocus) UiNarrator.QueueFocusUntil = Time.unscaledTime + 1f;
    }

    private static void Invoke(MethodInfo method, object target, params object[] args)
    {
        if (method == null)
        {
            Plugin.Log?.LogWarning("[Station] A station method was not found in this game version.");
            return;
        }
        method.Invoke(target, args.Length == 0 ? null : args);
    }

    private static void Guard(string what, Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            Plugin.Log?.LogError($"[Station] Reading the {what} failed: {ex.GetType().Name}: {ex.Message}");
        }
    }
}
