## 0.2.1

Updated for the game's patch 1.011.

* **Remove mode works again.** The patch changed how the game selects what to remove, which broke
the mod's remove mode.
* **Buildings can be moved.** The new Move entry in the build menu works like remove mode: Space or
End goes to the next building that can be moved, Shift back, Home says the selection, and Enter
picks it up. Then it is placed like a new building, with the arrow keys or Space and End for the
next free spot, R to rotate and Enter to set it down. Add-ons move along with it, and Escape puts
it back where it was. After setting one down you choose the next building to move. In German the
entry is called "Verschieben" instead of the game's untranslated "Move".
* **The total of the whole crafting queue is read out**, as the game now shows it: "whole queue
makes 30 iron bars" when more than one queued craft makes the same item. You hear it in a
station's status in the object list, in the station window's summary and on each queue entry.
* **Page up and Page down change the amount by 10** in the "how many?" window, like the
controller's bumpers since the patch.
* **Item pickers only stop on what you can choose.** Picking a seed, fertilizer, alchemy ingredient,
organ, grave decoration, offering or zombie equipment used to go through everything you carry and
every empty slot. Now the arrows go from one fitting item to the next, the picker opens on the first
one, and says "Nothing here fits" when there is none.
* **A full bag is announced.** Putting something into a full bag said nothing; now you hear that the
bag is full, that the item cannot go into this kind of bag, or that a chest has no room. Space no
longer says "moved" when nothing moved.
* **One error no longer switches off all keys.** A single failure while a key was handled (opening a
bag with another mod installed) left the arrows dead until the game was restarted.

## 0.2.0

