using TMPro;

namespace GraveyardKeeper2Accessibility;

/// <summary>
/// Every crafting station, in one place: workbenches and furnaces (the recipe list and its queue),
/// the recipe setup window, single-recipe stations (repairs, one-off builds), fuel stations, the
/// alchemy table, the study table and garden beds.
///
/// <para>
/// <b>What was wrong before this.</b> Recipe cells are drawn with <c>UIItemCell.DrawCraftOutput</c>,
/// which fills <c>DisplayingOutputPreview</c> and never touches <c>DisplayingItem</c>. The generic
/// item-cell reading looked only at <c>DisplayingItem</c>, so every recipe on every workbench read as
/// "empty slot" - or, worse, as whatever item a pooled cell happened to show last. The same goes for
/// queue entries and the output cell of the setup window. Each is now named from its recipe.
/// </para>
///
/// <para>
/// <b>What a station says.</b> Each window is read once from its data when it is drawn, the way the
/// autopsy table is: station and recipe count and queue on a workbench; recipe, output, every
/// ingredient as "have of need", energy and insanity cost, mastery, and whether it can be made - and
/// why not - in the setup window. Changes under a stationary focus (amount, ingredient kind, queue
/// amount, alchemy result) get their own hooks, as every such widget in this game does.
/// </para>
///
/// <para>
/// <b>Keys.</b> The game gives these windows their own <c>GameKey</c>s. Only one of their keyboard keys
/// is known for certain (StartCraft is F). For the rest the mod asks the running game
/// (<see cref="GameKeys"/>) and acts itself only when the game does not already react to the same key.
/// The arrow keys are routed the way W/S/A/D already are in-game: up/down change the amount on the
/// output cell and switch the kind of an ingredient; left/right change the alchemy amount.
/// </para>
/// </summary>
internal static class CraftReader
{
    private static ConfigEntry<KeyboardShortcut> _startKey;

    /// <summary>The one "do it" key for every station, shared with <see cref="StationsReader"/>.</summary>
    internal static KeyboardShortcut StartKey => _startKey?.Value ?? new KeyboardShortcut(KeyCode.F);
    private static ConfigEntry<KeyboardShortcut> _queueKey;
    private static ConfigEntry<KeyboardShortcut> _zoneKey;

    /// <summary>The last window summary spoken, so a redraw of an unchanged window is not read twice.</summary>
    private static string _current;

    /// <summary>The last amount line, so a count that did not move (at a limit) is not repeated by the postfix.</summary>
    private static string _lastCountLine;

    /// <summary>The last alchemy state, split so a count change says only the count.</summary>
    private static string _lastAlchemyKey;
    private static int _lastAlchemyCount = -1;

    /// <summary>The recipe group focus was last in, so the group is named only on crossing.</summary>
    private static UICraftsTabWidget _lastTab;

    // ---- game fields ------------------------------------------------------------------------

    /// <summary>
    /// The game's private fields and methods this reader uses, looked up once. Kept in a class of
    /// their own so that a game update renaming one fails <i>here</i>, where <see cref="Init"/>
    /// catches it and switches the reader off - rather than in a static initialiser of the reader
    /// itself, which would throw from every call site, key handling included.
    /// </summary>
    private static class G
    {
        internal static readonly AccessTools.FieldRef<LazyWidget<UIBaseCraftSelectionWindowData>, UIBaseCraftSelectionWindowData> SelectionData =
            AccessTools.FieldRefAccess<LazyWidget<UIBaseCraftSelectionWindowData>, UIBaseCraftSelectionWindowData>("data");

        internal static readonly AccessTools.FieldRef<UIBaseCraftSelectionWindow, UICraftSelectionOutputItemCell> SelectionOutput =
            AccessTools.FieldRefAccess<UIBaseCraftSelectionWindow, UICraftSelectionOutputItemCell>("outputItem");

        internal static readonly AccessTools.FieldRef<UIBaseCraftSelectionWindow, TextMeshProUGUI> SelectionHeader =
            AccessTools.FieldRefAccess<UIBaseCraftSelectionWindow, TextMeshProUGUI>("headerLabel");

        internal static readonly AccessTools.FieldRef<UIBaseCraftSelectionWindow, LazyButton> SelectionStartButton =
            AccessTools.FieldRefAccess<UIBaseCraftSelectionWindow, LazyButton>("startCraftButton");

        internal static readonly AccessTools.FieldRef<UIBaseCraftSelectionWindow, TextMeshProUGUI> SelectionStartText =
            AccessTools.FieldRefAccess<UIBaseCraftSelectionWindow, TextMeshProUGUI>("startCraftButtonText");

        internal static readonly AccessTools.FieldRef<UIBaseCraftSelectionWindow, LazyButton> SelectionPlus =
            AccessTools.FieldRefAccess<UIBaseCraftSelectionWindow, LazyButton>("plusCraftButton");

        internal static readonly AccessTools.FieldRef<UIBaseCraftSelectionWindow, List<UICraftItemCell>> SelectionIngredients =
            AccessTools.FieldRefAccess<UIBaseCraftSelectionWindow, List<UICraftItemCell>>("displayedIngredients");

        internal static readonly AccessTools.FieldRef<UICraftSelectionWindow, LazyButton> QueueButton =
            AccessTools.FieldRefAccess<UICraftSelectionWindow, LazyButton>("addToQueueButton");

        internal static readonly AccessTools.FieldRef<UICraftSelectionWindow, TextMeshProUGUI> QueueButtonText =
            AccessTools.FieldRefAccess<UICraftSelectionWindow, TextMeshProUGUI>("addToQueueButtonText");

        internal static readonly AccessTools.FieldRef<UIFuelCraftWindow, TextMeshProUGUI> FuelDescription =
            AccessTools.FieldRefAccess<UIFuelCraftWindow, TextMeshProUGUI>("descriptionLabel");

        internal static readonly AccessTools.FieldRef<UICraftItemCell, UICraftItemCellData> IngredientData =
            AccessTools.FieldRefAccess<UICraftItemCell, UICraftItemCellData>("data");

        internal static readonly AccessTools.FieldRef<UICraftItemCell, int> IngredientMultiplier =
            AccessTools.FieldRefAccess<UICraftItemCell, int>("currentMultiplier");

        internal static readonly AccessTools.FieldRef<UICraftWindow, List<UICraftsTabWidget>> DisplayedTabs =
            AccessTools.FieldRefAccess<UICraftWindow, List<UICraftsTabWidget>>("displayedTabs");

        internal static readonly AccessTools.FieldRef<UICraftWindow, bool> QueueSelected =
            AccessTools.FieldRefAccess<UICraftWindow, bool>("isQueueSelectedOnGamepad");

        internal static readonly AccessTools.FieldRef<LazyWidget<UICraftPreviewItemCellData>, UICraftPreviewItemCellData> PreviewData =
            AccessTools.FieldRefAccess<LazyWidget<UICraftPreviewItemCellData>, UICraftPreviewItemCellData>("data");

        internal static readonly AccessTools.FieldRef<LazyWidget<UICraftsTabWidgetData>, UICraftsTabWidgetData> TabData =
            AccessTools.FieldRefAccess<LazyWidget<UICraftsTabWidgetData>, UICraftsTabWidgetData>("data");

        internal static readonly AccessTools.FieldRef<LazyWidget<UIAlchemyWindowData>, UIAlchemyWindowData> AlchemyData =
            AccessTools.FieldRefAccess<LazyWidget<UIAlchemyWindowData>, UIAlchemyWindowData>("data");

        internal static readonly AccessTools.FieldRef<UIAlchemyWindow, UIItemCell> AlchemyResult =
            AccessTools.FieldRefAccess<UIAlchemyWindow, UIItemCell>("result");

        internal static readonly AccessTools.FieldRef<UIAlchemyWindow, UIItemCell> AlchemyBoostCell =
            AccessTools.FieldRefAccess<UIAlchemyWindow, UIItemCell>("boostItemCell");

