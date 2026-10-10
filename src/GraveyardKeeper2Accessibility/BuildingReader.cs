using System.Collections;
using System.Reflection;

namespace GraveyardKeeper2Accessibility;

/// <summary>
/// Building: the list of things a builder can put up (<c>UIBuildingWindow</c>), and the placement
/// mode that follows choosing one. The graveyard's first grave is built this way
/// (<c>5_intro_graveyard_build_grave</c> waits for <c>BuildBuilding grave_empty_place</c>), so the
/// story stops here without it.
///
/// <para>
/// <b>The list.</b> Focus narration read each entry's labels, which is the name and a quality
/// figure - but what it costs is a row of item pictures that only become focusable after a
/// controller-only "unfold" key. Each entry now says its cost with what the player holds ("2 of 5
/// planks") and whether it can be built at all. A builder with nothing to offer opened in silence
/// (the home builder, in the log); it now says the game's own "nothing to build" text. Tabs are on
/// <c>GameKey.NextTab</c>/<c>PrevTab</c>, controller shoulder buttons; Ctrl+Left/Right switch them.
/// </para>
///
/// <para>
/// <b>Placement.</b> <c>BuildController</c> moves a pointer across a grid under the mouse, or under
/// a screen-space cursor driven by the stick - both entirely visual. The mod drives the same pointer
/// through the controller's public <c>UpdatePointerObjectPosition</c>, a grid cell per arrow key,
/// and reads the pointer's own verdict (<c>shownAsActive</c>, the green/red tint) after each move.
/// It also keeps the controller's private "last snapped position" in step, because the game
/// re-derives the pointer from that every frame and would otherwise drag it straight back.
/// End looks for free spots itself: the pre-marked areas a building requires, when it requires
/// some, otherwise a ring search outward from the player. Enter builds, Space (as in GK1) or End
/// jumps to the next free spot, R is the game's own rotate, Escape the game's own cancel.
/// </para>
/// </summary>
internal static class BuildingReader
{
    // ---- the list ---------------------------------------------------------------------------

    private static readonly AccessTools.FieldRef<LazyWidget<UIBuildingWidgetData>, UIBuildingWidgetData> WidgetData =
        AccessTools.FieldRefAccess<LazyWidget<UIBuildingWidgetData>, UIBuildingWidgetData>("data");

    private static readonly AccessTools.FieldRef<UIBuildingWindow, List<string>> Tabs =
        AccessTools.FieldRefAccess<UIBuildingWindow, List<string>>("tabs");

    private static readonly AccessTools.FieldRef<UIBuildingWindow, int> CurrentTab =
        AccessTools.FieldRefAccess<UIBuildingWindow, int>("currentTab");

    private static readonly AccessTools.FieldRef<UIBuildingWindow, bool> TabsDrawn =
        AccessTools.FieldRefAccess<UIBuildingWindow, bool>("tabsDrawn");

    private static readonly AccessTools.FieldRef<UIBuildingWindow, TMPro.TextMeshProUGUI> NoBuildingsText =
        AccessTools.FieldRefAccess<UIBuildingWindow, TMPro.TextMeshProUGUI>("noBuildingsText");

    private static readonly AccessTools.FieldRef<UIBuildingWindow, List<UIBuildingWidget>> Displayed =
        AccessTools.FieldRefAccess<UIBuildingWindow, List<UIBuildingWidget>>("displayedBuildItemGUIs");

    /// <summary>A building-list entry: name, what it does, its cost against what is held. Null for anything else.</summary>
    internal static string DescribeWidget(GamepadNavigationItem item)
    {
        if (item == null || !item.TryGetComponent<UIBuildingWidget>(out var widget)) return null;

        try
        {
            var data = WidgetData(widget);
            if (data?.BuildData == null) return null;

            var parts = new List<string> { TmpText.Clean(LLBase.L(data.Name)) };
            var def = data.BuildData.Definition;
            if (def != null && def.HasLimits)
                parts.Add(Loc.Fmt("build.limit", def.GetLimitsString()));

            var description = TmpText.Clean(data.DescriptionModules ?? data.Description);
            if (!string.IsNullOrWhiteSpace(description)) parts.Add(description);

            var needs = new List<string>();
            foreach (var need in data.GetCurrentNeedItems())
            {
                if (need == null || need.IsEmpty) continue;
                var count = need.GetCount();
                var name = need.IsGroup ? TmpText.Clean(LLBase.L(need.groupType.ToString())) : ItemText.Name(need.id);
                needs.Add(need.IsGroup
                    ? Loc.Fmt("tooltip.need", count, name)
                    : Loc.Fmt("tooltip.need_have", data.MultiInventory?.GetTotalCount(need.id) ?? 0, count, name));
            }
            if (needs.Count > 0) parts.Add(Loc.Fmt("tooltip.needs", string.Join("; ", needs)));

            if (data.CanBuild != null && !data.CanBuild(data.GetCurrentNeedItems()))
                parts.Add(Loc.Get("build.cannot"));

            parts.RemoveAll(string.IsNullOrWhiteSpace);
            return string.Join(", ", parts);
        }
        catch (Exception ex)
        {
            Plugin.Log?.LogWarning($"[Build] Could not describe a building entry: {ex.Message}");
            return null;
        }
    }

