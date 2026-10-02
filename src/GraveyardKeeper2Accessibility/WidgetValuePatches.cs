using TMPro;

namespace GraveyardKeeper2Accessibility;

/// <summary>
/// Says a control's new value as it changes, for the controls that change without focus moving.
///
/// <see cref="UiNarrator"/> speaks whatever focus lands on, which covers buttons and list rows
/// completely. It cannot cover a slider or a switch: adjusting one leaves focus exactly where it
/// was - that is what those controls are - so no focus event fires and there is nothing to
/// re-read. Each such widget needs a hook on its own change.
///
/// Patched at the one method each widget routes every change through:
///
/// <list type="bullet">
/// <item><c>UISlider.UpdateValue</c> - the settings window's four volume sliders.</item>
/// <item><c>UISwitchButton.Apply</c> - every left/right option switch: language, voice-over mode,
/// graphics tier, resolution, fullscreen, v-sync, FPS lock, cursor mode.</item>
/// <item><c>SmartSlider</c> - the "how many?" quantity dialog (<c>UIItemCountWindow</c>).</item>
/// </list>
///
/// All of them share one shape: announce on change, stay silent while the window is seeding its
/// starting values, and dedupe per instance so a redraw does not repeat itself.
/// </summary>
internal static class WidgetValuePatches
{
    /// <summary>
    /// Last value spoken per slider instance, so a window redrawing itself does not re-announce a
    /// value that has not moved. Keyed on instance id rather than the object, because this must
    /// not keep destroyed widgets alive.
    /// </summary>
    private static readonly Dictionary<int, string> LastSpoken = new();

    /// <summary>
    /// Says a slider value, if it is new for this slider. Always interrupts: holding a key down
    /// produces a value per frame, and a queue of stale numbers is worse than silence.
    /// </summary>
    private static void Announce(int instanceId, string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;

        if (LastSpoken.TryGetValue(instanceId, out var previous) && previous == value) return;
        LastSpoken[instanceId] = value;

        ScreenReader.Say(TmpText.Clean(value));
    }

    /// <summary>
    /// The settings window's volume sliders. <c>UpdateValue</c> is the single point every change
    /// passes through, whether it came from a key, the arrow buttons or the mouse.
    ///
    /// Gated on the row actually having focus - the same condition <c>UISlider.Update</c> uses to
    /// decide whether the keys apply to it. Without it, opening the settings window would read out
    /// four volume levels nobody asked for, because <c>Initialize</c> sets each slider's starting
    /// value through this same method.
    /// </summary>
    [HarmonyPatch(typeof(UISlider), "UpdateValue")]
    [HarmonyPostfix]
    private static void UISlider_UpdateValue(
        UISlider __instance,
        TextMeshProUGUI ___amountLabel,
        GamepadNavigationItem ___gamepadNavigationItem)
    {
        try
        {
            if (___gamepadNavigationItem == null || !___gamepadNavigationItem.IsFocused) return;
            Announce(__instance.GetInstanceID(), ___amountLabel == null ? null : ___amountLabel.text);
        }
        catch (Exception ex)
        {
            Plugin.Log?.LogError($"[Slider] UISlider announce failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>
    /// Every option switch in the settings window - the ones that step left and right through a
    /// list of choices rather than a number. <c>Apply</c> is where all of them land: it is what
    /// writes the new choice into the label, whether the change came from a key or from the little
    /// arrow buttons either side.
    ///
    /// Gated on focus for the same reason as the sliders: <c>Initialize</c> seeds each switch's
    /// starting choice through this method, so without the guard, opening the settings window
    /// would recite every option on it.
    ///
    /// Note this deliberately does not also hook <c>ReinitLabels</c>, which rewrites the labels of
    /// *every* switch when the language changes. Those are the same choices in a new language, not
    /// new choices, and announcing them would turn one language switch into eight announcements.
    /// </summary>
    [HarmonyPatch(typeof(UISwitchButton), "Apply")]
    [HarmonyPostfix]
    private static void UISwitchButton_Apply(
        UISwitchButton __instance,
        TextMeshProUGUI ___amountLabel,
        GamepadNavigationItem ___gamepadNavigationItem)
    {
        try
        {
            if (___gamepadNavigationItem == null || !___gamepadNavigationItem.IsFocused) return;
            Announce(__instance.GetInstanceID(), ___amountLabel == null ? null : ___amountLabel.text);
        }
        catch (Exception ex)
        {
            Plugin.Log?.LogError($"[Switch] UISwitchButton announce failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>
    /// The quantity dialog, when something other than the slider handle moved the value - the
    /// plus/minus buttons, the text field, or a key.
    ///
    /// <c>invokeCallback</c> is false exactly when <c>Open</c> is seeding the starting value, which
    /// is the one case that must stay silent.
    /// </summary>
    [HarmonyPatch(typeof(SmartSlider), "SetValue")]
    [HarmonyPostfix]
    private static void SmartSlider_SetValue(SmartSlider __instance, bool invokeCallback)
    {
        try
        {
            if (!invokeCallback) return;
            Announce(__instance.GetInstanceID(), ItemCountReader.DescribeValue(__instance) ?? __instance.Value.ToString());
        }
        catch (Exception ex)
        {
            Plugin.Log?.LogError($"[Slider] SmartSlider announce failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>
    /// The quantity dialog, when the slider handle itself moved. A separate path from
    /// <c>SetValue</c> - it writes the slider without notifying - so it needs its own hook, and
    /// the two cannot double-fire for one change.
    /// </summary>
    [HarmonyPatch(typeof(SmartSlider), "OnSliderChanged")]
    [HarmonyPostfix]
    private static void SmartSlider_OnSliderChanged(SmartSlider __instance)
    {
        try
        {
            Announce(__instance.GetInstanceID(), ItemCountReader.DescribeValue(__instance) ?? __instance.Value.ToString());
        }
        catch (Exception ex)
        {
            Plugin.Log?.LogError($"[Slider] SmartSlider announce failed: {ex.GetType().Name}: {ex.Message}");
        }
    }
}
