namespace GraveyardKeeper2Accessibility;

/// <summary>
/// Notes, letters and anything else read in <c>UINotesWindow</c> - the inquisitor's note in the
/// prison is the first.
///
/// <para>
/// The window is a title, a block of text and an OK button, and registers no navigation items, so
/// focus narration has nothing to say about it: it opened in silence. It is read from its data, like
/// the confirmation boxes - <c>Draw</c> is given the note's item, and the window itself shows
/// <c>LLBase.L(id)</c> as the title and <c>LLBase.L(id + "_note")</c> as the text, so the mod reads
/// the same two keys rather than scraping the labels.
/// </para>
///
/// <para>
/// The OK button listens for <c>GameKey.Select</c>, which has no keyboard binding - the same gap as
/// the confirmation boxes - so Enter is routed here to close it. F8 reads the note again.
/// </para>
/// </summary>
internal static class NotesReader
{
    private static string _current;

    [HarmonyPatch(typeof(UINotesWindow), nameof(UINotesWindow.Draw))]
    [HarmonyPostfix]
    private static void UINotesWindow_Draw(UINotesWindowData data)
    {
        try
        {
            var id = data?.NoteItem?.id;
            if (string.IsNullOrEmpty(id)) return;

            var parts = new List<string>();
            Add(parts, LLBase.L(id));
            Add(parts, LLBase.L(id + "_note"));
            if (parts.Count == 0) return;

            _current = string.Join(". ", parts);
            Plugin.Log?.LogInfo($"[Notes] '{id}': \"{_current}\"");
            ScreenReader.Say($"{_current}. {Loc.Get("notes.close_hint")}");
        }
        catch (Exception ex)
        {
            Plugin.Log?.LogError($"[Notes] Failed to read the note: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static void Add(List<string> parts, string text)
    {
        var clean = string.IsNullOrEmpty(text) ? null : TmpText.Clean(text);
        if (!string.IsNullOrWhiteSpace(clean)) parts.Add(clean.Trim());
    }

    private static UINotesWindow ActiveNote => LazyWindowsStackController.ActiveWindow as UINotesWindow;

    /// <summary>Reads the open note again. False when no note is on top.</summary>
    internal static bool TryRepeat()
    {
        if (ActiveNote == null || string.IsNullOrEmpty(_current)) return false;
        ScreenReader.Say(_current);
        return true;
    }

    /// <summary>Closes the open note, as its OK button would. False when no note is on top.</summary>
    internal static bool TryClose()
    {
        var window = ActiveNote;
        if (window == null) return false;

        try
        {
            window.Close();
        }
        catch (Exception ex)
        {
            Plugin.Log?.LogError($"[Notes] Could not close the note: {ex.GetType().Name}: {ex.Message}");
        }
        return true;
    }
}
