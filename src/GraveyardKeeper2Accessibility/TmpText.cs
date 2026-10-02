using System.Text;

namespace GraveyardKeeper2Accessibility;

/// <summary>
/// Turns the game's TextMeshPro markup into something worth listening to.
///
/// GK1 had the same job for NGUI and taught the lesson this file is built around: <b>an icon is
/// not decoration, it is a word</b>. The game writes amounts as an icon followed by a number -
/// <c>"rune_r".FontIcon() + count</c> produces <c>&lt;sprite name="rune_r"&gt;3</c> - and the
/// sentence around it is composed assuming you can see which icon it was. Strip the sprite and
/// "3" is all that is left; name it and the line reads "3 red runes".
///
/// Only tags TextMeshPro actually defines are removed. A blanket <c>&lt;[^&gt;]+&gt;</c> sweep is
/// tempting and wrong: it would silently eat any angle-bracketed word in ordinary prose, and a
/// missed tag read aloud is a far cheaper failure than a missing noun.
/// </summary>
internal static class TmpText
{
    /// <summary>
    /// <c>&lt;sprite name="x"&gt;</c>, <c>&lt;sprite="sheet" index=3&gt;</c> and <c>&lt;sprite=7&gt;</c>,
    /// with the number that so often follows captured alongside it. The amount is part of the match
    /// because it belongs in front of the noun once spoken ("3 red runes", not "red runes 3"), and
    /// German needs it inside the phrase for the plural to agree.
    /// </summary>
    private static readonly Regex SpriteRegex = new(
        "<sprite[^>]*?(?:name\\s*=\\s*\"(?<name>[^\"]*)\"|index\\s*=\\s*(?<index>\\d+)|=\\s*(?<index2>\\d+))[^>]*>\\s?(?<amount>-?\\d+(?:[.,]\\d+)?)?",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Every tag TextMeshPro understands, opening and closing, with or without a value. Listed by
    /// name so that prose in angle brackets survives - see the class remarks.
    /// </summary>
    private static readonly Regex TagRegex = new(
        "</?(?:b|i|u|s|sub|sup|mark|nobr|br|align|allcaps|alpha|color|cspace|font|font-weight|" +
        "gradient|indent|line-height|line-indent|link|lowercase|margin|material|mspace|noparse|" +
        "page|pos|rotate|size|smallcaps|space|sprite|strikethrough|style|underline|uppercase|" +
        "voffset|width)(?:[=\\s][^>]*)?>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex MultiSpaceRegex = new(" {2,}", RegexOptions.Compiled);
    private static readonly Regex SpaceBeforePunctRegex = new(" ([,.;:!?])", RegexOptions.Compiled);

    /// <summary>
    /// Spoken names for the sprite ids the game composes text around, keyed by the id that appears
    /// in <c>&lt;sprite name="..."&gt;</c>. Values are <see cref="Loc"/> keys, not text: these are the
    /// mod's own words and have to follow the player's language.
    ///
    /// Only ids that carry meaning are listed. Anything missing falls through to
    /// <see cref="Humanise"/>, which says the raw id with its underscores opened out - not a real
    /// translation, but it keeps the noun in the sentence instead of dropping it silently.
    /// </summary>
    private static readonly Dictionary<string, string> IconWords = new(StringComparer.OrdinalIgnoreCase)
    {
        // Graveyard ratings - the numbers the whole first act is about.
        ["skull"] = "icon.white_skull",
        ["wskull"] = "icon.white_skull",
        ["rskull"] = "icon.red_skull",
        ["wrskull"] = "icon.grave_quality",
        ["wr"] = "icon.grave_quality",
        ["wr_red"] = "icon.grave_quality_red",
        ["cross"] = "icon.cross",

        // Tech points.
        ["tech_red"] = "icon.tech_red",
        ["tech_green"] = "icon.tech_green",
        ["tech_blue"] = "icon.tech_blue",
        ["science"] = "icon.science",
        ["techpoint_icon_red_s"] = "icon.tech_red",
        ["techpoint_icon_green_s"] = "icon.tech_green",
        ["techpoint_icon_blue_s"] = "icon.tech_blue",

        // Skills ("talents"): the colour of point a task draws on, as in the autopsy tutorial.
        ["talent_red"] = "icon.talent_red",
        ["talent_green"] = "icon.talent_green",
        ["talent_blue"] = "icon.talent_blue",
        ["talent_yellow"] = "icon.talent_yellow",
        ["talent_orange"] = "icon.talent_orange",

        // Inspiration points, which fill the talent bars ("1 inspiration bar point blue").
        ["inspiration_point_red"] = "icon.insp_red",
        ["inspiration_point_green"] = "icon.insp_green",
        ["inspiration_point_blue"] = "icon.insp_blue",
        ["inspiration_point_yellow"] = "icon.insp_yellow",
        ["inspiration_point_orange"] = "icon.insp_orange",
        ["inspiration_bar_point_red"] = "icon.insp_red",
        ["inspiration_bar_point_green"] = "icon.insp_green",
        ["inspiration_bar_point_blue"] = "icon.insp_blue",
        ["inspiration_bar_point_yellow"] = "icon.insp_yellow",
        ["inspiration_bar_point_orange"] = "icon.insp_orange",

        // Autopsy: the heart icon stands for organs in general, the crossed one for a botched cut.
        ["organ_hrt"] = "icon.organ",
        ["organ_mis_hrt"] = "icon.organ_mistake",
        ["hp"] = "icon.health",
        ["panic"] = "icon.panic",
        ["icon_star"] = "icon.star",
        ["icon-quality-bronze"] = "item.quality_1",
        ["icon-quality-silver"] = "item.quality_2",
        ["icon-quality-gold"] = "item.quality_3",
        ["time"] = "icon.time",

        // Alchemy runes.
        ["rune_r"] = "icon.rune_red",
        ["rune_g"] = "icon.rune_green",
        ["rune_b"] = "icon.rune_blue",
        ["alchemy_flask"] = "icon.alchemy",

        // Bars and standings.
        ["energy"] = "icon.energy",
        ["faith"] = "icon.faith",
        ["insanity"] = "icon.insanity",
        ["happiness"] = "icon.happiness",
        ["happiness_cross"] = "icon.happiness",
        ["icon_smile02"] = "icon.happiness",

        // Town and equipment.
        ["barracks"] = "icon.barracks",
        ["equip_icon_sword"] = "icon.sword",
        ["equip_icon_armor"] = "icon.armor",
        ["equip_icon_arrow"] = "icon.arrow",
        ["gear"] = "icon.gear",
        ["fire"] = "icon.fire",
        ["icon_lock"] = "icon.locked",
        ["icon_time"] = "icon.time",
        ["cell_time"] = "icon.time",
        ["reputation-citizens"] = "icon.reputation",

        // The days of the week, which the game only ever draws as icons.
        ["day_pride"] = "icon.day_pride",
        ["day_lust"] = "icon.day_lust",
        ["day_gluttony"] = "icon.day_gluttony",
        ["day_envy"] = "icon.day_envy",
        ["day_wrath"] = "icon.day_wrath",
        ["day_sloth"] = "icon.day_sloth",

        // Coins. Trading.FormatMoney writes each as an icon followed by its count; the "-m"
        // variants are the larger icons used in windows and in the game's own help text.
        ["gld"] = "icon.gold",
        ["slv"] = "icon.silver",
        ["brz"] = "icon.bronze",
        ["gld-m"] = "icon.gold",
        ["slv-m"] = "icon.silver",
        ["brz-m"] = "icon.bronze",
    };

    /// <summary>
    /// The one call the rest of the mod makes. Safe on null, on text with no markup at all, and on
    /// markup this does not know about.
    /// </summary>
    internal static string Clean(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;

        if (text.IndexOf('<') >= 0)
        {
            text = SpriteRegex.Replace(text, SpriteToWords);
            // <br> is a line break, not a word; everything else just goes.
            text = TagRegex.Replace(text, m => m.Value.StartsWith("<br", StringComparison.OrdinalIgnoreCase) ? "\n" : "");
        }

        // A named or dropped icon leaves a double space, or a space in front of the punctuation it
        // used to sit before. Tidy both so the sentence still reads cleanly.
        text = MultiSpaceRegex.Replace(text, " ");
        text = SpaceBeforePunctRegex.Replace(text, "$1");
        return text.Trim();
    }

    private static string SpriteToWords(Match m)
    {
        var name = m.Groups["name"].Success ? m.Groups["name"].Value : null;

        // An index-only sprite (<sprite=7>) names no id we could look up - the meaning lives in the
        // sheet, which is asset data we do not have at this layer. Dropping it is right: unlike a
        // named icon it carries no noun we could be losing, and reading "sprite 7" helps nobody.
        if (string.IsNullOrEmpty(name))
            return m.Groups["amount"].Success ? m.Groups["amount"].Value : "";

        var word = IconWords.TryGetValue(name, out var key) ? Loc.Get(key) : Humanise(name);

        // Padded, because the game butts icons against the next word ("<sprite>und" drew as an
        // icon then "and"), which read as one word: "weißer Schädelund". Clean collapses the
        // doubled spaces afterwards.
        if (!m.Groups["amount"].Success) return $" {word} ";

        return $" {m.Groups["amount"].Value} {word} ";
    }

    /// <summary>"equip_icon_sword" -&gt; "equip icon sword". A placeholder, not a translation.</summary>
    private static string Humanise(string id)
    {
        var sb = new StringBuilder(id.Length);
        foreach (var c in id)
            sb.Append(c == '_' || c == '-' ? ' ' : c);
        return sb.ToString();
    }
}
