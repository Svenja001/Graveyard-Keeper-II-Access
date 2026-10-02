## 0.1.0

**The first release. Graveyard Keeper 2 can be played from a new game through the intro to the
first save, and well beyond it, with a screen reader and the keyboard alone.** Fights and factories
are built in but not yet tested — see *Known limitations* in the README.

### Speech and menus

- **Every menu speaks and works from the keyboard.** The game's own focus navigation, which it only
  offered to controllers, is switched on for the keyboard. Arrow keys move, Enter activates, and
  holding an arrow repeats. W, A, S, D and the mouse keep working as before.
- **Sliders, switches and tabs say their new value** when you change them, and settings read the
  name before the value, so you know what is being set before you hear what it is set to.
- **Confirmation boxes are read with their answers**, and can be answered with Enter or Escape.
- **Windows without anything to focus** — notes and letters, tutorials, research results, the
  win and lose screens — are read out when they open. F8 reads any of them again.
- **Tooltips are read**, costs as "3 stone, you have 36".
- **The controls screen** reads every action with its key, and keys can be changed from it.
- **Speech goes through your screen reader** (NVDA, JAWS and others, braille included) via Prism,
  or through the Windows SAPI voice if none is running.
- English and German.

### Story and dialogue

- **Dialogue is read line by line**, with the speaker named when it changes. Answers are read as a
  list before you choose, and an answer you cannot pick says why and what it would cost.
- **Cutscene text and story cards are read.**
- **J says what each quest is actually waiting for** — "go to …", "use …", "destroy …" — and what is
  still missing first, such as armour not equipped.
- **Story steps the game missed are repaired.** When you stand where the story waits for you but
  the game did not notice (a trigger crossed in the wrong order, or crossed while walking), the mod
  fires the game's own event for it, so the intro cannot be soft-locked that way.
- **Level-ups, finished inspirations, new perks and achievements are announced.**

### Finding your way

- **Object list:** Page up / Page down go through what is around you, Ctrl+Page up / down through
  the categories — people, doors, plants, trees, stone and ore, graves, crafting stations, and so on.
  The list leaves out what you cannot use or reach yet.
- **"Next story steps"** comes first in the list, and End jumps straight to it: whatever the story
  is waiting for you to use, even when no quest text says so.
- **Story triggers** — the invisible spots that move the story on when walked into — are listed too.
- **Every object says its state**: what a furnace is making and how far along, how far a crop has
  grown, what tool or mastery you still need to work a stone.
- **Directions as compass directions** in movement keys — "12 metres: 9 north, 8 west", north
  being W — so they stay true while you walk.
- **Home says how long the way really is**, which areas it passes through, and warns before a route
  crosses a spot where a story scene may take over.
- **Auto-walk:** Ctrl+Home walks you to the selected object, F5 to the current objective (F3 says
  where it is). The game walks the player itself along its own route, round fences and buildings,
  across bridges and through doors. It stops at the spot where E actually picks the thing you walked
  to, and tells you if something else is in front of you instead. It will not start while a story
  scene is moving you.
- **Rooms the game gives no walking map, such as the guard barracks,** work too: the mod maps the
  room's floor itself when you walk in, so the object list, routes and auto-walk work inside.
- **Ctrl+F5 takes your controls back** if a scene ever leaves you stuck.
- **What you walk up to is named**, with what E and F do there, and why an action is not available.

### Your situation

- **H** energy, insanity and buffs, **K** money, **L** tech points and town happiness, **Z** day and
  time, **G** the zone you are in and its rating, **Y** the hotbar, **O** full item details.
- **Putting items on the hotbar:** in the inventory, press **1 to 4** on an item to put it on that
  hotbar slot. The game's own slot window is read too: arrows choose a slot, Enter puts it there.
  Pinned items say which slot they are on.
- **The item menu** (use, put on the hotbar, destroy) opens with **Shift+F10** or the Applications
  key on an inventory item, without using the item. Destroying an item says what was destroyed.
- **Planting with a seed in hand speaks:** "Pflanzen" on a seed (or its hotbar key) says what you are
  holding; E on each empty bed then plants it without the bed window and says how many are left, or
  why the bed refuses.
- **Gains and losses** of energy, money, tech points and happiness are said once per action.
- **Pickups, a full bag, equipping and unequipping** are announced. Items say their quality as
  bronze, silver or gold.

### Working and building

- **Chests, the shop and conveyor chests:** both sides are named, F6 jumps between them and Space
  moves an item across. The "how many?" window says the amount, the maximum and the price.
- **Trading:** prices, all four lists, what the deal comes to and why it cannot go through, and
  happiness from selling what the trader likes. F accepts the deal.
- **Crafting stations** — workbenches, furnaces, the alchemy table, the study table, garden beds,
  fuel stores — are read in full: recipes, ingredients against what you hold, energy, time, mastery,
  the queue. F makes it, Space queues it, the queue can be reordered.
- **The recipe book (Foliant)** can be paged and is read.
- **Holding F where nothing happens** says why: every work spot is blocked by another building, a
  tool is missing, or a worker is busy.
- **Building:** costs, then placing with the arrow keys, with Space or End jumping to the next free
  spot. It says what is in the way, keeps room for a workbench's add-ons, and tells you whether an
  add-on is connected to its workbench.
- **Remove mode** works: pick a building, Enter marks it for demolition.
- **Tech tree and inspirations** are read and can be bought from the keyboard.

### The graveyard and the morgue

- **Autopsy table, embalming table and grave** are read in full: skulls, organs, pockets, grave
  quality. B takes the body, or reach it as a button with Up arrow.
- **Autopsy results are said**: what came out, and the body's new skull count.
- **Resurrection table, prayer stand and sermon report, porter station and a zombie's window**
  are read and usable.
- **Fishing:** the bait window, and spoken cues for every stage of reeling in.

### Fights (untested)

- The intro fight works: the mod says when it starts and ends.
- Built, not yet played: squads in the barracks, the pre-fight window (Ctrl+Enter starts), preparing
  the battlefield, flags and capture points in the object list, fight status on F4 and readiness on
  Shift+F4, announcements during a fight, and swings that aim at the nearest enemy.
