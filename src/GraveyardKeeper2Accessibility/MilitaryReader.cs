namespace GraveyardKeeper2Accessibility;

/// <summary>
/// Everything around a fight that is not the fight itself: the squads in the town guard barracks,
/// the defence power the next fight asks for, the pre-fight window where squads are chosen, the
/// fight builder's start and end buttons on the battlefield, and the win and lose windows.
///
/// <para>
/// <b>Read from the code (2026-09-27), untested in game.</b> A squad is a
/// <c>fighter_container</c> in the barracks with four dock points; a zombie carried overhead is put
/// into one with E. Only a zombie holding a pike or bow <i>and</i> wearing body armour fights and
/// adds to the squad's power (<c>UIPrefightSquadWidgetData</c>). The mercenaries are one more
/// squad, paid for once. Defence power - the "Kaserne" icon in the quest text - is the quality of
/// the barricades and towers built at the base plus the power of the squads chosen for the fight,
/// and a fight's <c>defencePowerLock</c> is the least it will start with.
/// </para>
///
/// <para>
/// <b>Three controller-only buttons.</b> The pre-fight window starts the fight on
/// <c>GameKey.StartFight</c>, and on the battlefield the fight builder's window starts the waves on
/// <c>GameKey.StartFight</c> and ends the preparation on <c>GameKey.EndPrefight</c>. Neither key has
/// a keyboard binding (binding dump in LogOutput.log), and in gamepad mode the buttons are drawn as
/// key tips, not as buttons. Ctrl+Enter starts, Ctrl+Backspace ends the preparation.
/// </para>
/// </summary>
internal static class MilitaryReader
{
    private static ManualLogSource _log;
    private static ConfigEntry<KeyboardShortcut> _statusKey;

    /// <summary>The last fight whose pre-fight window was open, for the readiness key.</summary>
    private static string _lastFightName;
    private static int _lastFightLock;

    private static readonly AccessTools.FieldRef<LazyWidget<UIPrefightWindowData>, UIPrefightWindowData> PrefightData =
        AccessTools.FieldRefAccess<LazyWidget<UIPrefightWindowData>, UIPrefightWindowData>("data");

    private static readonly AccessTools.FieldRef<LazyWidget<FightEndWindowData>, FightEndWindowData> EndData =
        AccessTools.FieldRefAccess<LazyWidget<FightEndWindowData>, FightEndWindowData>("data");

    private static readonly AccessTools.FieldRef<LazyWidget<UIBuildingWindowData>, UIBuildingWindowData> BuildingData =
        AccessTools.FieldRefAccess<LazyWidget<UIBuildingWindowData>, UIBuildingWindowData>("data");

    private static readonly MethodInfo PrefightStart = AccessTools.Method(typeof(UIPrefightWindow), "OnStartPressed");
    private static readonly MethodInfo BuilderStartFight = AccessTools.Method(typeof(UIBuildingWindow), "StartFight");
    private static readonly MethodInfo BuilderEndPreFight = AccessTools.Method(typeof(UIBuildingWindow), "EndPreFight");

    internal static void Init(ManualLogSource log, ConfigFile config)
    {
        _log = log;
        _statusKey = ModKeys.Bind(config, "Keys", "FightReadiness", new KeyboardShortcut(KeyCode.F4, KeyCode.LeftShift),
            "Your squads, mercenaries, barricades and defence power, and what the last fight you looked at asked for.");
    }

    private static MilitaryBaseData Base => MainGame.Instance == null || MainGame.Instance.GameSave == null ? null : MainGame.Instance.GameSave.militaryBaseData;

    // ---- keys -------------------------------------------------------------------------------

    /// <summary>
    /// The readiness key, and Ctrl+Enter / Ctrl+Backspace in the windows that start or end a fight.
    /// True when a key was used, so nothing else acts on it this frame.
    /// </summary>
    internal static bool UpdateKeys()
    {
        try
        {
            if (_statusKey.Value.IsDown())
            {
                SayReadiness();
                return true;
            }

            var ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            if (!ctrl) return false;

            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
                return TryStartFight();
            if (Input.GetKeyDown(KeyCode.Backspace))
                return TryEndPreFight();
        }
        catch (Exception ex)
        {
            _log?.LogError($"[Military] Key failed: {ex.GetType().Name}: {ex.Message}");
        }
        return false;
    }

