using TMPro;

namespace GraveyardKeeper2Accessibility;

/// <summary>
/// The tutorial popups - the windows that explain how the game works, and therefore the ones a new
/// player can least afford to miss.
///
/// <para>
/// These cannot be read from a data object the way the confirmation boxes can.
/// <c>UITutorialWindowData</c> carries only a page <i>id</i>; the page itself is a prefab
/// <c>GameObject</c> that the window switches on by name, and its words live in whatever labels
/// happen to be under it. So this one is read from the scene - but only from the single page that
/// is currently visible, which keeps it honest.
/// </para>
///
/// <para>
/// The hook is <c>UpdateButtons</c> rather than <c>Open</c>, because it is the one method called
/// after the page has been made visible in all three cases that matter: opening the window, and
/// paging forward or back through a multi-page tutorial. Hooking <c>Open</c> would read the first
/// page and then go quiet for every page after it.
/// </para>
/// </summary>
internal static class TutorialReader
{
    private static string _lastSpoken;

    [HarmonyPatch(typeof(UITutorialWindow), "UpdateButtons")]
    [HarmonyPostfix]
    private static void UITutorialWindow_UpdateButtons(
        TextMeshProUGUI ___headerLabel,
        List<GameObject> ___currentTutorialPages,
        int ___currentPageIndex)
    {
        try
        {
            if (___currentTutorialPages == null || ___currentTutorialPages.Count == 0) return;
            if (___currentPageIndex < 0 || ___currentPageIndex >= ___currentTutorialPages.Count) return;

            var page = ___currentTutorialPages[___currentPageIndex];
            if (page == null) return;

            var parts = new List<string>();
            Add(parts, ___headerLabel == null ? null : ___headerLabel.text);

            foreach (var label in page.GetComponentsInChildren<TMP_Text>(includeInactive: false))
                Add(parts, label == null ? null : label.text);

            if (parts.Count == 0) return;

            // "Page 2 of 3" only when there is more than one, and at the end, so the explanation
            // is not delayed by bookkeeping the player may not need.
            if (___currentTutorialPages.Count > 1)
                parts.Add(Loc.Fmt("tutorial.page", ___currentPageIndex + 1, ___currentTutorialPages.Count));

            var line = string.Join(". ", parts);
            if (line == _lastSpoken) return;

            _lastSpoken = line;
            Plugin.Log?.LogInfo($"[Tutorial] page {___currentPageIndex + 1}/{___currentTutorialPages.Count}: \"{line}\"");
            ScreenReader.Say(line);
        }
        catch (Exception ex)
        {
            Plugin.Log?.LogError($"[Tutorial] Failed to read the tutorial page: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static readonly AccessTools.FieldRef<UITutorialWindow, bool> OkAvailable =
        AccessTools.FieldRefAccess<UITutorialWindow, bool>("isOkBtnAvailable");

    private static readonly AccessTools.FieldRef<UITutorialWindow, LazyButton> NextButton =
        AccessTools.FieldRefAccess<UITutorialWindow, LazyButton>("nextButton");

    /// <summary>
    /// Enter on a tutorial: the next page, or close once the game allows closing - on the last
    /// page, or any page of a tutorial flagged <c>CanCloseFromAnyPage</c>. The window's own OK
    /// listens for <c>GameKey.Select</c>, which has no keyboard key, so without this only Escape
    /// worked. False when no tutorial is on top.
    /// </summary>
    internal static bool TryConfirm()
    {
        var window = LazyWindowsStackController.ActiveWindow as UITutorialWindow;
        if (window == null) return false;

        try
        {
            if (OkAvailable(window))
            {
                window.Close();
                return true;
            }

            var next = NextButton(window);
            if (next != null && next.interactable) next.onClick?.Invoke();
        }
        catch (Exception ex)
        {
            Plugin.Log?.LogError($"[Tutorial] Enter failed: {ex.GetType().Name}: {ex.Message}");
        }
        return true;
    }

    private static readonly AccessTools.FieldRef<UITutorialWindow, LazyButton> PrevButton =
        AccessTools.FieldRefAccess<UITutorialWindow, LazyButton>("prevButton");

    /// <summary>
    /// Left and right arrows page through a tutorial. The window has no navigation items for the
    /// menu keys to move between, so they would otherwise do nothing. False when no tutorial is on top.
    /// </summary>
    internal static bool TryPage(int delta)
    {
        var window = LazyWindowsStackController.ActiveWindow as UITutorialWindow;
        if (window == null) return false;

        try
        {
            var button = delta > 0 ? NextButton(window) : PrevButton(window);
            if (button != null && button.interactable && button.gameObject.activeInHierarchy)
                button.onClick?.Invoke();
        }
        catch (Exception ex)
        {
            Plugin.Log?.LogError($"[Tutorial] Paging failed: {ex.GetType().Name}: {ex.Message}");
        }
        return true;
    }

    [HarmonyPatch(typeof(UITutorialWindow), nameof(UITutorialWindow.Close))]
    [HarmonyPostfix]
    private static void UITutorialWindow_Close() => _lastSpoken = null;

    private static void Add(List<string> parts, string raw)
    {
        var clean = TmpText.Clean(raw);
        if (string.IsNullOrWhiteSpace(clean) || parts.Contains(clean)) return;
        parts.Add(clean);
    }
}