* **Bugs can be reported to the developers.** The "Report a bug" window in the menu could be heard
but not used: its categories and Send button only took mouse clicks. Now the arrow keys go through
the title, the description, the categories, Send and Cancel, and Enter types into a field or
presses a button. Escape while typing leaves the field and reads back what you wrote, instead of
closing the window and losing the report.
* **A zombie's perks are read out.** Each perk says its name, whether the zombie has learned it,
what it costs against the zombie's points, what it needs first, and what it does; the page says
which perks the zombie has and how many it can learn now. **C / Shift+C** jump to the next perk it
can learn, **Ctrl+Left / Ctrl+Right** switch talent, **Ctrl+Up / Ctrl+Down** switch page, and
learning a perk with **Enter** is announced. Talents in the zombie's summary are named instead of
"talent\_orange".
* **The zombie choir** is named, says how many zombies are singing and how many places are free,
and says when a zombie goes in or comes out (**F** takes one out).
* **Zombies in the object list say what they have equipped:** their name, tool with its mastery
bonus, weapon, armour and collar, empty slots included.
* **Autopsy, embalming and resurrection tables in the object list say what is on them:** "empty",
the body with its skulls, or the zombie by name with its skulls. Pallets (the morgue's corpse
pallets and the warehouse's crate pallets) say the same, or what crates are on them.
* **Town building places in the object list name every building that can go there** and say
whether you can build it now or which materials are missing.
* **Factory chests in the object list say what is in them**, most first, or "empty", so the
outputs can be collected without walking to every chest.
* **A factory chest's side slots say their side and what they do:** "east: out, sends clay", "out,
nothing chosen yet", "in" or "no belt". Enter on an out side says which side you are choosing for,
and choosing an item in the chest says what that side now sends.
* **G in the factory says where each workbench stands**, in movement-key steps like the conveyor
lines, so two benches of the same kind can be told apart.
* **Factory workbenches in the object list say who works them:** "zombie" and its name, or "no
zombie at it", so cycling through the benches finds the ones still waiting for a zombie.
* **"Mastery too low" says what is needed and what the worker has:** "needs Building 6, zombie has
0" for a zombie, "you have" for you.
* **Conveyor belts with something lying on them are in the object list** under conveyors ("belt
with Wood Kit"), so you can walk there and take it off with **E**. Empty belts stay out.
* **A workbench stuck with a finished item says why and where to free it:** what is waiting to go
out, and where the belt at its output is and what lies on it. A started order cannot be cancelled
in the game; emptying that belt lets the bench finish and take new orders.
* **Shift+G lines say what is lying on them**, and "full" when every belt piece of the line holds
something, which is how a backed-up line shows.
* **Factory workbenches in the object list say how they are linked:** where their belts come from
and lead to, followed to the chest or bench at the other end ("takes from chest at 4 west, which
sends clay, sends to forge at 3 east"), or "a dead end" and "a belt nothing feeds". A chest that
feeds a bench says which item it sends down that belt, so you know what to put in it.
* **Remove mode in the factory says what each piece is joined to:** a belt says which way it runs,
where its items come from and where they go, followed to the chest or bench at each end, whether it
is a dead end and what it points at, and what lies on it. **Ctrl+Space** (Ctrl+Shift+Space back)
jumps straight to the next broken belt piece, so dead ends can be found and torn down.
* **Free spots found with End now say what a belt piece would join there**, like the arrow keys and
Home already did.
* **In the factory, End offers spots that join what already stands first:** every cell next to a
standing piece is tried, and the free ones where the piece would join something come first, the
most links first, so a belt that closes a gap at both ends is the first spot. R turns the piece,
and the next End searches again for the new direction.
* **The object list is fast again in the town guard barracks**, and in other rooms without a
navmesh of their own.

## 0.1.2

* **Chests: "move all identical items to the chest" can be reached.** Up arrow from the top row
lands on it, Enter presses it, and you hear what was moved.
* **Arrowing onto something that reads the same as the last thing is no longer silent.** Unknown
recipes and empty slots used to say nothing after the first, so counting them came out short.
* **Recipes say their position**, "5 of 24", matching the count the station names when it opens.
**C / Shift+C** at a workbench goes to the next / previous learned recipe and round again, through every
group.
* **Game update of 13 October:** the crow nest is listed with the chests, and says that you can
only take things out of it. Order slots locked by town population say how many citizens they
need. Auto-walk waits while an area loads.
* **Achievements are said again**, including the counted ones (fish caught, orders, sermons,
inspirations), which Steam unlocks by itself and the mod used to take for old ones.
* **Finished inspirations, new perks, achievements and learned technologies can no longer be cut
off** by the line said right after them; for a moment, everything else waits its turn.

## 0.1.1

improved performance in the objecttracker

## 0.1.0

**The first release. Graveyard Keeper 2 can be played from a new game through the intro to the
first save, and well beyond it, with a screen reader and the keyboard alone.** That includes the
first real squad fight, "Break Through to the Port", won by a blind player without help. Factories
are built in but not yet tested — see *Known limitations* in the README.

### Speech and menus

* **Every menu speaks and works from the keyboard.** The game's own focus navigation, which it only
offered to controllers, is switched on for the keyboard. Arrow keys move, Enter activates, and
holding an arrow repeats. W, A, S, D and the mouse keep working as before.
* **Sliders, switches and tabs say their new value** when you change them, and settings read the
name before the value, so you know what is being set before you hear what it is set to.
* **Confirmation boxes are read with their answers**, and can be answered with Enter or Escape.
* **Windows without anything to focus** — notes and letters, tutorials, research results, the
win and lose screens — are read out when they open. F8 reads any of them again.
* **Tooltips are read**, costs as "3 stone, you have 36".
* **The controls screen** reads every action with its key, and keys can be changed from it.
* **Speech goes through your screen reader** (NVDA, JAWS and others, braille included) via Prism,
or through the Windows SAPI voice if none is running.
* English and German.

### Story and dialogue

* **Dialogue is read line by line**, with the speaker named when it changes. Answers are read as a
list before you choose, and an answer you cannot pick says why and what it would cost.
* **Cutscene text and story cards are read.** A cutscene that can be skipped says so: hold Escape
for three seconds.
* **J opens the list of quests in progress**, one quest per line: Up and Down go through it, J
closes it. Quests waiting to be begun follow, saying how to begin them ("To start: talk to …").
Finished quests and the game's hidden bookkeeping quests are left out. Each quest says what it is
actually waiting for — "go to …", "use …", "destroy …" — and what is still missing first, such as
armour not equipped.
* **Story steps the game missed are repaired.** When you stand where the story waits for you but
the game did not notice (a trigger crossed in the wrong order, or crossed while walking), the mod
fires the game's own event for it, so the intro cannot be soft-locked that way.
* **Level-ups, finished inspirations, new perks and achievements are announced.** An achievement
earned in a scene is said once the scene is over, so the scene's own lines do not drown it.

### Finding your way

* **Object list:** Page up / Page down go through what is around you, Ctrl+Page up / down through
the categories — people, doors, plants, trees, stone and ore, graves, crafting stations, and so on.
The list leaves out what you cannot use or reach yet.
* **"Next story steps"** comes first in the list, and End jumps straight to it: whatever the story
is waiting for you to use, even when no quest text says so.
* **"Something new"** follows right after it, as in the first game: everyone and everything the
game shows a bubble over — a person who wants to talk, a reward to collect, something to look at
or pick up, a prayer stand waiting, fish biting — each saying which, plus furnaces and other
stations with finished goods waiting to be taken.
* **Beehives** have their own list, wild and built, with the spot to build one, and say whether a
hive is empty or refilling. They stay listed while refilling, when the game will not let you use them.
* **Story triggers** — the invisible spots that move the story on when walked into — are listed too.
* **Every object says its state**: what a furnace is making and how far along, how far a crop has
grown, what tool or mastery you still need to work a stone.
* **Directions as compass directions** in movement keys — "12 metres: 9 north, 8 west", north
being W — so they stay true while you walk.
* **Home says how long the way really is**, which areas it passes through, and warns before a route
crosses a spot where a story scene may take over.
* **Auto-walk:** Ctrl+Home walks you to the selected object, F5 to the current objective (F3 says
where it is). The game walks the player itself along its own route, round fences and buildings,
across bridges and through doors. When you arrive, the thing you walked to is focused, as in the
first game, so E uses it even if it is not quite in front of you. The focus stays until you move
or turn. It will not start while a story scene is moving you.
* **Rooms the game gives no walking map, such as the guard barracks,** work too: the mod maps the
room's floor itself when you walk in, so the object list, routes and auto-walk work inside.
* **Fast travel:** pressing E at a travel stone opens the map, which now says where you are and
lists the stones you have unlocked — only those — each with its distance and direction. Arrows
choose, a letter jumps to the next place starting with it, Enter travels, Escape closes.
* **Ctrl+F5 takes your controls back** if a scene ever leaves you stuck.
* **Ctrl+Shift+F5 gets you out of anywhere**: stuck, wedged in, or fallen out of the world, it takes
you back to the last safe place you stood, or to the home travel stone. Press twice to confirm.
* **What you walk up to is named**, with what E and F do there, and why an action is not available.
* **Carrying things overhead is spoken.** Lifting a log, a body or a supply crate says what you now
carry; putting it down, on the ground or into something, says that too. Y starts with what is on
your head. A supply crate also says where it goes (the delivery pallets in the Town Warehouse),
whether an accepted order wants it, and if none does, that orders are accepted at the order board
on the Day of Pride. While you carry one, the pallets that take it are in the story list.
* **Ladders on the way are handled.** When what you walk to is on another level, auto-walk takes you
to the ladder, tells you to press E and hold Up or Down, and walks on by itself once you are off
it. The warehouse order board, up on its platform, can now be reached.
* **Auto-walk never starts where the map has no floor under you** — on a platform it does not cover,
for example. It says so instead of walking you off the edge. Raised floors inside rooms, such as
the warehouse platform by the side door, now have a map of their own.
* **The order board is read**: every slot and every trader's order says what it wants, how much is
already on the delivery pallets, the reward, whether it is urgent, locked or done, and what Enter
does.

### Your situation

* **H** energy, insanity and buffs, **K** money, **L** tech points and town happiness, **Z** day and
time, **G** the zone you are in and its rating, **Y** the hotbar, **O** full item details.
* **Putting items on the hotbar:** in the inventory, press **1 to 4** on an item to put it on that
hotbar slot. The game's own slot window is read too: arrows choose a slot, Enter puts it there.
Pinned items say which slot they are on.
* **The item menu** (use, put on the hotbar, destroy) opens with **Shift+F10** or the Applications
key on an inventory item, without using the item. Destroying an item says what was destroyed.
* **Planting with a seed in hand speaks:** "plant" on a seed (or its hotbar key) says what you are
holding; E on each empty bed then plants it without the bed window and says how many are left,
whether the bed was fertilized, or why the bed refuses.
* **Fertilizer is read:** garden beds say which fertilizer is on them, in the object list and in the
bed window, and the bed window's fertilizer slots say what is in them instead of "empty".
* **Gains and losses** of energy, money, tech points and happiness are said once per action.
* **Pickups, a full bag, equipping and unequipping** are announced. Items say their quality as
bronze, silver or gold.

### Working and building

* **Chests, the shop and conveyor chests:** both sides are named, F6 jumps between them and Space
moves an item across. The "how many?" window says the amount, the maximum and the price.
* **Trading:** prices, all four lists, what the deal comes to and why it cannot go through, and
happiness from selling what the trader likes. F accepts the deal.
* **Crafting stations** — workbenches, furnaces, the alchemy table, the study table, garden beds,
fuel stores — are read in full: recipes, ingredients against what you hold, energy, time, mastery,
the queue. F makes it, Space queues it, the queue can be reordered.
* **The recipe book (Folio)** can be paged and is read.
* **Holding F where nothing happens** says why: you carry several things overhead, every work spot
is blocked, a tool is missing or worn out, a worker is busy, no job in the queue can start, or
energy, insanity or mastery fall short. If the game gives none of these reasons, the mod says
what state the station is in.
* **Building:** costs, then placing with the arrow keys, with Space or End jumping to the next free
spot. It says what is in the way, keeps room for a workbench's add-ons, and tells you whether an
add-on is connected to its workbench. A new workbench is offered first right against an add-on
left standing alone, so a replaced anvil joins the old one's bucket. When the materials have run out or the building's limit
is reached, it says so and names what is missing, instead of saying there is no room.
* **Remove mode** works: pick a building, Enter marks it for demolition.
* **The cellar factory** (new, not yet tried in play): G says whether the factory runs or stands
still for lack of power, the zombie carousels, and each workbench's zombie, work, missing
materials and unconnected sides. Shift+G and Ctrl+G read the conveyor lines one by one — where
they start, which way the belts run and where they lead, with dead ends called out. Placing a
belt or other factory piece says which way it runs and what it would join before you build it,
and what it joined after.
* **Tech tree and inspirations** are read and can be bought from the keyboard. C jumps to the
next thing you can buy right now, in any talent; on the tech tree, the next technology your points can learn
in any open branch.

### The graveyard and the morgue

* **Autopsy table, embalming table and grave** are read in full: skulls, organs, pockets, grave
quality. B takes the body, or reach it as a button with Up arrow.
* **Autopsy results are said**: what came out, and the body's new skull count.
* **Resurrection table, prayer stand and sermon report, porter station and a zombie's window**
are read and usable.
* **Fishing:** the bait window, and spoken cues for every stage of reeling in.

### Fights

The first squad fight, "Break Through to the Port", has been played and won with the mod alone.

* **The goal is said at the start:** how long the fight lasts, that the base must still be yours
when the time runs out, and which capture points you must take, in the order the game allows.
A point that cannot be taken yet says "locked, take the point before it first".
* **F3 says the next step and F5 walks you there:** fetch the banner, put it on the flag stand at
the next point, stand on the base while your squad takes the point, or get back to the base when
enemies are taking it. The next step is also said by itself when a point changes hands or you
move the banner.
* **Waves are announced** with how many enemies come and where: "Wave: 6 enemies, 30 metres from the
base: 31 east, 4 south, near the target point".
* **"3 enemies on you"** when enemies are within reach, with the nearest one's direction and that
Space attacks. Swings turn towards the nearest enemy.
* **Standing on the base is said** ("You are standing on the base" / "Left the base"), because only
being inside its small circle keeps the enemies from taking it. When enemies are on the base and
none of yours, the warning says so and what to do.
* **Health warnings name your healing potion** and its hot-bar key once you are at half health or
below, and every hot-bar key in a fight says whether it worked.
* **Time left** is said each minute and at 30 seconds; F4 adds it to the fight status, along with the
state of every point you must take.
* **The real reason for a loss** is said in the lose window: the base was taken, time ran out with a
point still the enemy's, or you fell.
* **During a fight the object list shrinks to what matters:** capture points (the base always
first), enemies with their health, your own squads and where they stand, flag stands, banners,
barricades and the planning table.
* **The pre-fight window and the battlefield's planning table** are read, Ctrl+Enter starts the
fight and the waves, and barricades are placed through the planning table like any building.
* **Barricades in the barracks** can all be placed: free spots are found on the tightly packed
barricade area, corner spots first, and the mod says when a barricade has to be turned with R.
* **Also:** squads in the barracks, readiness on Shift+F4, and the intro fight, which says when it
starts and ends.