        internal static readonly AccessTools.FieldRef<UIAlchemyWindow, LazyButton> AlchemyCreate =
            AccessTools.FieldRefAccess<UIAlchemyWindow, LazyButton>("createBtn");

        internal static readonly AccessTools.FieldRef<UIAlchemyWindow, List<UIAlchemyIngredient>> AlchemyIngredients =
            AccessTools.FieldRefAccess<UIAlchemyWindow, List<UIAlchemyIngredient>>("ingredients");

        internal static readonly AccessTools.FieldRef<UIAlchemyWindow, int> AlchemyCountRef =
            AccessTools.FieldRefAccess<UIAlchemyWindow, int>("craftCount");

        internal static readonly AccessTools.FieldRef<UIAlchemyWindow, string> AlchemyMixId =
            AccessTools.FieldRefAccess<UIAlchemyWindow, string>("mixCraftId");

        internal static readonly AccessTools.FieldRef<UIAlchemyWindow, CraftElement> AlchemyBoost =
            AccessTools.FieldRefAccess<UIAlchemyWindow, CraftElement>("boostCraftElement");

        internal static readonly AccessTools.FieldRef<LazyWidget<UIResourceBasedCraftWindowData>, UIResourceBasedCraftWindowData> SurveyData =
            AccessTools.FieldRefAccess<LazyWidget<UIResourceBasedCraftWindowData>, UIResourceBasedCraftWindowData>("data");

        internal static readonly AccessTools.FieldRef<UIResourceBasedCraftWindow, UIItemCell> SurveyMain =
            AccessTools.FieldRefAccess<UIResourceBasedCraftWindow, UIItemCell>("mainIngredient");

        internal static readonly AccessTools.FieldRef<UIResourceBasedCraftWindow, LazyButton> SurveyButton =
            AccessTools.FieldRefAccess<UIResourceBasedCraftWindow, LazyButton>("craftBtn");

        internal static readonly AccessTools.FieldRef<LazyWidget<UIGardenBedWindowData>, UIGardenBedWindowData> GardenData =
            AccessTools.FieldRefAccess<LazyWidget<UIGardenBedWindowData>, UIGardenBedWindowData>("data");

        internal static readonly AccessTools.FieldRef<UIGardenBedWindow, UIItemCell> GardenSeedCell =
            AccessTools.FieldRefAccess<UIGardenBedWindow, UIItemCell>("seedItemCell");

        internal static readonly AccessTools.FieldRef<UIGardenBedWindow, Item> GardenSeed =
            AccessTools.FieldRefAccess<UIGardenBedWindow, Item>("selectedSeed");

        internal static readonly AccessTools.FieldRef<UIGardenBedWindow, CraftDefBase> GardenSeedCraft =
            AccessTools.FieldRefAccess<UIGardenBedWindow, CraftDefBase>("selectedSeedCraft");

        internal static readonly AccessTools.FieldRef<UIItemCell, bool> CellIsNeedItem =
            AccessTools.FieldRefAccess<UIItemCell, bool>("isNeedItem");

        internal static readonly MethodInfo SelectionChangeCount = AccessTools.Method(typeof(UIBaseCraftSelectionWindow), "ChangeCraftCount");
        internal static readonly MethodInfo SelectionStart = AccessTools.Method(typeof(UIBaseCraftSelectionWindow), "OnStartCraft");
        internal static readonly MethodInfo SelectionQueue = AccessTools.Method(typeof(UICraftSelectionWindow), "OnAddToQueue");
        internal static readonly MethodInfo CraftSwitchZone = AccessTools.Method(typeof(UICraftWindow), "OnSwitchZonePressed");
        internal static readonly MethodInfo CraftMoveQueue = AccessTools.Method(typeof(UICraftWindow), "TryMoveFocusedQueueItem");
        internal static readonly MethodInfo AlchemyChangeCount = AccessTools.Method(typeof(UIAlchemyWindow), "ChangeCraftCount");
        internal static readonly MethodInfo AlchemyStart = AccessTools.Method(typeof(UIAlchemyWindow), "OnStartAlchemyPressed");
        internal static readonly MethodInfo SurveyStart = AccessTools.Method(typeof(UIResourceBasedCraftWindow), "OnStartSurveyPressed");
        internal static readonly MethodInfo GardenPlant = AccessTools.Method(typeof(UIGardenBedWindow), "OnPlantButtonPress");
        internal static readonly MethodInfo GardenCanPlant = AccessTools.Method(typeof(UIGardenBedWindow), "CanPlantSelectedSeed");
    }

    /// <summary>False when the game's fields could not be found; every entry point then stays out of the way.</summary>
    internal static bool Ready { get; private set; }

    internal static void Init(ConfigFile config)
    {
        try
        {
            System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(typeof(G).TypeHandle);
            Ready = true;
        }
        catch (Exception ex)
        {
            Plugin.Log?.LogError($"[Craft] Crafting stations are not supported by this game version: " +
                                 $"{(ex.InnerException ?? ex).GetType().Name}: {(ex.InnerException ?? ex).Message}");
        }

        _startKey = ModKeys.Bind(config,
            "Crafting", "Start", new KeyboardShortcut(KeyCode.F),
            "At a crafting station: make it - craft, place fuel, mix at the alchemy table, study at " +
            "the study table, plant at a garden bed. Where the game already starts on this key (it " +
            "does for crafting), the game's own key is left to do it.", sharedOnPurpose: true);
        _queueKey = ModKeys.Bind(config,
            "Crafting", "AddToQueue", new KeyboardShortcut(KeyCode.Space),
            "In a recipe's setup window: add it to the station's queue instead of starting it. Only " +
            "acts if the game does not already use this key for that.");
        _zoneKey = ModKeys.Bind(config,
            "Crafting", "SwitchToQueue", new KeyboardShortcut(KeyCode.F6),
            "At a workbench or furnace: jumps between the recipe list and the queue. Only acts if the " +
            "game does not already use this key for that.");
    }

    /// <summary>
    /// Forgets the last summary, so the same window opened again is read again. The alchemy state is
    /// kept on purpose: the ingredient picker is a window of its own opening over the table, and the
    /// ingredient it adds is exactly the change that must be spoken when it closes.
    /// </summary>
    internal static void Reset()
    {
        _current = null;
        _lastCountLine = null;
        _lastTab = null;
    }

    /// <summary>True for every window this reader speaks in full, so the generic open/close line stays quiet.</summary>
    internal static bool IsStationWindow(LazyWidgetBase window) =>
        Ready && (window is UICraftWindow || window is UIBaseCraftSelectionWindow || window is UIAlchemyWindow ||
        window is UIResourceBasedCraftWindow || window is UIGardenBedWindow);

    /// <summary>True for windows whose slots are filled from a picker, where "move" really means "take back out".</summary>
    internal static bool IsSlotWindow(LazyWidgetBase window) =>
        window is UIAlchemyWindow || window is UIResourceBasedCraftWindow || window is UIGardenBedWindow;

    // ---- summaries --------------------------------------------------------------------------

    private static void SpeakSummary(LazyWidgetBase window, string text)
    {
        if (string.IsNullOrEmpty(text) || text == _current) return;
        _current = text;
        Plugin.Log?.LogInfo($"[Craft] \"{text}\"");

        // The window focuses its first control while it draws, usually before this postfix runs, so
        // interrupting cuts off only that control's name - which is then said again after the
        // summary. A focus event that arrives a moment later is queued rather than cutting the
        // summary off (UiNarrator.QueueFocusUntil).
        ScreenReader.Say(text);
        var focused = UiNarrator.FocusedItem;
        if (focused != null && window != null && focused.transform.IsChildOf(window.transform) &&
            !string.IsNullOrEmpty(UiNarrator.LastFocusLabel))
            ScreenReader.Say(UiNarrator.LastFocusLabel, interrupt: false);
        UiNarrator.QueueFocusUntil = Time.unscaledTime + 0.6f;
    }

    [HarmonyPatch(typeof(UICraftWindow), nameof(UICraftWindow.Redraw))]
    [HarmonyPostfix]
    private static void UICraftWindow_Redraw(UICraftWindow __instance) =>
        Guard("workbench", () => SpeakSummary(__instance, DescribeWorkbench(__instance)));