    private static bool TryStartFight()
    {
        var window = LazyWindowsStackController.ActiveWindow;
        if (window is UIPrefightWindow prefight)
        {
            var data = PrefightData(prefight);
            if (data != null && !data.HasEnoughDefencePower)
            {
                ScreenReader.Say(Loc.Fmt("mil.prefight_not_enough", data.CurrentDefencePower, data.FightDefinition.defencePowerLock));
                return true;
            }
            if (!HasWeapon())
            {
                ScreenReader.Say(Loc.Get("mil.no_weapon"));
                return true;
            }
            _log?.LogInfo("[Military] Starting the fight from the pre-fight window.");
            PrefightStart?.Invoke(prefight, null);
            return true;
        }

        if (window is UIBuildingWindow building && IsFightBuilderInPreFight(building))
        {
            _log?.LogInfo("[Military] Starting the waves from the fight builder.");
            ScreenReader.Say(Loc.Get("mil.starting"));
            BuilderStartFight?.Invoke(building, null);
            return true;
        }
        return false;
    }

    private static bool TryEndPreFight()
    {
        if (!(LazyWindowsStackController.ActiveWindow is UIBuildingWindow building) || !IsFightBuilderInPreFight(building)) return false;
        _log?.LogInfo("[Military] Ending the preparation from the fight builder.");
        ScreenReader.Say(Loc.Get("mil.ending_prefight"));
        BuilderEndPreFight?.Invoke(building, null);
        return true;
    }

    private static bool HasWeapon()
    {
        var player = MainGame.PlayerController;
        return player != null && (player.Sword.id != "empty" || player.Bow.id != "empty");
    }

    private static bool IsFightBuilderInPreFight(UIBuildingWindow window)
    {
        var wgo = BuildingData(window)?.AssignedWgo;
        return wgo != null && wgo.Data?.Definition != null
               && wgo.Data.Definition.interactionType == WGODef.InteractionType.FightBuilder
               && FightAnnouncer.State == FightState.InPreFight;
    }

    // ---- windows ----------------------------------------------------------------------------

    internal static bool SpeaksForItself(LazyWidgetBase window) =>
        window is UIPrefightWindow || window is FightWinWindow || window is FightLoseWindow || window is FightDeadWindow;

    private static UIPrefightWindowData _readPrefight;

