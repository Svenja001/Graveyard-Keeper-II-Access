namespace GraveyardKeeper2Accessibility;

/// <summary>
/// Entry point. This first version exists to settle the two questions the whole plan hangs on,
/// and deliberately does nothing else:
///
///   1. Does BepInEx load at all under Unity 6.3 Mono? Everything downstream is worthless if not.
///   2. Does forcing the gamepad-active flag really light up keyboard focus navigation in menus,
///      the way reading LazyInput says it should? If it does, the largest single component of the
///      GK1 mod - its hand-built UI focus model - never has to be written for GK2.
/// </summary>
[BepInPlugin(PluginInfo.Guid, PluginInfo.Name, PluginInfo.Version)]
public class Plugin : BaseUnityPlugin
{
    internal static ManualLogSource Log { get; private set; }

    private ConfigEntry<KeyboardShortcut> _repeatKey;
    private bool _greeted;
    private bool _keysWork = true;

    private void Awake()
    {
        Log = Logger;
        Log.LogInfo($"{PluginInfo.Name} {PluginInfo.Version} starting on Unity {Application.unityVersion}");

        // Must come first: everything below can speak, and speech goes through Loc.
        Loc.Init(Log);
        ScreenReader.Init(Log);
        UiNarrator.Init(Log);
        MenuKeys.Init(Log, Config);
        WorldAnnouncer.Init(Log, Config);
        Navigator.Init(Log, Config);
        AutoWalk.Init(Log, Config);
        Rescue.Init(Log, Config);
        RouteInfo.Init(Log);
        ObjectStatus.Init(Log);
        QuestReader.Init(Log, Config);
        StatusKeys.Init(Log, Config);
        FactoryReader.Init(Log, Config);
        ResourceAnnouncer.Init(Log, Config);
        StoryRepair.Init(Log, Config);
        ZoneEntry.Init(Log);
        EquipmentAnnouncer.Init(Log);
        CarryAnnouncer.Init(Log);
        ProgressAnnouncer.Init(Log);
        FightAnnouncer.Init(Log, Config);
        MilitaryReader.Init(Log, Config);
        TravelMapReader.Init(Log);
        CombatAim.Init(Config);
        WorkSpotCheck.Init(Log);
        BodyWindowsReader.Init(Config);
        CraftReader.Init(Config);
        StationsReader.Init(Config);
        TreesReader.Init(Config);
        ItemCountReader.Init();
        BindingsReader.Init();
        QuestPageReader.Init();
        SurveyResultReader.Init();
        FolioReader.Init();
        VendorReader.Init();
        HotBarReader.Init();
        ItemMenuReader.Init(Config);

        _repeatKey = ModKeys.Bind(Config,
            "Keys", "RepeatFocus", new KeyboardShortcut(KeyCode.F8),
            "Says the currently focused menu control again.");

        // Patching is wrapped because a single bad patch target throws for the whole set, and the
        // mod speaking without slider values is far better than a mod that does not load.
        try
        {
            var harmony = new Harmony(PluginInfo.Guid);
            harmony.PatchAll(typeof(WidgetValuePatches));
            Log.LogInfo("Patched slider and switch widgets for value announcements.");

            harmony.PatchAll(typeof(TooltipReader));
            Log.LogInfo("Patched the tooltip system.");

            harmony.PatchAll(typeof(DialogueReader));
            Log.LogInfo("Patched dialogue and answer choices.");

            harmony.PatchAll(typeof(DialogWindowReader));
            Log.LogInfo("Patched confirmation dialogs.");

            harmony.PatchAll(typeof(CutsceneAnnouncer));
            Log.LogInfo("Patched cutscene state.");

            harmony.PatchAll(typeof(TutorialReader));
            Log.LogInfo("Patched tutorial windows.");

            harmony.PatchAll(typeof(NotesReader));
            Log.LogInfo("Patched the notes window.");

            harmony.PatchAll(typeof(BodyWindowsReader));
            Log.LogInfo("Patched the autopsy table and grave windows.");

            harmony.PatchAll(typeof(BuildingReader));
            Log.LogInfo("Patched the building window and placement mode.");

            harmony.PatchAll(typeof(WorldAnnouncer));
            Log.LogInfo("Patched world interaction and pickups.");

            harmony.PatchAll(typeof(FocusLock));
            Log.LogInfo("Patched the interaction target for holding focus after a walk.");

            harmony.PatchAll(typeof(StoryRepair));
            Log.LogInfo("Patched story repair.");

            harmony.PatchAll(typeof(ProgressAnnouncer));
            Log.LogInfo("Patched inspiration, perk and achievement announcements.");
        }
        catch (Exception ex)
        {
            Log.LogError($"Patching failed, some things will not be spoken: {ex.GetType().Name}: {ex.Message}");
        }

        // Separate, and last: this set touches a dozen private methods across eight windows, and a
        // game patch that renames one must not take the rest of the mod's patches down with it.
        try
        {
            if (CraftReader.Ready)
            {
                new Harmony(PluginInfo.Guid + ".craft").PatchAll(typeof(CraftReader));
                Log.LogInfo("Patched the crafting stations.");
            }
            if (StationsReader.Ready)
            {
                new Harmony(PluginInfo.Guid + ".stations").PatchAll(typeof(StationsReader));
                Log.LogInfo("Patched the other station windows.");
            }
            if (TreesReader.Ready)
            {
                new Harmony(PluginInfo.Guid + ".trees").PatchAll(typeof(TreesReader));
                Log.LogInfo("Patched the tech tree and the inspiration page.");
            }
            if (ItemCountReader.Ready)
            {
                new Harmony(PluginInfo.Guid + ".count").PatchAll(typeof(ItemCountReader));
                Log.LogInfo("Patched the quantity window.");
            }
            if (BindingsReader.Ready)
            {
                new Harmony(PluginInfo.Guid + ".bindings").PatchAll(typeof(BindingsReader));
                Log.LogInfo("Patched the controls screen.");
            }
            if (SurveyResultReader.Ready)
            {
                new Harmony(PluginInfo.Guid + ".survey").PatchAll(typeof(SurveyResultReader));
                Log.LogInfo("Patched the research result.");
            }
            if (FolioReader.Ready)
            {
                new Harmony(PluginInfo.Guid + ".folio").PatchAll(typeof(FolioReader));
                Log.LogInfo("Patched the recipe book.");
            }
            if (QuestPageReader.Ready)
            {
                new Harmony(PluginInfo.Guid + ".quests").PatchAll(typeof(QuestPageReader));
                Log.LogInfo("Patched the quest page.");
            }
            if (VendorReader.Ready)
            {
                new Harmony(PluginInfo.Guid + ".vendor").PatchAll(typeof(VendorReader));
                Log.LogInfo("Patched the trading window.");
            }
            if (HotBarReader.Ready)
            {
                new Harmony(PluginInfo.Guid + ".hotbar").PatchAll(typeof(HotBarReader));
                Log.LogInfo("Patched the hotbar slot window.");
            }
            new Harmony(PluginInfo.Guid + ".itemmenu").PatchAll(typeof(ItemMenuReader));
            Log.LogInfo("Patched destroying items from the item menu.");
            new Harmony(PluginInfo.Guid + ".planting").PatchAll(typeof(PlantingReader));
            Log.LogInfo("Patched planting with a seed in hand.");
            new Harmony(PluginInfo.Guid + ".factory").PatchAll(typeof(FactoryReader));
            Log.LogInfo("Patched building conveyor pieces.");
        }
        catch (Exception ex)
        {
            Log.LogError($"Patching the crafting stations failed: {ex.GetType().Name}: {ex.Message}");
        }

        // Fights on their own, so a changed fight window cannot take the rest down with it.
        try
        {
            new Harmony(PluginInfo.Guid + ".military").PatchAll(typeof(MilitaryReader));
            Log.LogInfo("Patched the pre-fight, fight builder and fight result windows.");
        }
        catch (Exception ex)
        {
            Log.LogError($"Patching the fight windows failed: {ex.GetType().Name}: {ex.Message}");
        }
        try
        {
            new Harmony(PluginInfo.Guid + ".carry").PatchAll(typeof(CarryAnnouncer));
            Log.LogInfo("Patched lifting, dropping and handing over carried items.");
        }
        catch (Exception ex)
        {
            Log.LogError($"Patching carried items failed: {ex.GetType().Name}: {ex.Message}");
        }
        try
        {
            new Harmony(PluginInfo.Guid + ".map").PatchAll(typeof(TravelMapReader));
            Log.LogInfo("Patched the travel map.");
        }
        catch (Exception ex)
        {
            Log.LogError($"Patching the travel map failed: {ex.GetType().Name}: {ex.Message}");
        }
        try
        {
            new Harmony(PluginInfo.Guid + ".aim").PatchAll(typeof(CombatAim));
            Log.LogInfo("Patched melee attacks to aim at the nearest enemy.");
        }
        catch (Exception ex)
        {
            Log.LogError($"Patching the attack aim failed: {ex.GetType().Name}: {ex.Message}");
        }

        // The greeting waits for Update. At Awake the game has not loaded a language table yet
        // (LLBase.IsCurrentLangLoaded is false), and speaking now would come out English for
        // everyone regardless of their setting.
        Log.LogInfo("Greeting deferred until the game language is known.");
    }