    [HarmonyPatch(typeof(UICraftSelectionWindow), nameof(UICraftSelectionWindow.Redraw))]
    [HarmonyPostfix]
    private static void UICraftSelectionWindow_Redraw(UICraftSelectionWindow __instance) => SelectionRedrawn(__instance);

    [HarmonyPatch(typeof(UISingleCraftWindow), nameof(UISingleCraftWindow.Redraw))]
    [HarmonyPostfix]
    private static void UISingleCraftWindow_Redraw(UISingleCraftWindow __instance) => SelectionRedrawn(__instance);

    [HarmonyPatch(typeof(UIFuelCraftWindow), nameof(UIFuelCraftWindow.Redraw))]
    [HarmonyPostfix]
    private static void UIFuelCraftWindow_Redraw(UIFuelCraftWindow __instance) => SelectionRedrawn(__instance);

    private static void SelectionRedrawn(UIBaseCraftSelectionWindow window) =>
        Guard("setup", () =>
        {
            _lastCountLine = CountLine(window);
            SpeakSummary(window, DescribeSelection(window));
        });

    [HarmonyPatch(typeof(UIAlchemyWindow), "DisplayAlchemyTab")]
    [HarmonyPrefix]
    private static void UIAlchemyWindow_DisplayAlchemyTab_Prefix() => _lastAlchemyKey = null;

    [HarmonyPatch(typeof(UIAlchemyWindow), "DisplayAlchemyTab")]
    [HarmonyPostfix]
    private static void UIAlchemyWindow_DisplayAlchemyTab(UIAlchemyWindow __instance) =>
        Guard("alchemy", () =>
        {
            _lastAlchemyKey = AlchemyKey(__instance);
            _lastAlchemyCount = AlchemyAmount(__instance);
            var header = Header(G.AlchemyData(__instance)?.Wgo?.Data);
            var slots = G.AlchemyIngredients(__instance)?.Count(i => i != null && i.gameObject.activeSelf) ?? 0;
            SpeakSummary(__instance, Join(header, Loc.Fmt("craft.alchemy_open", slots), AlchemyState(__instance)));
        });

    [HarmonyPatch(typeof(UIAlchemyWindow), "RedrawAlchemyTabLite")]
    [HarmonyPostfix]
    private static void UIAlchemyWindow_RedrawAlchemyTabLite(UIAlchemyWindow __instance) =>
        Guard("alchemy", () =>
        {
            var key = AlchemyKey(__instance);
            var count = AlchemyAmount(__instance);

            // DisplayAlchemyTab calls this while opening; its own postfix reads the whole window.
            if (_lastAlchemyKey == null)
            {
                _lastAlchemyKey = key;
                _lastAlchemyCount = count;
                return;
            }
            if (key != _lastAlchemyKey)
            {
                _lastAlchemyKey = key;
                _lastAlchemyCount = count;
                Say(AlchemyState(__instance));
            }
            else if (count != _lastAlchemyCount)
            {
                _lastAlchemyCount = count;
                Say(AlchemyCountLine(__instance));
            }
        });

    [HarmonyPatch(typeof(UIResourceBasedCraftWindow), nameof(UIResourceBasedCraftWindow.Redraw))]
    [HarmonyPostfix]
    private static void UIResourceBasedCraftWindow_Redraw(UIResourceBasedCraftWindow __instance) =>
        Guard("study", () => SpeakSummary(__instance, DescribeSurvey(__instance)));

    [HarmonyPatch(typeof(UIGardenBedWindow), nameof(UIGardenBedWindow.Redraw))]
    [HarmonyPostfix]
    private static void UIGardenBedWindow_Redraw(UIGardenBedWindow __instance) =>
        Guard("garden", () => SpeakSummary(__instance, DescribeGarden(__instance)));

    [HarmonyPatch(typeof(UIGardenBedWindow), "OnSeedSelected")]
    [HarmonyPostfix]
    private static void UIGardenBedWindow_OnSeedSelected(UIGardenBedWindow __instance) =>
        Guard("garden", () => Say(GardenSeedLine(__instance), holdFocus: true));

    [HarmonyPatch(typeof(UIGardenBedWindow), "OnSeedItemCellPress2")]
    [HarmonyPostfix]
    private static void UIGardenBedWindow_OnSeedItemCellPress2(UIGardenBedWindow __instance) =>
        Guard("garden", () => Say(GardenSeedLine(__instance)));

    // ---- changes under a stationary focus ---------------------------------------------------

    [HarmonyPatch(typeof(UIBaseCraftSelectionWindow), "ChangeCraftCount")]
    [HarmonyPostfix]
    private static void UIBaseCraftSelectionWindow_ChangeCraftCount(UIBaseCraftSelectionWindow __instance) => CountChanged(__instance);

    [HarmonyPatch(typeof(UIFuelCraftWindow), "ChangeCraftCount")]
    [HarmonyPostfix]
    private static void UIFuelCraftWindow_ChangeCraftCount(UIFuelCraftWindow __instance) => CountChanged(__instance);

    private static void CountChanged(UIBaseCraftSelectionWindow window) =>
        Guard("amount", () =>
        {
            var line = CountLine(window);
            if (line == _lastCountLine) return;
            _lastCountLine = line;
            Say(line);
        });

    [HarmonyPatch(typeof(UICraftItemCell), "OnNextItem")]
    [HarmonyPostfix]
    private static void UICraftItemCell_OnNextItem(UICraftItemCell __instance) =>
        Guard("ingredient", () => Say(IngredientText(__instance)));

    [HarmonyPatch(typeof(UICraftItemCell), "OnPrevItem")]
    [HarmonyPostfix]
    private static void UICraftItemCell_OnPrevItem(UICraftItemCell __instance) =>
        Guard("ingredient", () => Say(IngredientText(__instance)));

    [HarmonyPatch(typeof(UICraftQueueElementWidget), nameof(UICraftQueueElementWidget.ChangeCount))]
    [HarmonyPostfix]
    private static void UICraftQueueElementWidget_ChangeCount(UICraftQueueElementWidget __instance) =>
        Guard("queue amount", () =>
        {
            var element = __instance.Data?.CraftQueueElement;
            if (element == null) return;
            Say(element.Count <= 0 ? Loc.Get("craft.queue_removed") : Loc.Fmt("craft.amount", element.Count));
        });

    [HarmonyPatch(typeof(UIBaseCraftSelectionWindow), "OnStartCraftPressed")]
    [HarmonyPrefix]
    private static void UIBaseCraftSelectionWindow_OnStartCraftPressed(UIBaseCraftSelectionWindow __instance) =>
        Guard("start", () => Confirm(__instance, G.SelectionStartText(__instance)));

    [HarmonyPatch(typeof(UISingleCraftWindow), "OnStartCraftPressed")]
    [HarmonyPrefix]
    private static void UISingleCraftWindow_OnStartCraftPressed(UISingleCraftWindow __instance) =>
        Guard("start", () => Confirm(__instance, G.SelectionStartText(__instance)));

    [HarmonyPatch(typeof(UICraftSelectionWindow), "OnAddToQueuePressed")]
    [HarmonyPrefix]
    private static void UICraftSelectionWindow_OnAddToQueuePressed(UICraftSelectionWindow __instance) =>
        Guard("queue", () => Confirm(__instance, G.QueueButtonText(__instance)));

    /// <summary>
    /// "Craft: 3 × plank" - said as the window closes, in a prefix because closing clears the data.
    /// The list window underneath re-focuses its cell a moment later; that is queued behind this.
    /// </summary>
    private static void Confirm(UIBaseCraftSelectionWindow window, TextMeshProUGUI label)
    {
        var data = G.SelectionData(window);
        if (data?.CraftDefinition == null) return;

        var action = CleanLabel(label) ?? Loc.Get("craft.done");
        var count = Math.Max(data.CraftsCount, 0);
        Say(Loc.Fmt("craft.confirmed", action, count, RecipeName(data.CraftDefinition, data.WgoData)), holdFocus: true);
    }

