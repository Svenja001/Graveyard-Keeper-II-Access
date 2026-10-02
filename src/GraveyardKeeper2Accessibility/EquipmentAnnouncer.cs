namespace GraveyardKeeper2Accessibility;

/// <summary>
/// Says when something is put on or taken off.
///
/// <para>
/// "Equipped" in GK2 means "in the player's tool belt inventory" - that is exactly what the story
/// tests (<c>HasPlayerItemEquip</c> reads <c>PlayerData.toolBeltInventory</c>). On screen an
/// equipped item moves to a slot on the character; through focus alone, Enter on "Rostige Rüstung"
/// sounds the same whether it went on or nothing happened. The workshop in chapter 3 will not let
/// the player leave until armour and sword are both on, so the difference matters.
/// </para>
///
/// <para>
/// The tool belt is a plain <c>Inventory</c> with add/remove events. It is rebuilt with each save,
/// so the subscription is checked once a frame and moved to the new one.
/// </para>
/// </summary>
internal static class EquipmentAnnouncer
{
    private static ManualLogSource _log;
    private static Inventory _belt;

    internal static void Init(ManualLogSource log) => _log = log;

    internal static void Update()
    {
        try
        {
            var belt = MainGame.PlayerData == null ? null : MainGame.PlayerData.toolBeltInventory;
            if (ReferenceEquals(belt, _belt)) return;

            if (_belt != null)
            {
                _belt.OnItemsAdd -= OnPutOn;
                _belt.OnItemsRemove -= OnTakenOff;
            }

            _belt = belt;
            if (_belt == null) return;

            _belt.OnItemsAdd += OnPutOn;
            _belt.OnItemsRemove += OnTakenOff;
        }
        catch (Exception ex)
        {
            _log?.LogError($"[Equip] Could not watch the tool belt: {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>True when the player has this item equipped - the story's own test.</summary>
    internal static bool IsEquipped(string itemId)
    {
        var belt = MainGame.PlayerData == null ? null : MainGame.PlayerData.toolBeltInventory;
        return belt != null && belt.Data != null && belt.Data.HasItemQuantityInInventory(itemId, 1);
    }

    private static void OnPutOn(List<Item> items) => Say(items, "equip.on");

    private static void OnTakenOff(List<Item> items) => Say(items, "equip.off");

    private static void Say(List<Item> items, string key)
    {
        try
        {
            var names = items?.Where(i => i != null && !i.IsEmpty).Select(i => ItemText.Name(i.id)).Where(n => !string.IsNullOrEmpty(n)).ToList();
            if (names == null || names.Count == 0) return;

            _log?.LogInfo($"[Equip] {key}: {string.Join(", ", items.Select(i => i?.id))}");
            ScreenReader.Say(Loc.Fmt(key, string.Join(", ", names)), interrupt: false);
        }
        catch (Exception ex)
        {
            _log?.LogError($"[Equip] Announce failed: {ex.GetType().Name}: {ex.Message}");
        }
    }
}
