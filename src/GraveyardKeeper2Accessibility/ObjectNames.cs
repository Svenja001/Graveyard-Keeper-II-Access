namespace GraveyardKeeper2Accessibility;

/// <summary>
/// Spoken names for world objects the game has no text for.
///
/// <para>
/// <c>LLBase.L(id)</c> hands the id back when there is no entry, and most scenery has none: the log
/// of 2026-09-25 spoke "bush_dry_ivy_s", "tree_fir_clr1_m_v2_onetime", "bed", "grave_ground" and
/// "tp_RT_home_exit". The ids are consistent words joined by underscores, followed by size and
/// variant tags, so a name is found by trying ever shorter prefixes - "tree_fir_clr1_m_v2" …
/// "tree_fir" - first in the game's text, then in the mod's own word list (<c>obj.*</c>). Doorways
/// (<c>tp_…_enter/exit</c>) are named after where they lead. Anything still unknown is said as its
/// words without the tags, which beats spelling out the id.
/// </para>
/// </summary>
internal static class ObjectNames
{
    private static readonly Regex Noise =
        new(@"^(rt|clr\d*|v\d+|onetime|xxs|xs|s|m|l|xl|xxl|\d+|place|quest)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    internal static string Of(string id)
    {
        if (string.IsNullOrEmpty(id)) return id;

        // Blueprint desks are named after the area they build - "builder_yard" is just "Yard" - so
        // nothing in the name said this is the desk you build a sawhorse at.
        if (IsBuilderDesk(id))
        {
            var area = GameName(id) ?? string.Join(" ", id.Split('_').Skip(1));
            return Loc.Fmt("obj.builder_desk", area);
        }

        var own = GameName(id);
        if (own != null) return own;

        var words = id.Split('_').Where(w => w.Length > 0).ToList();
        if (words.Count > 0 && words[0].Equals("tp", StringComparison.OrdinalIgnoreCase)) return Doorway(words);

        // Fast-travel stones: "teleport_milestone_village".
        if (words.Count > 2 && words[0].Equals("teleport", StringComparison.OrdinalIgnoreCase) &&
            words[1].Equals("milestone", StringComparison.OrdinalIgnoreCase))
            return Loc.Fmt("obj.milestone", PlaceName(words.Skip(2).ToList()));

        // A single word is looked up in the mod's list only: in the game's text "common" or "bed"
        // alone can be some unrelated UI string.
        for (var n = words.Count; n >= 1; n--)
        {
            var prefix = string.Join("_", words.Take(n));
            var name = Loc.Find("obj." + prefix.ToLowerInvariant()) ?? (n >= 2 && n < words.Count ? GameName(prefix) : null);
            if (name != null) return name;
        }

        var kept = words.Where(w => !Noise.IsMatch(w)).ToList();

        // People without a name of their own: "npc_wispy" is Wispy, not "npc wispy".
        if (kept.Count > 1 && kept[0].Equals("npc", StringComparison.OrdinalIgnoreCase))
            return string.Join(" ", kept.Skip(1).Select(w => char.ToUpperInvariant(w[0]) + w.Substring(1)));

        return kept.Count == 0 ? id : string.Join(" ", kept);
    }

    /// <summary>A blueprint desk ("Planungstisch"): <c>builder_yard</c>, <c>builder_graveyard</c>, ...</summary>
    internal static bool IsBuilderDesk(string id) =>
        id != null && id.StartsWith("builder_", StringComparison.OrdinalIgnoreCase);

    /// <summary>The game's own name for an id, or null when it has none.</summary>
    private static string GameName(string id)
    {
        var name = TmpText.Clean(LLBase.L(id));
        return string.IsNullOrWhiteSpace(name) || name == id ? null : name;
    }

    private static string Doorway(List<string> words)
    {
        var lower = words.Select(w => w.ToLowerInvariant()).ToList();
        var key = lower.Contains("exit") ? "obj.door_exit" : lower.Contains("enter") || lower.Contains("entrance") ? "obj.door_enter" : "obj.door";

        var place = lower.Skip(1)
            .Where(w => !Noise.IsMatch(w) && w != "exit" && w != "enter" && w != "entrance" && w != "main" && w != "inside" && w != "outside")
            .ToList();
        var name = PlaceName(place);
        return name == null ? Loc.Get("obj.door_plain") : Loc.Fmt(key, name);
    }

    private static string PlaceName(List<string> place)
    {
        place = place.Select(w => w.ToLowerInvariant()).Where(w => !Noise.IsMatch(w)).ToList();
        var joined = string.Join("_", place);
        return joined.Length == 0
            ? null
            : Loc.Find("place." + joined) ?? GameName(joined) ?? string.Join(" ", place.Select(p => Loc.Find("place." + p) ?? p));
    }
}
