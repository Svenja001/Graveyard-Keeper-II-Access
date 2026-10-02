using System.IO;

namespace GraveyardKeeper2Accessibility;

/// <summary>
/// Loads the mod's own spoken strings from <c>lang/GraveyardKeeper2Accessibility.&lt;code&gt;.json</c>
/// next to the DLL. Everything the mod says in its own voice - "main menu", "3 of 7", "locked" -
/// goes through <see cref="Get"/> or <see cref="Fmt"/>; text that comes out of the game is already
/// localised and must never be routed through here.
///
/// The rule carried over from the GK1 mod: <b>never match on a translated label</b>. Identify
/// things by id or type, translate only at the moment of speaking. GK2 makes that easy, because
/// every def id is also its locale key.
///
/// Ported from the GK1 mod with one change: the language comes from
/// <see cref="LLBase.CurrentLang"/> rather than GK1's GameSettings._cur_lng.
/// </summary>
internal static class Loc
{
    private static Dictionary<string, string> _translations = new();
    private static Dictionary<string, string> _fallback = new();
    // Normalized lang code -> file path, so "pt-br", "pt_BR" etc. all resolve to one file.
    private static readonly Dictionary<string, string> LangFiles = new();
    private static string _langDir;
    private static string _prefix;
    private static string _currentLang;
    private static ManualLogSource _log;

    /// <summary>Raised when the game language changes, for anything holding cached spoken text.</summary>
    internal static event Action OnLanguageChanged;

    internal static void Init(ManualLogSource log)
    {
        _log = log;
        var assembly = Assembly.GetExecutingAssembly();
        _langDir = Path.Combine(Path.GetDirectoryName(assembly.Location) ?? ".", "lang");
        _prefix = assembly.GetName().Name;

        IndexLangFiles();

        _fallback = LoadLang("en");
        if (_fallback.Count == 0)
            _log?.LogWarning("[Loc] No English fallback loaded - translations will return raw keys");

        Reload();
    }

    /// <summary>Current game language, or "en" before the game has settled on one.</summary>
    private static string CurrentGameLang()
    {
        try
        {
            var lang = LLBase.CurrentLang;
            return string.IsNullOrEmpty(lang) ? "en" : lang;
        }
        catch
        {
            // LL is not up yet during very early plugin Awake.
            return "en";
        }
    }

    /// <summary>
    /// True once the game has actually loaded a language table. <see cref="LLBase.CurrentLang"/>
    /// answers "en" before that happens rather than admitting it does not know, so anything spoken
    /// too early would silently come out English for a German player.
    /// </summary>
    internal static bool LanguageKnown
    {
        get
        {
            try { return LLBase.IsCurrentLangLoaded; }
            catch { return false; }
        }
    }

    internal static void Reload()
    {
        var lang = CurrentGameLang();
        if (_currentLang == lang) return;
        bool hadLang = _currentLang != null;
        _currentLang = lang;
        _translations = Normalize(lang) == "en" ? _fallback : LoadLang(lang);

        if (hadLang)
            OnLanguageChanged?.Invoke();
    }

    /// <summary>Looks up a spoken string. Falls back to English, then to the key itself.</summary>
    internal static string Get(string key)
    {
        var found = Find(key);
        if (found != null) return found;
        _log?.LogWarning($"[Loc] Missing key: {key}");
        return key;
    }

    /// <summary>
    /// Like <see cref="Get"/>, but returns null instead of the key when there is no translation and
    /// stays quiet about it. For lookups that are *expected* to miss and have their own fallback -
    /// e.g. naming a window after its class when we have not given that window a name of its own.
    /// </summary>
    internal static string Find(string key)
    {
        if (_currentLang != CurrentGameLang()) Reload();

        if (_translations.TryGetValue(key, out var value)) return value;
        if (_fallback.TryGetValue(key, out var fallback)) return fallback;
        return null;
    }

