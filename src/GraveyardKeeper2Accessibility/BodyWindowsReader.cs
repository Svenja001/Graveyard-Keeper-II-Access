namespace GraveyardKeeper2Accessibility;

/// <summary>
/// The autopsy table and the grave - the two windows the graveyard's whole economy runs through.
///
/// <para>
/// <b>What went wrong with focus narration alone.</b> The first autopsy in the log read "empty
/// slot" for every cell but one, on a body that had organs in it. The cells were not empty: an organ
/// the player has not yet learned about is drawn by <c>UIFixedTypeItemCell.TryUpdateUnknownOrgan</c>
/// as a question-mark picture over a cell that is <i>emptied</i> on purpose. A sighted player sees
/// "heart, unknown"; the mod saw nothing and said "empty". And the slot's organ type - which is the
/// whole meaning of the cell - exists only as a silhouette. <see cref="UiNarrator"/> now asks
/// <see cref="DescribeCell"/> first, which names the slot and says unknown.
/// </para>
///
/// <para>
/// <b>What the window as a whole says.</b> The skull totals - what the body will do to the
/// graveyard's rating, which is the point of the operation - are two labels beside a picture of the
/// body, and no navigation item ever lands on them. So each window is read once from its data when it
/// is drawn: body, skulls, organs, pockets on the table; body, tombstone, fence and total quality on
/// the grave. F8 reads it again.
/// </para>
///
/// <para>
/// <b>Taking the body.</b> Both windows take or exhume the body on <c>GameKey.ExtractBody</c>, a
/// controller button with no keyboard binding (the same gap as <c>ItemMove</c>). The mod's own key,
/// T by default, presses the widget's button instead.
/// </para>
/// </summary>
internal static class BodyWindowsReader
{
    private static ConfigEntry<KeyboardShortcut> _takeBodyKey;

    /// <summary>The last summary, for F8, and to avoid re-reading an unchanged window on redraw.</summary>
    private static string _current;

    private static readonly AccessTools.FieldRef<UIAutopsyWindow, UICorpseWidget> AutopsyCorpse =
        AccessTools.FieldRefAccess<UIAutopsyWindow, UICorpseWidget>("corpseWidget");

    private static readonly AccessTools.FieldRef<UIGraveWindow, UICorpseWidget> GraveCorpse =
        AccessTools.FieldRefAccess<UIGraveWindow, UICorpseWidget>("corpseWIdget");

    private static readonly AccessTools.FieldRef<LazyWidget<UIGraveElementWidgetData>, UIGraveElementWidgetData> GraveElementData =
        AccessTools.FieldRefAccess<LazyWidget<UIGraveElementWidgetData>, UIGraveElementWidgetData>("data");

    private static readonly AccessTools.FieldRef<LazyWidget<UIAutopsyWindowData>, UIAutopsyWindowData> AutopsyData =
        AccessTools.FieldRefAccess<LazyWidget<UIAutopsyWindowData>, UIAutopsyWindowData>("data");

    private static readonly AccessTools.FieldRef<LazyWidget<UIGraveWindowData>, UIGraveWindowData> GraveData =
        AccessTools.FieldRefAccess<LazyWidget<UIGraveWindowData>, UIGraveWindowData>("data");

    private static readonly AccessTools.FieldRef<UIEmbalmWindow, UICorpseWidget> EmbalmCorpse =
        AccessTools.FieldRefAccess<UIEmbalmWindow, UICorpseWidget>("corpseWidget");

    private static readonly AccessTools.FieldRef<LazyWidget<UIEmbalmWindowData>, UIEmbalmWindowData> EmbalmData =
        AccessTools.FieldRefAccess<LazyWidget<UIEmbalmWindowData>, UIEmbalmWindowData>("data");

    private static readonly AccessTools.FieldRef<UIResurrectionWindow, UICorpseWidget> ResurrectionCorpse =
        AccessTools.FieldRefAccess<UIResurrectionWindow, UICorpseWidget>("corpseWidget");

    private static readonly AccessTools.FieldRef<LazyWidget<UIResurrectionWindowData>, UIResurrectionWindowData> ResurrectionData =
        AccessTools.FieldRefAccess<LazyWidget<UIResurrectionWindowData>, UIResurrectionWindowData>("data");

