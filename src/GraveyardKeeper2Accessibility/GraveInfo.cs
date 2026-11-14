namespace GraveyardKeeper2Accessibility;

/// <summary>
/// A filled grave's rating and the most it can reach - shared by the object list and the grave window.
///
/// <para>
/// <b>Where the cap comes from</b> (<c>WgoData.GetQuality</c> → <c>Inventory.GetTotalQualityGrave</c>):
/// a <c>grave_ground</c> is rated its definition's base quality, plus the quality of everything in
/// its inventory (the body, the headstone and the fence all live there) minus the body's red
/// skulls, clamped to the body's white skulls. So the best a grave can ever reach is the base plus
/// the white skulls, and no headstone lifts it past that. On screen the cap is only the length of a
/// slider; GK1's players had it read as "3 of 5" and asked for the same here (2026-11-07).
/// </para>
/// </summary>
internal static class GraveInfo
{
    /// <summary>
    /// The grave's rating now, the most it can reach, and the body's red skulls, which pull the
    /// rating down until headstone and fence make up for them. False for anything without a body.
    /// </summary>
    internal static bool TryRating(WgoData data, out float current, out float max, out int red)
    {
        current = max = 0f;
        red = 0;
        try
        {
            var inventory = data?.Inventory;
            var items = inventory?.Data?.Inventory;
            if (items == null) return false;

            Item body = null;
            foreach (var item in items)
            {
                var groups = item?.Definition?.itemGroupIds;
                if (groups != null && groups.Contains("body")) { body = item; break; }
            }
            if (body == null) return false;

            var white = 0;
            foreach (var part in body.Inventory)
            {
                if (part?.Definition == null) continue;
                white += part.Definition.whiteSkulls * part.Count;
                red += part.Definition.redSkulls * part.Count;
            }
            white = Mathf.Clamp(white, 0, 999);
            red = Mathf.Clamp(red, 0, 999);

            current = data.Quality;
            var baseQuality = current - inventory.GetTotalQualityGrave();
            max = baseQuality + white;
            return true;
        }
        catch (Exception ex)
        {
            Plugin.Log?.LogWarning($"[Grave] Could not read a grave's rating: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// "rating 9 of 10, 1 red skull lowers it", "rating 5, the most this body allows", or null
    /// without a body. Reported (2026-11-07): "9 of 10" alone left the player guessing whether a red
    /// skull or a missing decoration was the gap.
    /// </summary>
    internal static string RatingText(WgoData data)
    {
        if (!TryRating(data, out var current, out var max, out var red)) return null;
        return AtMax(current, max)
            ? Loc.Fmt("grave.rating_max", Number(current))
            : WithRed(Loc.Fmt("grave.rating", Number(current), Number(max)), red);
    }

    internal static bool AtMax(float current, float max) => current >= max - 0.01f;

    /// <summary>
    /// Appends "1 red skull lowers it" when the body has any. Only below the cap: at the cap the
    /// headstone and fence have already made up for them.
    /// </summary>
    internal static string WithRed(string text, int red) =>
        red > 0 ? $"{text}, {Loc.Plural("grave.red_lowers", red, red)}" : text;

    internal static string Number(float value) =>
        Mathf.Approximately(value, Mathf.Round(value)) ? Mathf.RoundToInt(value).ToString() : value.ToString("0.#");
}
