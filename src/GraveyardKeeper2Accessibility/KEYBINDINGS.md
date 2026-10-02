# Graveyard Keeper II Accessibility — keys

Version 0.1.0. This file ships beside the mod so the keys can be found without a web search.

## The mod's own keys

| Key | What it does |
|---|---|
| **Arrow keys** | Move between controls in a menu. Hold one down to repeat. |
| **Enter** | Activate the focused control, or answer "yes" in a confirmation box. |
| **Page down** / **Page up** | Next / previous object in the current category. |
| **Ctrl+Page down** / **Ctrl+Page up** | Next / previous category of objects. |
| **End** | **Jumps straight to "next story steps"** — what the story is waiting for you to use — and says the nearest. If nothing is waiting, says your current task. |
| **Home** | Says the selected object again — its state, distance and direction — then how long the way there really is and which areas it passes through. |
| **Ctrl+Home** | **Walks you to the selected object.** Press again to stop. |
| **J** | Reads the quests you currently have, and for each one in progress what the game is actually waiting for: "Jetzt: geh zu …", "benutze …", "zerstöre …" — plus anything still missing first, such as armour not equipped. |
| **F4** | Fight status: whether a fight is running, your health, the health of what you are defending, and how many enemies are near with the closest one's direction. In a squad fight also: how many enemies are left in total, whether the base is safe or under attack, which lines are breached, how many of each squad are still alive, and which flag you are carrying. During a fight, your health and the defended object's health are also announced at 75, 50 and 25 percent. |
| **Shift+F4** | Fight readiness, anywhere: your total defence power, the barricades and towers at the base, the mercenaries, and each zombie squad with how many zombies it has and how many are ready to fight. Also what the last fight you looked at asked for. |
| **Ctrl+Enter** | In the pre-fight window (after choosing a fight at Herbert), and in the fight builder's window on the battlefield: **starts the fight.** The game only offers this on a controller. |
| **Ctrl+Backspace** | In the fight builder's window on the battlefield: ends the preparation and leaves without fighting. |
| **F3** | Says where the current objective is — whatever the game's tutorial arrow points at; if there is no arrow, the nearest object the story is waiting for you to use; failing both, your current task. |
| **F5** | **Walks you to that objective.** Press again to stop. Autowalk does nothing while a story scene is moving you: it says to wait. |
| **Ctrl+F5** | **Emergency: take your controls back.** First press says whether the game is holding them, and why (story scene, cutscene, …). A second press within 5 seconds forces them back. Only for when a scene has clearly ended and nothing but the mod's keys works. |
| **F8** | Says the currently focused menu control again. |

## Fights

Fights in GK2 are won by your squads, not by your sword: you win when every enemy zombie is dead,
and you lose only if they take your base. What you do is prepare, and move flags.

- **Squads** live in the town guard barracks, listed under **squads** with how many places are
  filled and how strong they are. Carry a zombie there and press E on a squad to put it in. A
  zombie only fights, and only adds strength, when it holds a **pike or bow and wears armour**.
  The mercenaries are one more squad, paid for once.
- **Defence power** (the "Kaserne" in quest texts) is the strength of your barricades and towers
  plus the squads you choose for a fight. Shift+F4 adds it up.
- **The pre-fight window** opens when you pick a fight. It says the fight, how many squads you may
  take, and your defence against what the fight needs. Arrow through the squads; Enter chooses or
  unchooses one and says the new total; Ctrl+Enter starts.
- **On the battlefield** you first prepare. The fight builder puts up barricades and towers (the
  usual building keys); Ctrl+Enter in its window starts the waves.
- **Flags give the orders.** Each squad follows its flag. Pick one up with E from a flag stand and
  plant it with E on another flag stand or on a barricade; the squad walks there. The mod says when
  you pick up and plant a flag, and where. Flags, flag stands, barricades and **capture points** have
  their own categories at the top of the object list, each with its state: which squad, how many
  alive, who holds a point and how firmly.
- **Said on its own during a fight:** a capture point changing hands, a line breached, the base
  under attack (at most every ten seconds), and the enemy count at 20, 10, 5, 3, 2 and 1.