    [HarmonyPatch(typeof(UIPrefightWindow), nameof(UIPrefightWindow.Redraw))]
    [HarmonyPostfix]
    private static void UIPrefightWindow_Redraw(UIPrefightWindow __instance)
    {
        try
        {
            var data = PrefightData(__instance);
            if (data == null || ReferenceEquals(data, _readPrefight)) return;
            _readPrefight = data;

            var name = TmpText.Clean(LLBase.L(data.FightDefinition.id));
            _lastFightName = name;
            _lastFightLock = data.ShowDefencePowerRequirement ? data.FightDefinition.defencePowerLock : 0;

            var parts = new List<string> { Loc.Fmt("mil.prefight_title", name), PrefightTotals(data) };
            parts.Add(Loc.Fmt("mil.prefight_buildings", data.BuildingsQuality, data.BuildingInZone.Count));
            if (data.FightDefinition.isBarricadesUnavailable) parts.Add(Loc.Get("mil.no_barricades_here"));
            if (data.FightDefinition.isTowersUnavailable) parts.Add(Loc.Get("mil.no_towers_here"));

            var rewards = data.FightDefinition.rewards.Select(r => $"{r.GetCount()} {ItemText.Name(r.id)}").ToList();
            if (rewards.Count > 0) parts.Add(Loc.Fmt("mil.rewards", string.Join(", ", rewards)));
            if (!HasWeapon()) parts.Add(Loc.Get("mil.no_weapon"));
            parts.Add(Loc.Get("mil.prefight_keys"));

            var line = string.Join(". ", parts);
            _log?.LogInfo($"[Military] Pre-fight: \"{line}\"");
            ScreenReader.Say(line, interrupt: false);
        }
        catch (Exception ex)
        {
            _log?.LogError($"[Military] Reading the pre-fight window failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static string PrefightTotals(UIPrefightWindowData data)
    {
        var power = data.ShowDefencePowerRequirement
            ? Loc.Fmt(data.HasEnoughDefencePower ? "mil.power_enough" : "mil.power_short", data.CurrentDefencePower, data.FightDefinition.defencePowerLock)
            : Loc.Fmt("mil.power_plain", data.CurrentDefencePower);
        return $"{Loc.Fmt("mil.squads_chosen", data.CurrentSquadCount, data.FightDefinition.squads)}, {power}";
    }

    /// <summary>A squad was switched on or off in the pre-fight window: say which, and the new totals.</summary>
    [HarmonyPatch(typeof(UIPrefightWindowData), "OnSquadPressedDefault")]
    [HarmonyPostfix]
    private static void SquadOn(UIPrefightWindowData __instance, UIPrefightSquadWidget squadWidget) => SaySquadToggle(__instance, squadWidget, on: true);

    [HarmonyPatch(typeof(UIPrefightWindowData), "OnSquadPressedTurnedOn")]
    [HarmonyPostfix]
    private static void SquadOff(UIPrefightWindowData __instance, UIPrefightSquadWidget squadWidget) => SaySquadToggle(__instance, squadWidget, on: false);

    private static void SaySquadToggle(UIPrefightWindowData data, UIPrefightSquadWidget widget, bool on)
    {
        try
        {
            var name = SquadName(widget?.Data?.WgoData, widget?.Data?.IsMercenary ?? false);
            ScreenReader.Say($"{Loc.Fmt(on ? "mil.squad_on" : "mil.squad_off", name)}. {PrefightTotals(data)}");
        }
        catch (Exception ex)
        {
            _log?.LogError($"[Military] Squad toggle announce failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>A squad in the pre-fight window: who, how strong, how equipped, whether it is chosen. Null for anything else.</summary>
    internal static string DescribeWidget(GamepadNavigationItem item)
    {
        var widget = item == null ? null : item.GetComponentInParent<UIPrefightSquadWidget>();
        var data = widget == null ? null : widget.Data;
        if (data == null) return null;

        if (data.WgoData == null)
            return data.IsMercenary ? Loc.Get("mil.mercs_not_hired") : Loc.Get("mil.squad_slot_empty");

        var parts = new List<string> { SquadName(data.WgoData, data.IsMercenary), Loc.Fmt("mil.power", data.SquadPower) };
        parts.Add(Equipment(data.FightersWeapons, data.FightersArmors));
        if (data.IsMercenary) parts.Add(Loc.Get("mil.mercs_always"));
        else if (data.IsTurnedOn) parts.Add(Loc.Get("mil.chosen"));
        else if (!data.HasAnyFighterInSquad) parts.Add(Loc.Get("mil.no_fighters"));
        else parts.Add(Loc.Get(data.CanBeTurnedOn ? "mil.not_chosen" : "mil.cannot_choose"));
        return string.Join(", ", parts.Where(p => !string.IsNullOrEmpty(p)));
    }

    private static string Equipment(List<ItemType> weapons, List<ItemType> armors)
    {
        int pikes = 0, bows = 0, bare = 0;
        for (var i = 0; i < weapons.Count; i++)
        {
            var armored = i < armors.Count && armors[i] != ItemType.None;
            if (weapons[i] == ItemType.None || !armored) bare++;
            else if (weapons[i] == ItemType.Bow) bows++;
            else pikes++;
        }
        var parts = new List<string>();
        if (pikes > 0) parts.Add(Loc.Fmt("mil.with_pike", pikes));
        if (bows > 0) parts.Add(Loc.Fmt("mil.with_bow", bows));
        if (bare > 0) parts.Add(Loc.Fmt("mil.unequipped", bare));
        return parts.Count == 0 ? Loc.Get("mil.empty") : string.Join(", ", parts);
    }

    /// <summary>The fight builder's window during the preparation: say the two keys once per opening.</summary>
    private static UIBuildingWindowData _hintedBuilder;

    [HarmonyPatch(typeof(UIBuildingWindow), nameof(UIBuildingWindow.Redraw))]
    [HarmonyPostfix]
    private static void UIBuildingWindow_Redraw(UIBuildingWindow __instance)
    {
        try
        {
            var data = BuildingData(__instance);
            if (data == null || ReferenceEquals(data, _hintedBuilder) || !IsFightBuilderInPreFight(__instance)) return;
            _hintedBuilder = data;
            ScreenReader.Say(Loc.Get("mil.builder_keys"), interrupt: false);
        }
        catch (Exception ex)
        {
            _log?.LogError($"[Military] Fight builder hint failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    [HarmonyPatch(typeof(FightWinWindow), nameof(FightWinWindow.Redraw))]
    [HarmonyPostfix]
    private static void Won(FightWinWindow __instance) => SayEnd(EndData(__instance), "mil.won", rewards: true);

    [HarmonyPatch(typeof(FightLoseWindow), nameof(FightLoseWindow.Redraw))]
    [HarmonyPostfix]
    private static void Lost(FightLoseWindow __instance) => SayEnd(EndData(__instance), "mil.lost", rewards: false);

    [HarmonyPatch(typeof(FightingGameController), nameof(FightingGameController.FinishAsLost))]
    [HarmonyPrefix]
    private static void BeforeLoss() => FightAnnouncer.RecordLoss();

    [HarmonyPatch(typeof(FightDeadWindow), nameof(FightDeadWindow.Redraw))]
    [HarmonyPostfix]
    private static void Died(FightDeadWindow __instance) => SayEnd(EndData(__instance), "mil.died", rewards: false);

    private static FightEndWindowData _saidEnd;

    private static void SayEnd(FightEndWindowData data, string key, bool rewards)
    {
        try
        {
            if (data == null || ReferenceEquals(data, _saidEnd)) return;
            _saidEnd = data;
            var name = data.FightDefinition == null ? "" : TmpText.Clean(LLBase.L(data.FightDefinition.id));
            var line = Loc.Fmt(key, name);
            if (key == "mil.lost" && FightAnnouncer.LossReason != null) line = $"{line}. {FightAnnouncer.LossReason}";
            if (rewards && data.FightDefinition != null && data.FightDefinition.rewards.Count > 0)
                line = $"{line}. {Loc.Fmt("mil.rewards", string.Join(", ", data.FightDefinition.rewards.Select(r => $"{r.GetCount()} {ItemText.Name(r.id)}")))}";
            line = $"{line}. {Loc.Get("mil.enter_closes")}";
            _log?.LogInfo($"[Military] \"{line}\"");
            ScreenReader.Say(line);
        }
        catch (Exception ex)
        {
            _log?.LogError($"[Military] Reading the fight result failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>Enter closes the win, lose and death windows (their OK listens for Space only). False when none is on top.</summary>
    internal static bool TryConfirm()
    {
        var window = LazyWindowsStackController.ActiveWindow;
        if (!(window is FightWinWindow || window is FightLoseWindow || window is FightDeadWindow)) return false;
        try
        {
            _saidEnd = null;
            if (window is FightWinWindow won) won.Close();
            else if (window is FightLoseWindow lost) lost.Close();
            else if (window is FightDeadWindow died) died.Close();
        }
        catch (Exception ex)
        {
            _log?.LogError($"[Military] Closing the fight result failed: {ex.GetType().Name}: {ex.Message}");
        }
        return true;
    }

    // ---- objects ----------------------------------------------------------------------------

    /// <summary>
    /// The state of a fight object for the object list: a squad's members and power, which squad a
    /// flag leads, what a flag stand or barricade holds. Null for anything else.
    /// </summary>
    internal static string Status(WgoData data)
    {
        if (data?.Definition == null) return null;
        if (data.id != null && data.id.StartsWith("fighter_container_place", StringComparison.Ordinal)) return Loc.Get("mil.place_locked");
        switch (data.Definition.interactionType)
        {
            case WGODef.InteractionType.FighterContainer: return ContainerStatus(data);
            case WGODef.InteractionType.Flag: return FlagStatus(data);
            case WGODef.InteractionType.FlagStand: return StandStatus(data);
            case WGODef.InteractionType.Barricade: return BarricadeStatus(data);
        }
        return null;
    }

    /// <summary>"Trupp 2" / "Söldner"; the slot order is the game's own (mercenaries first).</summary>
    internal static string SquadName(WgoData container, bool mercenary = false)
    {
        if (mercenary || (container != null && container.id == "fighter_container_mercenary")) return Loc.Get("mil.mercs");
        var index = container == null || Base == null ? -1 : Base.GetSquadSlotIndex(container.UniqueId);
        return index > 0 ? Loc.Fmt("mil.squad", index) : Loc.Get("mil.squad_unnamed");
    }

    private struct SquadInfo
    {
        internal int Members, Ready, Power;
        internal string Equipment;
    }

    /// <summary>What <c>UIPrefightSquadWidgetData</c> works out, without drawing anything.</summary>
    private static SquadInfo Squad(WgoData container)
    {
        var info = new SquadInfo();
        var part = container?.MainWgoPartData;
        if (part == null) return info;

        var mercenary = container.id == "fighter_container_mercenary";
        var weapons = new List<ItemType>();
        var armors = new List<ItemType>();
        foreach (var dock in part.DockPointDataList)
        {
            if (dock == null || !dock.IsOccupied) continue;
            info.Members++;
            if (mercenary)
            {
                var merc = MainGame.WorldData.GetWgoData(dock.OccupiedBy);
                if (merc == null) continue;
                info.Power += (int)merc.Quality;
                info.Ready++;
                continue;
            }

            var zombie = MainGame.ZombieSystemData.GetZombie(dock.OccupiedBy);
            if (zombie == null) continue;
            // Weapons have their own slot since the 2026-10-02 update; Hand now holds only tools.
            var weapon = zombie.Weapon;
            var armor = zombie.Armor;
            var armed = !weapon.IsEmpty && (weapon.Definition.type == ItemType.Pike || weapon.Definition.type == ItemType.Bow);
            var armored = !armor.IsEmpty && armor.Definition.type == ItemType.BodyArmor;
            weapons.Add(armed ? weapon.Definition.type : ItemType.None);
            armors.Add(armored ? ItemType.BodyArmor : ItemType.None);
            if (armed && armored)
            {
                info.Ready++;
                info.Power += weapon.Definition.quality + armor.Definition.quality;
            }
        }
        info.Equipment = mercenary ? null : Equipment(weapons, armors);
        return info;
    }

    private static string ContainerStatus(WgoData container)
    {
        var mil = Base;
        var mercenary = container.id == "fighter_container_mercenary";
        if (mercenary && mil != null && !mil.IsMercenaryPayed) return Loc.Get("mil.mercs_not_hired");

        var info = Squad(container);
        var slots = container.MainWgoPartData?.DockPointsCount ?? 0;
        var parts = new List<string> { SquadName(container) };
        parts.Add(Loc.Fmt("mil.members", info.Members, slots));
        if (info.Members > 0) parts.Add(Loc.Fmt("mil.power", info.Power));
        if (!string.IsNullOrEmpty(info.Equipment) && info.Members > 0) parts.Add(info.Equipment);
        if (!mercenary && info.Members > info.Ready) parts.Add(Loc.Get("mil.needs_gear"));
        if (!mercenary && CarriesZombie() && info.Members < slots) parts.Add(Loc.Get("mil.put_zombie_hint"));
        return string.Join(", ", parts);
    }

    private static bool CarriesZombie()
    {
        try
        {
            return MainGame.PlayerData != null && MainGame.PlayerData.TryGetOverheadItem(i => i.Definition.itemGroupIds.Contains("zombie"), out _);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>The squad a flag leads: the barracks flag via its container, the battlefield flag via its spawn.</summary>
    private static string FlagOwner(WgoData flag)
    {
        if (flag == null) return null;
        var level = FightAnnouncer.Level;
        if (level != null && level.AlliesSpawns != null)
        {
            foreach (var spawn in level.AlliesSpawns)
            {
                var controller = spawn == null ? null : spawn.FlagController;
                if (controller == null || controller.FlagWgo == null || controller.FlagWgo.Data != flag) continue;
                var alive = spawn.Fighters.Count(f => f != null && (f.HpComponent == null || f.HpComponent.Hp > 0));
                var name = spawn.SquadSlotIndex == 0 ? Loc.Get("mil.mercs") : Loc.Fmt("mil.squad", spawn.SquadSlotIndex);
                return Loc.Fmt("mil.flag_of_alive", name, alive, spawn.Fighters.Count);
            }
        }

        var mil = Base;
        if (mil == null) return null;
        var containers = new List<SGuid>(mil.fighterContainers);
        if (mil.FighterContainerMercenary != null) containers.Add(mil.FighterContainerMercenary);
        foreach (var id in containers)
        {
            var container = MainGame.WorldData.GetWgoData(id);
            if (container != null && container.GameResStr.Get("fighters_flag") == flag.UniqueId.ToString())
                return Loc.Fmt("mil.flag_of", SquadName(container));
        }
        return null;
    }

    private static string FlagStatus(WgoData flag)
    {
        var parts = new List<string>();
        var owner = FlagOwner(flag);
        if (owner != null) parts.Add(owner);
        var point = PointAt(flag.Position);
        if (point != null) parts.Add(Loc.Fmt("mil.at_point", point));
        return parts.Count == 0 ? null : string.Join(", ", parts);
    }

    private static string StandStatus(WgoData stand)
    {
        var parts = new List<string>();
        var held = stand.GameResStr.Get("flag_stand_sguid");
        var flag = string.IsNullOrEmpty(held) ? null : MainGame.WorldData.GetWgoData(SGuid.Parse(held));
        parts.Add(flag == null ? Loc.Get("mil.stand_empty") : FlagOwner(flag) ?? Loc.Get("mil.stand_has_flag"));
        var point = PointAt(stand.Position);
        if (point != null) parts.Add(Loc.Fmt("mil.at_point", point));
        if (CarriedFlag() != null) parts.Add(Loc.Get(flag == null ? "mil.stand_plant_hint" : "mil.stand_swap_hint"));
        else if (flag != null) parts.Add(Loc.Get("mil.stand_take_hint"));
        return string.Join(", ", parts);
    }

    private static string BarricadeStatus(WgoData barricade)
    {
        var parts = new List<string>();
        var hp = barricade.HpComponent;
        if (hp != null && hp.MaxHpValue > 0) parts.Add(Loc.Fmt("mil.hp", Mathf.Clamp(Mathf.RoundToInt(100f * hp.Hp / hp.MaxHpValue), 0, 100)));

        var view = GameScene.GetWgoViewGlobal(barricade.UniqueId);
        var slot = view == null ? null : view.GetComponentInChildren<FlagPlacementPoint>();
        if (slot != null)
        {
            if (slot.FlagWgo != null) parts.Add(FlagOwner(slot.FlagWgo.Data) ?? Loc.Get("mil.stand_has_flag"));
            else parts.Add(Loc.Get(CarriedFlag() != null ? "mil.barricade_plant_hint" : "mil.barricade_flag_free"));
        }
        return parts.Count == 0 ? null : string.Join(", ", parts);
    }

    /// <summary>The flag the player is carrying, or null.</summary>
    internal static Wgo CarriedFlag()
    {
        var player = MainGame.PlayerController;
        var carried = player == null ? null : player.attachedWgo;
        if (carried == null || carried.Data?.Definition == null) return null;

        // The game's flag stand only asks whether something is attached. The type test alone
        // never matched in seven fights (2026-10-03): the banner is not typed Flag, so "you carry
        // the flag" was never said and F5 sent the player to the base mid-carry. Its id is logged
        // once, to pin the test down.
        var id = carried.Data.id ?? "";
        var isFlag = carried.Data.Definition.interactionType == WGODef.InteractionType.Flag
            || id.IndexOf("flag", StringComparison.OrdinalIgnoreCase) >= 0
            || id.IndexOf("banner", StringComparison.OrdinalIgnoreCase) >= 0;
        if (_loggedCarried != id)
        {
            _loggedCarried = id;
            _log?.LogInfo($"[Military] Carrying '{id}' (type {carried.Data.Definition.interactionType}); counted as a flag: {isFlag}.");
        }
        return isFlag ? carried : null;
    }

    private static string _loggedCarried;

    internal static string CarriedFlagText()
    {
        var flag = CarriedFlag();
        return flag == null ? null : FlagOwner(flag.Data) ?? ItemText.Name(flag.Data.id);
    }

    /// <summary>"line 2, section 1" for a spot on a capture point of the running level, or null.</summary>
    internal static string PointAt(Vector3 position)
    {
        var level = FightAnnouncer.Level;
        if (level == null) return null;
        try
        {
            if (level.BaseCapturePoint != null && level.BaseCapturePoint.ContainsPosition(position)) return Loc.Get("mil.base_point");
            var point = level.FindCapturePointForFlagStand(position);
            return point == null ? null : PointName(point);
        }
        catch
        {
            return null;
        }
    }

    internal static string PointName(FightingCapturePoint point)
    {
        if (point == null) return null;
        var level = FightAnnouncer.Level;
        var isMainBase = level == null ? point.isBasePoint : ReferenceEquals(point, level.BaseCapturePoint);
        if (isMainBase) return Loc.Get("mil.base_point");
        var sector = point.Sector;
        if (sector == null || sector.fightingLine == null) return Loc.Get(point.isBasePoint ? "mil.goal_point" : "mil.point");
        // A line point the game also calls a base must be ours when the time runs out.
        return Loc.Fmt(point.isBasePoint ? "mil.goal_point_named" : "mil.point_named", sector.fightingLine.lineIdx + 1, sector.sectorIdx + 1);
    }

    // ---- readiness key ----------------------------------------------------------------------

    private static void SayReadiness()
    {
        var mil = Base;
        if (mil == null || MainGame.WorldData == null)
        {
            ScreenReader.Say(Loc.Get("nav.not_in_game"));
            return;
        }

        var parts = new List<string>();
        var total = 0;

        // Barricades and towers standing at the base, as the pre-fight window counts them.
        var quality = 0;
        var buildings = 0;
        foreach (var id in mil.baseBuildings)
        {
            var building = MainGame.WorldData.GetWgoData(id);
            if (building == null || GameBalance.Me.GetDataOrNull<BuildingDef>(building.id + "_fb") == null) continue;
            buildings++;
            quality += (int)building.Quality;
        }
        total += quality;
        parts.Add(Loc.Fmt("mil.ready_buildings", buildings, quality));

        if (mil.IsMercenaryPayed)
        {
            var merc = Squad(MainGame.WorldData.GetWgoData(mil.FighterContainerMercenary));
            total += merc.Power;
            parts.Add(Loc.Fmt("mil.ready_mercs", merc.Members, merc.Power));
        }
        else
        {
            parts.Add(Loc.Get("mil.mercs_not_hired"));
        }

        var anyUnready = false;
        for (var i = 0; i < mil.fighterContainers.Count; i++)
        {
            var container = MainGame.WorldData.GetWgoData(mil.fighterContainers[i]);
            var info = Squad(container);
            total += info.Power;
            anyUnready |= info.Members > info.Ready;
            parts.Add(info.Members == 0
                ? Loc.Fmt("mil.ready_squad_empty", i + 1)
                : Loc.Fmt("mil.ready_squad", i + 1, info.Members, info.Ready, info.Power));
        }
        if (mil.fighterContainers.Count == 0) parts.Add(Loc.Get("mil.no_squads_yet"));
        if (anyUnready) parts.Add(Loc.Get("mil.needs_gear"));

        parts.Insert(0, Loc.Fmt("mil.ready_total", total));
        if (!string.IsNullOrEmpty(_lastFightName))
            parts.Add(_lastFightLock > 0
                ? Loc.Fmt("mil.ready_last_fight", _lastFightName, _lastFightLock)
                : Loc.Fmt("mil.ready_last_fight_free", _lastFightName));
        parts.Add(Loc.Get("mil.ready_note"));

        var line = string.Join(". ", parts);
        _log?.LogInfo($"[Military] Readiness: \"{line}\"");
        ScreenReader.Say(line);
    }
}