    // ---- descriptions of focused controls ---------------------------------------------------

    /// <summary>
    /// What to say for a focused control in a station window, or null to let the generic reading
    /// handle it. Called from <see cref="UiNarrator"/> before the item-cell reading, because these
    /// cells are exactly the ones it gets wrong.
    /// </summary>
    internal static string DescribeWidget(GamepadNavigationItem item)
    {
        if (item == null || !Ready) return null;
        try
        {
            var preview = item.GetComponentInParent<UICraftPreviewItemCell>();
            if (preview != null) return WithTab(preview, DescribePreview(preview));

            var queued = item.GetComponentInParent<UICraftQueueElementWidget>();
            if (queued != null) return DescribeQueueElement(queued);

            var output = item.GetComponentInParent<UICraftSelectionOutputItemCell>();
            if (output != null) return DescribeOutputCell(output);

            var ingredient = item.GetComponent<UICraftItemCell>() ?? item.GetComponentInParent<UICraftItemCell>();
            if (ingredient != null && G.IngredientData(ingredient) != null) return IngredientText(ingredient);

            var window = LazyWindowsStackController.ActiveWindow;
            var cell = item.GetComponent<UIItemCell>() ?? item.GetComponentInParent<UIItemCell>();

            switch (window)
            {
                case UIAlchemyWindow alchemy when cell != null:
                    return DescribeAlchemyCell(alchemy, cell);
                case UIResourceBasedCraftWindow survey:
                    if (cell != null && cell == G.SurveyMain(survey)) return DescribeSurveyMain(survey);
                    var button = G.SurveyButton(survey);
                    if (button != null && item.transform.IsChildOf(button.transform) && !button.interactable)
                        return Loc.Fmt("craft.unavailable", CleanLabel(item.GetComponentInChildren<TMP_Text>()) ?? "");
                    break;
                case UIGardenBedWindow garden when cell != null && cell == G.GardenSeedCell(garden):
                    return GardenSeedLine(garden);
            }
        }
        catch (Exception ex)
        {
            Plugin.Log?.LogWarning($"[Craft] Could not describe '{item.name}': {ex.GetType().Name}: {ex.Message}");
        }
        return null;
    }

    /// <summary>
    /// "3 of 5 planks" for any item cell drawn as a requirement - the game shows that pair as a
    /// "3/5" label tinted red when short. Null for an ordinary cell.
    /// </summary>
    internal static string NeedText(UIItemCell cell, string name)
    {
        try
        {
            if (!Ready || cell == null || !G.CellIsNeedItem(cell) || cell.HasItemCount < 0) return null;
            return Loc.Fmt("tooltip.need_have", cell.HasItemCount, cell.Value, name);
        }
        catch
        {
            return null;
        }
    }

    private static string DescribePreview(UICraftPreviewItemCell preview)
    {
        var data = G.PreviewData(preview);
        if (data == null) return null;

        if (data.IsTab)
        {
            // Only extension tabs can be focused: an attachment the station can be upgraded with.
            if (!data.IsExtension) return null;
            var name = TmpText.Clean(LLBase.L(data.ExtensionId));
            return Loc.Fmt(data.IsExtensionAvailable ? "craft.extension_built" : "craft.extension_missing", name);
        }

        if (data.CraftDef == null) return null;
        if (data.IsUnknown) return Loc.Get("craft.unknown_recipe");

        var parts = new List<string> { RecipeWithOutput(data.CraftDef, data.WgoData) };

        var mastery = MasteryShort(data.CraftDef, data.WgoData, data.WgoData?.Worker ?? (IWorker)MainGame.PlayerController);
        if (mastery != null) parts.Add(mastery);
        if (!data.CanStart) parts.Add(Loc.Get("craft.cannot_start"));

        return string.Join(", ", parts);
    }

    /// <summary>Prefixes the recipe group's name when focus crosses into a new one on a station with several.</summary>
    private static string WithTab(UICraftPreviewItemCell preview, string label)
    {
        if (string.IsNullOrEmpty(label)) return label;

        var tab = preview.GetComponentInParent<UICraftsTabWidget>();
        var window = LazyWindowsStackController.ActiveWindow as UICraftWindow;
        if (tab == null || window == null || tab == _lastTab) return label;
        _lastTab = tab;

        var tabs = G.DisplayedTabs(window);
        if (tabs == null || tabs.Count < 2) return label;

        var data = G.TabData(tab);
        var name = TabName(data?.TabId, window.Data?.AssignedWgo?.Data);
        return string.IsNullOrEmpty(name) ? label : Loc.Fmt("craft.group", name, label);
    }

    private static string TabName(string tabId, WgoData wgo)
    {
        if (string.IsNullOrEmpty(tabId)) return null;
        var name = TmpText.Clean(LLBase.L(tabId));
        if (!string.IsNullOrWhiteSpace(name) && name != tabId) return name;
        if (wgo != null && tabId == wgo.Definition?.id) return Header(wgo);

        // The station's main page has no name of its own; "default tab" was read out as if it
        // were one. The group is only worth naming when it is one of the extensions.
        if (tabId.IndexOf("default", StringComparison.OrdinalIgnoreCase) >= 0) return null;
        return Navigator.Humanise(tabId);
    }

    private static string DescribeQueueElement(UICraftQueueElementWidget widget)
    {
        var data = widget.Data;
        var element = data?.CraftQueueElement;
        if (element == null) return null;

        var queue = data.CraftQueue;
        var position = queue == null ? 1 : queue.IndexOf(element) + 1;
        var total = queue?.Count ?? 1;

        var amount = element.IsInfinite ? Loc.Get("craft.endless") : element.Count.ToString();
        var parts = new List<string>
        {
            Loc.Fmt("craft.queue_item", position, total, amount, RecipeName(element.Def, data.WgoData)),
            element.IsStarted
                ? Loc.Fmt("craft.running", Mathf.RoundToInt(Mathf.Clamp01(element.ProgressTimeNormalized) * 100f))
                : Loc.Get("craft.waiting"),
        };

        if (element.CraftStatus != CraftStatus.OK && element.CraftStatus != CraftStatus.Other)
            parts.Add(StatusText(element.CraftStatus));

        return string.Join(", ", parts);
    }

    private static string DescribeOutputCell(UICraftSelectionOutputItemCell output)
    {
        var window = output.GetComponentInParent<UIBaseCraftSelectionWindow>();
        if (window == null) return null;
        return CountLine(window);
    }

    private static string DescribeAlchemyCell(UIAlchemyWindow window, UIItemCell cell)
    {
        if (cell == G.AlchemyResult(window))
        {
            // Empty until the ingredients make a known mixture; "Ergebnis: 0 x" with no name said nothing.
            var result = cell.DisplayingItem;
            if (result == null || result.IsEmpty || string.IsNullOrEmpty(result.id)) return Loc.Get("craft.alchemy_result_empty");
            return Loc.Fmt("craft.alchemy_result_cell", AlchemyAmount(window), ItemText.Name(result.id));
        }

        if (cell == G.AlchemyBoostCell(window))
        {
            var boost = G.AlchemyBoost(window);
            return boost == null
                ? Loc.Get("craft.alchemy_boost_empty")
                : Loc.Fmt("craft.alchemy_boost", RecipeName(boost.Definition, G.AlchemyData(window)?.Wgo?.Data));
        }

        var slots = G.AlchemyIngredients(window);
        if (slots == null) return null;
        var active = slots.Where(s => s != null && s.gameObject.activeSelf).ToList();
        var index = active.FindIndex(s => s.cell == cell);
        if (index < 0) return null;

        var item = cell.DisplayingItem;
        if (item == null || item.IsEmpty)
            return Loc.Fmt("craft.alchemy_slot_empty", index + 1, active.Count);

        var name = ItemText.Name(item.id);
        return Loc.Fmt("craft.alchemy_slot", index + 1, active.Count, NeedText(cell, name) ?? name, Runes(item.Definition.GetRunesAsVector3Int()));
    }