- **Your own swings aim themselves**: each sword or spear swing turns toward the nearest enemy within
  4 metres. Space attacks. `AutoAim` in the config file switches it off.
- The win, lose and death windows are read out; Enter closes them.

## Reading your situation

Mostly the same letters as the Graveyard Keeper mod. Money, tech points and the time moved to K, L
and Z, because R, P and Q are the game's own keys here (rotate, inspirations, quest menu).

| Key | What it does |
|---|---|
| **H** | Energy (and your current maximum), insanity, and any active buffs with the time they have left. |
| **K** | Money, in gold, silver and bronze. |
| **L** | Red, green and blue tech points, and town happiness. |
| **Z** | Day of the week, day number and time of day. |
| **G** | The zone you are standing in and its rating — the graveyard's quality, for example. |
| **Y** | What is in the four hotbar slots, and how many of each you are carrying. |
| **O** | In a window: full details of the focused item — name, stars, how many you have, and its tooltip or description. Anywhere else a tooltip is showing, reads it again in full. |

The game shows the time only as a sun and moon moving round a wheel, with no clock. The time the
mod gives is where the wheel is, read as a 24-hour clock: midnight at the bottom, noon at the top.

## Said automatically

- **Gains and losses** of energy, insanity, tech points, money and happiness — "Got 2 red points",
  "Lost 3 energy", "Spent 5 silver". Changes from one action are added up and said once.
- **"Not enough energy"** when a tool swing or a craft fails because you are out of it.
- **Star quality** on items, wherever an item is named — picked up, in your inventory, on the
  ground. "Carrot, 2 stars".

The gains and losses can be switched off with `ResourceChanges` in the config file.

**Directions are given as compass directions**: "Grabstein, 12 Meter: 9 Norden, 8 Westen" means
about nine metres north and eight west. North is W, south is S, west is A, east is D. Movement in GK2
is fixed to the screen — W always goes north, whatever way your character faces — so these
directions stay true while you walk. Press Home again as you go to hear what is left. The bigger
distance is said first.

**The list only offers what you can use and walk to now.** Things the story has not switched on
yet, and places you cannot reach yet (the far side of a cliff or a closed gate, the inside of a
building you are not in), are left out, so categories with nothing usable disappear. Next story
steps are the exception: they are always listed, reachable or not.

**The list also says what state a thing is in.** A furnace or workbench says what it is making and
how far along it is ("Ofen, stellt Bronzebarren her, 40 Prozent, danach noch 15"), or that its output
is ready to collect; a garden bed says how far its crop has grown. Stones, trees and ore you cannot
work yet say why: "braucht Spitzhacke" when the tool is not on your belt, "braucht Meisterschaft 4 in
Schmieden, du hast 2" when your skill is too low. Nothing is added when you can simply work it.

**The metres in the list are a straight line; the way may be much longer.** When it is — say a
cliff lies between you and the target and the only way down is through the village — the list adds
"Weg 140 Meter, großer Umweg". Home always says the length of the way and the areas it passes
through. If the way crosses a spot where the story may take over (the forest guards, say), you hear
"Achtung, Story-Bereich auf dem Weg" before you set off, and again when walking starts.

These are the same finding-and-reaching keys as the Graveyard Keeper mod, so they should already
be familiar. Objects are grouped by what they are: people, doors and passages, plants, berries and
mushrooms, trees, stone and ore, chests and junk to search, obstacles, broken things to repair, ladders
and stairs, enemies, blueprint desks. Graves are one list whatever their state — marked and still
to dig, dug and empty, with or without a body — each saying which; graves with a body that still
need a headstone or fence are also in "graves to decorate". Everything else keeps the game's own classification — graves, chests, crafting
stations, building spots, fast travel points and so on. Only categories with something in them are
offered. Three of them are worth knowing about:

The categories come in a fixed order, most needed first: next story steps, people, doors and
passages, places and landmarks, blueprint desks, special story objects, special objects, items on the ground, plants,
trees, stone and ore, chests and junk to search, obstacles, graves, graves to decorate, crafting stations, broken things to
repair, chests, other work, ladders and stairs, enemies, story triggers — then everything else
alphabetically. Whenever the story adds a new step, your next
Page down starts from the top again, at next story steps.

