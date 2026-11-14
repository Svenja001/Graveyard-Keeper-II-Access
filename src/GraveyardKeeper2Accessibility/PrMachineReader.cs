namespace GraveyardKeeper2Accessibility;

/// <summary>
/// The PR machine window, reworked in game update 1.012 (<c>UIPanicReductionMachineWindow</c>).
///
/// <para>
/// It has one navigation item: the load cell, drawn empty, which opens a picker for hypno-devices
/// and newspapers (on the Day of Gluttony only). Everything else - whether loading is possible
/// today, how many days the campaign has left, the next upgrade and what it costs in graveyard
/// quality - is plain labels that focus narration never reaches. The upgrade button is hidden in
/// gamepad mode behind <c>GameKey.SurveyStart</c>, a controller button with no keyboard binding.
/// </para>
///
/// <para>
/// So the window is read from its data when it draws, the load cell is named for what it does,
/// and the upgrade gets a stop of its own: any arrow moves between the load cell and the upgrade,
/// Enter presses it (see the user's rule in [[gk2-hidden-buttons-become-arrow-stops]]).
/// </para>
/// </summary>
internal static class PrMachineReader
{
    internal static bool Ready { get; private set; }

    private static class G
    {
        internal static readonly AccessTools.FieldRef<LazyWidget<UIPanicReductionMachineWindowData>, UIPanicReductionMachineWindowData> Data =
            AccessTools.FieldRefAccess<LazyWidget<UIPanicReductionMachineWindowData>, UIPanicReductionMachineWindowData>("data");

        internal static readonly AccessTools.FieldRef<UIPanicReductionMachineWindow, UIItemCell> LoadCell =
            AccessTools.FieldRefAccess<UIPanicReductionMachineWindow, UIItemCell>("loadResourcesCell");
    }

    private static bool _onStop;
    private static GamepadNavigationItem _returnItem;
    private static UIPanicReductionMachineWindowData _readData;
    private static string _lastLine;

    internal static void Init()
    {
        try
        {
            System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(typeof(G).TypeHandle);
            Ready = true;
        }
        catch (Exception ex)
        {
            Plugin.Log?.LogError($"[PR] The PR machine window is not supported by this game version: " +
                                 $"{(ex.InnerException ?? ex).GetType().Name}: {(ex.InnerException ?? ex).Message}");
        }
    }

    internal static bool SpeaksForItself(LazyWidgetBase window) => Ready && window is UIPanicReductionMachineWindow;

    internal static void Reset()
    {
        _onStop = false;
        _returnItem = null;
    }

    private static UIPanicReductionMachineWindow ActiveWindow() =>
        Ready && LazyWindowsStackController.ActiveWindow is UIPanicReductionMachineWindow w && w.IsShown ? w : null;