    private static string DescribeSurveyMain(UIResourceBasedCraftWindow window)
    {
        var data = G.SurveyData(window);
        var item = data?.SelectedItem;
        if (item == null || item.IsEmpty) return Loc.Get("craft.study_slot_empty");
        return Loc.Fmt("craft.study_slot", ItemText.Name(item.id), data.MainIngredientCount);
    }

    // ---- window descriptions ----------------------------------------------------------------

    private static string DescribeWorkbench(UICraftWindow window)
    {
        var data = window.Data;
        var wgo = data?.AssignedWgo?.Data;
        if (data == null || wgo == null) return null;

        var parts = new List<string> { TmpText.Clean(data.InfoWidgetData?.Header) ?? Header(wgo) };

        var crafts = new List<CraftDef>();
        foreach (var tab in data.CraftsByTabs) if (tab.Value != null) crafts.AddRange(tab.Value);
        foreach (var tab in data.ExtensionCrafts) if (tab.Value != null) crafts.AddRange(tab.Value);

        var unknown = crafts.Count(c => c != null && c.isNeedsUnlock &&
                                        !MainGame.Instance.GameSave.knowledgeSystem.unlockedCrafts.Contains(c.id));
        parts.Add(unknown > 0
            ? Loc.Fmt("craft.recipes_some_unknown", crafts.Count, unknown)
            : Loc.Plural("craft.recipes", crafts.Count, crafts.Count));

        var groups = G.DisplayedTabs(window)?.Count ?? 0;
        if (groups > 1) parts.Add(Loc.Fmt("craft.groups", groups));

        if (wgo.Worker is ZombieWgoData) parts.Add(Loc.Get("craft.zombie_works"));

        if (!data.IsAddToQueueDisabledForAllCrafts)
        {
            var queue = data.CraftComponent?.CraftElementsQueue;
            if (queue == null || queue.Count == 0)
            {
                parts.Add(Loc.Get("craft.queue_empty"));
            }
            else
            {
                var entries = queue.Select(e =>
                {
                    var amount = e.IsInfinite ? Loc.Get("craft.endless") : e.Count.ToString();
                    var line = Loc.Fmt("craft.queue_entry", amount, RecipeName(e.Def, wgo));
                    return e.IsStarted
                        ? $"{line} ({Loc.Fmt("craft.running", Mathf.RoundToInt(Mathf.Clamp01(e.ProgressTimeNormalized) * 100f))})"
                        : line;
                });
                parts.Add(Loc.Fmt("craft.queue", string.Join("; ", entries)));
                parts.Add(Loc.Fmt("craft.queue_keys", KeyFor(GameKey.CraftWindowZoneSwitch, _zoneKey)));
            }
        }

        parts.Add(Loc.Get("craft.list_hint"));
        return Join(parts.ToArray());
    }

    private static string DescribeSelection(UIBaseCraftSelectionWindow window)
    {
        var data = G.SelectionData(window);
        var def = data?.CraftDefinition;
        if (def == null) return null;

        var parts = new List<string>
        {
            CleanLabel(G.SelectionHeader(window)) ?? RecipeName(def, data.WgoData),
        };

        if (window is UIFuelCraftWindow fuel) parts.Add(CleanLabel(G.FuelDescription(fuel)));

        // What the station already holds - on screen a small counter in its info panel.
        var stock = ObjectStatus.Stock(data.WgoData);
        if (stock != null) parts.Add(stock);

        parts.Add(CountLine(window));

        var ingredients = (G.SelectionIngredients(window) ?? new List<UICraftItemCell>())
            .Where(c => c != null && c.gameObject.activeSelf && G.IngredientData(c) != null)
            .Select(IngredientText)
            .ToList();
        if (ingredients.Count > 0) parts.Add(Loc.Fmt("tooltip.needs", string.Join(", ", ingredients)));

        var costs = Requirements(data);
        if (!string.IsNullOrEmpty(costs)) parts.Add(costs);

        var ticks = SafeInt(() => def.duration.EvaluateInt());
        if (ticks > 0) parts.Add(Loc.Plural("craft.duration", ticks, ticks));

        var mastery = MasteryLong(def, data.WgoData, data.DisplayableWorker);
        if (mastery != null) parts.Add(mastery);

        parts.Add(Readiness(window, data));
        return Join(parts.ToArray());
    }

    /// <summary>"Can be made. F: Craft, Space: Add to queue" - or why not, from the game's own start check.</summary>
    private static string Readiness(UIBaseCraftSelectionWindow window, UIBaseCraftSelectionWindowData data)
    {
        var parts = new List<string>();
        var start = G.SelectionStartButton(window);
        var startShown = start != null && start.gameObject.activeSelf;
        var startLabel = CleanLabel(G.SelectionStartText(window)) ?? Loc.Get("craft.done");

        if (startShown && start.interactable)
            parts.Add(Loc.Fmt("craft.key_action", KeyFor(GameKey.StartCraft, _startKey), startLabel));

        if (window is UICraftSelectionWindow selection)
        {
            var queue = G.QueueButton(selection);
            if (queue != null && queue.gameObject.activeSelf && queue.interactable)
                parts.Add(Loc.Fmt("craft.key_action", KeyFor(GameKey.AddCraftToQueue, _queueKey), CleanLabel(G.QueueButtonText(selection)) ?? Loc.Get("craft.add_to_queue")));
        }

        if (!startShown && data.WgoData?.Worker is ZombieWgoData)
            parts.Insert(0, Loc.Get("craft.zombie_queue_only"));
        else if (startShown && !start.interactable)
            parts.Insert(0, Loc.Fmt("craft.cannot_because", StartProblem(data)));

        var plus = G.SelectionPlus(window);
        if (plus != null && plus.gameObject.activeSelf) parts.Add(Loc.Get("craft.amount_hint"));

        return Join(parts.ToArray());
    }

    /// <summary>"Amount 3, makes 6 planks" plus "not enough" when the start button is off.</summary>
    private static string CountLine(UIBaseCraftSelectionWindow window)
    {
        var data = G.SelectionData(window);
        var def = data?.CraftDefinition;
        if (def == null) return null;

        // A fuel station with an order running changes the order, not the draft amount.
        var queue = data.CraftComponent?.CraftElementsQueue;
        if (window is UIFuelCraftWindow && queue != null && queue.Count > 0)
            return Loc.Fmt("craft.fuel_ordered", queue.Sum(e => e.Count));

        var preview = SafePreview(def, data.WgoData);
        var line = Loc.Fmt("craft.amount", data.CraftsCount);
        if (preview != null && !string.IsNullOrEmpty(preview.itemId))
        {
            var total = Math.Max(preview.count, 1) * Math.Max(data.CraftsCount, 0);
            line = Loc.Fmt("craft.amount_makes", data.CraftsCount, total, ItemText.Name(preview.itemId));
        }

        var start = G.SelectionStartButton(window);
        if (start != null && start.gameObject.activeSelf && !start.interactable)
            line += ", " + Loc.Get("craft.cannot_start");
        return line;
    }

    /// <summary>Why the start button is off, from the same check the game runs to switch it off.</summary>
    private static string StartProblem(UIBaseCraftSelectionWindowData data)
    {
        try
        {
            if (!data.CraftDefinition.isFuelCraft)
            {
                var element = new CraftElement(data.CraftDefinition.id, 1, data.CurrentNeedItems, data.ParamsData);
                var status = data.WgoData.CraftComponent.GetStartCraftStatus(element);
                if (status != CraftStatus.OK) return StatusText(status);
            }
        }
        catch (Exception ex)
        {
            Plugin.Log?.LogWarning($"[Craft] Could not work out why a craft cannot start: {ex.Message}");
        }
        return StatusText(CraftStatus.NotEnoughResources);
    }

