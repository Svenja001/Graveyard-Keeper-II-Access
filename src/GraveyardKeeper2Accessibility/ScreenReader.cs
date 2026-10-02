using System.Diagnostics;
using System.IO;

namespace GraveyardKeeper2Accessibility;

/// <summary>
/// The mod's voice. Ported from the Graveyard Keeper mod, minus the Tolk backend: GK2 is x64 only,
/// and Tolk existed there purely to cover GOG's 32-bit process, where Prism has no library to load.
/// Two backends remain - Prism (NVDA, JAWS, and a braille display when one is present) with the
/// Windows SAPI voice behind it, so the mod still says something on a machine with no screen reader.
/// </summary>
internal static class ScreenReader
{
    private static bool _prismAvailable;
    private static bool _sapiAvailable;
    private static Process _sapiProcess;
    private static StreamWriter _sapiStdin;
    private static string _lastMenuText = "";
    private static ManualLogSource _log;

    internal static void Init(ManualLogSource log)
    {
        _log = log;

        _prismAvailable = PrismWrapper.Init(log);
        if (!_prismAvailable)
            _sapiAvailable = InitSapi();

        if (!_prismAvailable && !_sapiAvailable)
            log.LogError("No TTS output available");
    }

    private static bool InitSapi()
    {
        try
        {
            var vbsPath = Path.Combine(Path.GetTempPath(), "gk2_accessibility_tts.vbs");
            File.WriteAllText(vbsPath,
                "Set v=CreateObject(\"SAPI.SpVoice\")\r\n" +
                "Do While Not WScript.StdIn.AtEndOfStream\r\n" +
                "On Error Resume Next\r\n" +
                "s=WScript.StdIn.ReadLine\r\n" +
                "If Len(s)>0 Then v.Speak s,3\r\n" +
                "On Error Goto 0\r\n" +
                "Loop\r\n");

            _sapiProcess = new Process();
            _sapiProcess.StartInfo = new ProcessStartInfo
            {
                FileName = "cscript.exe",
                Arguments = "//nologo \"" + vbsPath + "\"",
                UseShellExecute = false,
                RedirectStandardInput = true,
                CreateNoWindow = true
            };
            _sapiProcess.Start();
            _sapiStdin = _sapiProcess.StandardInput;
            _sapiStdin.AutoFlush = true;

            _log.LogInfo("SAPI voice process started");
            return true;
        }
        catch (Exception ex)
        {
            _log.LogError($"SAPI init failed: {ex.Message}");
            return false;
        }
    }

    internal static void Shutdown()
    {
        if (_prismAvailable)
            PrismWrapper.Shutdown();

        KillSapi();
        _prismAvailable = false;
        _sapiAvailable = false;
    }

    /// <summary>When we last said anything at all (unscaled time), for detecting stretches of silence.</summary>
    internal static float LastSpokenAt { get; private set; }

    /// <summary>
    /// True when output also reaches a braille display. Only a real screen reader can do this; the
    /// SAPI fallback is speech-only.
    /// </summary>
    internal static bool SupportsBraille => _prismAvailable && PrismWrapper.SupportsBraille;

    internal static bool Say(string text, bool interrupt = true)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;

        LastSpokenAt = Time.unscaledTime;

        // Prism routes this to speech *and* braille in one call when the screen reader supports
        // both, so everything the mod says is readable on a display without a second call site.
        if (_prismAvailable)
            return PrismWrapper.Speak(text, interrupt);

        return SapiSpeak(text);
    }

    /// <summary>
    /// Writes to the braille display only, leaving speech alone - for text worth reading but not
    /// worth interrupting speech for. No-op when there is no braille-capable backend.
    /// </summary>
    internal static bool Braille(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        return _prismAvailable && PrismWrapper.Braille(text);
    }

    private static bool SapiSpeak(string text)
    {
        if (_sapiProcess == null || _sapiProcess.HasExited)
        {
            _log?.LogWarning("[SAPI] Process died, restarting");
            KillSapi();
            _sapiAvailable = InitSapi();
            if (!_sapiAvailable) return false;
        }

        try
        {
            var clean = text.Replace("\r", "").Replace("\n", " ").Replace("\0", "");
            if (clean.Length > 500) clean = clean.Substring(0, 500);
            _sapiStdin.WriteLine(clean);
            _sapiStdin.Flush();
            return true;
        }
        catch (Exception ex)
        {
            _log?.LogWarning($"[SAPI] Write failed: {ex.Message}, restarting");
            KillSapi();
            _sapiAvailable = InitSapi();
            return false;
        }
    }

    private static void KillSapi()
    {
        try { _sapiStdin?.Close(); } catch { }
        try { if (_sapiProcess != null && !_sapiProcess.HasExited) _sapiProcess.Kill(); } catch { }
        _sapiProcess = null;
        _sapiStdin = null;
    }

    /// <summary>
    /// Speaks a line only when it differs from the last one said this way. Menu focus fires
    /// repeatedly for the same control - a window refreshing its widgets re-focuses the current
    /// item - and without this the reader stutters the same name over and over.
    /// </summary>
    internal static bool SayMenu(string text, bool interrupt = true)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        if (text == _lastMenuText) return false;
        _lastMenuText = text;
        return Say(text, interrupt);
    }

    /// <summary>
    /// Forgets the last menu line, so the next <see cref="SayMenu"/> speaks even if it repeats it.
    /// Called when a window opens or closes: the same control name means something different in a
    /// new context, and the player needs to hear it again.
    /// </summary>
    internal static void ClearMenuContext()
    {
        _lastMenuText = "";
    }
}