    [HarmonyPatch(typeof(UIBuildingWindow), nameof(UIBuildingWindow.Redraw))]
    [HarmonyPostfix]
    private static void UIBuildingWindow_Redraw(UIBuildingWindow __instance)
    {
        try
        {
            var tabs = Tabs(__instance);
            if (tabs != null && tabs.Count > 0) return;

            var label = NoBuildingsText(__instance);
            var text = label == null ? null : TmpText.Clean(label.text);
            if (string.IsNullOrWhiteSpace(text)) text = Loc.Get("build.nothing");
            Plugin.Log?.LogInfo($"[Build] Builder has nothing to offer: \"{text}\"");
            ScreenReader.Say(text, interrupt: false);
        }
        catch (Exception ex)
        {
            Plugin.Log?.LogError($"[Build] Building window read failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    [HarmonyPatch(typeof(UIBuildingWindow), "DrawTab")]
    [HarmonyPostfix]
    private static void UIBuildingWindow_DrawTab(UIBuildingWindow __instance, int tab)
    {
        try
        {
            if (tab < 0 || !TabsDrawn(__instance)) return;
            var tabs = Tabs(__instance);
            if (tabs == null || tab >= tabs.Count) return;

            var count = Displayed(__instance)?.Count ?? 0;
            var line = Loc.Fmt("build.tab", TmpText.Clean(LLBase.L(tabs[tab])), tab + 1, tabs.Count, count);

            // The first entry was focused (and spoken) while the tab drew; the tab comes first so
            // the entry makes sense, then the entry again.
            ScreenReader.Say(line);
            var focused = UiNarrator.LastFocusLabel;
            if (!string.IsNullOrEmpty(focused)) ScreenReader.Say(focused, interrupt: false);
        }
        catch (Exception ex)
        {
            Plugin.Log?.LogError($"[Build] Tab read failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>Ctrl+Left/Right in the building window. False when not applicable.</summary>
    internal static bool TryTabKeys()
    {
        if (!(LazyWindowsStackController.ActiveWindow is UIBuildingWindow window)) return false;
        if (!Input.GetKey(KeyCode.LeftControl) && !Input.GetKey(KeyCode.RightControl)) return false;

        var next = Input.GetKeyDown(KeyCode.RightArrow);
        var prev = Input.GetKeyDown(KeyCode.LeftArrow);
        if (!next && !prev) return false;

        try
        {
            if (!(next ? window.OnPressedNextTab() : window.OnPressedPrevTab()))
                ScreenReader.Say(Loc.Get("build.one_tab"));
        }
        catch (Exception ex)
        {
            Plugin.Log?.LogError($"[Build] Tab switch failed: {ex.GetType().Name}: {ex.Message}");
        }
        return true;
    }

    // ---- placement --------------------------------------------------------------------------

    private static ManualLogSource Log => Plugin.Log;

    private static BuildController _controller;

    private static readonly AccessTools.FieldRef<BuildController, BuildPointer> Pointer =
        AccessTools.FieldRefAccess<BuildController, BuildPointer>("buildPointer");

    private static readonly AccessTools.FieldRef<BuildController, Vector3> CurPos =
        AccessTools.FieldRefAccess<BuildController, Vector3>("curPosVisualCenter");

    private static readonly AccessTools.FieldRef<BuildController, Vector3> LastSnapped =
        AccessTools.FieldRefAccess<BuildController, Vector3>("lastSnappedCursorPos");

    private static readonly AccessTools.FieldRef<BuildController, Vector3> LastCursor =
        AccessTools.FieldRefAccess<BuildController, Vector3>("lastCursorPos");

    private static readonly AccessTools.FieldRef<BuildController, Vector2Int> GridStep =
        AccessTools.FieldRefAccess<BuildController, Vector2Int>("gridStep");

    private static readonly AccessTools.FieldRef<BuildController, bool> InputLocked =
        AccessTools.FieldRefAccess<BuildController, bool>("isBuildModeInputLocked");

    private static readonly AccessTools.FieldRef<BuildController, BuildData> CurrentBuild =
        AccessTools.FieldRefAccess<BuildController, BuildData>("currentBuildData");

    private static readonly AccessTools.FieldRef<BuildPointerObject, bool> ShownAsActive =
        AccessTools.FieldRefAccess<BuildPointerObject, bool>("shownAsActive");

    private static readonly MethodInfo LockAndDelay = AccessTools.Method(typeof(BuildController), "LockBuildInputAndDoDelayedAction");
    private static readonly MethodInfo UpdateSoftHints = AccessTools.Method(typeof(BuildController), "UpdateFullCoverSoftBuildAreaHints");
    private static readonly MethodInfo UpdateDockHints = AccessTools.Method(typeof(BuildController), "UpdateDockPointsHints");

    /// <summary>Free spots found by End, nearest the player first; cleared whenever the cursor is moved by hand.</summary>
    private static List<Vector3> _spots;
    private static int _spotIndex;

    /// <summary>True when <see cref="_spots"/> are exact pointer positions (full-cover areas), not points to snap.</summary>
    private static bool _spotsExact;

    /// <summary>True while the game is in placement mode; the plugin keeps the navigation keys off meanwhile.</summary>
    internal static bool Active => _controller != null && _controller.IsBuildModeActive;

    [HarmonyPatch(typeof(BuildController), nameof(BuildController.EnableBuildMode))]
    [HarmonyPostfix]
    private static void BuildController_EnableBuildMode(BuildController __instance, BuildData buildData)
    {
        _controller = __instance;
        _spots = null;
        _placedByMod = false;
        try
        {
            // "Entfernen" in the build menu uses the same mode with no building; the free-spot
            // keys mean nothing there, so say what it is instead of "placing <nothing>".
            if (buildData != null && buildData.BuildingMode == BuildingDef.BuildingMode.Remove)
            {
                _removables = null;
                var count = Removables().Count;
                Log?.LogInfo($"[Build] Remove mode, {count} removable building(s) here.");
                ScreenReader.Say(Loc.Fmt("build.remove_mode_on", count));
                return;
            }

            var name = BuildingName(buildData);
            Log?.LogInfo($"[Build] Placement mode for '{buildData?.WgoId}'.");
            var line = Loc.Fmt("build.mode_on", name);
            var extension = PlacingExtension(buildData);
            if (extension != null) line = $"{line}. {Loc.Fmt("build.ext_intro", ObjectStatus.ParentNames(extension))}";
            if (IsConveyorBuild(buildData)) line = $"{line}. {Loc.Get("factory.build_hint")}";
            ScreenReader.Say(line);
        }
        catch (Exception ex)
        {
            Log?.LogError($"[Build] Placement announcement failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    [HarmonyPatch(typeof(BuildController), nameof(BuildController.DisableBuildMode))]
    [HarmonyPostfix]
    private static void BuildController_DisableBuildMode()
    {
        _spots = null;
        _rotationSayFrame = 0;
        _placedByMod = false;
        if (_controller == null) return;
        ScreenReader.Say(Loc.Get("build.mode_off"));
    }

    /// <summary>Placement keys. Returns true while placement mode is on, so no other key handler runs.</summary>
    internal static bool UpdatePlacement()
    {
        if (!Active) return false;

        // A window on top (pause menu, a confirmation) gets the keys as usual.
        if (LazyWindowsStackController.ActiveWindow != null) return false;

        try
        {
            SayPendingRotation();

            if (InputLocked(_controller)) return true;

            if (IsRemoveMode)
            {
                UpdateRemove();
                return true;
            }

            if (Input.GetKeyDown(KeyCode.UpArrow)) Step(0, 1);
            else if (Input.GetKeyDown(KeyCode.DownArrow)) Step(0, -1);
            else if (Input.GetKeyDown(KeyCode.LeftArrow)) Step(-1, 0);
            else if (Input.GetKeyDown(KeyCode.RightArrow)) Step(1, 0);
            // Space snaps, as it did in GK1. The game binds Space to nothing in placement mode.
            else if (Input.GetKeyDown(KeyCode.End) || Input.GetKeyDown(KeyCode.Space)) NextFreeSpot();
            else if (Input.GetKeyDown(KeyCode.Home) || Input.GetKeyDown(KeyCode.F8)) SayCursor(interrupt: true);
            else if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)) Build();
            // The game rotates on its own Rotate key (R) and the postfix on BuildPointer.Rotate
            // announces it; only the case the game silently ignores is said here.
            else if (LazyInput.GetKeyDown(GameKey.Rotate)) SayIfNoRotation();
        }
        catch (Exception ex)
        {
            Log?.LogError($"[Build] Placement key failed: {ex.GetType().Name}: {ex.Message}");
        }
        return true;
    }

    // ---- remove mode ("Entfernen") ------------------------------------------------------------

    /// <summary>
    /// Remove mode, made usable (user, 2026-09-26: a hardening bucket in the wrong place could not
    /// be taken down).
    ///
    /// <para>
    /// The game's remove mode is a cursor: whatever removable building lies under it is tinted,
    /// and the build key acts on it (<c>RemovePointer.TryDoBuildAction</c>). A building that is
    /// deleted instantly goes at once and drops what it cost; any other is only <i>marked</i> - it
    /// gets a demolition order (<c>destroy_basic</c>) that the player then works off with F, like
    /// any craft, and pressing again on a marked one takes the mark back off. So the mod lists the
    /// removable buildings of the zone, nearest first, puts the game's own cursor on the chosen one
    /// (the same way the free-spot keys move it), and presses the build key for the player.
    /// </para>
    /// </summary>
    private static bool IsRemoveMode => CurrentBuild(_controller)?.BuildingMode == BuildingDef.BuildingMode.Remove;

    private static List<Wgo> _removables;
    private static int _removableIndex = -1;

    private static readonly AccessTools.FieldRef<RemovePointer, HashSet<IBuildRemovable>> Undestroyable =
        AccessTools.FieldRefAccess<RemovePointer, HashSet<IBuildRemovable>>("unDestroyableRemovables");

    private static readonly FieldInfo RemovingSelection = AccessTools.Field(typeof(RemovePointer), "currentRemovingSelection");
    private static readonly MethodInfo UpdateHover = AccessTools.Method(typeof(RemovePointer), "UpdateHoverFromOverlapCenter");
    private static readonly MethodInfo SnappedOverlapCenter = AccessTools.Method(typeof(RemovePointer), "GetSnappedOverlapCenter");
    private static readonly MethodInfo UpdatePointerAtPos = AccessTools.Method(typeof(BuildController), "UpdatePointerAtPos");

    private static void UpdateRemove()
    {
        var back = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
        if (Input.GetKeyDown(KeyCode.End) || Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.PageDown))
            StepRemovable(back ? -1 : 1);
        else if (Input.GetKeyDown(KeyCode.PageUp)) StepRemovable(-1);
        else if (Input.GetKeyDown(KeyCode.Home) || Input.GetKeyDown(KeyCode.F8)) SayRemoveSelection(interrupt: true);
        else if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)) RemoveSelected();
    }

    /// <summary>Removable buildings in the zone, nearest the player first - what the game would tint red under its cursor.</summary>
    private static List<Wgo> Removables()
    {
        var result = new List<Wgo>();
        try
        {
            var zone = LazySingleton<BuildManager>.Instance?.WorldZone;
            if (zone?.Wgos == null) return result;

            var pointer = Pointer(_controller)?.PointerObject as RemovePointer;
            var blocked = pointer == null ? null : Undestroyable(pointer);
            foreach (var wgo in zone.Wgos)
            {
                if (wgo == null || wgo.Data == null || !wgo.IsBuildRemovable()) continue;
                if (blocked != null && blocked.Contains(wgo)) continue;
                result.Add(wgo);
            }

            var from = MainGame.PlayerController != null ? MainGame.PlayerController.MovablePosition : Vector3.zero;
            result.Sort((a, b) => Flat(a.Data.Position - from).sqrMagnitude.CompareTo(Flat(b.Data.Position - from).sqrMagnitude));
        }
        catch (Exception ex)
        {
            Log?.LogWarning($"[Build] Could not list removable buildings: {ex.Message}");
        }
        return result;
    }

    private static void StepRemovable(int delta)
    {
        if (_removables == null)
        {
            _removables = Removables();
            _removableIndex = -1;
        }
        _removables.RemoveAll(w => w == null || w.Data == null);

        if (_removables.Count == 0)
        {
            ScreenReader.Say(Loc.Get("build.remove_none"));
            return;
        }

        _removableIndex = ((_removableIndex < 0 && delta < 0 ? 0 : _removableIndex + delta) % _removables.Count + _removables.Count) % _removables.Count;
        var target = _removables[_removableIndex];
        if (!PointAt(target))
            Log?.LogWarning($"[Build] Remove cursor did not land on '{target.Data.id}'.");
        SayRemoveSelection(interrupt: true);
    }

    /// <summary>
    /// Puts the game's cursor on a building and makes the remove pointer pick it at once rather
    /// than on its next physics step, so what is said and what Enter acts on are the same thing.
    /// Tries the middle of each part of the building's footprint until the game picks this one.
    /// </summary>
    private static bool PointAt(Wgo target)
    {
        var pointer = Pointer(_controller)?.PointerObject as RemovePointer;
        var spots = new List<Vector3>();
        foreach (var col in target.GetComponentsInChildren<Collider>())
        {
            if (col == null || !col.enabled || (col.gameObject.layer != FootprintLayer && col.gameObject.layer != 8)) continue;
            spots.Add(col.bounds.center);
        }
        spots.Add(target.Data.Position);

        // The game tests for a building half a cell off the snapped cursor (GetSnappedOverlapCenter),
        // so aim that far the other way.
        var cellOffset = new Vector3(BuildConsts.CELL_SIZE.x, 0f, -BuildConsts.CELL_SIZE.y) / 2f;
        var zone = _controller.CurrentWorldZone;
        foreach (var spot in spots)
        {
            var aim = spot - cellOffset;
            var ground = zone == null ? aim : new Vector3(aim.x, zone.GroundPlaneY, aim.z);

            // Through the game's own cursor update, so its per-frame update computes the same
            // snapped spot and leaves the cursor where it is.
            var screen = CameraSystem.WorldToScreenPoint(ground);
            try
            {
                if (UpdatePointerAtPos != null) UpdatePointerAtPos.Invoke(_controller, new object[] { screen, true });
                else
                {
                    LastCursor(_controller) = screen;
                    _controller.UpdatePointerObjectPosition(ground);
                }
            }
            catch (Exception ex)
            {
                Log?.LogWarning($"[Build] Could not move the remove cursor: {ex.Message}");
                return false;
            }

            if (pointer == null) return true;
            try
            {
                // The game's own hover test, run now rather than on the next physics step.
                var center = SnappedOverlapCenter?.Invoke(pointer, null) is Vector3 c ? c : spot + Vector3.up * 0.01f;
                UpdateHover?.Invoke(pointer, new object[] { center });
            }
            catch (Exception ex)
            {
                Log?.LogWarning($"[Build] Remove hover failed: {ex.Message}");
                return false;
            }
            if (ReferenceEquals(SelectedRemovable() as Wgo, target)) return true;
        }
        return false;
    }

    private static IBuildRemovable SelectedRemovable()
    {
        try { return RemovingSelection?.GetValue(null) as IBuildRemovable; }
        catch { return null; }
    }

    /// <summary>"hardening bucket, marked for demolition, 2 of 5, 3 metres: 2 north".</summary>
    private static void SayRemoveSelection(bool interrupt)
    {
        var wgo = SelectedRemovable() as Wgo;
        if (wgo == null || wgo.Data == null)
        {
            ScreenReader.Say(Loc.Get("build.remove_nothing_here"), interrupt);
            return;
        }

        var parts = new List<string> { ObjectNames.Of(wgo.Data.id) };
        var marked = IsMarked(wgo);
        if (marked) parts.Add(Loc.Get("build.remove_marked"));
        var extension = ObjectStatus.Extension(wgo.Data);
        if (extension != null) parts.Add(extension);
        var goesWith = SlotAddOnsOf(wgo.Data);
        if (goesWith.Count > 0) parts.Add(Loc.Fmt("build.remove_takes_addons", string.Join(", ", goesWith)));
        if (_removables != null && _removableIndex >= 0 && _removableIndex < _removables.Count && ReferenceEquals(_removables[_removableIndex], wgo))
            parts.Add(Loc.Fmt("build.remove_position", _removableIndex + 1, _removables.Count));

        var player = MainGame.PlayerController;
        if (player != null)
        {
            var offset = wgo.Data.Position - player.MovablePosition;
            var flat = new Vector2(offset.x, offset.z);
            parts.Add(Loc.Fmt("build.remove_where", Mathf.RoundToInt(flat.magnitude), Navigator.KeyDirections(flat)));
        }
        parts.Add(Loc.Get(marked ? "build.remove_key_unmark" : "build.remove_key"));
        ScreenReader.Say(string.Join(", ", parts), interrupt);
    }

    /// <summary>
    /// The add-ons sitting in a workbench's slots (the bellows on a furnace). The game tears them
    /// down with it and drops their materials (<c>WgoData.RemoveFullCoverSoftSlotExtensions</c>);
    /// the user took down a Schmelzofen I and then looked in vain for its bellows (2026-10-04).
    /// </summary>
    private static List<string> SlotAddOnsOf(WgoData data)
    {
        var names = new List<string>();
        try
        {
            var world = MainGame.Instance?.GameSave?.WorldData;
            if (world == null || data.AttachedWorkbenchExtensions == null) return names;
            foreach (var guid in data.AttachedWorkbenchExtensions)
            {
                var ext = world.GetWgoData(guid);
                if (ext == null || string.IsNullOrEmpty(ext.id)) continue;
                if (!GameBalance.Me.buildableWgos.TryGetValue(ext.id, out var building) || building == null) continue;
                if (building.chooseCustomBuildAreaType != BuildingDef.BuildAreaChoosingType.FullCoverSoft) continue;
                names.Add(ObjectNames.Of(ext.id));
            }
        }
        catch (Exception ex)
        {
            Log?.LogWarning($"[Build] Could not read slot add-ons of '{data.id}': {ex.Message}");
        }
        return names;
    }

    private static bool IsMarked(Wgo wgo)
    {
        try { return wgo.Data.CraftComponent != null && wgo.Data.CraftComponent.IsDestroyingCraftActive; }
        catch { return false; }
    }

    private static void RemoveSelected()
    {
        var wgo = SelectedRemovable() as Wgo;
        if (wgo == null || wgo.Data == null)
        {
            ScreenReader.Say(Loc.Get("build.remove_nothing_here"));
            return;
        }

        var name = ObjectNames.Of(wgo.Data.id);
        var id = wgo.Data.id;
        var removed = Pointer(_controller).TryBuildActionInput();
        Log?.LogInfo($"[Build] Remove on '{id}': {(removed ? "removed" : IsMarked(wgo) ? "marked" : "unmarked")}.");

        if (removed)
        {
            _removables = null;
            ScreenReader.Say(Loc.Fmt("build.removed", name));
            return;
        }

        ScreenReader.Say(Loc.Fmt(IsMarked(wgo) ? "build.remove_now_marked" : "build.remove_now_unmarked", name));
    }

    private static void Step(int dx, int dz)
    {
        _spots = null;
        var grid = GridStep(_controller);
        var ground = CursorGround();
        MoveTo(ground + new Vector3(dx * 0.02f * grid.x, 0f, dz * 0.025f * grid.y));
        SayCursor(interrupt: true);
    }

    /// <summary>The pointer's centre, projected back from any raised build level to the zone's ground plane.</summary>
    private static Vector3 CursorGround()
    {
        var zone = _controller.CurrentWorldZone;
        var cur = CurPos(_controller);
        return zone == null ? cur : VisualConsts.ProjectElevationPointToGround(cur, zone.GroundPlaneY);
    }

    /// <summary>
    /// Puts the pointer's centre on the grid cell nearest a ground point, the same way the game
    /// snaps a mouse hit (<c>UpdatePointerAtPos</c>), and returns whether it may be built there.
    /// </summary>
    private static bool MoveTo(Vector3 ground)
    {
        var zone = _controller.CurrentWorldZone;
        var pointer = Pointer(_controller);
        var shift = pointer.ShiftToVisualCenter;

        if (zone != null) ground.y = zone.GroundPlaneY;
        var snapped = VisualConsts.GetRoundedPosXZ(ground - shift, GridStep(_controller));
        snapped += Vector3.up * 0.006f;
        snapped += shift;
        if (zone != null && zone.TryGetBuildElevationY(snapped.x, snapped.z, out var elevation))
            snapped = VisualConsts.ProjectGroundPointToElevation(snapped, elevation);

        LastSnapped(_controller) = snapped;
        LastCursor(_controller) = CameraSystem.WorldToScreenPoint(snapped);
        _controller.UpdatePointerObjectPosition(snapped);
        _placedByMod = true;
        return CanBuildHere();
    }

    /// <summary>Puts the pointer at an exact position, without the grid snap of <see cref="MoveTo"/>.</summary>
    private static bool MoveExact(Vector3 position)
    {
        LastSnapped(_controller) = position;
        LastCursor(_controller) = CameraSystem.WorldToScreenPoint(position);
        _controller.UpdatePointerObjectPosition(position);
        _placedByMod = true;
        return CanBuildHere();
    }

    /// <summary>True while the pointer stands where the mod put it and the player has not steered it since.</summary>
    private static bool _placedByMod;

    /// <summary>
    /// Keeps the pointer where the mod put it while the camera catches up.
    ///
    /// <para>
    /// Every frame the game re-aims the pointer through the camera: the pointer's spot to the
    /// screen, clamped to the screen's edge (<c>GetCursorPosition</c> → <c>SnapToBounds</c>), and
    /// back to the ground by a raycast. The build camera follows the pointer only over the next
    /// frames, so a free spot off screen - the first one End finds, usually, since the camera still
    /// looks at the player - was pulled to the edge of the screen at once, onto ground that was
    /// blocked, and Enter there said "cannot build here". The spots after it lay near the first, on
    /// screen by then, and worked (user, 2026-10-10: "the first spot is always not buildable").
    /// So a clamped re-aim is skipped until the player steers the pointer with the game's own input.
    /// </para>
    /// </summary>
    [HarmonyPatch(typeof(BuildController), "UpdatePointerAtPos")]
    [HarmonyPrefix]
    private static bool BuildController_UpdatePointerAtPos(BuildController __instance, Vector3 pos, bool forceUpdate)
    {
        if (!_placedByMod || forceUpdate || __instance != _controller) return true;
        try
        {
            Vector3 steer = LazyInput.GetDirection();
            if (!LazyInput.IsGamepadActive || steer.sqrMagnitude > 0f
                || LazyInput.GetKey(GameKey.DpadUp) || LazyInput.GetKey(GameKey.DpadDown)
                || LazyInput.GetKey(GameKey.DpadLeft) || LazyInput.GetKey(GameKey.DpadRight))
            {
                _placedByMod = false;
                return true;
            }

            // Unclamped, the re-aim lands on the same spot and does nothing; only a clamp moves it.
            var aim = CameraSystem.WorldToScreenPoint(LastSnapped(__instance));
            return Mathf.Abs(aim.x - pos.x) < 0.5f && Mathf.Abs(aim.y - pos.y) < 0.5f;
        }
        catch
        {
            return true;
        }
    }

    private static bool CanBuildHere()
    {
        var pointer = Pointer(_controller);
        return pointer?.PointerObject is BuildPointerObject obj && ShownAsActive(obj);
    }

    private static void SayCursor(bool interrupt)
    {
        var player = MainGame.PlayerController;
        var cursor = CurPos(_controller);
        var where = player == null
            ? ""
            : Navigator.KeyDirections(new Vector2(cursor.x - player.MovablePosition.x, cursor.z - player.MovablePosition.z));
        var state = StateText();
        var link = FactoryReader.Placement(PointerWgo()) ?? LinkText() ?? AddOnRoomText();
        if (link != null) state = $"{state}, {link}";
        ScreenReader.Say(Loc.Fmt("build.cursor", state, where), interrupt);
    }

    // ---- what is in the way ------------------------------------------------------------------

    /// <summary>What the last add-on search found standing in the way, most often first.</summary>
    private static List<string> _inTheWay = new List<string>();

    private static readonly AccessTools.FieldRef<BuildPointerObject, List<BuildSelectionCell>> PointerCells =
        AccessTools.FieldRefAccess<BuildPointerObject, List<BuildSelectionCell>>("cells");
    private static readonly AccessTools.FieldRef<WgoBuildPointer, int> PointerMask =
        AccessTools.FieldRefAccess<WgoBuildPointer, int>("overlapMask");
    private static readonly AccessTools.FieldRef<WgoBuildPointer, List<Collider>> PointerOwnColliders =
        AccessTools.FieldRefAccess<WgoBuildPointer, List<Collider>>("buildColliders");
    private static readonly AccessTools.FieldRef<WgoBuildPointer, Wgo> PointerTarget =
        AccessTools.FieldRefAccess<WgoBuildPointer, Wgo>("target");

    private static readonly Collider[] BlockerHits = new Collider[20];

    /// <summary>"blocked by Sawhorse, Chest" - or plain "blocked" when nothing nameable is found.</summary>
    private static string StateText()
    {
        if (CanBuildHere()) return Loc.Get("build.free");
        var unpaid = CannotPayText();
        if (unpaid != null) return unpaid;
        var def = CurrentBuild(_controller)?.Definition;
        List<string> names;
        if (def != null && def.chooseCustomBuildAreaType == BuildingDef.BuildAreaChoosingType.FullCoverSoft)
            names = SlotBlockers(out _);
        else
        {
            var extension = PlacingExtension(CurrentBuild(_controller));
            names = PointerBlockers(extension == null ? null : ConnectedHosts(extension));
        }
        return names.Count == 0 ? Loc.Get("build.blocked") : Loc.Fmt("build.blocked_by", string.Join(", ", names));
    }

    private static readonly MethodInfo CoveredSlot = AccessTools.Method(typeof(WgoBuildPointer), "TryGetCoveredFullCoverSoftBuildArea");

    /// <summary>
    /// Why a building that fills a slot (<c>FullCoverSoft</c>: the bellows on a furnace, the
    /// barracks barricades) is red where the pointer is, by the game's own tests: the pointer must
    /// cover the whole slot (<c>TryGetCoveredFullCoverSoftBuildArea</c>), every cell must be inside
    /// the build zone, off no-build areas and on a marked area, and the slot must be empty but for the building that owns it (<c>IsFullCoverSoftSlotFree</c>: layers
    /// 8, 16, 19). <see cref="PointerBlockers"/> does not fit here - for slot buildings the game
    /// ignores everything else under the cells - and the slot search said nothing at all: the bellows
    /// for a new furnace found "no free spot" with no reason (user, 2026-10-04).
    /// <paramref name="slot"/> is the slot the pointer covers, or null.
    /// </summary>
    private static List<string> SlotBlockers(out BuildArea slot)
    {
        var names = new List<string>();
        slot = null;
        if (!(Pointer(_controller)?.PointerObject is WgoBuildPointer pointer)) return names;
        void Add(string name)
        {
            if (name != null && !names.Contains(name)) names.Add(name);
        }

        try
        {
            var args = new object[] { null };
            if (CoveredSlot == null || !(bool)CoveredSlot.Invoke(pointer, args))
            {
                Add(Loc.Get("build.blocker_not_on_slot"));
                return names;
            }
            slot = (BuildArea)args[0];

            var def = CurrentBuild(_controller)?.Definition;
            var target = PointerTarget(pointer);
            var own = PointerOwnColliders(pointer);
            var owner = slot.GetComponentInParent<Wgo>();
            var zoneId = _controller.CurrentWorldZone?.Data?.Definition?.id;

            // Per cell, as UpdateSelectionCellsState.
            var cells = PointerCells(pointer);
            var mask = PointerMask(pointer);
            if (cells != null)
            {
                foreach (var cell in cells)
                {
                    // Buildings under a cell do not count here (the game skips them for slot
                    // buildings); only the zone, no-build areas and some marked area do.
                    if (cell == null || cell is BuffCell) continue;
                    var inZone = false;
                    var onArea = false;
                    var count = cell.OverlapBoxNonAlloc(BlockerHits, mask);
                    for (var i = 0; i < count; i++)
                    {
                        var col = BlockerHits[i];
                        if (col == null) continue;
                        if (col.gameObject.layer == 29) { Add(Loc.Get("build.blocker_area")); continue; }
                        if (col.TryGetComponent<WorldZone>(out var zone))
                        {
                            if (zone.Data?.Definition?.id == zoneId) inZone = true;
                            continue;
                        }
                        if (own != null && own.Contains(col)) continue;
                        if (PlacementBlockingArea.TryGet(col, out _))
                        {
                            if (PlacementBlockingArea.IsBlockingFor(col, def, target)) Add(Loc.Get("build.blocker_area"));
                            continue;
                        }
                        if (col.TryGetComponent<BuildArea>(out var area) && !area.foprceShowAsBuffAreaForPointerPlacement) onArea = true;
                    }
                    if (!inZone) Add(Loc.Get("build.blocker_outside"));
                    if (!onArea) Add(Loc.Get("build.blocker_not_on_slot"));
                }
            }

            // What stands on the slot, as IsFullCoverSoftSlotFree.
            foreach (var name in SlotOccupants(slot, owner, target, def)) Add(name);
        }
        catch (Exception ex)
        {
            Log?.LogWarning($"[Build] Could not check the slot: {ex.Message}");
        }
        return names;
    }

    /// <summary>
    /// What stands on a slot, by the game's <c>IsFullCoverSoftSlotFree</c>: any building but the
    /// slot's owner and <paramref name="target"/> (the building being placed), temporary objects and
    /// groups <paramref name="def"/> ignores; a no-build area; a bare wall (layers 8, 16).
    /// </summary>
    private static List<string> SlotOccupants(BuildArea slot, Wgo owner, Wgo target, BuildingDef def)
    {
        var names = new List<string>();
        var slotCol = slot.Collider != null ? slot.Collider : slot.GetComponent<Collider>();
        if (slotCol == null) return names;
        var b = slotCol.bounds;
        var half = Vector3.Max(Vector3.zero, b.extents - VisualConsts.XYZ_STEP);
        var hits = new Collider[20];
        var count = Physics.OverlapBoxNonAlloc(b.center, half, hits, Quaternion.identity, 590080);
        for (var i = 0; i < count; i++)
        {
            var col = hits[i];
            if (col == null || col.TryGetComponent<BuildArea>(out _) || col.TryGetComponent<ModuleSlotArea>(out _)) continue;
            if (col.GetComponentInParent<BuildPointerObject>() != null) continue;
            string name = null;
            if (PlacementBlockingArea.TryGet(col, out _))
            {
                if (PlacementBlockingArea.IsBlockingFor(col, def, target)) name = Loc.Get("build.blocker_area");
            }
            else if (col.GetComponentInParent<Wgo>() is Wgo wgo)
            {
                if (wgo == target || wgo == owner || wgo.Data == null || wgo.Data.isTempObject) continue;
                if (def != null && wgo.Data.Definition != null && def.ShouldIgnoreWgoGroupAsObstacle(wgo.Data.Definition.wgoGroup)) continue;
                name = ObjectNames.Of(wgo.Data.id);
            }
            else if (col.gameObject.layer == 8 || col.gameObject.layer == 16) name = Loc.Get("build.blocker_wall");
            if (name != null && !names.Contains(name)) names.Add(name);
        }
        return names;
    }

    /// <summary>
    /// The add-on slots built into a workbench - the marked area a bellows must fill on a furnace
    /// (an area id that <c>GameBalance.customBuildAreaIdToWgoIds</c> maps to one of its add-ons).
    /// Such an add-on goes nowhere else, so the room to keep is that slot, not a strip beside it:
    /// a Schmelzofen II was placed with a sawhorse in its bellows slot while the mod said there was
    /// room for an add-on (user, 2026-10-04).
    /// </summary>
    private static List<BuildArea> AddOnSlots(Wgo workbench)
    {
        var result = new List<BuildArea>();
        if (workbench == null || workbench.Data == null) return result;
        try
        {
            var def = GameBalance.Me.GetWorkbenchExtensionLogicDef(workbench.Data.id);
            var allowed = def == null ? null : GameBalance.Me.GetAllowedExtensionIdsForParentWorkbench(def.id);
            foreach (var area in workbench.GetComponentsInChildren<BuildArea>())
            {
                if (area == null || string.IsNullOrEmpty(area.Id)) continue;
                if (!GameBalance.Me.customBuildAreaIdToWgoIds.TryGetValue(area.Id, out var ids) || ids == null) continue;
                if (ids.Any(id => (allowed != null && allowed.Contains(id)) || GameBalance.Me.IsWorkbenchExtensionId(id))) result.Add(area);
            }
        }
        catch (Exception ex)
        {
            Log?.LogWarning($"[Build] Could not read add-on slots of '{workbench.Data.id}': {ex.Message}");
        }
        return result;
    }

    /// <summary>
    /// For the workbench being placed: null when it has no add-on slot, else what stands in any of
    /// its slots - empty when all are free. Every slot counts: a furnace has one for its bellows and
    /// one for its casting table, and with "one free is enough" a furnace went up with a sawhorse in
    /// its bellows slot because the casting table's was free (log of 2026-10-04).
    /// </summary>
    private static List<string> PlacingSlotBlockers()
    {
        if (!(Pointer(_controller)?.PointerObject is WgoBuildPointer pointer)) return null;
        var target = PointerTarget(pointer);
        var slots = AddOnSlots(target);
        if (slots.Count == 0) return null;

        var names = new List<string>();
        var zone = _controller.CurrentWorldZone;
        foreach (var slot in slots)
        {
            var reasons = SlotOccupants(slot, target, target, null);
            var col = slot.Collider != null ? slot.Collider : slot.GetComponent<Collider>();
            if (col != null && zone != null && zone.ZoneCollider != null)
            {
                var zb = zone.ZoneCollider.bounds;
                var b = col.bounds;
                if (!zb.Contains(new Vector3(b.min.x, zb.center.y, b.min.z)) || !zb.Contains(new Vector3(b.max.x, zb.center.y, b.max.z)))
                    reasons.Add(Loc.Get("build.blocker_outside"));
            }
            foreach (var r in reasons)
                if (!names.Contains(r)) names.Add(r);
        }
        return names;
    }

    private static readonly AccessTools.FieldRef<WgoBuildPointer, Func<bool>> PointerCanPay =
        AccessTools.FieldRefAccess<WgoBuildPointer, Func<bool>>("canTakeResources");

    /// <summary>
    /// "Not enough materials: ..." or "limit reached" when the game would refuse the building
    /// anywhere, else null. <c>WgoBuildPointer</c> shows the pointer red whenever its
    /// <c>canTakeResources</c> fails - the materials ran out after the last one built, or the limit
    /// is reached - so the spot search found nothing and said it does not fit (user, 2026-10-04).
    /// The needs and inventory are only held by that delegate's closure.
    /// </summary>
    private static string CannotPayText()
    {
        try
        {
            if (!(Pointer(_controller)?.PointerObject is WgoBuildPointer pointer)) return null;
            var canPay = PointerCanPay(pointer);
            if (canPay == null || canPay()) return null;

            var closure = canPay.Target;
            T Captured<T>(string name) where T : class =>
                closure == null ? null : AccessTools.Field(closure.GetType(), name)?.GetValue(closure) as T;
            var def = Captured<BuildingDef>("buildingDef") ?? CurrentBuild(_controller)?.Definition;
            var needs = Captured<List<NeedItemData>>("itemNeeds");
            var inventory = Captured<MultiInventory>("multiInventory");

            if (inventory == null || needs == null || inventory.HasItemsById(needs))
                return def != null && def.HasLimits ? Loc.Fmt("build.limit_reached", def.GetLimitsString()) : Loc.Get("build.cannot_pay");

            var missing = new List<string>();
            foreach (var need in needs)
            {
                if (need == null || need.IsEmpty) continue;
                var count = need.GetCount();
                if (need.IsGroup)
                {
                    missing.Add(Loc.Fmt("tooltip.need", count, TmpText.Clean(LLBase.L(need.groupType.ToString()))));
                    continue;
                }
                var have = inventory.GetTotalCount(need.id);
                if (have < count) missing.Add(Loc.Fmt("tooltip.need_have", have, count, ItemText.Name(need.id)));
            }
            return missing.Count == 0 ? Loc.Get("build.cannot_pay") : Loc.Fmt("build.no_materials", string.Join("; ", missing));
        }
        catch (Exception ex)
        {
            Log?.LogWarning($"[Build] Could not check the building's cost: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// The objects under the pointer that make it red, by the game's own per-cell test
    /// (<c>WgoBuildPointer.UpdateSelectionCellsState</c>): layers 8, 19 and 16 within the pointer's
    /// mask, skipping its own colliders, its target, temporary objects and groups the building
    /// ignores; a no-build area (layer 29 or <c>PlacementBlockingArea</c>) is named as such. The game
    /// keeps only "red or not"; a sighted player sees the rest (user, 2026-09-27: the bellows
    /// against the furnace). Workbenches in <paramref name="skip"/> - the ones an add-on stands
    /// against - are left out, since being next to them is the point.
    /// </summary>
    private static List<string> PointerBlockers(List<Wgo> skip)
    {
        var names = new List<string>();
        if (!(Pointer(_controller)?.PointerObject is WgoBuildPointer pointer)) return names;
        var cells = PointerCells(pointer);
        if (cells == null) return names;

        var mask = PointerMask(pointer);
        var own = PointerOwnColliders(pointer);
        var target = PointerTarget(pointer);
        var def = CurrentBuild(_controller)?.Definition;

        foreach (var cell in cells)
        {
            if (cell == null || cell is BuffCell) continue;
            var count = cell.OverlapBoxNonAlloc(BlockerHits, mask);
            for (var i = 0; i < count; i++)
            {
                var col = BlockerHits[i];
                if (col == null || (own != null && own.Contains(col))) continue;

                string name = null;
                var layer = col.gameObject.layer;
                if (layer == 29 || (PlacementBlockingArea.TryGet(col, out _) && PlacementBlockingArea.IsBlockingFor(col, def, target)))
                    name = Loc.Get("build.blocker_area");
                else if (layer == 8 || layer == 19 || layer == 16)
                {
                    if (col.TryGetComponent<WorldZone>(out _) || col.TryGetComponent<BuildArea>(out _)) continue;
                    var wgo = col.GetComponentInParent<Wgo>();
                    if (wgo != null && wgo == target) continue;
                    if (wgo != null && skip != null && skip.Contains(wgo)) continue;
                    if (wgo != null && wgo.Data != null && (wgo.Data.isTempObject
                        || (def != null && wgo.Data.Definition != null && def.ShouldIgnoreWgoGroupAsObstacle(wgo.Data.Definition.wgoGroup)))) continue;
                    name = wgo != null && wgo.Data != null ? ObjectNames.Of(wgo.Data.id) : Loc.Get("build.blocker_wall");
                }
                if (name != null && !names.Contains(name)) names.Add(name);
            }
        }
        return names;
    }

    // ---- workbench add-ons ------------------------------------------------------------------

    /// <summary>
    /// The parent workbenches the pointer overlaps right now - the game's own list, which it draws
    /// as a connection line (<c>WgoBuildPointer.UpdateSelectionStuff</c>, refreshed on every move).
    /// </summary>
    private static readonly AccessTools.FieldRef<WgoBuildPointer, HashSet<Wgo>> OverlappingHosts =
        AccessTools.FieldRefAccess<WgoBuildPointer, HashSet<Wgo>>("overlappingHostsBuffer");

    /// <summary>
    /// The add-on id when the building being placed is a workbench add-on (hardening bucket,
    /// bellows), else null. Add-ons only work standing against their workbench, which the free-spot
    /// search knew nothing about: the first bucket built went beside the woodshed, connected to
    /// nothing (reported 2026-09-26).
    /// </summary>
    private static string PlacingExtension(BuildData data)
    {
        if (data == null) return null;
        try
        {
            foreach (var id in new[] { data.Definition?.customWgoPlacePreview, data.WgoId })
                if (!string.IsNullOrEmpty(id) && GameBalance.Me.IsWorkbenchExtensionId(id)) return id;
        }
        catch
        {
            // Not an add-on, as far as can be told.
        }
        return null;
    }

    private static List<Wgo> ConnectedHosts(string extensionId)
    {
        var result = new List<Wgo>();
        if (!(Pointer(_controller)?.PointerObject is WgoBuildPointer pointer)) return result;
        var hosts = OverlappingHosts(pointer);
        if (hosts == null) return result;

        GameBalance.Me.TryGetParentWorkbenchDefsForExtension(extensionId, out var defs);
        foreach (var host in hosts)
        {
            if (host == null || host.Data?.Definition == null) continue;
            if (defs == null || defs.Contains(host.Data.Definition)) result.Add(host);
        }
        return result;
    }

    /// <summary>
    /// The add-ons the workbench being placed would take on where the pointer is - for a workbench
    /// the game fills the same list with the add-ons under its footprint
    /// (<c>FillSelectionRectsForParentOverlappingExtensions</c>).
    /// </summary>
    private static List<Wgo> ConnectedAddOns()
    {
        var result = new List<Wgo>();
        if (!(Pointer(_controller)?.PointerObject is WgoBuildPointer pointer)) return result;
        var hosts = OverlappingHosts(pointer);
        if (hosts == null) return result;
        foreach (var host in hosts)
            if (host != null && host.Data != null && GameBalance.Me.IsWorkbenchExtensionId(host.Data.id)) result.Add(host);
        return result;
    }

    /// <summary>The add-on ids the workbench being placed takes, or null when it takes none.</summary>
    private static HashSet<string> AllowedAddOns(BuildData data)
    {
        if (data == null || PlacingExtension(data) != null) return null;
        try
        {
            foreach (var id in new[] { data.Definition?.customWgoPlacePreview, data.WgoId })
            {
                if (!TakesAddOns(id)) continue;
                var def = GameBalance.Me.GetWorkbenchExtensionLogicDef(id);
                return GameBalance.Me.GetAllowedExtensionIdsForParentWorkbench(def.id);
            }
        }
        catch
        {
            // Takes none, as far as can be told.
        }
        return null;
    }

    /// <summary>
    /// Add-ons in this zone the workbench being placed could take and that have no workbench,
    /// nearest the player first. Tearing down an old anvil leaves its hardening
    /// bucket standing alone; the new anvil belongs right against it, and the free-spot search
    /// offered only spots around the player - the user built the anvil elsewhere and had to tear
    /// the bucket down too (2026-10-04).
    /// </summary>
    private static List<Wgo> AddOnsToJoin(HashSet<string> allowed, Vector3 playerPos)
    {
        var result = new List<Wgo>();
        if (allowed == null || allowed.Count == 0) return result;
        var zone = _controller.CurrentWorldZone;
        var world = MainGame.Instance?.GameSave?.WorldData;
        foreach (var wgo in Navigator.SpawnedWgos)
        {
            if (wgo == null || wgo.Data == null || !allowed.Contains(wgo.Data.id)) continue;
            if (wgo.GetComponentInParent<BuildPointerObject>() != null) continue;
            var p = wgo.Data.Position;
            if (zone != null && zone.ZoneCollider != null
                && !zone.ZoneCollider.bounds.Contains(new Vector3(p.x, zone.ZoneCollider.bounds.center.y, p.z))) continue;
            result.Add(wgo);
        }
        // Only add-ons standing alone: one still serving another workbench needs nothing new.
        bool Alone(Wgo w) => w.Data.WorkbenchParents == null || !w.Data.WorkbenchParents.Any(g => world?.GetWgoData(g) != null);
        return result.Where(Alone).OrderBy(w => Flat(w.Data.Position - playerPos).sqrMagnitude).ToList();
    }

    /// <summary>"connects to wooden anvil" / "not connected, ...", or null when not placing an add-on.</summary>
    private static string LinkText()
    {
        var extension = PlacingExtension(CurrentBuild(_controller));
        if (extension == null)
        {
            // A workbench set down against an add-on that is already there.
            var addOns = AllowedAddOns(CurrentBuild(_controller)) != null ? ConnectedAddOns() : null;
            return addOns != null && addOns.Count > 0
                ? Loc.Fmt("build.ext_linked", string.Join(", ", addOns.Select(h => ObjectNames.Of(h.Data.id)).Distinct()))
                : null;
        }

        var hosts = ConnectedHosts(extension);
        return hosts.Count > 0
            ? Loc.Fmt("build.ext_linked", string.Join(", ", hosts.Select(h => ObjectNames.Of(h.Data.id)).Distinct()))
            : Loc.Fmt("build.ext_unlinked", ObjectStatus.ParentNames(extension));
    }

    /// <summary>The workbenches in this zone an add-on could stand against, nearest the player first.</summary>
    private static List<Wgo> ParentWorkbenches(string extensionId, Vector3 playerPos)
    {
        var result = new List<Wgo>();
        if (!GameBalance.Me.TryGetParentWorkbenchDefsForExtension(extensionId, out var defs) || defs == null) return result;

        var zone = _controller.CurrentWorldZone;
        foreach (var wgo in Navigator.SpawnedWgos)
        {
            if (wgo == null || wgo.Data?.Definition == null || !defs.Contains(wgo.Data.Definition)) continue;
            var p = wgo.Data.Position;
            if (zone != null && zone.ZoneCollider != null
                && !zone.ZoneCollider.bounds.Contains(new Vector3(p.x, zone.ZoneCollider.bounds.center.y, p.z))) continue;
            result.Add(wgo);
        }
        result.Sort((a, b) => Flat(a.Data.Position - playerPos).sqrMagnitude.CompareTo(Flat(b.Data.Position - playerPos).sqrMagnitude));
        return result;
    }

    private static void NextFreeSpot()
    {
        // Unpaid, the pointer is red everywhere: say why instead of searching.
        var unpaid = CannotPayText();
        if (unpaid != null)
        {
            Log?.LogInfo($"[Build] Cannot pay for '{CurrentBuild(_controller)?.WgoId}': {unpaid}");
            ScreenReader.Say(unpaid);
            return;
        }

        if (_spots == null)
        {
            _spots = FindFreeSpots();
            _spotIndex = -1;
            Log?.LogInfo($"[Build] Found {_spots.Count} free spot(s).");
        }

        if (_spots.Count == 0)
        {
            if (_coverHint != null)
            {
                ScreenReader.Say(_coverHint);
                return;
            }
            var extension = PlacingExtension(CurrentBuild(_controller));
            var none = extension == null
                ? Loc.Get("build.no_free_spot")
                : Loc.Fmt("build.ext_no_spot", ObjectStatus.ParentNames(extension));
            if (_inTheWay.Count > 0)
                none = Loc.Fmt("build.in_the_way_long", none, string.Join(", ", _inTheWay));
            ScreenReader.Say(none);
            return;
        }

        _spotIndex = (_spotIndex + 1) % _spots.Count;
        if (_spotsExact) MoveExact(_spots[_spotIndex]);
        else MoveTo(_spots[_spotIndex]);
        var player = MainGame.PlayerController;
        var cursor = CurPos(_controller);
        var where = player == null ? "" : Navigator.KeyDirections(new Vector2(cursor.x - player.MovablePosition.x, cursor.z - player.MovablePosition.z));
        var state = StateText();
        var link = LinkText() ?? AddOnRoomText();
        if (link != null) state = $"{state}, {link}";
        var line = Loc.Fmt("build.spot", _spotIndex + 1, _spots.Count, state, where);
        if (_spotsCompromised && _spotIndex == 0) line = $"{Loc.Get("build.addon_compromise")} {line}";
        ScreenReader.Say(line);
    }

    /// <summary>
    /// Candidate points on every pre-marked area with this id in the zone: the centre first, then a
    /// 3 × 3 spread over the area, so an area larger than the building still gets a green spot.
    ///
    /// <para>
    /// The areas are found from their <c>BuildArea</c> components, not only by the physics overlap
    /// the game uses for its own hints. In the church the overlap found no <c>bench_place</c> area
    /// at all (log of 2026-09-26), the search fell back to rings around the player, and its
    /// 2500-try cap reaches only about 6 m - one bench was found, and after it nothing. Both ways
    /// are counted in the log so the difference shows.
    /// </para>
    /// </summary>
    /// <summary>Corners of the marked areas last searched, for offering packed spots first.</summary>
    private static readonly List<Vector3> AreaCorners = new();

    /// <summary>
    /// How finely <see cref="AddMarkedAreas"/> covers each area: 0 the centres only, 1 a 3 × 3
    /// spread, 2 every build-grid cell. Each pass runs only when the one before found nothing:
    /// the fine grid on all 32 grave plots of the temple graveyard was 3552 points, and the 2500
    /// placement checks it ran before answering left Space silent for seconds (user, 2026-10-03).
    /// </summary>
    private const int AreaPasses = 3;

    private static void AddMarkedAreas(string areaId, WorldZone zone, List<Vector3> candidates, int pass)
    {
        var bounds = zone.ZoneCollider.bounds;
        bool InZone(Vector3 p) => bounds.Contains(new Vector3(p.x, bounds.center.y, p.z));

        var areas = new HashSet<BuildArea>();
        var byOverlap = 0;
        foreach (var col in Physics.OverlapBox(bounds.center, bounds.extents, Quaternion.identity, 524288, QueryTriggerInteraction.Collide))
        {
            if (col == null || !col.TryGetComponent<BuildArea>(out var area) || area.Id != areaId) continue;
            if (areas.Add(area)) byOverlap++;
        }

        var inactive = 0;
        AreaCorners.Clear();
        foreach (var area in UnityEngine.Object.FindObjectsByType<BuildArea>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (area == null || area.Id != areaId) continue;
            var col = area.Collider != null ? area.Collider : area.GetComponent<Collider>();
            if (col == null || !InZone(col.bounds.center)) continue;
            if (!area.isActiveAndEnabled || !col.enabled)
            {
                inactive++;
                continue;
            }
            areas.Add(area);
        }

        // The centre, then every build-grid cell over the area and a metre around it. A Soft area
        // (the barracks barricades) needs every cell of the building on it, and the barracks area
        // is packed tight: 2.56 x 1.8 m holds two barricades (1.6 x 0.9) side by side and a third,
        // turned (0.96 x 1.5), in the 0.96 m strip left over - at exactly one position. A 3 x 3
        // spread, then a grid of every second cell, both missed it (user, 2026-10-03). One cell is
        // the step the arrow keys move (see Step). Capped at ~60 x 60 per area.
        var grid = GridStep(_controller);
        var cellX = Mathf.Max(0.05f, 0.02f * grid.x);
        var cellZ = Mathf.Max(0.05f, 0.025f * grid.y);
        foreach (var area in areas)
        {
            var b = (area.Collider != null ? area.Collider : area.GetComponent<Collider>()).bounds;
            candidates.Add(VisualConsts.ProjectElevationPointToGround(b.center, zone.GroundPlaneY));
            AreaCorners.Add(new Vector3(b.min.x, 0f, b.min.z));
            AreaCorners.Add(new Vector3(b.min.x, 0f, b.max.z));
            AreaCorners.Add(new Vector3(b.max.x, 0f, b.min.z));
            AreaCorners.Add(new Vector3(b.max.x, 0f, b.max.z));
            if (pass == 0) continue;
            if (pass == 1)
            {
                for (var i = -1; i <= 1; i++)
                for (var j = -1; j <= 1; j++)
                {
                    if (i == 0 && j == 0) continue;
                    var p = b.center + new Vector3(i * b.extents.x * 0.6f, 0f, j * b.extents.z * 0.6f);
                    candidates.Add(VisualConsts.ProjectElevationPointToGround(p, zone.GroundPlaneY));
                }
                continue;
            }
            var stepX = Mathf.Max(cellX, (2f * b.extents.x + 2f) / 60f);
            var stepZ = Mathf.Max(cellZ, (2f * b.extents.z + 2f) / 60f);
            for (var x = b.min.x - 1f; x <= b.max.x + 1f; x += stepX)
            for (var z = b.min.z - 1f; z <= b.max.z + 1f; z += stepZ)
                candidates.Add(VisualConsts.ProjectElevationPointToGround(new Vector3(x, b.center.y, z), zone.GroundPlaneY));
            Log?.LogInfo($"[Build] Area '{areaId}' {2f * b.extents.x:0.00} x {2f * b.extents.z:0.00} m at ({b.center.x:0.00}, {b.center.z:0.00}), tried every {stepX:0.00} x {stepZ:0.00} m.");
        }

        Log?.LogInfo($"[Build] {areas.Count} marked area(s) '{areaId}' in the zone ({byOverlap} by overlap, {inactive} switched off), pass {pass}: {candidates.Count} point(s) to try.");
    }

    /// <summary>
    /// Ground points where the pointer shows green, nearest the player first. Buildings that must sit
    /// on a pre-marked area (graves on grave plots, for instance) are tried at every such area in the
    /// zone; anything else by a ring search around the player. The pointer is put back afterwards.
    /// </summary>
    private static List<Vector3> FindFreeSpots()
    {
        var result = new List<Vector3>();
        var start = CursorGround();
        var playerPos = MainGame.PlayerController != null ? MainGame.PlayerController.MovablePosition : start;
        var zone = _controller.CurrentWorldZone;

        var candidates = new List<Vector3>();
        var def = CurrentBuild(_controller)?.Definition;
        AreaCorners.Clear();

        _spotsExact = false;
        _coverHint = null;
        _inTheWay = new List<string>();
        if (def != null && def.chooseCustomBuildAreaType == BuildingDef.BuildAreaChoosingType.FullCoverSoft
            && !string.IsNullOrEmpty(def.customBuildAreaId) && zone != null && zone.ZoneCollider != null)
            return FullCoverSpots(def.customBuildAreaId, zone, start, playerPos);

        // An add-on is only any use against its workbench: search around each one instead of
        // around the player, and keep only spots the game would connect.
        var extension = PlacingExtension(CurrentBuild(_controller));
        if (extension != null)
        {
            var parents = ParentWorkbenches(extension, playerPos);
            Log?.LogInfo($"[Build] Add-on '{extension}': {parents.Count} workbench(es) to stand against.");

            var grid = GridStep(_controller);
            var sx = Mathf.Max(0.1f, 0.04f * grid.x);
            var sz = Mathf.Max(0.1f, 0.05f * grid.y);
            var rings = Mathf.Min(40, Mathf.CeilToInt(4f / Mathf.Min(sx, sz)));
            foreach (var parent in parents)
            {
                var origin = new Vector3(parent.Data.Position.x, start.y, parent.Data.Position.z);
                for (var r = 1; r <= rings; r++)
                for (var i = -r; i <= r; i++)
                for (var j = -r; j <= r; j++)
                {
                    if (Mathf.Abs(i) != r && Mathf.Abs(j) != r) continue;
                    candidates.Add(origin + new Vector3(i * sx, 0f, j * sz));
                }
            }

            var linked = new List<Vector3>();
            var tally = new Dictionary<string, int>();
            var tried = 0;
            foreach (var c in candidates)
            {
                if (++tried > 2500 || linked.Count >= 8) break;
                var free = MoveTo(c);
                var hosts = ConnectedHosts(extension);
                if (hosts.Count == 0) continue;
                if (!free)
                {
                    // Against the workbench but red: note what is in the way (user, 2026-09-27).
                    foreach (var name in PointerBlockers(hosts))
                        tally[name] = tally.TryGetValue(name, out var n) ? n + 1 : 1;
                    continue;
                }
                var at = CursorGround();
                if (linked.Exists(s => Flat(s - at).sqrMagnitude < 1f)) continue;
                linked.Add(at);
            }

            MoveTo(start);
            _spotsCompromised = false;
            var ranked = tally.OrderByDescending(kv => kv.Value).ToList();
            _inTheWay = ranked.Select(kv => kv.Key).ToList();
            Log?.LogInfo($"[Build] In the way of '{extension}': " +
                         (ranked.Count == 0 ? "nothing found" : string.Join(", ", ranked.Select(kv => $"{kv.Key} x{kv.Value}"))) + ".");
            return linked;
        }

        var joining = JoiningSpots(start, playerPos);

        var marked = def != null && !string.IsNullOrEmpty(def.customBuildAreaId) && zone != null && zone.ZoneCollider != null;
        var reserved = ReservedBands();
        var placingParent = TakesAddOns(CurrentBuild(_controller));
        if (placingParent && Pointer(_controller)?.PointerObject is WgoBuildPointer slotPointer)
        {
            var slots = AddOnSlots(PointerTarget(slotPointer));
            Log?.LogInfo($"[Build] '{CurrentBuild(_controller)?.WgoId}' has {slots.Count} add-on slot(s){(slots.Count == 0 ? "; keeping a strip beside it instead" : ": " + string.Join(", ", slots.Select(a => a.Id)))}.");
        }
        var fallback = new List<Vector3>();
        // A Soft area holding several buildings (the barracks barricades) must be packed from a
        // corner. The centre alone passes on an empty area, and a first barricade built there left
        // no room for any other (log of 2026-10-04): on a few such areas, go on to the fine grid.
        var packTight = marked && def.chooseCustomBuildAreaType == BuildingDef.BuildAreaChoosingType.Soft;
        for (var pass = 0; ; pass++)
        {
            candidates.Clear();
            if (marked) AddMarkedAreas(def.customBuildAreaId, zone, candidates, pass);
            var noAreas = candidates.Count == 0;
            CheckSpots(candidates, start, playerPos, zone, def, reserved, placingParent, result, fallback);
            if (noAreas || pass + 1 >= AreaPasses) break;
            // Only spots that fail the add-on rules so far: look finer before settling for them (a
            // furnace on the yard found one spot at the area centres, its bellows slot blocked).
            if (result.Count > 0)
            {
                if (!packTight || AreaCorners.Count > 16) break;
                pass = AreaPasses - 2;
            }
        }

        Log?.LogInfo($"[Build] {reserved.Count} workbench(es) keeping room for an add-on; placing one that takes add-ons: {placingParent}; {fallback.Count} spot(s) set aside.");
        MoveTo(start);

        // Nothing found on a marked area: the free room may only fit the building turned.
        if (result.Count == 0 && fallback.Count == 0 && def != null && !string.IsNullOrEmpty(def.customBuildAreaId))
            _coverHint = Loc.Get("build.cover_turn");

        if (result.Count == 0 && fallback.Count > 0 && joining.Count == 0)
        {
            _spotsCompromised = true;
            return fallback;
        }
        _spotsCompromised = false;

        // On a marked area, a building in a corner leaves the most room for the next one: in the
        // barracks a first barricade in the middle would leave no room for the other two.
        if (AreaCorners.Count > 0)
            result = result.OrderBy(r => AreaCorners.Min(c => Flat(c - r).sqrMagnitude)).Take(8).ToList();

        // Spots against an add-on already standing come first; they need no room kept for one.
        if (joining.Count > 0)
            result = joining.Concat(result.Where(r => !joining.Exists(j => Flat(j - r).sqrMagnitude < 1f))).ToList();
        return result;
    }

    /// <summary>
    /// Green spots where the workbench being placed would connect to an add-on already standing
    /// (see <see cref="AddOnsToJoin"/>), at most three per add-on, or none when it takes no add-ons.
    /// </summary>
    private static List<Vector3> JoiningSpots(Vector3 start, Vector3 playerPos)
    {
        var found = new List<Vector3>();
        var addOns = AddOnsToJoin(AllowedAddOns(CurrentBuild(_controller)), playerPos);
        if (addOns.Count == 0) return found;

        var grid = GridStep(_controller);
        var sx = Mathf.Max(0.1f, 0.04f * grid.x);
        var sz = Mathf.Max(0.1f, 0.05f * grid.y);
        var rings = Mathf.Min(40, Mathf.CeilToInt(4f / Mathf.Min(sx, sz)));
        var tried = 0;
        foreach (var addOn in addOns)
        {
            var origin = new Vector3(addOn.Data.Position.x, start.y, addOn.Data.Position.z);
            var mine = 0;
            for (var r = 1; r <= rings && mine < 3 && tried < 2500; r++)
            for (var i = -r; i <= r && mine < 3; i++)
            for (var j = -r; j <= r && mine < 3; j++)
            {
                if (Mathf.Abs(i) != r && Mathf.Abs(j) != r) continue;
                if (++tried > 2500) break;
                if (!MoveTo(origin + new Vector3(i * sx, 0f, j * sz))) continue;
                if (!ConnectedAddOns().Contains(addOn)) continue;
                var at = CursorGround();
                if (found.Exists(s => Flat(s - at).sqrMagnitude < 1f)) continue;
                found.Add(at);
                mine++;
            }
        }
        MoveTo(start);
        Log?.LogInfo($"[Build] {addOns.Count} add-on(s) to join ({string.Join(", ", addOns.Select(a => a.Data.id))}): {found.Count} spot(s) against them, {tried} tried.");
        return found;
    }

    /// <summary>
    /// Puts the pointer on each candidate, nearest the player first, and adds the green ones to
    /// <paramref name="result"/>, or to <paramref name="fallback"/> when they fail only the add-on
    /// room tests. With no candidates, searches rings around the player.
    /// </summary>
    private static void CheckSpots(List<Vector3> candidates, Vector3 start, Vector3 playerPos, WorldZone zone, BuildingDef def,
        List<(Wgo Owner, Bounds Band)> reserved, bool placingParent, List<Vector3> result, List<Vector3> fallback)
    {
        if (candidates.Count == 0)
        {
            // A spiral of grid-sized steps, two cells apart, out to about twelve metres.
            var grid = GridStep(_controller);
            var sx = Mathf.Max(0.1f, 0.04f * grid.x);
            var sz = Mathf.Max(0.1f, 0.05f * grid.y);
            var origin = new Vector3(playerPos.x, start.y, playerPos.z);
            var rings = Mathf.Min(60, Mathf.CeilToInt(12f / Mathf.Min(sx, sz)));
            for (var r = 1; r <= rings; r++)
            for (var i = -r; i <= r; i++)
            for (var j = -r; j <= r; j++)
            {
                if (Mathf.Abs(i) != r && Mathf.Abs(j) != r) continue;
                var p = origin + new Vector3(i * sx, 0f, j * sz);
                if (zone != null && zone.ZoneCollider != null && !zone.ZoneCollider.bounds.Contains(new Vector3(p.x, zone.ZoneCollider.bounds.center.y, p.z))) continue;
                candidates.Add(p);
            }
        }

        candidates.Sort((a, b) => Flat(a - playerPos).sqrMagnitude.CompareTo(Flat(b - playerPos).sqrMagnitude));

        // Room for add-ons, both ways round: a spot that would take the space a workbench keeps for
        // its first add-on is not offered, and a workbench that takes add-ons is offered first
        // where one would still fit beside it. Spots that fail only these tests are kept as a
        // fallback rather than leaving the player with nothing.
        var minGap = def != null && !string.IsNullOrEmpty(def.customBuildAreaId) ? 0.05f : 1f;
        const int maxChecks = 2500;
        var wanted = AreaCorners.Count > 0 ? 200 : 8;
        var checks = 0;
        foreach (var c in candidates)
        {
            if (++checks > maxChecks || result.Count >= wanted) break;
            if (!MoveTo(c)) continue;

            // Spots closer than a metre are one spot - except on a marked area, where two
            // barricade places are 0.9 m apart and each one counts.
            var at = CursorGround();
            if (result.Exists(s => Flat(s - at).sqrMagnitude < minGap * minGap)) continue;

            if (ReservedBy(reserved) != null || (placingParent && !HasRoomForAddOn()))
            {
                if (fallback.Count < wanted && !fallback.Exists(s => Flat(s - at).sqrMagnitude < 1f)) fallback.Add(at);
                continue;
            }
            result.Add(at);
        }
    }

    // ---- areas the building must cover ------------------------------------------------------

    /// <summary>Said instead of "no free spot" when the free areas need the building turned.</summary>
    private static string _coverHint;

    /// <summary>
    /// Spots for a building whose area type is <c>FullCoverSoft</c> (the barracks barricades):
    /// the building's footprint must contain the whole marked area
    /// (<c>WgoBuildPointer.TryGetCoveredFullCoverSoftBuildArea</c>) and nothing may stand on the
    /// area. A grid search near it can never pass - the footprint is barely larger than the area -
    /// so the third barricade found nothing at any rotation (user, 2026-10-03). The game's own
    /// green ghost goes on the area's centre, turned to the area's <c>RotationRequirement</c>; this
    /// does the same: put the pointer down, measure where its footprint lands, shift it onto the
    /// centre, and keep the exact position.
    /// </summary>
    private static List<Vector3> FullCoverSpots(string areaId, WorldZone zone, Vector3 start, Vector3 playerPos)
    {
        var found = new List<(Vector3 Position, float Distance)>();
        var wrongTurn = 0;
        var pointer = Pointer(_controller);
        var rotation = pointer?.PointerObject is WgoBuildPointer wgoPointer && wgoPointer.Target != null
            ? wgoPointer.Target.MainWgoPart.WgoPartData.rotationIndex : -1;

        var bounds = zone.ZoneCollider.bounds;
        foreach (var area in UnityEngine.Object.FindObjectsByType<BuildArea>(FindObjectsSortMode.None))
        {
            if (area == null || area.Id != areaId || !area.isActiveAndEnabled) continue;
            var col = area.Collider != null ? area.Collider : area.GetComponent<Collider>();
            if (col == null || !col.enabled) continue;
            var b = col.bounds;
            if (!bounds.Contains(new Vector3(b.center.x, bounds.center.y, b.center.z))) continue;

            MoveTo(VisualConsts.ProjectElevationPointToGround(b.center, zone.GroundPlaneY));
            var footprint = pointer?.PointerObject is BuildPointerObject obj ? obj.GetWorldRoundedBounds() : default;
            var exact = LastSnapped(_controller) + new Vector3(b.center.x - footprint.center.x, 0f, b.center.z - footprint.center.z);
            var free = MoveExact(exact);
            var after = pointer?.PointerObject is BuildPointerObject obj2 ? obj2.GetWorldRoundedBounds() : default;

            var needsTurn = area.HasRotationRequirement && rotation != -1 && area.RotationRequirement != rotation;
            Log?.LogInfo($"[Build] Cover area at ({b.center.x:0.00}, {b.center.z:0.00}) size {b.size.x:0.00} x {b.size.z:0.00}; " +
                         $"building {after.size.x:0.00} x {after.size.z:0.00} at ({after.center.x:0.00}, {after.center.z:0.00}); " +
                         $"rotation {rotation}, area wants {(area.HasRotationRequirement ? area.RotationRequirement.ToString() : "any")}; free: {free}.");

            if (!free)
            {
                // Say why, per slot: "Schmelzofen II: Holzvorrat" (user, 2026-10-04).
                var reasons = CannotPayText() is string unpaid ? new List<string> { unpaid } : SlotBlockers(out _);
                var owner = area.GetComponentInParent<Wgo>();
                var host = owner != null && owner.Data != null ? ObjectNames.Of(owner.Data.id) : null;
                Log?.LogInfo($"[Build] Slot of '{owner?.Data?.id}' at ({b.center.x:0.00}, {b.center.z:0.00}) not free: {(reasons.Count == 0 ? "no reason found" : string.Join(", ", reasons))}.");
                if (reasons.Count > 0)
                {
                    var why = string.Join(", ", reasons);
                    _inTheWay.Add(host == null ? why : Loc.Fmt("build.slot_blocked", host, why));
                }
            }

            if (free) found.Add((exact, Flat(exact - playerPos).magnitude));
            else if (needsTurn || after.size.x + 0.01f < b.size.x || after.size.z + 0.01f < b.size.z) wrongTurn++;
        }

        MoveTo(start);
        _spotsExact = true;
        if (found.Count == 0 && wrongTurn > 0) _coverHint = Loc.Get("build.cover_turn");
        return found.OrderBy(f => f.Distance).Select(f => f.Position).ToList();
    }

    // ---- keeping room for add-ons -----------------------------------------------------------

    /// <summary>
    /// How much room beside a workbench is kept for an add-on. The game has no size for an add-on
    /// until one is built; the ones seen (hardening bucket, bellows) are about a metre across, so
    /// a metre and a half leaves space to set one down.
    /// </summary>
    private const float AddOnBand = 1.5f;

    /// <summary>Footprint layer: every building's own collider, the one placement tests against.</summary>
    private const int FootprintLayer = 19;

    /// <summary>True when the free spots on offer all failed the add-on room tests (said once on the first spot).</summary>
    private static bool _spotsCompromised;

    private static readonly AccessTools.FieldRef<WgoBuildPointer, List<Collider>> BuildColliders =
        AccessTools.FieldRefAccess<WgoBuildPointer, List<Collider>>("buildColliders");

    /// <summary>True for a workbench that add-ons attach to (furnace, anvil, ...).</summary>
    private static bool TakesAddOns(string wgoId)
    {
        if (string.IsNullOrEmpty(wgoId)) return false;
        try
        {
            var def = GameBalance.Me.GetWorkbenchExtensionLogicDef(wgoId);
            return def != null && GameBalance.Me.workbenchesWhichUseExtensions.Contains(def)
                   && GameBalance.Me.GetAllowedExtensionIdsForParentWorkbench(def.id).Count > 0;
        }
        catch
        {
            return false;
        }
    }

    private static bool TakesAddOns(BuildData data) =>
        data != null && PlacingExtension(data) == null
        && (TakesAddOns(data.Definition?.customWgoPlacePreview) || TakesAddOns(data.WgoId));

    private static Bounds? Footprint(IEnumerable<Collider> colliders)
    {
        Bounds? result = null;
        foreach (var col in colliders)
        {
            if (col == null || !col.enabled || col.gameObject.layer != FootprintLayer) continue;
            if (result == null) result = col.bounds;
            else { var b = result.Value; b.Encapsulate(col.bounds); result = b; }
        }
        return result;
    }

    private static Bounds? PointerFootprint()
    {
        if (!(Pointer(_controller)?.PointerObject is WgoBuildPointer pointer)) return null;
        var cols = BuildColliders(pointer);
        return cols == null ? null : Footprint(cols);
    }

    /// <summary>
    /// The band beside every workbench here that takes add-ons and has none yet - the user's rule:
    /// the space stays free until the first add-on is there.
    /// </summary>
    private static List<(Wgo Owner, Bounds Band)> ReservedBands()
    {
        var result = new List<(Wgo, Bounds)>();
        try
        {
            foreach (var wgo in Navigator.SpawnedWgos)
            {
                if (wgo == null || wgo.Data == null || wgo.GetComponentInParent<BuildPointerObject>() != null) continue;
                if (!TakesAddOns(wgo.Data.id)) continue;
                var attached = wgo.Data.AttachedWorkbenchExtensions;
                if (attached != null && attached.Count > 0) continue;

                // A workbench with a slot for its add-on keeps that slot, nothing more.
                var slots = AddOnSlots(wgo);
                if (slots.Count > 0)
                {
                    foreach (var slot in slots)
                    {
                        var col = slot.Collider != null ? slot.Collider : slot.GetComponent<Collider>();
                        if (col == null) continue;
                        var area = col.bounds;
                        area.Expand(new Vector3(0f, 1000f, 0f));
                        result.Add((wgo, area));
                    }
                    continue;
                }

                var footprint = Footprint(wgo.GetComponentsInChildren<Collider>());
                if (footprint == null) continue;
                var band = footprint.Value;
                band.Expand(new Vector3(AddOnBand * 2f, 1000f, AddOnBand * 2f));
                result.Add((wgo, band));
            }
        }
        catch (Exception ex)
        {
            Log?.LogWarning($"[Build] Could not read workbench add-on room: {ex.Message}");
        }
        return result;
    }

    /// <summary>The workbench whose add-on room the building under the pointer would take, or null.</summary>
    private static Wgo ReservedBy(List<(Wgo Owner, Bounds Band)> bands)
    {
        if (bands.Count == 0 || PlacingExtension(CurrentBuild(_controller)) != null) return null;
        var footprint = PointerFootprint();
        if (footprint == null) return null;

        // A hair smaller, so sharing an edge with the band is not counted as standing in it.
        var fp = footprint.Value;
        fp.Expand(new Vector3(-0.1f, 0f, -0.1f));
        foreach (var (owner, band) in bands)
            if (owner != null && band.Intersects(fp)) return owner;
        return null;
    }

    /// <summary>
    /// True when a strip as wide as <see cref="AddOnBand"/> is clear along at least one side of the
    /// building under the pointer - no other building's footprint in it, and inside the build zone.
    /// For a workbench with an add-on slot, true when a slot is free.
    /// </summary>
    private static bool HasRoomForAddOn()
    {
        // A workbench with a slot for its add-on (furnace and bellows): only that slot counts.
        var slotBlockers = PlacingSlotBlockers();
        if (slotBlockers != null) return slotBlockers.Count == 0;

        var footprint = PointerFootprint();
        if (footprint == null) return true;
        var fp = footprint.Value;
        var own = Pointer(_controller)?.PointerObject is WgoBuildPointer p ? BuildColliders(p) : null;
        var zone = _controller.CurrentWorldZone;
        var hits = new Collider[16];

        var sides = new[]
        {
            (new Vector3(fp.extents.x + AddOnBand / 2f, 0f, 0f), new Vector3(AddOnBand / 2f, 1f, fp.extents.z)),
            (new Vector3(-fp.extents.x - AddOnBand / 2f, 0f, 0f), new Vector3(AddOnBand / 2f, 1f, fp.extents.z)),
            (new Vector3(0f, 0f, fp.extents.z + AddOnBand / 2f), new Vector3(fp.extents.x, 1f, AddOnBand / 2f)),
            (new Vector3(0f, 0f, -fp.extents.z - AddOnBand / 2f), new Vector3(fp.extents.x, 1f, AddOnBand / 2f)),
        };

        foreach (var (offset, half) in sides)
        {
            var center = fp.center + offset;
            if (zone != null && zone.ZoneCollider != null)
            {
                var zb = zone.ZoneCollider.bounds;
                var y = zb.center.y;
                if (!zb.Contains(new Vector3(center.x - half.x, y, center.z - half.z))
                    || !zb.Contains(new Vector3(center.x + half.x, y, center.z + half.z))) continue;
            }

            var shrunk = new Vector3(Mathf.Max(0.05f, half.x - 0.05f), half.y, Mathf.Max(0.05f, half.z - 0.05f));
            var count = Physics.OverlapBoxNonAlloc(center, shrunk, hits, Quaternion.identity, 1 << FootprintLayer, QueryTriggerInteraction.Collide);
            var blocked = false;
            for (var i = 0; i < count; i++)
            {
                var col = hits[i];
                if (col == null || (own != null && own.Contains(col)) || col.GetComponentInParent<BuildPointerObject>() != null) continue;
                blocked = true;
                break;
            }
            if (!blocked) return true;
        }
        return false;
    }

    /// <summary>What the add-on rules say about the pointer's spot, or null when they have nothing to say.</summary>
    private static string AddOnRoomText()
    {
        try
        {
            var owner = ReservedBy(ReservedBands());
            if (owner != null) return Loc.Fmt("build.reserved", ObjectNames.Of(owner.Data.id));
            if (TakesAddOns(CurrentBuild(_controller)))
            {
                var slotBlockers = PlacingSlotBlockers();
                if (slotBlockers != null)
                    return slotBlockers.Count == 0
                        ? Loc.Get("build.addon_slot_free")
                        : Loc.Fmt("build.addon_slot_blocked", string.Join(", ", slotBlockers));
                return Loc.Get(HasRoomForAddOn() ? "build.addon_room" : "build.addon_no_room");
            }
        }
        catch (Exception ex)
        {
            Log?.LogWarning($"[Build] Add-on room check failed: {ex.Message}");
        }
        return null;
    }

    private static Vector2 Flat(Vector3 v) => new(v.x, v.z);

    private static void Build()
    {
        if (!CanBuildHere())
        {
            ScreenReader.Say(CannotPayText() ?? Loc.Get("build.blocked_here"));
            return;
        }

        var pointer = Pointer(_controller);
        var name = BuildingName(CurrentBuild(_controller));
        FactoryReader.BeginBuild();
        if (!pointer.TryBuildActionInput())
        {
            ScreenReader.Say(Loc.Get("build.failed"));
            return;
        }

        Log?.LogInfo($"[Build] Built '{CurrentBuild(_controller)?.WgoId}' at {CurPos(_controller)}.");
        _spots = null;
        AfterChange(modulesWidget: true);
        var links = FactoryReader.BuiltLinks();
        ScreenReader.Say(links == null ? Loc.Fmt("build.built", name) : $"{Loc.Fmt("build.built", name)}. {links}");
    }

    private static void SayIfNoRotation()
    {
        var pointer = Pointer(_controller);
        if (pointer?.PointerObject == null || !pointer.PointerObject.HasRotation())
            ScreenReader.Say(Loc.Get("build.no_rotation"));
    }

    /// <summary>
    /// The game turned the building (its Rotate key, R). Its only caller is the game's own
    /// placement input, so this is always a keypress. Free spots found before no longer hold -
    /// a turned building has a different footprint.
    /// </summary>
    [HarmonyPatch(typeof(BuildPointer), nameof(BuildPointer.Rotate))]
    [HarmonyPostfix]
    private static void BuildPointer_Rotate()
    {
        _spots = null;

        // A factory piece's connectors only stand in their new places once the turned piece is
        // laid out, so which way it runs is said a couple of frames later.
        if (FactoryReader.IsConveyor(PointerWgo()))
        {
            _rotationSayFrame = Time.frameCount + 2;
            return;
        }
        ScreenReader.Say(Loc.Get("build.rotated"));
    }

    private static int _rotationSayFrame;

    private static void SayPendingRotation()
    {
        if (_rotationSayFrame == 0 || Time.frameCount < _rotationSayFrame) return;
        _rotationSayFrame = 0;
        var placement = FactoryReader.Placement(PointerWgo());
        ScreenReader.Say(placement == null ? Loc.Get("build.rotated") : Loc.Fmt("factory.rotated", placement));
    }

    /// <summary>The building on the pointer - a temporary copy standing where it would go.</summary>
    private static Wgo PointerWgo() =>
        _controller != null && Pointer(_controller)?.PointerObject is WgoBuildPointer pointer ? PointerTarget(pointer) : null;

    private static bool IsConveyorBuild(BuildData data)
    {
        try
        {
            return data?.WgoId != null && GameBalance.Me.conveyorWgosCache.ContainsKey(data.WgoId);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>What the game does a physics step after a build or a rotation - see <c>UpdateBuildModeInput</c>.</summary>
    private static void AfterChange(bool modulesWidget)
    {
        var controller = _controller;
        var pointer = Pointer(controller);
        Action refresh = () =>
        {
            controller.BuildLayout.UpdateBuildingMode();
            pointer.UpdateAvailability();
            if (modulesWidget) pointer.UpdateModulesLimitsWidget();
            UpdateSoftHints?.Invoke(controller, null);
            UpdateDockHints?.Invoke(controller, new object[] { true });
        };

        if (LockAndDelay?.Invoke(controller, new object[] { refresh }) is IEnumerator routine)
            controller.StartCoroutine(routine);
        else
            refresh();
    }

    private static string BuildingName(BuildData data)
    {
        if (data == null) return "";
        if (data.BuildingMode == BuildingDef.BuildingMode.Remove) return TmpText.Clean(LLBase.L("remove"));
        return TmpText.Clean(LLBase.L(data.Definition?.id ?? data.WgoId));
    }
}