    /// <summary>Energy and insanity per step, the insanity limit, and alchemy fuel - the icons above the start button.</summary>
    private static string Requirements(UIBaseCraftSelectionWindowData data)
    {
        var list = data.CraftRequirementWidgetData;
        if (list == null || list.Count == 0) return null;

        var parts = new List<string>();
        foreach (var req in list)
        {
            if (req == null) continue;
            switch (req.Id)
            {
                case "energy":
                case "insanity":
                    parts.Add(Loc.Fmt("craft.cost_" + req.Id, Level(req.IconId)));
                    break;
                case "insanity_lock":
                    parts.Add(Loc.Fmt("craft.insanity_lock", req.RequirementValue));
                    break;
                default:
                    var name = ItemText.Name(req.Id);
                    parts.Add(Loc.Fmt(req.IsEnough ? "craft.fuel_enough" : "craft.fuel_short", req.RequirementValue, name));
                    break;
            }
        }
        return parts.Count == 0 ? null : string.Join(", ", parts);
    }

    /// <summary>The game draws cost as one to three icons (energy_1..3); said as low, medium, high.</summary>
    private static string Level(string iconId)
    {
        if (string.IsNullOrEmpty(iconId)) return Loc.Get("craft.level_1");
        var last = iconId[iconId.Length - 1];
        return Loc.Find("craft.level_" + last) ?? Loc.Get("craft.level_1");
    }

    private static string AlchemyState(UIAlchemyWindow window)
    {
        var wgo = G.AlchemyData(window)?.Wgo?.Data;
        var slots = G.AlchemyIngredients(window) ?? new List<UIAlchemyIngredient>();
        var filled = slots.Where(s => s != null && s.gameObject.activeSelf && s.cell != null &&
                                      s.cell.DisplayingItem != null && !s.cell.DisplayingItem.IsEmpty)
                          .Select(s => s.cell.DisplayingItem).ToList();
        var boost = G.AlchemyBoost(window);

        if (filled.Count == 0 && boost == null) return Loc.Get("craft.alchemy_empty");

        var parts = new List<string>();
        parts.Add(Loc.Fmt("craft.alchemy_in", string.Join(", ", filled.Select(i => ItemText.Name(i.id)))));

        var runes = Vector3Int.zero;
        foreach (var item in filled) runes += item.Definition.GetRunesAsVector3Int();
        if (boost != null) runes += boost.Definition.GetBoostRunesAsVector3Int();
        parts.Add(Runes(runes));

        var mix = GameBalance.GetAlchemyMixDef(G.AlchemyMixId(window));
        if (mix == null)
        {
            parts.Add(Loc.Get("craft.alchemy_no_mix"));
        }
        else
        {
            var known = MainGame.Instance.GameSave.knowledgeSystem.IsAlchemyFormulaKnown(mix.Formula);
            parts.Add(known
                ? Loc.Fmt("craft.alchemy_result", AlchemyAmount(window), ItemText.Name(mix.ResultItem?.id))
                : Loc.Fmt("craft.alchemy_unknown", mix.talentLock));
            parts.Add(AlchemyCountLine(window));
        }

        var create = G.AlchemyCreate(window);
        parts.Add(create != null && create.interactable
            ? Loc.Fmt("craft.key_action", KeyFor(GameKey.AlchemyStart, _startKey), TmpText.Clean(LLBase.L("hint_alchemy")))
            : Loc.Get("craft.alchemy_not_ready"));

        if (mix != null) parts.Add(Loc.Get("craft.alchemy_amount_hint"));
        return Join(parts.ToArray());
    }

    private static string AlchemyCountLine(UIAlchemyWindow window)
    {
        var wgo = G.AlchemyData(window)?.Wgo?.Data;
        var count = AlchemyAmount(window);
        var flasks = SafeInt(() => wgo.GetCraftableMultiInventory(excludeWorkerInventory: true).GetTotalCount("alchemy_flask"));
        return Loc.Fmt("craft.alchemy_amount", count, flasks);
    }

    /// <summary>What goes in, so a change of ingredient reads the whole state and a change of amount only the amount.</summary>
    private static string AlchemyKey(UIAlchemyWindow window)
    {
        var slots = G.AlchemyIngredients(window) ?? new List<UIAlchemyIngredient>();
        var ids = slots.Where(s => s != null && s.gameObject.activeSelf)
                       .Select(s => s.cell?.DisplayingItem == null || s.cell.DisplayingItem.IsEmpty ? "-" : s.cell.DisplayingItem.id);
        return string.Join("|", ids) + "#" + (G.AlchemyBoost(window)?.CraftId ?? "") + "#" + G.AlchemyMixId(window);
    }

    private static int AlchemyAmount(UIAlchemyWindow window)
    {
        try { return G.AlchemyCountRef(window); }
        catch { return 1; }
    }

    private static string DescribeSurvey(UIResourceBasedCraftWindow window)
    {
        var data = G.SurveyData(window);
        if (data == null) return null;

        var parts = new List<string> { Header(data.WgoData), TmpText.Clean(data.LabelText) };

        var selected = data.SelectedItem;
        if (selected == null || selected.IsEmpty)
        {
            parts.Add(Loc.Get("craft.study_slot_empty"));
            return Join(parts.ToArray());
        }

        parts.Add(Loc.Fmt("craft.study_slot", ItemText.Name(selected.id), data.MainIngredientCount));

        var needs = new List<string>();
        for (var i = 0; i < data.Ingredients.Count; i++)
        {
            var need = data.Ingredients[i];
            var count = i < data.CraftNeedItemsCount ? need.GetCount(data.WgoData) : need.GetCount();
            var have = i < data.IngredientsHasCount.Count ? data.IngredientsHasCount[i] : 0;
            needs.Add(Loc.Fmt("tooltip.need_have", have, count, ItemText.Name(need.Id)));
        }
        if (needs.Count > 0) parts.Add(Loc.Fmt("tooltip.needs", string.Join("; ", needs)));

        parts.Add(data.CanStartCraft()
            ? Loc.Fmt("craft.key_action", KeyFor(GameKey.SurveyStart, _startKey), TmpText.Clean(data.BtnText))
            : Loc.Get("craft.cannot_start"));
        return Join(parts.ToArray());
    }

    private static string DescribeGarden(UIGardenBedWindow window)
    {
        var data = G.GardenData(window);
        if (data == null) return null;

        var parts = new List<string> { TmpText.Clean(data.UIInfoWidgetData?.Header) ?? Header(data.WgoData) };

        if (data.IsGrowing && data.CraftDefinition != null)
        {
            var preview = SafePreview(data.CraftDefinition, data.WgoData);
            var element = data.WgoData?.CraftComponent?.CurrentCraftElement;
            var percent = element == null ? 0 : Mathf.RoundToInt(Mathf.Clamp01(element.ProgressTimeNormalized) * 100f);
            parts.Add(Loc.Fmt("craft.garden_growing", ItemText.Name(preview?.itemId), percent));
            var mastery = MasteryShort(data.CraftDefinition, data.WgoData, MainGame.PlayerController);
            if (mastery != null) parts.Add(mastery);
        }
        else
        {
            parts.Add(Loc.Get("craft.garden_empty"));
            parts.Add(GardenSeedLine(window));
        }
        return Join(parts.ToArray());
    }

    private static string GardenSeedLine(UIGardenBedWindow window)
    {
        var data = G.GardenData(window);
        if (data == null || data.IsGrowing) return null;

        var seed = G.GardenSeed(window);
        if (seed == null || seed.IsEmpty) return Loc.Get("craft.garden_seed_empty");

        var craft = G.GardenSeedCraft(window);
        var need = craft != null && craft.needItems.Count > 0 ? craft.needItems[0].GetCount(data.WgoData) : 0;
        var have = SafeInt(() => new MultiInventory(MainGame.PlayerData).GetTotalCount(seed.id));
        var line = Loc.Fmt("craft.garden_seed", Loc.Fmt("tooltip.need_have", have, need, ItemText.Name(seed.id)));

        var canPlant = G.GardenCanPlant != null && (bool)G.GardenCanPlant.Invoke(window, null);
        return Join(line, canPlant
            ? Loc.Fmt("craft.key_action", KeyFor(GameKey.Fold, _startKey), TmpText.Clean(LLBase.L("ui_plant")))
            : Loc.Get("craft.cannot_start"));
    }

    // ---- keys -------------------------------------------------------------------------------