    /// <summary>
    /// Read on every draw. The window redraws after loading items and after an upgrade, so the
    /// player hears the new day count or limit; an identical redraw stays quiet.
    /// </summary>
    [HarmonyPatch(typeof(UIPanicReductionMachineWindow), nameof(UIPanicReductionMachineWindow.Redraw))]
    [HarmonyPostfix]
    private static void Redraw_Postfix(UIPanicReductionMachineWindow __instance)
    {
        try
        {
            var data = G.Data(__instance);
            if (data == null) return;

            var opening = !ReferenceEquals(data, _readData);
            _readData = data;
            if (opening) Reset();

            var line = Summary(data);
            if (!opening && line == _lastLine) return;
            _lastLine = line;

            var text = opening
                ? $"{Loc.Get("window.UIPanicReductionMachineWindow")}. {line}. {Loc.Get("prmachine.keys")}"
                : line;
            Plugin.Log?.LogInfo($"[PR] \"{text}\"");
            ScreenReader.Say(text);
        }
        catch (Exception ex)
        {
            Plugin.Log?.LogError($"[PR] Reading the PR machine failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>"Running, 4 days left. Loading possible today. Upgrade: zombie limit to 15, needs ..."</summary>
    private static string Summary(UIPanicReductionMachineWindowData data)
    {
        var parts = new List<string>
        {
            data.LeftDays > 0 ? Loc.Fmt("prmachine.days_left", data.LeftDays) : Loc.Get("prmachine.idle"),
            TmpText.Clean(data.LoadDescriptionText),
            UpgradeText(data),
        };
        return string.Join(". ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));
    }

    private static string UpgradeText(UIPanicReductionMachineWindowData data)
    {
        if (!data.HasUpgrade) return TmpText.Clean(LLBase.L("pr_machine_max_upgraded"));

        var what = Loc.Fmt("prmachine.upgrade", data.UpgradedLimitValue, Cost(data));
        return data.CanUpgrade ? what : $"{what}, {Loc.Get("prmachine.not_enough")}";
    }

    /// <summary>The upgrade's price, with what the player has of each: "graveyard 40 of 25".</summary>
    private static string Cost(UIPanicReductionMachineWindowData data)
    {
        var atoms = data.UpgradePrMachineDef?.needRes?.List;
        if (atoms == null) return TmpText.Clean(data.UpgradeCostText);

        var parts = new List<string>();
        foreach (var atom in atoms)
        {
            if (atom == null || string.IsNullOrEmpty(atom.type)) continue;
            var have = Mathf.FloorToInt(MainGame.PlayerData?.GetRes(atom.type) ?? 0f);
            parts.Add(Loc.Fmt("prmachine.cost_part", ResName(atom.type), Mathf.RoundToInt(atom.value), have));
        }
        return parts.Count > 0 ? string.Join(", ", parts) : TmpText.Clean(data.UpgradeCostText);
    }

    private static string ResName(string type)
    {
        var zone = MainGame.WorldData?.GetWorldZoneDataById(type);
        if (zone != null)
        {
            var name = TmpText.Clean(LLBase.L("wz_" + type));
            if (!string.IsNullOrWhiteSpace(name) && name != "wz_" + type) return name;
        }
        return Loc.Find("icon." + type) ?? TmpText.Clean(LLBase.L(type));
    }

    /// <summary>The load cell, named for what Enter does on it. Null for any other control.</summary>
    internal static string DescribeWidget(GamepadNavigationItem item)
    {
        var window = ActiveWindow();
        if (window == null || item == null) return null;
        try
        {
            var cell = G.LoadCell(window);
            if (cell == null || cell.GamepadNavigationItem != item) return null;
            var data = G.Data(window);
            if (data == null) return null;

            var days = data.LeftDays > 0 ? Loc.Fmt("prmachine.days_left", data.LeftDays) : Loc.Get("prmachine.idle");
            return data.CanLoadResources
                ? Loc.Fmt("prmachine.load_cell", days)
                : Loc.Fmt("prmachine.load_cell_closed", days, TmpText.Clean(data.LoadDescriptionText));
        }
        catch (Exception ex)
        {
            Plugin.Log?.LogWarning($"[PR] Could not name the load cell: {ex.Message}");
            return null;
        }
    }

    private static bool OnStop(out UIPanicReductionMachineWindow window)
    {
        window = ActiveWindow();
        if (!_onStop) return false;
        if (window == null || window.GetComponentInChildren<GamepadNavigationController>()?.FocusedItem != null)
        {
            Reset();
            return false;
        }
        return true;
    }

    private static string StopText(UIPanicReductionMachineWindow window)
    {
        var data = G.Data(window);
        var label = TmpText.Clean(LLBase.L("pr_machine_upgrade"));
        if (string.IsNullOrWhiteSpace(label) || label == "pr_machine_upgrade") label = Loc.Get("prmachine.upgrade_button");
        if (data == null) return label;
        if (!data.HasUpgrade) return Loc.Fmt("body.take_stop_disabled", label, TmpText.Clean(LLBase.L("pr_machine_max_upgraded")));
        var what = Loc.Fmt("prmachine.upgrade", data.UpgradedLimitValue, Cost(data));
        return data.CanUpgrade
            ? $"{Loc.Fmt("body.take_stop", label)}. {what}"
            : Loc.Fmt("body.take_stop_disabled", label, $"{what}, {Loc.Get("prmachine.not_enough")}");
    }

    /// <summary>
    /// An arrow in the PR machine window. The load cell is the only item the game navigates, so
    /// every arrow moves between it and the upgrade stop.
    /// </summary>
    internal static bool TryArrow(GamepadNavigationController controller, GUIDirection direction)
    {
        try
        {
            if (OnStop(out var window))
            {
                var back = _returnItem;
                Reset();
                if (back != null && back.isActiveAndEnabled) controller.SetFocusedItem(back);
                else controller.FocusOnFirstActive();
                return true;
            }
            if (window == null) return false;

            _returnItem = controller.FocusedItem;
            _returnItem?.Unfocus();
            _onStop = true;
            ScreenReader.Say(StopText(window));
            return true;
        }
        catch (Exception ex)
        {
            Plugin.Log?.LogError($"[PR] Upgrade stop failed: {ex.GetType().Name}: {ex.Message}");
            Reset();
            return false;
        }
    }

    /// <summary>Enter on the upgrade stop. False when the stop is not current.</summary>
    internal static bool TryPress()
    {
        if (!OnStop(out var window)) return false;
        try
        {
            var data = G.Data(window);
            if (data == null || !data.HasUpgrade || !data.CanUpgrade)
            {
                ScreenReader.Say(StopText(window));
                return true;
            }
            Plugin.Log?.LogInfo($"[PR] Upgrading to a zombie limit of {data.UpgradedLimitValue}.");
            // The same action the hidden button and the controller key call; the window redraws,
            // and the redraw reads the new state.
            data.OnUpgradePressedAction?.Invoke();
            OnStop(out _);
            return true;
        }
        catch (Exception ex)
        {
            Plugin.Log?.LogError($"[PR] Upgrading the PR machine failed: {ex.GetType().Name}: {ex.Message}");
            return true;
        }
    }

    /// <summary>F8: the stop, or the whole window again.</summary>
    internal static bool TryRepeat()
    {
        var window = ActiveWindow();
        if (window == null) return false;
        if (OnStop(out _))
        {
            ScreenReader.Say(StopText(window));
            return true;
        }
        var data = G.Data(window);
        if (data == null) return false;
        ScreenReader.Say(Summary(data));
        return true;
    }
}
