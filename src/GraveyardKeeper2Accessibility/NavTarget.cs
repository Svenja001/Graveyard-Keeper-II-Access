namespace GraveyardKeeper2Accessibility;

/// <summary>
/// Something the player might want to walk to.
///
/// A single type for three different things - an object you interact with, a named landmark, and
/// an item lying on the ground - because from the player's side they are all just "that thing over
/// there", and forcing them into separate keys would mean knowing which kind a mushroom is before
/// being able to look for one.
/// </summary>
internal readonly struct NavTarget
{
    /// <summary>Definition id, which is also the locale key for the name.</summary>
    internal readonly string Id;

    internal readonly Vector3 Position;

    /// <summary>The scene the target is in, needed to ask the game to walk there.</summary>
    internal readonly string WorldId;

    /// <summary>Which list this belongs to. See <c>Navigator.CategoryName</c> for the wording.</summary>
    internal readonly string CategoryKey;

    /// <summary>
    /// What to call it when the id alone does not say enough - "grave, dug, empty" rather than
    /// just "grave". Null to use the id's own name.
    /// </summary>
    internal readonly string Label;

    /// <summary>
    /// The world object behind the target, for reading its state when it is spoken (see
    /// <c>ObjectStatus</c>). Null for ground items and zones.
    /// </summary>
    internal readonly WgoData Data;

    /// <summary>
    /// The story zone behind the target, so a walk can aim at floor inside it rather than at its
    /// middle (see <c>ZoneEntry.Spot</c>). Null for everything else.
    /// </summary>
    internal readonly GDZone Zone;

    internal NavTarget(string id, Vector3 position, string worldId, string categoryKey, string label = null, WgoData data = null, GDZone zone = null)
    {
        Id = id;
        Position = position;
        WorldId = worldId;
        CategoryKey = categoryKey;
        Label = label;
        Data = data;
        Zone = zone;
    }

    internal bool IsValid => !string.IsNullOrEmpty(Id);

    /// <summary>The spoken name.</summary>
    internal string Name => Label ?? ItemText.Name(Id);

    /// <summary>
    /// Two targets are the same when they are the same kind of thing in the same spot. Compared by
    /// position rather than by identity because the underlying lists are rebuilt on every
    /// keypress - the selection has to survive that, or paging would reset constantly.
    /// </summary>
    internal bool SameAs(NavTarget other)
    {
        return Id == other.Id && (Position - other.Position).sqrMagnitude < 0.01f;
    }
}