    /// <summary>
    /// The start, queue and switch keys in a station window. True when the press was handled (or
    /// deliberately left to the game, which also means nothing else in the mod should act on it).
    /// </summary>
    internal static bool TryKeys()
    {
        if (_startKey == null || !Ready) return false;
        var window = LazyWindowsStackController.ActiveWindow;
        if (!IsStationWindow(window)) return false;

        try
        {
            if (_startKey.Value.IsDown()) return TryStart(window);
            if (_queueKey.Value.IsDown() && window is UICraftSelectionWindow selection) return TryQueue(selection);
            if (_zoneKey.Value.IsDown() && window is UICraftWindow craft) return TrySwitchZone(craft);
        }
        catch (Exception ex)
        {
            Plugin.Log?.LogError($"[Craft] Station key failed: {ex.GetType().Name}: {ex.Message}");
            return true;
        }
        return false;
    }

    private static bool TryStart(LazyWidgetBase window)
    {
        switch (window)
        {
            case UIBaseCraftSelectionWindow selection:
            {
                var start = G.SelectionStartButton(selection);
                var data = G.SelectionData(selection);
                if (start == null || data == null) return false;
                if (!start.gameObject.activeSelf)
                {
                    Say(data.WgoData?.Worker is ZombieWgoData ? Loc.Get("craft.zombie_queue_only") : Loc.Get("craft.cannot_start"));
                    return true;
                }
                if (!start.interactable)
                {
                    Say(Loc.Fmt("craft.cannot_because", StartProblem(data)));
                    return true;
                }
                if (GameKeys.GameHandles(GameKey.StartCraft, _startKey.Value)) return true;
                Invoke(G.SelectionStart, selection);
                return true;
            }

            case UIAlchemyWindow alchemy:
            {
                var create = G.AlchemyCreate(alchemy);
                if (create == null || !create.interactable)
                {
                    Say(AlchemyState(alchemy));
                    return true;
                }
                if (GameKeys.GameHandles(GameKey.AlchemyStart, _startKey.Value)) return true;
                Say(Loc.Get("craft.alchemy_started"), holdFocus: true);
                Invoke(G.AlchemyStart, alchemy);
                return true;
            }

            case UIResourceBasedCraftWindow survey:
            {
                var data = G.SurveyData(survey);
                if (data == null || !data.CanStartCraft())
                {
                    Say(data?.SelectedItem == null ? Loc.Get("craft.study_slot_empty") : Loc.Get("craft.cannot_start"));
                    return true;
                }
                if (GameKeys.GameHandles(GameKey.SurveyStart, _startKey.Value)) return true;
                Say(Loc.Fmt("craft.confirmed", TmpText.Clean(data.BtnText), data.MainIngredientCount, ItemText.Name(data.SelectedItem.id)), holdFocus: true);
                Invoke(G.SurveyStart, survey);
                return true;
            }

            case UIGardenBedWindow garden:
            {
                var data = G.GardenData(garden);
                if (data == null || data.IsGrowing)
                {
                    Say(Loc.Get("craft.garden_busy"));
                    return true;
                }
                var canPlant = G.GardenCanPlant != null && (bool)G.GardenCanPlant.Invoke(garden, null);
                if (!canPlant)
                {
                    Say(GardenSeedLine(garden));
                    return true;
                }
                if (GameKeys.GameHandles(GameKey.Fold, _startKey.Value)) return true;
                Say(Loc.Fmt("craft.garden_planted", ItemText.Name(G.GardenSeed(garden)?.id)), holdFocus: true);
                Invoke(G.GardenPlant, garden);
                return true;
            }
        }
        return false;
    }

    private static bool TryQueue(UICraftSelectionWindow window)
    {
        var button = G.QueueButton(window);
        if (button == null || !button.gameObject.activeSelf || !button.interactable)
        {
            Say(Loc.Get("craft.no_queue_here"));
            return true;
        }
        if (GameKeys.GameHandles(GameKey.AddCraftToQueue, _queueKey.Value)) return true;
        Invoke(G.SelectionQueue, window);
        return true;
    }

    private static bool TrySwitchZone(UICraftWindow window)
    {
        if (GameKeys.GameHandles(GameKey.CraftWindowZoneSwitch, _zoneKey.Value)) return true;

        var queue = window.Data?.CraftComponent?.CraftElementsQueue;
        if (window.Data == null || window.Data.IsAddToQueueDisabledForAllCrafts)
        {
            Say(Loc.Get("craft.no_queue_here"));
            return true;
        }
        if (queue == null || queue.Count == 0)
        {
            Say(Loc.Get("craft.queue_empty"));
            return true;
        }

        // Crossing into a new area: the focused entry must be spoken even if it repeats.
        ScreenReader.ClearMenuContext();
        Invoke(G.CraftSwitchZone, window);
        return true;
    }