- **next story steps** — always first in the list when it is there, and End jumps to it directly. These are the objects the story
  is waiting for you to use right now: the rubble to break, the chest to open. The game marks them
  itself, even when no arrow and no quest text says so. **If you are lost, look here first.**

- **items on the ground** — things lying in the world, like mushrooms you have been sent to gather.
- **story triggers** — invisible spots that make the story continue when you walk into them. A
  sighted player crosses these by accident just by walking about; you may need to go to one
  deliberately. If a door or an exit does nothing, an untriggered story spot is a likely reason.

**Walking (Ctrl+Home and F5) hands the steering to the game itself**, along a route the game works out,
so it will go round fences and buildings rather than into them. It will not take you anywhere the
story has not opened yet: "no route, the way is blocked or not open yet" usually means there is
something to clear first — check **next story steps**. Any movement key — W, A, S, D, an
arrow key, space or Escape — stops it immediately and gives you back control, as does pressing the
same key again.

Everything else the mod says, it says on its own — when a window opens or closes, and when focus
moves to a new control.

Every key above can be changed in `BepInEx\config\svenja001.gk2.accessibility.cfg`, which the game
writes on first launch. The same file has an `ArrowKeysAndEnter` switch to turn the menu keys off
if they ever get in the way.

## Moving around menus

The game can already move focus around a window, but only ever offered that to a controller. The
mod switches it on for the keyboard, and adds the arrow keys and Enter above, because the game
binds neither: its menu navigation sits on the movement keys, and its "activate" action has no
keyboard key at all.

So there are two sets of keys that both work, and neither disturbs the other:

| Key | What it does |
|---|---|
| **Arrow keys** | Move between controls — added by the mod. |
| **W / A / S / D** | The same thing, using the game's own bindings. |
| **Enter** | Activate the focused control — added by the mod. |
| **F6** | In a chest, shop or conveyor chest: jump between your bag, the chest and the conveyor slots. The side is also named whenever you arrow into it. |
| **Space** | On an item in a chest, shop or your bag: move it to the other side (out of the chest into your bag, or back) — added by the mod. The game only offers this on a controller or the right mouse button. |
| **Escape** | Go back / close the window — the game's own. |
| **Mouse** | Still works exactly as before. |

**Trading window:** when it opens you hear the trader, your money, theirs, and the keys. There are
four lists — your bag, what you sell, the trader's goods, what you buy — and each is named when you
arrow into it; **F6** jumps to the next one. Every item says its price. **Space** puts the focused
item into the deal (or takes it back out), and after each change you hear what the deal comes to
("Du bekommst 3 Bronze" / "Du zahlst …") or why it cannot go through. **F** accepts the deal, **F8**
says the whole deal again, **Escape** empties the deal, or closes the window when it is empty.

**How many? window** (moving, selling or buying part of a stack): says the item, the amount and the
most you can take — and the price at a vendor. Left / Right arrow (or A / D) change the amount, Up
(or W) jumps to the most, Down (or S) to the least; each change is spoken. Enter (or E) confirms,
Escape (or Q) cancels.

**Controls screen** (pause menu → Controls): says how many actions there are and the first one.
Up / Down arrow go through them one by one — "Interact: E" — then Restore defaults and OK; Home and
End jump to the first and last. Enter on an action asks for the new key: press it, or Escape to
keep the old one. If another action had that key, it is told to you, because the game takes the
key away from it. F1–F12, Escape and a few system keys cannot be used, as in the game itself. F8
repeats the current line.

**Notes and letters** are read aloud when they open. F8 reads the note again; Enter closes it.

**Tutorial windows:** Enter turns to the next page, and on the last page closes the window. Left and
right arrows page back and forth.

**Autopsy table and grave:** when the window opens, the whole thing is read: the body's white and
red skulls, each organ slot (heart, brain, … — "unknown" for organs you have not learned yet, just
as the game shows a question mark), what is in the pockets; at a grave, its quality, tombstone and
fence. F8 reads it again. Arrowing onto an organ slot names the slot as well as the organ.
To take the body off the table (or exhume it at a grave), press **Up arrow** until you are past the
top row: you land on "Leiche nehmen" (or the reason it cannot be taken right now). **Enter** there
takes the body, Down arrow goes back to where you were. **B** does the same from anywhere in the
window. The game itself offers this button only to the mouse or a controller.

