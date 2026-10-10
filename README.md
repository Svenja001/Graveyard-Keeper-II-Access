# Graveyard Keeper II Accessibility

A BepInEx mod that makes **Graveyard Keeper 2** playable for blind players. Menus, dialogue,
cutscene text, the world around you, crafting, building, trading, the graveyard and the morgue are
narrated through a screen reader, and the mod can walk you to whatever you pick from its object
list.

This is an early release. The way from a new game to the first save has been played through
without help, and a good deal beyond it — but not everything yet. See *Known limitations*
before you start.

## Requirements

- **Graveyard Keeper 2** on Windows. The game is 64-bit only, so there is one download for
  everyone. It has been tested with the Steam version.
- **Nothing else.** BepInEx, the loader that makes mods run at all, is included in the download —
  see below if you already have it.
- **A screen reader is optional.** NVDA, JAWS and others are driven directly, braille included; if
  none is running, the mod falls back to the Windows SAPI voice and still speaks.

Everything the mod needs travels inside its ZIP — the Prism speech library is bundled, so there is
nothing separate to install.

## Install

There is no installer, and the mod does not look for your game folder — you extract one ZIP into
it yourself.

### Which download to take

- **`GraveyardKeeper2Accessibility_<version>_WithBepInEx.zip`** — take this one unless you know
  you need the other. It contains the mod *and* BepInEx, so there is nothing else to fetch.
- **`GraveyardKeeper2Accessibility_<version>_ModOnly.zip`** — the mod on its own, without BepInEx.
  **If you already run other Graveyard Keeper 2 mods, use this one**: the bundled ZIP would
  overwrite your loader with version 5.4.23.5 and could disturb mods that expect a different one.

### Finding your game folder

The folder you want is the one that contains `GraveyardKeeper2.exe` and a folder called
`GraveyardKeeper2_Data`. On Steam that is usually

```
C:\Program Files (x86)\Steam\steamapps\common\Graveyard Keeper 2
```

Steam libraries can live on any drive. In the Steam client: right-click the game → *Manage* →
*Browse local files*.

### Installing (the bundled ZIP)