    internal static void Init(ConfigFile config)
    {
        _takeBodyKey = ModKeys.Bind(config,
            "Menus", "TakeBody", new KeyboardShortcut(KeyCode.B),
            "On the autopsy table: takes the body off the table. At a grave: exhumes it. The game's " +
            "own key for this (ExtractBody) exists for controllers only.").Moved(new KeyboardShortcut(KeyCode.T));
    }

    /// <summary>Forgets the last summary, so the same window opened again is read again.</summary>
    internal static void Reset()
    {
        _current = null;
        _onTakeStop = false;
        _returnItem = null;
    }

    // ---- the windows ------------------------------------------------------------------------

    [HarmonyPatch(typeof(UIAutopsyWindow), nameof(UIAutopsyWindow.Redraw))]
    [HarmonyPostfix]
    private static void UIAutopsyWindow_Redraw(UIAutopsyWindow __instance)
    {
        try
        {
            var data = AutopsyData(__instance);
            if (data != null)
                Speak(DescribeTable(data.InfoWidgetData?.Header, "body.autopsy_table", data.IsEmpty, data.CorpseWidgetData));
        }
        catch (Exception ex)
        {
            Plugin.Log?.LogError($"[Body] Could not read the autopsy table: {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>The embalming table: the same body, skulls, organs and pockets as the autopsy table.</summary>
    [HarmonyPatch(typeof(UIEmbalmWindow), nameof(UIEmbalmWindow.Redraw))]
    [HarmonyPostfix]
    private static void UIEmbalmWindow_Redraw(UIEmbalmWindow __instance)
    {
        try
        {
            var data = EmbalmData(__instance);
            if (data != null)
                Speak(DescribeTable(data.InfoWidgetData?.Header, "body.embalm_table", data.IsEmpty, data.CorpseWidgetData));
        }
        catch (Exception ex)
        {
            Plugin.Log?.LogError($"[Body] Could not read the embalming table: {ex.GetType().Name}: {ex.Message}");
        }
    }

    [HarmonyPatch(typeof(UIGraveWindow), nameof(UIGraveWindow.Redraw))]
    [HarmonyPostfix]
    private static void UIGraveWindow_Redraw(UIGraveWindow __instance)
    {
        try
        {
            Speak(DescribeGrave(GraveData(__instance)));
        }
        catch (Exception ex)
        {
            Plugin.Log?.LogError($"[Body] Could not read the grave: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static void Speak(string text)
    {
        if (string.IsNullOrEmpty(text) || text == _current) return;
        _current = text;
        Plugin.Log?.LogInfo($"[Body] \"{text}\"");

        // Interrupting is right here: the window focuses its first cell while it draws, so the
        // only thing this can cut off is that cell's name - and the summary says it anyway. F8
        // still repeats the focused cell afterwards. The one exception is the result of an
        // operation that has just finished, which the redraw it causes must not cut off.
        ScreenReader.Say(text, interrupt: Time.unscaledTime - _resultSpokenAt > ResultGuardSeconds);
    }

    // ---- what an operation did --------------------------------------------------------------
    //
    // Extracting, inserting or swapping an organ and emptying a pocket are crafts on the table,
    // worked in a timing game. When one ends, the game decides the outcome from the successful
    // hits (WgoData.DefineResultFor*Craft): at the gold level an extracted organ drops beside the
    // table, at silver it is removed and nothing is kept, below that it is botched and a "surgeon's
    // mistake" item is left in the body. A sighted player sees the organ fly out or the slot change;
    // the GK1 mod said "<organ> extracted" at this point, and the player asked for the same here.
    // So the outcome is worked out the way the game does it, and whatever the craft drops is
    // collected while it finishes and named as well.

    private sealed class PendingResult
    {
        public CraftDef Def;
        public CraftElementBase Element;
        public WgoData Table;
        public int Hits;
        public readonly List<string> Dropped = new();
    }

    private static PendingResult _pending;
    private static float _resultSpokenAt = float.NegativeInfinity;
    private const float ResultGuardSeconds = 3f;

    [HarmonyPatch(typeof(CraftComponent), "Finish")]
    [HarmonyPrefix]
    private static void CraftComponent_Finish_Prefix(CraftComponent __instance)
    {
        _pending = null;
        try
        {
            var element = __instance.CurrentCraftElement;
            if (!(element?.Def is CraftDef def) || def.autopsyTypeCraft == AutopsyTypeCraft.None) return;
            // A zombie working the table is not the player's operation to hear about.
            if (!(__instance.CraftableObject is WgoData table) || table.Worker is ZombieWgoData) return;
            _pending = new PendingResult { Def = def, Element = element, Table = table, Hits = element.SucceededProgressTicks };
        }
        catch (Exception ex)
        {
            Plugin.Log?.LogError($"[Body] Could not note the finishing operation: {ex.GetType().Name}: {ex.Message}");
        }
    }

    [HarmonyPatch(typeof(WgoData), nameof(WgoData.MakeDrop), typeof(Item))]
    [HarmonyPrefix]
    private static void WgoData_MakeDrop_Prefix(Item item)
    {
        if (_pending == null || item == null || item.IsEmpty) return;
        try
        {
            _pending.Dropped.Add(item.Count > 1 ? Loc.Fmt("inventory.item_many", ItemText.Name(item.id), item.Count) : ItemText.Name(item.id));
        }
        catch (Exception ex)
        {
            Plugin.Log?.LogError($"[Body] Could not name a dropped item: {ex.GetType().Name}: {ex.Message}");
        }
    }

    [HarmonyPatch(typeof(CraftComponent), "Finish")]
    [HarmonyPostfix]
    private static void CraftComponent_Finish_Postfix()
    {
        var pending = _pending;
        _pending = null;
        if (pending == null) return;
        try
        {
            var text = DescribeResult(pending);
            if (string.IsNullOrEmpty(text)) return;
            Plugin.Log?.LogInfo($"[Body] Result ({pending.Def.id}, {pending.Hits} hits, silver {pending.Def.silverLevel}, gold {pending.Def.goldLevel}): \"{text}\"");
            ScreenReader.Say(text);
            _resultSpokenAt = Time.unscaledTime;
        }
        catch (Exception ex)
        {
            Plugin.Log?.LogError($"[Body] Could not describe the operation's result: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static string DescribeResult(PendingResult p)
    {
        var def = p.Def;
        var organ = ItemText.Name(def.autopsyItemId);
        var mistake = ItemText.Def(def.autopsyItemId)?.GetMistakeForThisItem()?.id;
        var custom = p.Element.CustomItems != null && p.Element.CustomItems.Count > 0 ? p.Element.CustomItems[0] : null;
        var customName = custom == null ? null
            : custom.Count > 1 ? Loc.Fmt("inventory.item_many", ItemText.Name(custom.id), custom.Count) : ItemText.Name(custom.id);
        bool gold = p.Hits >= def.goldLevel;
        bool silver = !gold && p.Hits >= def.silverLevel;

        string headline;
        switch (def.autopsyTypeCraft)
        {
            case AutopsyTypeCraft.ExtractOrgan:
                headline = gold ? Loc.Fmt("body.result.extracted", organ)
                    : silver ? Loc.Fmt(p.Dropped.Count == 0 ? "body.result.extract_lost" : "body.result.extract_medium", organ)
                    : Loc.Fmt("body.result.extract_botched", organ, ItemText.Name(mistake));
                break;
            case AutopsyTypeCraft.InsertOrgan:
                headline = gold ? Loc.Fmt("body.result.inserted", organ)
                    : silver ? Loc.Fmt("body.result.insert_medium", organ)
                    : Loc.Fmt("body.result.insert_botched", organ, ItemText.Name(mistake));
                break;
            case AutopsyTypeCraft.ChangeOrgan:
                headline = gold ? Loc.Fmt("body.result.changed", organ, customName)
                    : Loc.Fmt("body.result.change_failed", organ);
                break;
            case AutopsyTypeCraft.PocketExtract:
                headline = Loc.Fmt("body.result.pocket_taken", customName);
                break;
            case AutopsyTypeCraft.Embalm:
                headline = Loc.Fmt("body.result.embalmed", customName);
                break;
            default:
                return null;
        }

        // What drops is not named again: the game hands it straight to the player's inventory,
        // which the pickup announcement already covers.
        var parts = new List<string> { headline, BodySkulls(p.Table) };
        parts.RemoveAll(string.IsNullOrWhiteSpace);
        return string.Join(". ", parts);
    }

    /// <summary>
    /// The body's skulls after the operation - what it will now do to the graveyard's rating, which
    /// is why the organ was cut out. Summed the way <c>UICorpseWidgetData</c> does it for the panel.
    /// </summary>
    private static string BodySkulls(WgoData table)
    {
        var inventory = table?.Inventory?.Data?.Inventory;
        if (inventory == null) return null;
        foreach (var body in inventory)
        {
            if (body?.Definition == null || !body.Definition.itemGroupIds.Contains("body")) continue;
            int red = 0, white = 0;
            foreach (var item in body.Inventory)
            {
                if (item?.Definition == null) continue;
                red += item.Definition.redSkulls * item.Count;
                white += item.Definition.whiteSkulls * item.Count;
            }
            return Loc.Fmt("body.skulls", Mathf.Clamp(white, 0, 999), Loc.Fmt("body.red_skulls", Mathf.Clamp(red, 0, 999)));
        }
        return null;
    }

    private static string DescribeTable(string rawHeader, string fallbackHeaderKey, bool isEmpty, UICorpseWidgetData corpse)
    {
        var header = TmpText.Clean(rawHeader);
        var parts = new List<string> { string.IsNullOrWhiteSpace(header) ? Loc.Get(fallbackHeaderKey) : header };
        if (isEmpty || corpse == null)
        {
            parts.Add(Loc.Get("body.table_empty"));
            return string.Join(". ", parts);
        }

        parts.Add(Skulls(corpse));

        if (corpse.IsZombie && corpse.CollarRedSkullsLimit >= 0)
            parts.Add(Loc.Fmt("body.collar_limit", corpse.CollarRedSkullsLimit));

        var body = corpse.Body;
        if (body != null)
        {
            parts.Add(OrgansLine(body));

            var pockets = new List<string>();
            foreach (var item in body.Inventory)
            {
                if (item == null || item.IsEmpty || item.Definition == null || item.Definition.isMainOrgan) continue;
                pockets.Add(item.Count > 1 ? Loc.Fmt("inventory.item_many", ItemText.Name(item.id), item.Count) : ItemText.Name(item.id));
            }
            parts.Add(pockets.Count == 0 ? Loc.Get("body.pockets_empty") : Loc.Fmt("body.pockets", string.Join(", ", pockets)));
        }

        parts.Add(Loc.Fmt("body.take_hint", _takeBodyKey.Value.ToString()));
        return string.Join(". ", parts);
    }

    private static string DescribeGrave(UIGraveWindowData data)
    {
        if (data == null) return null;

        var parts = new List<string>
        {
            data.WgoData?.id == null ? TmpText.Clean(LLBase.L("ui_grave")) : ObjectNames.Of(data.WgoData.id),
            Loc.Fmt("body.grave_quality", Number(data.Quality)),
        };

        var corpse = data.CorpseWidgetData;
        parts.Add(corpse == null || corpse.IsEmpty ? Loc.Get("body.grave_no_body") : Skulls(corpse));

        parts.Add(GraveElement(data.TombstoneWidgetData));
        parts.Add(GraveElement(data.FenceWidgetData));

        if (corpse != null && !corpse.IsEmpty)
            parts.Add(corpse.ButtonInteractable
                ? Loc.Fmt("body.exhume_hint", _takeBodyKey.Value.ToString())
                : TmpText.Clean(corpse.DescriptionText));

        parts.RemoveAll(string.IsNullOrWhiteSpace);
        return string.Join(". ", parts);
    }

    /// <summary>"Organs: heart: human heart, brain: unknown, …" for a body item. Also used by the resurrection table.</summary>
    internal static string OrgansLine(Item body)
    {
        if (body == null) return null;
        var inventory = new Inventory(body);
        var organs = new List<string>();
        foreach (var type in LazyConsts.MAIN_ORGANS_TYPES)
        {
            Item organ = null;
            if (inventory.Data.HasItemsByItemType(type)) organ = inventory.Data.GetItemByType(type);
            organs.Add(OrganSlot(type, organ));
        }
        return Loc.Fmt("body.organs", string.Join(", ", organs));
    }

    private static string Skulls(UICorpseWidgetData corpse)
    {
        var red = corpse.CollarRedSkullsLimit >= 0
            ? Loc.Fmt("body.red_skulls_of", corpse.RedSkulls, corpse.CollarRedSkullsLimit)
            : Loc.Fmt("body.red_skulls", corpse.RedSkulls);
        return Loc.Fmt("body.skulls", corpse.WhiteSkulls, red);
    }

    private static string GraveElement(UIGraveElementWidgetData element)
    {
        if (element == null) return null;

        var slot = Loc.Get(element.GraveElementType == GraveElementType.Top ? "body.tombstone" : "body.fence");
        if (!element.IsEmpty && element.GraveElementItem != null)
            return Loc.Fmt("body.grave_element", slot, ItemText.Name(element.GraveElementItem.id), element.Quality);

        return Loc.Fmt("body.grave_element_empty", slot, TmpText.Clean(LLBase.L(element.EmptyDescriptionLocale)));
    }

    private static string Number(float value) =>
        Mathf.Approximately(value, Mathf.Round(value)) ? Mathf.RoundToInt(value).ToString() : value.ToString("0.#");

    // ---- cells ------------------------------------------------------------------------------

    /// <summary>
    /// "Heart: human heart", "Brain: unknown", "Tombstone: empty, needs shovel" - or null when the
    /// cell is not an organ slot or a grave part, so the ordinary item-cell reading applies.
    /// </summary>
    internal static string DescribeCell(UIItemCell cell)
    {
        if (cell == null) return null;

        try
        {
            var element = cell.GetComponentInParent<UIGraveElementWidget>();
            if (element != null)
            {
                var data = GraveElementData(element);
                if (data == null) return null;
                var text = GraveElement(data);
                if (!data.HasRequiredTool && data.RequiredTool != ItemType.None)
                    text = Loc.Fmt("body.needs_tool", text, ItemTypeName(data.RequiredTool));
                return text;
            }

            var fixedCell = cell.GetComponentInParent<UIFixedTypeItemCell>();
            if (fixedCell != null && LazyConsts.MAIN_ORGANS_TYPES.Contains(fixedCell.ItemType))
            {
                var shown = cell.DisplayingItem;
                return OrganSlot(fixedCell.ItemType, shown != null && !shown.IsEmpty ? shown : null);
            }
        }
        catch (Exception ex)
        {
            Plugin.Log?.LogWarning($"[Body] Could not describe cell '{cell.name}': {ex.Message}");
        }

        return null;
    }

    /// <summary>
    /// One organ slot. An organ type the player has not learned is "unknown" whether or not the body
    /// has one - exactly what the question-mark picture tells a sighted player, and no more.
    /// </summary>
    private static string OrganSlot(ItemType type, Item organ)
    {
        var slot = ItemTypeName(type);
        if (!OrganKnown(type)) return Loc.Fmt("body.organ_unknown", slot);
        if (organ == null || organ.IsEmpty) return Loc.Fmt("body.organ_missing", slot);

        var name = ItemText.Name(organ.id);
        if (organ.Definition != null && organ.Definition.isOrganMistake)
            return Loc.Fmt("body.organ_mistake", slot, name);
        return Loc.Fmt("body.organ", slot, name);
    }

    private static bool OrganKnown(ItemType type)
    {
        try
        {
            return MainGame.Instance.GameSave.knowledgeSystem.unlockedOrgans.Contains(type);
        }
        catch
        {
            return true;
        }
    }

    internal static string ItemTypeName(ItemType type) =>
        Loc.Find("itemtype." + type) ?? Navigator.Humanise(type.ToString());

    // ---- keys -------------------------------------------------------------------------------

    /// <summary>F8 on one of these windows. False when neither is on top.</summary>
    internal static bool TryRepeat()
    {
        var window = LazyWindowsStackController.ActiveWindow;
        if (!(window is UIAutopsyWindow) && !(window is UIGraveWindow) && !(window is UIEmbalmWindow)) return false;
        if (TryRepeatStop()) return true;
        if (string.IsNullOrEmpty(_current)) return false;
        ScreenReader.Say(_current);
        return true;
    }

    /// <summary>The corpse panel of the window on top, or false when it has none.</summary>
    private static bool Corpse(LazyWidgetBase window, out UICorpseWidget widget, out UICorpseWidgetData data)
    {
        switch (window)
        {
            case UIAutopsyWindow autopsy:
                widget = AutopsyCorpse(autopsy);
                data = AutopsyData(autopsy)?.CorpseWidgetData;
                return true;
            case UIGraveWindow grave:
                widget = GraveCorpse(grave);
                data = GraveData(grave)?.CorpseWidgetData;
                return true;
            case UIEmbalmWindow embalm:
                widget = EmbalmCorpse(embalm);
                data = EmbalmData(embalm)?.CorpseWidgetData;
                return true;
            case UIResurrectionWindow resurrection:
                widget = ResurrectionCorpse(resurrection);
                data = ResurrectionData(resurrection)?.CorpseWidgetData;
                return true;
            default:
                widget = null;
                data = null;
                return false;
        }
    }

    /// <summary>The take-body / exhume key. False when it is not pressed or no such window is on top.</summary>
    internal static bool TryTakeBody()
    {
        if (_takeBodyKey == null || !_takeBodyKey.Value.IsDown()) return false;
        if (!Corpse(LazyWindowsStackController.ActiveWindow, out var widget, out var data)) return false;
        Take(widget, data);
        return true;
    }

    private static void Take(UICorpseWidget widget, UICorpseWidgetData data)
    {
        try
        {
            if (widget == null || data == null || data.IsEmpty)
            {
                ScreenReader.Say(Loc.Get("body.no_body"));
                return;
            }
            if (!data.ButtonInteractable)
            {
                ScreenReader.Say(TmpText.Clean(data.DescriptionText) ?? Loc.Get("body.cannot_take"));
                return;
            }

            _onTakeStop = false;
            Plugin.Log?.LogInfo("[Body] Pressing the take-body button.");
            widget.OnButtonPressed();
        }
        catch (Exception ex)
        {
            Plugin.Log?.LogError($"[Body] Taking the body failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    // ---- the "take body" stop ---------------------------------------------------------------
    //
    // The panel's "Leiche nehmen" / "Exhumieren" button is a mouse button: in gamepad mode the game
    // hides it behind a controller hint (GameKey.ExtractBody), and it is no navigation item. So the
    // mod adds one stop above the window's own: Up arrow from the top row lands on it, Enter presses
    // it, Down goes back to where focus was. The game's focus is taken off the item underneath while
    // the stop is current, so Enter and Space cannot act on an organ by accident.

    private static bool _onTakeStop;
    private static GamepadNavigationItem _returnItem;

    private static string TakeStopText(UICorpseWidgetData data)
    {
        if (data == null || data.IsEmpty) return Loc.Get("body.no_body");
        var label = TmpText.Clean(data.ButtonText);
        if (string.IsNullOrWhiteSpace(label)) label = Loc.Get("body.take_button");
        return data.ButtonInteractable
            ? Loc.Fmt("body.take_stop", label)
            : Loc.Fmt("body.take_stop_disabled", label, TmpText.Clean(data.DescriptionText) ?? Loc.Get("body.cannot_take"));
    }

    /// <summary>
    /// An arrow in a body window. True when the take-body stop used it: Up from the top row, or
    /// any arrow while on the stop.
    /// </summary>
    internal static bool TryArrow(GamepadNavigationController controller, GUIDirection direction)
    {
        if (!Corpse(LazyWindowsStackController.ActiveWindow, out _, out var data))
        {
            _onTakeStop = false;
            return false;
        }

        try
        {
            if (_onTakeStop)
            {
                if (direction == GUIDirection.Up)
                {
                    ScreenReader.Say(TakeStopText(data));
                    return true;
                }
                _onTakeStop = false;
                if (_returnItem != null && _returnItem.isActiveAndEnabled) controller.SetFocusedItem(_returnItem);
                else controller.FocusOnFirstActive();
                return true;
            }

            if (direction != GUIDirection.Up || data == null || data.IsEmpty) return false;

            // Up from the top row: the game either leaves focus where it is or wraps to the bottom.
            // Neither is wanted here, so the wrap is switched off for this one step.
            var before = controller.FocusedItem;
            var loop = controller.loopVerticalNavigation;
            controller.loopVerticalNavigation = false;
            try { controller.Navigate(direction); }
            finally { controller.loopVerticalNavigation = loop; }
            if (before != null && controller.FocusedItem != before) return true;

            _onTakeStop = true;
            _returnItem = controller.FocusedItem ?? before;
            _returnItem?.Unfocus();
            ScreenReader.Say(TakeStopText(data));
            return true;
        }
        catch (Exception ex)
        {
            Plugin.Log?.LogError($"[Body] Take-body stop failed: {ex.GetType().Name}: {ex.Message}");
            _onTakeStop = false;
            return false;
        }
    }

    /// <summary>Enter on the take-body stop. False when the stop is not current.</summary>
    internal static bool TryEnter()
    {
        if (!_onTakeStop) return false;
        if (!Corpse(LazyWindowsStackController.ActiveWindow, out var widget, out var data))
        {
            _onTakeStop = false;
            return false;
        }
        Take(widget, data);
        return true;
    }

    /// <summary>F8 on the stop says the stop again.</summary>
    private static bool TryRepeatStop()
    {
        if (!_onTakeStop || !Corpse(LazyWindowsStackController.ActiveWindow, out _, out var data)) return false;
        ScreenReader.Say(TakeStopText(data));
        return true;
    }
}