**Embalming table:** read like the autopsy table — body, skulls, organs, pockets. F8 reads it
again, **B** takes the body.

## Crafting stations

Workbenches, furnaces, the alchemy table, the study table, garden beds, fuel stores, and things
with a single recipe such as a broken ladder to repair. Each station is read out when it opens,
and **F8** reads it again at any time.

| Key | What it does |
|---|---|
| **F** | Make it: craft, place fuel, mix, study, plant. The game's own key for crafting; the mod makes it work at the alchemy table, study table and garden bed too. If it cannot be done, F says why. |
| **Space** | In a recipe's setup window: add it to the station's queue instead of starting it now — for zombies, or for later. |
| **F6** | At a workbench or furnace: jump between the recipe list and the queue. |
| **Up / Down** | On the amount (the first cell in a recipe's setup window): more or fewer. On an ingredient that comes in several kinds ("any wood"): switch kind. In the queue: more or fewer of that order; down to zero removes it. |
| **Left / Right** | At the alchemy table, on the result: more or fewer. |
| **Ctrl+Left / Ctrl+Right** | In the queue: move an order earlier or later. |
| **Enter** | On a recipe: open its setup window. On an empty slot (alchemy, study table, garden bed): pick what goes in. |
| **Space** | On a filled slot (alchemy, study table, garden bed): take it back out. |

**What you hear.** At a workbench: its name, how many recipes (and how many not learned yet), and
the queue. Each recipe says what it makes, and "cannot be made right now" or the mastery it needs;
its tooltip then lists the ingredients as "3 of 5 planks". A recipe's setup window says the amount,
every ingredient with how many you have, energy and insanity cost, how long it takes, your mastery
against what it needs, and either the keys to make it or the reason it cannot be made — "not enough
ingredients", "the right tool is not equipped", and so on. The alchemy table says what is in the
flask, the runes they add up to, what they make, and how many flasks you have.

Space and F6 only do this while a station is open; elsewhere they keep their usual jobs. If the
game turns out to have its own keyboard key for one of these actions, the mod leaves that key to the
game and the station tells you which key it is.

## Other stations

Each is read out when it opens; **F8** reads it again.

- **Resurrection table:** the zombie's name, skulls, organs, the liquids it needs ("1 of 2 …"),
  the collar, and whether it can be raised — or why not. **Enter** on the collar slot picks a
  collar, **N** rolls a new name, **F** raises it, **B** takes the body.
- **Prayer stand:** church quality, happiness, how many parishioners will come, the sermon and its
  success chance. **Enter** on the sermon slot picks one, **F** starts it. The **sermon report**
  afterwards is read out (faith, money, bonus, blessing); **Enter** closes it.
- **Porter station:** what it carries. **Enter** on an item switches it on or off, and says which.
- **A zombie's window:** name, skulls, mastery in each talent, points, perks, and the page you are
  on. **Ctrl+Left / Ctrl+Right** switch between its character page and perks page; **Space** takes an
  item out of its organs, pockets or equipment.
- **Recipe book (Foliant):** says which page is open and how many entries it has. **Left / Right**
  (or **Ctrl+Left / Ctrl+Right**) turn the page: simple, medium, hard, epic and heroic recipes, then
  the ingredients that give red, green and blue runes. Each recipe says the runes it needs
  ("needs 1 red, 1 green") and what the potion does; each ingredient says the runes it gives.
- **Town building desk:** how many buildings; each says its name, description, and every material
  as "3 of 5". **Enter** builds.
- **Fishing:** the bait window says the bait, how many you have, and which fish can bite on it
  ("an unknown fish" until you have caught one). **Left / Right** change the bait, **F** casts.
  Then, while reeling, the mod speaks the parts the game only shows:

  | You hear | Do |
  |---|---|
  | "Cast. Wait, do not pull yet" | Nothing — pulling now scares the fish. |
  | "Bite! Press E" | Press E, quickly. |
  | "Hooked…" / "Reel" | Hold E. |
  | "Fights! Let go" / "Line tight, let go" | Let go of E until you hear "Reel". |
  | "25 / 50 / 75 percent" | How close the catch is. "Getting away" means it is slipping. |

  **F8** during the fight says the catch and line tension in percent. Escape stops fishing. The
  cues can be switched off with `FishingCues` in the config file.

## The character window: tech tree and inspirations

The window the game opens with Tab has several pages: character, tech tree, inspirations, quests,
map. The page is named when it comes up; **F8** says the page summary again.

| Key | What it does |
|---|---|
| **Ctrl+Up / Ctrl+Down** | Previous / next page. |
| **Ctrl+Left / Ctrl+Right** | On the tech tree: previous / next branch (building, metallurgy, …). On the inspiration page: previous / next talent. |
| **Arrow keys** | Move around the tree. The tree is laid out left to right, so Right usually goes to what comes next. |
| **Enter** | On a technology: open it. In the opened technology: unlock it. On an inspiration or a perk: buy it. |
| **C / Shift+C** | On the inspiration page: jump to the next / previous thing you can buy right now — a finished inspiration you have the faith for, or a perk you have the talent points for. Says so when there is nothing. |

**Tech tree.** Each branch starts with how many technologies it has, how many you have learned, how
many you could learn right now, and your red, green and blue points. Each technology says whether
it is learned, can be learned, or what it needs first; its cost as "10 of 25 red points" (what you
have of what it costs); and what it unlocks. Some nodes are reputation gates ("reputation with the
foreman: 3 of 10"): they open by themselves when that reputation is reached. Opening a technology
reads it in full and says whether Enter will unlock it — or why not. Escape closes it.

**Inspirations.** Each talent (building, smithing, farming, anatomy, book writing) starts with its
level, experience, talent points and mastery, your faith, and how many inspirations are finished
and waiting to be bought. An inspiration is a task — "Gather berries, progress 12 of 20". When it is
finished, Enter buys it with faith (a question comes up first; Enter answers yes) and the talent
gets experience; a full experience bar gives a talent point. Talent points buy the perks in the
talent's own tree, further down the page: each perk says whether it is learned, what it costs
against the points you have, what it needs first, and what it does.

**Craft confirmations** (extracting an organ, any craft): **F** starts it — the game's own key.

**Building (for example the graveyard builder, to lay out a grave):** each entry in the list says
its cost against what you carry ("2 of 5 planks") and whether it can be built. Ctrl+Left / Ctrl+Right
switch tabs. After choosing an entry you place it:

| Key | While placing |
|---|---|
| **Arrow keys** | Move one square north, south, west or east, then say whether it is free and where it is from you. |
| **Space** or **End** | Jump to the nearest free spot; press again for the next one. Buildings that need a marked plot (a grave on a grave plot) are tried on every such plot. |
| **Home** | Say again whether the spot is free, and where it is. |

**Remove mode ("Entfernen" in the building menu):** Space, End or Page down goes to the next building
that can be removed here, nearest first (Shift+Space or Page up goes back); Home says the selected
one; Enter removes it. Most buildings are not removed at once but *marked for demolition*: leave
remove mode, walk to it (the object list says "zum Abriss markiert") and knock it down with F. Enter
on a marked building takes the mark back off.
| **Enter** | Build here. |
| **R** | Rotate — the game's own key; the mod says "rotated". |
| **Escape** | Stop placing — the game's own key. |

**Cutscenes and dialogue move on with E** — the game's own key, the same one you use to interact.
The mod adds nothing here.

The mod does not change any of the game's key bindings, so nothing here can affect your saved
controls settings.

**One visible side effect:** on-screen button prompts switch to controller glyphs, because the mod
tells the game a controller is present in order to unlock the focus navigation above. Nothing about
the keyboard changes — the keys in the table are still the keyboard's. If you share the screen with
someone sighted, this is the thing they will notice.

## The game's own key worth knowing

| Key | What it does |
|---|---|
| **Shift+F10** | Writes the game's full English text table to `…\AppData\LocalLow\Lazy Bear Games\Graveyard Keeper 2\Mods\Languages\~Example\strings.csv`. |

Not part of this mod — it is the game's built-in language-mod scaffolding. It writes the file only
once; if the `~Example` folder already exists, pressing it again only reloads language packs. Useful for translating,
and useful to the mod's development.