    /// <summary>
    /// Arrow keys in a station window, routed the way the game routes W/S/A/D there. True when the
    /// arrow changed something instead of moving focus.
    /// </summary>
    internal static bool TryArrow(GUIDirection direction)
    {
        if (!Ready) return false;
        var window = LazyWindowsStackController.ActiveWindow;
        if (!IsStationWindow(window)) return false;
        var focused = UiNarrator.FocusedItem;
        if (focused == null) return false;

        try
        {
            var vertical = direction == GUIDirection.Up || direction == GUIDirection.Down;
            var up = direction == GUIDirection.Up || direction == GUIDirection.Right;
            var ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);

            switch (window)
            {
                case UIBaseCraftSelectionWindow selection when vertical:
                {
                    var output = G.SelectionOutput(selection);
                    var plus = G.SelectionPlus(selection);
                    if (output != null && focused == output.UIItemCell.GamepadNavigationItem)
                    {
                        if (plus == null || !plus.gameObject.activeSelf)
                        {
                            Say(Loc.Get("craft.amount_fixed"));
                            return true;
                        }
                        var before = CountLine(selection);
                        Invoke(G.SelectionChangeCount, selection, up ? 1 : -1);
                        if (CountLine(selection) == before) Say(before);
                        return true;
                    }

                    if (focused.TryGetComponent<UICraftItemCell>(out var ingredient))
                    {
                        var button = up ? ingredient.NextItemButton : ingredient.PrevItemButton;
                        if (button == null || !button.gameObject.activeSelf) return false;
                        if (button.interactable) button.onClick?.Invoke();
                        return true;
                    }
                    return false;
                }

                case UICraftWindow craft:
                {
                    var queued = focused.GetComponentInParent<UICraftQueueElementWidget>();
                    if (queued == null) return false;

                    if (vertical && !ctrl)
                    {
                        var button = up ? queued.PlusCraftButton : queued.MinusCraftButton;
                        if (button == null || !button.interactable)
                        {
                            Say(Loc.Get(queued.Data?.CraftQueueElement?.IsStarted == true ? "craft.queue_started_fixed" : "craft.amount_fixed"));
                            return true;
                        }
                        queued.ChangeCount(up ? 1 : -1);
                        return true;
                    }

                    if (!vertical && ctrl)
                    {
                        // The game moves an order only while its "queue selected" flag is set, which
                        // only its own zone-switch key sets - not arrowing into the queue.
                        G.QueueSelected(craft) = true;
                        var moved = G.CraftMoveQueue != null && (bool)G.CraftMoveQueue.Invoke(craft, new object[] { up });
                        if (moved) ScreenReader.ClearMenuContext();
                        else Say(Loc.Get("craft.queue_cannot_move"));
                        return true;
                    }
                    return false;
                }

                case UIAlchemyWindow alchemy when !vertical:
                {
                    var result = G.AlchemyResult(alchemy);
                    if (result == null || focused != result.GamepadNavigationItem) return false;
                    var before = AlchemyAmount(alchemy);
                    Invoke(G.AlchemyChangeCount, alchemy, up ? 1 : -1);
                    if (AlchemyAmount(alchemy) == before) Say(AlchemyCountLine(alchemy));
                    return true;
                }
            }
        }
        catch (Exception ex)
        {
            Plugin.Log?.LogError($"[Craft] Arrow key failed: {ex.GetType().Name}: {ex.Message}");
        }
        return false;
    }

    /// <summary>F8 on a station window: the window read again, fresh. False when no station window is on top.</summary>
    internal static bool TryRepeat()
    {
        if (!Ready) return false;
        var window = LazyWindowsStackController.ActiveWindow;
        if (!IsStationWindow(window)) return false;

        string text = null;
        try
        {
            text = window switch
            {
                UICraftWindow craft => DescribeWorkbench(craft),
                UIBaseCraftSelectionWindow selection => DescribeSelection(selection),
                UIAlchemyWindow alchemy => Join(Header(G.AlchemyData(alchemy)?.Wgo?.Data), AlchemyState(alchemy)),
                UIResourceBasedCraftWindow survey => DescribeSurvey(survey),
                UIGardenBedWindow garden => DescribeGarden(garden),
                _ => null,
            };
        }
        catch (Exception ex)
        {
            Plugin.Log?.LogError($"[Craft] Could not read the station again: {ex.GetType().Name}: {ex.Message}");
        }

        if (string.IsNullOrEmpty(text)) return false;
        ScreenReader.Say(text);
        if (!string.IsNullOrEmpty(UiNarrator.LastFocusLabel)) ScreenReader.Say(UiNarrator.LastFocusLabel, interrupt: false);
        return true;
    }

    // ---- shared pieces ----------------------------------------------------------------------

    private static string IngredientText(UICraftItemCell cell)
    {
        var data = G.IngredientData(cell);
        var item = data?.currentItem;
        if (item == null) return null;

        var name = ItemText.Name(item.Id);
        if (string.IsNullOrWhiteSpace(name)) name = item.Id;

        var multiplier = SafeInt(() => G.IngredientMultiplier(cell));
        var need = item.GetCount(data.WgoData) * multiplier;

        string text;
        if (data.needDurability > 0f)
        {
            var enough = data.MultiInventory != null && data.MultiInventory.HasItemWithEnoughDurability(item.Id, data.needDurability);
            text = Loc.Fmt(enough ? "craft.tool_ok" : "craft.tool_missing", name);
        }
        else if (data.MultiInventory == null)
        {
            text = Loc.Fmt("tooltip.need", need, name);
        }
        else
        {
            text = Loc.Fmt("tooltip.need_have", data.MultiInventory.GetTotalCount(item.Id), need, name);
        }

        if (data.itemVariants != null && data.itemVariants.Count > 1)
            text = Loc.Fmt("craft.variant", text, data.currentItemIndex + 1, data.itemVariants.Count);
        return text;
    }

    /// <summary>The recipe's own name, falling back to what it makes - some recipes have no text of their own.</summary>
    internal static string RecipeName(CraftDefBase def, WgoData wgo)
    {
        if (def == null) return null;
        var name = TmpText.Clean(LLBase.L(def.id));
        if (!string.IsNullOrWhiteSpace(name) && name != def.id) return name;

        var preview = SafePreview(def, wgo);
        if (!string.IsNullOrEmpty(preview?.itemId)) return ItemText.Name(preview.itemId);
        return Navigator.Humanise(def.id);
    }

    /// <summary>The recipe name, plus what it makes when that is not obvious from the name.</summary>
    private static string RecipeWithOutput(CraftDefBase def, WgoData wgo)
    {
        var name = RecipeName(def, wgo);
        var preview = SafePreview(def, wgo);
        if (preview == null || string.IsNullOrEmpty(preview.itemId)) return name;

        var output = ItemText.Name(preview.itemId);
        if (preview.count > 1) return Loc.Fmt("craft.recipe_makes", name, preview.count, output);
        return output == name ? name : Loc.Fmt("craft.recipe_makes", name, 1, output);
    }

    private static OutputPreview SafePreview(CraftDefBase def, WgoData wgo)
    {
        try { return def.GetOutputPreview(wgo); }
        catch { return null; }
    }

    /// <summary>"needs mastery 2" when the worker's level is short - on screen, a red number by a talent icon.</summary>
    private static string MasteryShort(CraftDefBase def, WgoData wgo, IWorker worker)
    {
        if (def == null || wgo == null || worker == null || def.talentLock <= 0) return null;
        if (def.isStarCraft || def.isAutopsyCraft || def.isPocketExtractCraft) return null;
        var talent = wgo.Definition?.talent;
        if (string.IsNullOrEmpty(talent)) return null;

        var level = worker.GetMasteryLevelForTalentBranch(talent, def);
        return level >= def.talentLock ? null : Loc.Fmt("craft.mastery_short", def.talentLock, TalentName(talent));
    }

    /// <summary>
    /// Mastery in the setup window. Star crafts use it differently - higher mastery means more stars,
    /// and the lock is the level for the best result - so they are said that way.
    /// </summary>
    private static string MasteryLong(CraftDefBase def, WgoData wgo, IWorker worker)
    {
        if (def == null || wgo == null || worker == null || def.talentLock <= 0) return null;
        var talent = wgo.Definition?.talent;
        if (string.IsNullOrEmpty(talent)) return null;

        var level = worker.GetMasteryLevelForTalentBranch(talent, def);
        if (def.isStarCraft || def.isAutopsyCraft || def.isPocketExtractCraft)
            return Loc.Fmt("craft.mastery_star", TalentName(talent), level, def.talentLock);
        return Loc.Fmt(level >= def.talentLock ? "craft.mastery_ok" : "craft.mastery_missing", TalentName(talent), level, def.talentLock);
    }

    internal static string TalentName(string talent)
    {
        // The game has no text for its talent ids ("talent_orange"); the mod names them.
        var own = Loc.Find("talent." + talent);
        if (own != null) return own;

        var name = TmpText.Clean(LLBase.L(talent));
        return string.IsNullOrWhiteSpace(name) || name == talent ? Navigator.Humanise(talent) : name;
    }

    internal static string StatusText(CraftStatus status) =>
        Loc.Find("craft.status." + status) ?? Navigator.Humanise(status.ToString());

    private static string Runes(Vector3Int runes) => Loc.Fmt("craft.runes", runes.x, runes.y, runes.z);

    /// <summary>The station's name.</summary>
    private static string Header(WgoData wgo)
    {
        if (wgo == null) return null;
        var name = TmpText.Clean(LLBase.L(wgo.id));
        return string.IsNullOrWhiteSpace(name) || name == wgo.id ? Navigator.Humanise(wgo.id) : name;
    }

    /// <summary>The key the player presses for an action: the game's own if it has a keyboard key, else the mod's.</summary>
    private static string KeyFor(GameKey key, ConfigEntry<KeyboardShortcut> mine) =>
        GameKeys.Name(key) ?? GameKeys.Name(mine.Value);

    private static string CleanLabel(TMP_Text label)
    {
        if (label == null || !label.gameObject.activeInHierarchy) return null;
        var text = TmpText.Clean(label.text);
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    private static string Join(params string[] parts) =>
        string.Join(". ", parts.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p.Trim().TrimEnd('.')));

    private static int SafeInt(Func<int> get)
    {
        try { return get(); }
        catch { return 0; }
    }

    private static void Say(string text, bool holdFocus = false)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        Plugin.Log?.LogInfo($"[Craft] \"{text}\"");
        ScreenReader.Say(text);

        // After a window closes, the one underneath focuses its control again; queue that behind
        // this line instead of letting it cut the confirmation off.
        if (holdFocus) UiNarrator.QueueFocusUntil = Time.unscaledTime + 1f;
    }

    private static void Invoke(MethodInfo method, object target, params object[] args)
    {
        if (method == null)
        {
            Plugin.Log?.LogWarning("[Craft] A station method was not found in this game version.");
            return;
        }
        method.Invoke(target, args.Length == 0 ? null : args);
    }

    // Postfixes run inside the game's UI code; an exception escaping one would break the window.
    private static void Guard(string what, Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            Plugin.Log?.LogError($"[Craft] Reading the {what} failed: {ex.GetType().Name}: {ex.Message}");
        }
    }
}
