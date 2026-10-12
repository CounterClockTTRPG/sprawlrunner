# Sprawlrunner: what next

> **Status (sixth pass).** All of this is built except day and night runs
> (left out by choice), along with towers, the top tier, car work, an intro
> and graded cyberware. See `docs/DESIGN.md`, "Sixth pass".

A second analysis, after the fourth and fifth passes. The career layer now has
far more systems than it had (storylets, turf, clocks, crew, Johnsons, three
campaigns), so the biggest risks have moved: from "nothing happens" to "the
player can't see, understand or steer what happens", from "the run is the
whole game" to "some archetypes have nothing to do on a run", and from "the
career runs dry" to "the money runs away". Ordered by how much each would
improve the game for the effort.

---

## 1. UX: make the systems legible

### 1.1 Bugs visible in the walkthrough
- **The full map collides with everything** (`shots/09_map.png`): the map
  panel covers the status and job panels (the job line is cut off at "13
  day(s"), pin labels overlap ("Th JOB oonlit Charm"), the off-screen job arrow
  and its "JOB 143m" label are drawn on top of the open map, and the radio
  line runs into "M: close map". Fix: hide the edge arrow and radio while the
  map is open, give the map its own margin, push labels apart (or show only
  the hovered/nearest names).
- **The run HUD says the objective twice** (top centre and top left,
  `shots/13_run_fight.png`), and the weather ("Storm") is a faint word in a
  corner, though it changes the rules. One objective line; weather and
  conditions as small icons with tooltips beside it.

### 1.2 The map should be a planning tool
The map is the one screen every player opens, and it shows only places.
- **Click a place to set a route**: the autopilot already plans routes
  (`Sim/Autopilot.cs`); draw that route as a line on the minimap and the
  street, the way GPS games do. Optional "drive me there" for a slow,
  traffic-obeying autopilot (honest, interruptible) would also make the agent
  harness's best tool a player feature.
- **Overlays**: a toggle for turf (district tint by holder -- the PDA bars,
  on the map where they mean something), for heat (where patrols are), and
  for people (a dot on contacts with a scene waiting, a cross on a shop that
  is shut, a crew member recovering).
- **Fast travel by taxi**: taxis already drive in traffic. Flag one down (horn
  beside it) or call one from the PDA: a fee by distance, the day doesn't
  pass, your car is left where it is. The city is large; long drives between
  a shop and a job are the commonest dead time.

### 1.3 Teach the new systems where they bite
There is one "welcome" mail and a tutorial job that covers only the run.
Nothing tells a new player what notoriety, awareness, turf, clocks or
etiquettes do until they open the PDA.
- **First-time explainers**, one line each, the first time a system actually
  changes something: "Your notoriety rose to 1: some contacts will charge you
  more", "The Vipers lost ground in the Barrens", "That Johnson accepted too
  fast...". `Run.Hint` already does this for runs; the career needs the same.
- **Say why** on every number that moved: the debrief lists pay, karma and
  rep, but not what the job did to the city; add "Halcyon's plan set back (Datasteal)", "Viper turf -8 % in the
  Barrens".
- **A "what changed today" card** on waking: the morning's mail, news, rent,
  happenings and any shop that closed, on one screen instead of scattered over
  the toast, the radio and the inbox.

### 1.4 The inbox will fill up
Calls are modal and must be answered on the spot -- fine for a twist, tiring
for a run of them after a long job. Mail never leaves.
- Calls that *can* wait get a "call back later" button (it becomes mail).
- Archive answered mail out of the main list; filter by person; mark
  storyline mail (campaigns, contact stories) so it doesn't drown in ambient
  mail.
- Show what an answer will *do* when it's mechanical ("-¥2,000, heat 0")
  in the button tooltip; keep the flavour text for the reply.

### 1.5 Shops and loadout
- Shops are long flat lists. Sort and filter (affordable, by category), and
  **compare with what you carry** ("+4 dmg, -2 mag vs your Kestrel").
- Show **damage per second** and **noise** for guns -- the two numbers
  players actually choose by.
- A **loadout preset** per job type (stealth kit, assault kit) saves the
  click-through before every run.

### 1.6 Accessibility and comfort
- A **text size** setting (the HUD and PDA use 13-17 px type at 1600x900).
- **Colour-blind safe faction colours**: Vipers green vs Lotus red/magenta is
  the classic deuteranopia pair, and turf, graffiti and markers lean on it.
  Add a palette option and a shape cue (icons) next to colour.
- **Subtitles for voiced lines** exist on the contact panel; keep that rule
  if more lines are voiced (§2.5).
- A **gentler mode** beside ironman: the same game with one save per day, for
  players who want the story without losing a 40-day career to one bad door.

---

## 2. Story

### 2.1 Give the runner a past
Creation picks an archetype and a name; the story never knows who you were.
A **background** (ex-corporate, street kid, ex-Lone Star, tribal shaman,
Barrens-born) is the Dragonfall/Hong Kong move that makes the opening
personal:
- a starting etiquette and one contact at trust 1 (ex-Lone Star: Dace knows
  you; street kid: Wen does),
- storylet choices only that background can take ("(Ex-Lone Star) You know
  this precinct's codes"),
- and a short **personal campaign** -- the past catching up -- in the same
  four-chapter shape as the others.

### 2.2 Make the crew live at home
Crew talk only through mail and a debrief line. Dragonfall's strength was
talking to the crew between runs. Put the hired crew **in the safehouse** as a
scene each (Place hook, cooldown): a conversation with a choice that moves
their trust, reveals their history, or opens their personal job. Cheap
(storylets already support it) and the most remembered part of that game.

### 2.3 Choices on the run, not only after it
Run choices are calls after the debrief, so the player decides with the job
already finished. Move some **into the run**: the hostage begging at the
extraction (let them run off -- the job fails, karma and a friend), the
Johnson's dirt found on the mainframe (a second datastore you can pull or
leave), a guard who surrenders (spare him -- he becomes a fate, like targets).
`Run.Twist` and the hostage AI are the hooks.

### 2.4 Brief texture
Generated briefs are one template per job type. Voice them by **who is
asking**: Juno's dry two-liners, Sal's shouting, Null's lowercase, each
Johnson's manner (the fifth pass gave them personalities; the briefs don't
show it yet). Two or three templates per employer per type is enough
to stop the board reading like a form.

### 2.5 Voice the beats
Only greetings are voiced. The campaign openers, the midpoint calls and the
endings are 20-30 lines; voicing them through the existing assetgen pipeline
is cheap and lifts exactly the moments that matter.

### 2.6 Loose ends in the text
- The Iron Spire's epilogue lists contacts and districts; extend it to the
  later campaigns and to the **career receipt** at death or retirement (days,
  jobs, people saved, people lost, who runs each district, what the radio
  says about you).

---

## 3. Mechanics

### 3.1 Every archetype needs a verb on the run
- **Face**: Charisma does nothing on a run (it lowers prices and helps a
  haggle). Add **talking**: walk up to an unaware guard and bluff (Charisma +
  the matching etiquette vs threat) -- he waves you through, sends his patrol
  away, or calls it in. A disguise item (a guard's uniform from a knocked-out
  guard: carry the body, take the uniform) lets a Face walk corridors. This
  also makes the social etiquettes matter on the job.
- **Mage**: spells are combat abilities. Add **astral projection** (leave the
  body, scout through walls, like the drone but with spirit dangers) and
  **wards** (a placed barrier). Spell levels bought with karma.
- **Decker**: the Matrix is a separate minigame. Let the deck work on the site
  too: hack doors, cameras and turrets from range is there; add **reading a
  terminal** to mark guards, **spoofing a radio** to send a patrol away.
- **Rigger** play exists through drones; let the **car** join the run: park it
  at the extraction, and a getaway with chasers if the alarm was raised (the
  ambush system is most of this).

### 3.2 Money needs somewhere to go
The simulated career ends day 70 with about ¥650,000. Most good gear is
bought by mid-career, so cash stops meaning anything.
- **Cyberware grades** (alpha / beta / delta): the same implant for less
  essence at two to ten times the price. The most Shadowrun of sinks, and it
  matters most to exactly the chromed-up late game.
- **Lifestyle tiers** beyond rent (squatter -> low -> middle -> high): better
  safehouse perks, faster healing, a nicer view, and a raid is harder.
- **Property**: buy a business (a noodle bar, a club, a garage) that pays
  weekly, can be leaned on by a faction (a storylet), and raided.
- **Crew upkeep**: a retainer per week for a standing crew instead of a cut,
  with gear you buy them.

### 3.3 The police side of the city
- **Roadblocks** during a crackdown (the happening exists; the barriers
  don't): avenue segments closed by cruisers, forcing routes.
- **Day and night**: every run is at night. A time-of-day choice on accepting
  (day: more civilians, fewer guards, more witnesses; night: the opposite)
  would double the variety of every generated site.

### 3.4 Factions beyond grudges
Factions only remember harm (grudge). Add **standing** that rises when you
work *for* them (a Johnson backed by the Lotus, siding with them in a gang
war): safe passage in their districts, a faction-specific shop item, their
own jobs offered through their own contact. Turf already tracks who wins; this
lets the player pick a side for a career.

### 3.5 Edge
A Shadowrun signature: a few **Edge** points per run (from Charisma/karma) to
cheat fate -- shrug off a killing shot once, reroll a failed hack, slip a
detection. It softens ironman deaths without removing them, and gives
low-combat archetypes a safety valve.

### 3.6 Threat beyond 5
Late careers outgrow threat 5. Prestige jobs (threat 6: two bosses, elite
everything, a TraumaLink HTR team on every corp site) with unique loot would
give the endgame a target.

---

## 4. Tooling

- **A balance report**: the long simulated career is the best balance tool
  the project has. Run it for each archetype across several seeds in the
  self-check and print cash, karma, notoriety and turf at days 20/40/70, so a
  tuning change shows its effect. Today it plays one face career with a fixed
  85 % win rate.
- **Agent playtests on the new systems**: the harness can already read mail
  and press choice buttons; a scripted 20-day agent career would exercise the
  real UI (calls interrupting driving, scenes on entry) the way the bot
  exercises runs.

---

## Suggested order

| # | What | Why first | Size |
|---|---|---|---|
| 1 | Map fixes, route line, overlays, taxis (1.1, 1.2) | every player meets them in the first ten minutes | M |
| 2 | Teaching the systems, morning card, inbox tidy (1.3, 1.4) | the fourth and fifth passes are invisible without it | M |
| 3 | The Face's verb on the run, disguises (3.1) | an archetype with nothing to do | M |
| 4 | Crew at home, backgrounds (2.2, 2.1) | the strongest story lever for the cost | M |
| 5 | Money sinks: cyber grades, lifestyle (3.2) | the economy runs away by mid-career | S-M |
| 6 | In-run choices, brief voices (2.3, 2.4) | choices that feel made, a board that reads alive | M |
| 7 | Balance report (4) | before tuning anything above | S |
| 8 | Day/night, roadblocks, faction standing, Edge, threat 6 (3.3-3.6) | depth for long careers | L |
| 9 | Accessibility options (1.6) | cheap, and should not wait forever | S |