    private void Update()
    {
        if (!_greeted && Loc.LanguageKnown)
        {
            _greeted = true;
            ScreenReader.Say(Loc.Fmt("mod.ready", PluginInfo.Version));
            Log.LogInfo($"Language is '{LLBase.CurrentLang}'; spoke the greeting.");
        }

        // Not a key, so it runs even when the keys have been given up on.
        ResourceAnnouncer.Update();
        StoryRepair.Update();
        EquipmentAnnouncer.Update();
        CarryAnnouncer.Update();
        Rescue.Record();
        FightAnnouncer.Update();
        StationsReader.UpdateFishing();
        WorkSpotCheck.Update();
        UiNarrator.Update();
        ProgressAnnouncer.Update();

        if (!_keysWork) return;

        try
        {
            // Placement mode claims the arrows, Enter, Space, Home and End for its cursor.
            if (BuildingReader.UpdatePlacement()) return;

            // The controls screen reads its own keys - all of them while waiting for a new one.
            if (BindingsReader.Update()) return;

            if (MilitaryReader.UpdateKeys()) return;

            // The quest list (J) claims Up, Down and Enter while it is open; it closes on any window.
            if (QuestReader.Update()) return;

            MenuKeys.Update();
            Navigator.Update();
            AutoWalk.Update();
            Rescue.Update();
            StatusKeys.Update();
            FactoryReader.Update();

            if (_repeatKey.Value.IsDown() && !NotesReader.TryRepeat() && !BodyWindowsReader.TryRepeat() &&
                !CraftReader.TryRepeat() && !StationsReader.TryRepeat() && !TreesReader.TryRepeat() &&
                !BindingsReader.TryRepeat() && !VendorReader.TryRepeat() && !TravelMapReader.TryRepeat())
                UiNarrator.RepeatFocus();
        }
        catch (Exception ex)
        {
            // Unity 6 can ship with the legacy input backend disabled, in which case reading a
            // key throws every frame. Give up on keys only - not on Update, which still owes the
            // player the greeting and will owe it more later.
            Log.LogError($"Key handling failed, disabling the mod's keys: {ex.GetType().Name}: {ex.Message}");
            _keysWork = false;
        }
    }

    private void OnDestroy()
    {
        UiNarrator.Shutdown();
        ScreenReader.Shutdown();
    }
}