Extract `..._WithBepInEx.zip` **into the game folder itself**, the one holding
`GraveyardKeeper2.exe`. If Windows asks whether to merge folders, say yes. When it is done,
`winhttp.dll` sits next to `GraveyardKeeper2.exe`, and the mod is in
`BepInEx\plugins\GraveyardKeeper2Accessibility\`.

Four documents land next to `GraveyardKeeper2.exe` as well —
`Accessibility-Mod-README.md` (this file), `Accessibility-Mod-KEYBINDINGS.md`,
`Accessibility-Mod-CHANGELOG.md` and `Accessibility-Mod-LICENSE.txt`. They are there so the keys
and the instructions are in the folder you are already standing in, not buried inside the mod.

### Installing (the mod-only ZIP)

If you already have BepInEx 5, extract `..._ModOnly.zip` into the **`BepInEx` folder inside** your
game folder instead:

```
C:\Program Files (x86)\Steam\steamapps\common\Graveyard Keeper 2\BepInEx\
```

It contains a `plugins` folder, so the mod lands in
`BepInEx\plugins\GraveyardKeeper2Accessibility\`, and the four `Accessibility-Mod-*` documents land
directly in `BepInEx\` where you can see them.

Either way, do not move the individual files around afterwards — the speech library and the `lang`
folder have to stay beside the DLL.

### Start the game

The mod says "Accessibility mod <version> ready" once the game has finished loading. From there the main
menu, the save slots and everything after them are read aloud.

## Keys

`Accessibility-Mod-KEYBINDINGS.md` lists every key the mod adds, grouped by where it works, and
explains fights, building and the other larger systems in more detail. It sits in the folder you
extracted the ZIP into, right beside the other three documents. (A second copy also travels inside
`BepInEx\plugins\GraveyardKeeper2Accessibility\`.)

In short: the arrow keys and Enter work in every menu, Page up / Page down go through the objects
around you, Ctrl+Home walks you to the selected one, End jumps to what the story is waiting for,
and F5 walks you to the current objective.

The mod's own keys can be changed in `BepInEx\config\svenja001.gk2.accessibility.cfg`, which is
written on the first launch with the mod. The game's own keys can be changed in the game's
settings under Controls, which the mod reads out. The mod never touches the game's key bindings,
so it cannot affect your saved controls.

**One visible side effect:** on-screen button prompts show controller buttons, because the mod
tells the game a controller is present in order to unlock keyboard navigation of the menus.
Nothing about the keyboard changes. If you share the screen with someone sighted, this is the
thing they will notice.

## Languages

English and German are complete. If your game runs in another language, the mod's own sentences
fall back to English; everything the game itself says (item names, dialogue, quests) is read in
the game's language.

## Save compatibility

The mod writes nothing of its own to your save, and it is safe to add or remove at any point in a
playthrough. The one thing it does change is story progress when the game itself has missed a
step: if you stand where the story is waiting for you but the game did not notice, the mod fires
the game's own event for it, exactly as walking in would have.

## Known limitations

- **Only the first squad fight has been played.** "Break Through to the Port" was won with the mod
  alone; later fights, with more squads and other battlefields, are not tested yet. Not done at
  all: aiming the bow.
- **The cellar factory is built from the game's code but not yet tried in play.** G, Shift+G and
  Ctrl+G read it, and placing belts says what they join, but expect gaps. How zombies are put on
  the carousel has not been looked at separately.
- **Auto-walk can still fail.** Sometimes it says there is no route where there is one —
  across some bridges and doors, or in rooms the game has no walking map for (the mod builds one
  itself for those, which is new and untested). Walking there by hand usually works, and auto-walk
  can do the rest from there. Auto-walk refuses to start while a story scene is moving you.
- **If a scene ends and only the mod's keys still work,** press Ctrl+F5 twice to take your controls
  back. That should not happen any more, but it is there in case it does.
- **Not covered yet:** the map page in the character window (only the travel stone's map is read).
- **A game bug, not the mod:** in a German game, the first two main menu entries are read in
  Russian. That is what the game itself has in that place.
- Only Windows and the Steam version have been tried.

## Something went wrong?

`BepInEx\LogOutput.log` inside the game folder records what the mod did. It is a plain text file,
and the lines containing `Graveyard Keeper II Accessibility` are this mod's. That log is the first
thing worth looking at, and the most useful thing to attach to a bug report.

Bug reports, questions and suggestions are welcome through any of these:

- **GitHub** — [open an issue](https://github.com/Svenja001/Graveyard-Keeper-II-Access/issues)
- **Mastodon** — [@svenja@mstdn.games](https://mstdn.games/@svenja)
- **Discord** — `@svenjadev`
- **E-mail** — [stream@svenja-blog.de](mailto:stream@svenja-blog.de)

## Licence & credits

Licensed under the **GNU General Public License v3.0** — see [LICENSE](LICENSE) (shipped with the
mod as `Accessibility-Mod-LICENSE.txt`) for the full text.
In short: you are free to use, study, modify and redistribute the source under the same licence;
any distributed fork must also be GPL v3.

Speech goes through [**Prism**](https://github.com/ethindp/prism), under the **Mozilla Public
License 2.0**, redistributed unmodified. Its licence and notice ship as `prism-LICENSE.txt` and
`prism-NOTICE.txt` beside the mod.

The bundled ZIP also contains [BepInEx](https://github.com/BepInEx/BepInEx) and its
[Doorstop](https://github.com/NeighTools/UnityDoorstop) loader, both under the **GNU Lesser General
Public License 2.1**, redistributed unmodified. Their licence texts ship as `BepInEx-LICENSE.txt`
and `Doorstop-LICENSE.txt` in the game folder. Both come from release
[v5.4.23.5](https://github.com/BepInEx/BepInEx/releases/tag/v5.4.23.5), asset
`BepInEx_win_x64_5.4.23.5.zip`; see [libs/bepinex-dist/README.md](libs/bepinex-dist/README.md).
