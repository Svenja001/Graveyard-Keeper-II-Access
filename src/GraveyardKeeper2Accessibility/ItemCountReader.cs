namespace GraveyardKeeper2Accessibility;

/// <summary>
/// The "how many?" window (<c>UIItemCountWindow</c>) that opens when a stack is moved, sold or
/// bought in part.
///
/// <para>
/// It was silent on 2026-09-25: opened from a chest on "Brett, 4" and closed with nothing spoken.
/// It has no navigation items, so focus narration never reaches it, and its OK button listens for
/// <c>GameKey.Select</c> only while a gamepad is active. The slider itself already answers the
/// keyboard (left/right arrows or A/D change the amount, up/down or W/S jump to the most and the
/// least, E confirms, Q cancels), so this reader only says what the window is about, confirms on
/// Enter, and keeps the mod's arrow-key menu navigation away from the slider's keys.
/// </para>
/// </summary>
internal static class ItemCountReader
{
    internal static bool Ready { get; private set; }

    private static class G
    {
        internal static readonly AccessTools.FieldRef<LazyWidget<UIItemCountWindowData>, UIItemCountWindowData> Data =
            AccessTools.FieldRefAccess<LazyWidget<UIItemCountWindowData>, UIItemCountWindowData>("data");

        internal static readonly AccessTools.FieldRef<UIItemCountWindow, SmartSlider> Slider =
            AccessTools.FieldRefAccess<UIItemCountWindow, SmartSlider>("slider");

        internal static readonly AccessTools.FieldRef<UIItemCountWindow, UIDialogWindowButton> Ok =
            AccessTools.FieldRefAccess<UIItemCountWindow, UIDialogWindowButton>("okBtn");

        internal static readonly MethodInfo OnConfirm =
            AccessTools.Method(typeof(UIItemCountWindow), "OnConfirm") ?? throw new MissingMethodException("UIItemCountWindow.OnConfirm");
    }

    internal static void Init()
    {
        try
        {
            System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(typeof(G).TypeHandle);
            Ready = true;
        }
        catch (Exception ex)
        {
            Plugin.Log?.LogError($"[Count] The quantity window is not supported by this game version: " +
                                 $"{(ex.InnerException ?? ex).GetType().Name}: {(ex.InnerException ?? ex).Message}");
        }
    }

    private static UIItemCountWindow Active =>
        Ready ? LazyWindowsStackController.ActiveWindow as UIItemCountWindow : null;

    /// <summary>True while the quantity window is on top; the arrows then belong to its slider.</summary>
    internal static bool IsActive => Active != null;

    internal static bool SpeaksForItself(LazyWidgetBase window) => Ready && window is UIItemCountWindow;

    private static string ItemName(UIItemCountWindowData data)
    {
        var item = data?.Item;
        if (item == null) return null;
        return ItemText.Name(item.id) ?? TmpText.Clean(item.Definition?.GetHeader());
    }

    [HarmonyPatch(typeof(UIItemCountWindow), nameof(UIItemCountWindow.Open))]
    [HarmonyPostfix]
    private static void UIItemCountWindow_Open(UIItemCountWindow __instance)
    {
        try
        {
            var data = G.Data(__instance);
            var slider = G.Slider(__instance);
            if (data == null || slider == null) return;

            var parts = new List<string> { Loc.Fmt("count.open", ItemName(data), slider.Value, data.Max) };
            var price = Price(data, slider.Value);
            if (price != null) parts.Add(price);
            parts.Add(Loc.Get("count.hint"));
            Say(string.Join(". ", parts));
        }
        catch (Exception ex)
        {
            Plugin.Log?.LogError($"[Count] Reading the quantity window failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static string Price(UIItemCountWindowData data, int amount)
    {
        if (data == null || !data.IsForVendor || data.PriceCalculateDel == null) return null;
        return Loc.Fmt("count.price", Money.ToSpeech(data.PriceCalculateDel(amount)));
    }

    /// <summary>
    /// What to say when this slider's value changes: the number, "maximum" at the top, and the
    /// price at a vendor. Null when the slider is not the quantity window's.
    /// </summary>
    internal static string DescribeValue(SmartSlider slider)
    {
        var window = Active;
        if (window == null || G.Slider(window) != slider) return null;

        var data = G.Data(window);
        var value = slider.Value;
        var text = data != null && value == data.Max && data.Max > data.Min
            ? Loc.Fmt("count.max", value)
            : value.ToString();
        var price = Price(data, value);
        return price == null ? text : $"{text}, {price}";
    }

    /// <summary>Enter presses OK. False when the quantity window is not on top.</summary>
    internal static bool TryConfirm()
    {
        var window = Active;
        if (window == null) return false;

        try
        {
            var data = G.Data(window);
            var amount = G.Slider(window)?.Value ?? 0;
            var ok = G.Ok(window);
            if (ok != null && ok.LazyButton != null && !ok.LazyButton.interactable)
            {
                Say(Loc.Get("count.cannot"));
                return true;
            }

            ok?.LazyButton?.ForceOnClick();

            // A pooled button can lose its listener (see TreesReader.TryConfirm); confirm directly then.
            if (LazyWindowsStackController.ActiveWindow == window)
            {
                Plugin.Log?.LogInfo("[Count] OK did nothing; confirming directly.");
                G.OnConfirm.Invoke(window, null);
            }

            Say(Loc.Fmt("count.confirmed", amount, ItemName(data)));
        }
        catch (Exception ex)
        {
            Plugin.Log?.LogError($"[Count] Confirming failed: {ex.GetType().Name}: {ex.Message}");
        }
        return true;
    }

    private static void Say(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        Plugin.Log?.LogInfo($"[Count] \"{text}\"");
        ScreenReader.Say(text);
    }
}