    /// <summary>
    /// Looks up a string containing <c>{0}</c>-style placeholders and fills them in. Translators can
    /// reorder the placeholders freely, which German word order regularly needs.
    /// </summary>
    internal static string Fmt(string key, params object[] args)
    {
        var format = Get(key);
        try
        {
            return string.Format(format, args);
        }
        catch (FormatException)
        {
            // A translation with a malformed placeholder should not silence the mod.
            _log?.LogWarning($"[Loc] Bad format string for key: {key}");
            return format;
        }
    }

    /// <summary>Picks the singular or plural form of a key ("<c>key.one</c>" / "<c>key.other</c>").</summary>
    internal static string Plural(string key, int count, params object[] args)
    {
        return Fmt(count == 1 ? key + ".one" : key + ".other", args);
    }

    // Duplicate normalized keys (e.g. pt-br.json and pt_BR.json) keep the first one found.
    private static void IndexLangFiles()
    {
        LangFiles.Clear();
        if (!Directory.Exists(_langDir))
        {
            _log?.LogWarning($"[Loc] Lang directory not found: {_langDir}");
            return;
        }

        foreach (var path in Directory.GetFiles(_langDir, $"{_prefix}.*.json"))
        {
            var name = Path.GetFileNameWithoutExtension(path);
            var prefix = _prefix + ".";
            if (name == null || !name.StartsWith(prefix)) continue;
            var key = Normalize(name.Substring(prefix.Length));
            if (LangFiles.ContainsKey(key))
            {
                _log?.LogWarning($"[Loc] Duplicate lang file for '{key}': keeping {Path.GetFileName(LangFiles[key])}, ignoring {Path.GetFileName(path)}");
                continue;
            }
            LangFiles[key] = path;
        }
    }

    private static Dictionary<string, string> LoadLang(string lang)
    {
        var key = Normalize(lang);
        if (!LangFiles.TryGetValue(key, out var path))
        {
            if (key != "en")
                _log?.LogInfo($"[Loc] No translation file for '{lang}' (normalized '{key}'), falling back to English");
            return new Dictionary<string, string>();
        }

        try
        {
            var dict = new Dictionary<string, string>();
            var json = File.ReadAllText(path, System.Text.Encoding.UTF8);

            // Minimal parser for a flat string->string object, so the mod has no hard dependency
            // on a JSON library at runtime.
            var i = json.IndexOf('{') + 1;
            while (i < json.Length)
            {
                var keyStart = json.IndexOf('"', i);
                if (keyStart < 0) break;
                var keyEnd = FindUnescapedQuote(json, keyStart + 1);
                var jsonKey = Unescape(json.Substring(keyStart + 1, keyEnd - keyStart - 1));

                var valStart = json.IndexOf('"', keyEnd + 1);
                if (valStart < 0) break;
                var valEnd = FindUnescapedQuote(json, valStart + 1);
                dict[jsonKey] = Unescape(json.Substring(valStart + 1, valEnd - valStart - 1));

                i = valEnd + 1;
            }

            _log?.LogInfo($"[Loc] Loaded {dict.Count} keys from {Path.GetFileName(path)}");
            return dict;
        }
        catch (Exception ex)
        {
            _log?.LogError($"[Loc] Failed to read {Path.GetFileName(path)}: {ex.Message}");
            return new Dictionary<string, string>();
        }
    }

    private static string Unescape(string s)
    {
        return s.Replace("\\\"", "\"").Replace("\\n", "\n").Replace("\\\\", "\\");
    }

    private static string Normalize(string code)
    {
        return string.IsNullOrEmpty(code) ? "en" : code.ToLowerInvariant().Replace('-', '_');
    }

    private static int FindUnescapedQuote(string s, int start)
    {
        for (var i = start; i < s.Length; i++)
        {
            if (s[i] == '\\') { i++; continue; }
            if (s[i] == '"') return i;
        }
        return s.Length;
    }
}
