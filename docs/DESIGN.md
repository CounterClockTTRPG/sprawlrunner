# Sprawlrunner design

An overhead action roguelike in a rain-soaked cyberpunk city, inspired by the
Shadowrun tabletop and SNES/Genesis games (original names, setting and text).
You are a shadowrunner in **Kessler Bay**: drive the streets, meet your
contacts, take jobs from the terminal in your safehouse, and run them --
on foot, real time, top-down, with guns, grenades, cyberware and a little
magic on the other side. Every number lives in `src/Sim/Tables.cs`.

One career, saved after every run (ironman). Die on a run without a
TraumaLink contract and the career is over.

## The loop

    safehouse terminal --> accept a job --> drive to the site --> run it
          ^                                                         |
          |                                                         v
    spend nuyen + karma <-- debrief (pay, karma, loot) <-- extract (or die)

A **day** passes with every run (and when you sleep at the safehouse). The
job board refreshes a little every day; jobs expire.

## The city (driving)

- 160x120 tiles, 32 px a tile in the view. A grid of avenues (4 tiles: two
  2-tile lanes) splits the city into blocks; each block is sidewalk round its
  edge and lots inside, split by alleys. Lots are buildings (solid, drawn as
  rooftops with a fake-3D lean away from the camera), parks, car parks.
  The bay runs along the south and east edges.
- Five **districts**, each with a threat range and a site style:
  Docks (1-2, warehouses), Barrens (1-3, gang dens), Neon Strip (2-4, clubs),
  Industrial (2-4, labs and plants), Spire (3-5, corporate offices).
- **Places** are lots with a lit door on the sidewalk: the safehouse, six
  contacts, and the sites of accepted jobs. Stop on the door marker and press
  E to go in.
- **Car**: arcade physics. Throttle, brake/reverse, steering that tightens at
  speed, lateral grip (handbrake drops it, so you drift), bounce off walls.
  Hard hits damage the car; a wrecked car is towed home (fee) and repaired.
  Traffic follows the lanes and stops for you; pedestrians scatter. The horn
  (H, pad B) sends everyone on foot within 9 tiles running away from the car
  for a second or so (half a second between blasts).

## Contacts

| Contact | Where | Offers |
|---|---|---|
| Juno Vance, fixer | The Rusty Halo, Neon Strip | jobs, sells intel (site schematics) |
| Doc Ichikawa, street doc | Clinic, Docks | cyberware, healing, TraumaLink contracts |
| Big Sal, arms dealer | Sal's Surplus, Industrial | weapons, armour, grenades, medkits, weapon mods |
| Torque, rigger | Chop shop, Barrens | vehicles, repairs |
| Null, decker | Data haven, Spire | cyberdeck programs, terminal upgrades |
| Old Wen, fence | Pawn shop, Barrens | buys loot you carry out |
| Madame Sabine, talismonger | The Moonlit Charm, Neon Strip | spells and a power focus (the Awakened only) |

**Loyalty** (0-5) rises when you finish that contact's jobs (fixer: any job)
and opens better stock and a discount of 4 % per point. Charisma takes off
another 3 % per point.

## The terminal

A green-on-black console at the safehouse (and the fixer's back room). It
lists the job board: type, employer, district, threat (1-5 skulls), pay,
karma, days left; `details` shows the brief; `accept` marks the site on the
map. One active job at a time. Higher threat jobs only show with enough
street rep. A Matrix rig upgrade lists more jobs and can pull site schematics.

## Jobs

| Type | Brief | Done when |
|---|---|---|
| Eliminate | kill a named target (with bodyguards, flees when hurt) | target dead |
| Retrieve | find an item (prototype, datachip, package) | item carried to extraction |
| Destroy | wreck N things (server racks, drug vats, a prototype drone) | all destroyed |
| Datasteal | hack a mainframe (hold E; Logic and Electronics speed it) | download complete |
| Rescue | free a hostage and bring them out (they follow you) | hostage at extraction |
| Clear | wipe out a gang's hideout | every hostile dead |

Every job ends at the **extraction** zone (often where you came in): walk
in once the objective is done. Leaving early (hold E in the zone, or bail
out from the pause menu) is a failed job (rep loss, no pay). **Bonuses**: no
alarm raised (+25 % pay; not on couriers and races, which raise none), loot
(cash stacks, paydata, valuables to fence).

Pay: `800 * threat^1.6 * (0.9..1.2)` nuyen, karma `2 + 2 * threat`.

## Runs (the action)

Real time, fixed 60 Hz simulation. Tiles 32 px; the player is a circle of
radius 0.3 tiles. Camera follows, leaning towards the mouse.

### Controls

| | |
|---|---|
| WASD | move (Shift: sneak -- slower, quiet footsteps) |
| Mouse | aim; LMB fire; RMB throw grenade (hold: shows the arc) |
| 1 / 2 / 3, wheel | primary / secondary / melee |
| G | next grenade type |
| R | reload |
| Space | dash (short invulnerable burst) |
| Q / F | ability slots |
| E | interact: doors, terminals, pickups, hostage |
| H | medkit |
| Esc | pause |

### Terrain and destruction

Every tile has a material with hit points. Bullets chip walls, explosions
crush them by radius falloff; a destroyed wall becomes rubble (walkable,
does not block sight).

| Tile | HP | Blocks move | Blocks sight | Blocks shots |
|---|---|---|---|---|
| Floor, street, rubble | -- | | | |
| Glass | 5 | yes | | yes (breaks) |
| Drywall | 60 | yes | yes | yes |
| Brick | 160 | yes | yes | yes |
| Concrete | 400 | yes | yes | yes |
| Reinforced (map edge, vaults) | inf | yes | yes | yes |
| Door | 80 | closed | closed | closed |
| Locked door | 200 | yes | yes | yes |
| Crate (cover) | 50 | yes | | yes* |
| Barrel (explodes) | 25 | yes | | yes |
| Server rack, vat (objectives) | 120 | yes | | yes |

\* Low cover (crates, desks, barrels, racks, wrecks) is shot over by
whoever is right behind it when aiming past it, so it protects the one
crouched behind it -- but it is hit when it is what you aim at.

Doors open when someone walks into them. Locked doors are hacked (hold E)
or blown. Explosive barrels chain.

### Field of view

Recursive shadowcasting from the player's tile, radius 13 (cybereyes +3).
Unseen tiles keep a dim memory once explored; unexplored is black. Enemies,
shots and pickups are drawn only on visible tiles. Thermal cybereyes show
warm bodies through walls within 7 tiles. The same opacity rules drive
enemy sight, so if you can see them they could see you.

### Stealth and alarm

Enemies start unaware: patrolling, vision cone 110 deg x 10 tiles. A unaware
enemy who sees you fills a detection meter (faster when near, slower when
you sneak, slower with Stealth); full means alert. **Noise** -- shots
(per weapon), explosions, sprinting near -- makes enemies within the radius
investigate. An alerted enemy shouts (alerts those within 8 tiles). Cameras
that see you raise the **alarm**: everyone hunts you and reinforcements
arrive from the map edge every 25 s.

### Enemies

| Enemy | HP | Armour | Kit | Notes |
|---|---|---|---|---|
| Ganger | 45 | 0 | pistol / SMG | erratic aim, rushes |
| Hound | 35 | 0 | bite | fast melee paracritter |
| Gang mage | 55 | 0 | fireball | telegraphed, explodes |
| Guard | 75 | 2 | SMG / rifle | keeps range, strafes |
| Heavy | 170 | 6 | LMG / shotgun | slow |
| Sniper | 60 | 1 | sniper rifle | laser sight warns you |
| Drone | 40 | 1 | light gun | flies over crates, EMP kills |
| Turret | 130 | 4 | autocannon | fixed, EMP stuns, hackable |
| Camera | 15 | 0 | -- | raises the alarm |
| VIP | 80 | 2 | pistol | flees |

Factions: **Ash Vipers** gang (gangers, hounds, mages), **Halcyon-Matsuda
security** (guards, heavies, snipers, drones, turrets, cameras), **Red Lotus**
syndicate (guards with blades, heavies, mages).

On threat 3+ guards, heavies and gangers carry one frag each and lob it
at your last known position when you break line of sight behind cover.
An unaware guard who sees a body raises the alert.

AI: idle/patrol -> suspicious (investigate a noise) -> alert (fight: hold
range, strafe, reposition when out of LOS; melee types rush) -> search (go to
last known position, look around, give up after 12 s). Chasing uses a shared
distance field from the player's tile, rebuilt 4x a second.

### Damage

`damage after armour = max(raw - armour, raw * 0.25)`. Explosions ignore half
the armour. Headshots don't exist; crits do: 8 % chance for x2 (Firearms
adds 2 % a point).

## Weapons

| Weapon | Dmg | Rate/s | Mag | Reload | Spread | Noise | Cost |
|---|---|---|---|---|---|---|---|
| Hollow-9 pistol | 18 | 4 | 12 | 1.1 | 4 | 12 | start |
| Whisper pistol (silenced) | 16 | 4 | 10 | 1.2 | 4 | 3 | 1,800 |
| Kestrel SMG | 11 | 12 | 32 | 1.6 | 9 | 14 | 2,600 |
| Breacher shotgun (8 pellets) | 9 | 1.4 | 6 | 2.4 | 16 | 18 | 3,400 |
| Vanta assault rifle | 20 | 8 | 30 | 2.0 | 5 | 18 | 6,500 |
| Longshot sniper | 95 | 0.8 | 5 | 2.6 | 0.5 | 22 | 9,000 |
| Mauler LMG | 17 | 11 | 80 | 3.8 | 8 | 20 | 12,000 |
| Pike launcher (rocket) | 120 blast | 0.6 | 1 | 2.2 | 1 | 26 | 15,000 |
| Combat knife | 30 | 2.5 | -- | -- | arc | 1 | start |
| Katana | 55 | 1.8 | -- | -- | arc | 1 | 2,200 |
| Monowhip | 70 | 1.4 | -- | -- | long arc | 1 | 8,000 |

Spread in degrees; Firearms (pistols, SMG, rifles) or Heavy Weapons
(shotgun, LMG, launcher) take 10 % a point off it and speed reloads 5 %
a point. Melee uses Strength and Blades. Reserve ammo is unlimited --
magazines and reloads are the constraint.

Grenades (bought, carried up to 3 + Demolitions): **frag** (90, r 3),
**flashbang** (stun 3 s, r 4), **EMP** (kills drones, stuns turrets and
cameras 8 s, r 4), **incendiary** (fire on the floor for 6 s, spreads to
crates), **breach charge** (placed, 300 to tiles, r 1.5). Throw range 6 +
Strength tiles; fuse 1.4 s; they bounce off walls.

Armour (one worn): armoured jacket (3), lined coat (5), full body armour
(8, -6 % speed). Medkits heal 50 x (0.5 + Essence/12).

## You

**Attributes** 1-6 (karma cost: next level x 5): Body (+20 max HP each on
60), Agility (+4 % speed, -6 % spread), Strength (throw range, melee damage
+12 %), Reaction (-6 % ability cooldowns, faster dash recovery), Logic
(hacking speed +20 %), Charisma (prices -3 %).

**Skills** 0-6 (karma: next level x 3): Firearms, Heavy Weapons, Blades,
Demolitions (grenade damage +10 %, +1 carried), Electronics (hacking speed
+15 %, hack turrets at 3), Stealth (detection -12 %, quieter), Driving
(grip and steering +6 %).

**Archetypes** at the start: Street Mage (Logic, Charisma, Manabolt and Heal), Street Samurai (Body, Agility, Firearms,
Blades, wired reflexes), Decker (Logic, Electronics, cyberdeck), Face
(Charisma, Stealth, more starting nuyen), Combat Engineer (Strength,
Demolitions, Heavy Weapons, extra grenades).

### Cyberware

Essence starts at 6.0; each implant costs some, and cannot take you below
0.1. Low essence weakens medkits.

| Implant | Essence | Effect | Cost |
|---|---|---|---|
| Cybereyes | 0.2 | FOV +3 | 3,000 |
| Thermal overlay (needs eyes) | 0.1 | see bodies through walls (7) | 6,000 |
| Smartlink | 0.3 | spread -35 %, aim line | 8,000 |
| Dermal plating | 0.5 | +3 armour | 9,000 |
| Muscle replacement | 1.0 | Strength +2 | 12,000 |
| Bone lacing | 0.8 | +40 HP, melee +30 % | 14,000 |
| Synthacardium | 0.3 | dash cooldown -40 % | 7,000 |
| Wired reflexes | 2.0 | ability **Overclock**: bullet-time 4 s | 25,000 |
| Cyberdeck | 0.5 | ability **Hack**: turn a turret/drone, open a door at range | 15,000 |
| Ruthenium skin | 1.0 | ability **Cloak**: unseen 5 s | 22,000 |
| Spur blades | 0.3 | replace the knife (and become your melee): 65 dmg, 1.6/s | 5,000 |

Abilities go in slots Q and F. Everyone also has **Dash**.

### Magic

Only the Street Mage is Awakened. Spells are abilities on Q/F with
cooldowns; their power is `(0.4 + 0.6 x Essence/6) x (1 + 0.08 x Logic)`,
x1.25 with a power focus -- so every implant dims the magic (the doc says by
how much).

| Spell | Cooldown | Effect | Cost |
|---|---|---|---|
| Manabolt | 3.5 s | 55 x power to one living target in sight, ignores armour; nothing on machines | start |
| Heal | 16 s | +45 x power HP, puts out burning | start |
| Fireball | 9 s | slow fireball, bursts r 2.2 for 70 x power, sets fires | 9,000 |
| Invisibility | 25 s | as Cloak | 14,000 |

## Vehicles

| Vehicle | Top | Accel | Grip | HP | Perk | Cost |
|---|---|---|---|---|---|---|
| Junker sedan | 11 | 6 | 0.85 | 100 | -- | start |
| Vector bike | 15 | 10 | 0.9 | 60 | small: slips through traffic | 6,000 |
| Hauler van | 9 | 4.5 | 0.8 | 220 | +1 grenade of each type you own on every run | 9,000 |
| Raptor GT | 17 | 9 | 0.92 | 120 | -- | 28,000 |
| Bulldog armoured | 11 | 5 | 0.85 | 400 | shrugs off ramming | 40,000 |

Speeds in tiles/s. Driving skill adds grip and steering.

## The safehouse

Upgrades, each bought once in order where tiered:

| Upgrade | Effect | Cost |
|---|---|---|
| Matrix rig I / II | +2 jobs on the board; II: free schematics (map revealed at the start) | 6,000 / 18,000 |
| Med bay | start runs with +1 medkit; TraumaLink 30 % cheaper | 8,000 |
| Armory | +1 grenade carried; 1 free frag per run | 7,000 |
| Garage | free repairs, store more cars (2 -> 5) | 5,000 |
| Training room | karma costs -15 % | 12,000 |
| Move to the Loft | start the day in the Spire (closer to rich jobs), +1 rep a week | 30,000 |

## TraumaLink

A contract from the doc: if you go down on a run, a trauma team pulls you out
(job failed, gear kept, lose 20 % of your cash). Each contract covers one
pick-up; 3,000 x (1 + rep/4).

## Street rep

+1 per finished job (+1 more at threat 4+), -1 per failed. Shows jobs up to
threat `1 + rep/3` (max 5).

## Levels

Generated per job from the job's seed, size by threat (56x40 up to 84x60).
The edge is reinforced; outside is street or yard with cover (cars,
crates, barrels). The main building is a BSP of rooms (min 5x5): outer walls
concrete with glass runs, interior walls drywall or brick (gang dens) or
glass (labs). Doors join BSP siblings, so every room is reachable, plus a
few extra doors for loops; vault rooms get reinforced walls and a locked
door. Objectives go far from the insertion point; extraction is the
insertion point or another edge. A flood fill checks every objective is
reachable through doors (locked doors count as passable -- they can be
hacked or blown).

Site styles: warehouse (big rooms, crates, catwalk-free), office (many
small rooms, glass), gang den (brick, clutter, barrels), lab (glass, vats,
cameras), club (a big dance floor, a VIP room upstairs-equivalent at the back).

## Additions since the first version

### The streets
- **Heat** 0-5 stars: an alarmed run +1, a bloody one (6+ kills) +0.5, a
  hit-and-run +0.6, reckless crashes and ramming police add more. It cools by
  a star every ~22 s with no patrol near, and sleeping clears it. Each star
  puts a Lone Star cruiser on you (up to 4); they box you in rather than ram,
  and stopping beside one for 2 s is **busted**: a fine of ¥300 + 10 % cash +
  ¥400 a star. The market informant can also make it go away.
- **Gangers** ambush you after rich or loud runs (35 %), on courier jobs, and
  always once a faction's grudge reaches 5. They drive cars or bikes and
  shoot at yours; get 45 tiles away for 6 s to lose them.
- **Courier jobs**: collect a package, deliver it across town before the
  timer (distance-based) runs out, with gangers after you. Pay x0.8; bikes
  get +20 % time.
- **Weather** per day (clear, rain, storm, fog): rain masks footsteps and
  shortens enemy sight a little, fog shortens everyone's.
- **Hubs** (the night market in the Strip, the Barrens bazaar): park and walk.
  No weapons. Noodles (+15 % max HP next run), an info broker (schematics),
  a chip dealer (cheap grenades), a Lone Star informant (heat to 0), street
  kids (rumours that are real tips).
- **Motorcycles**: Vector, Kuro R9 (fastest), Harrow chopper (tough),
  Dirt scrambler (offroad). Sharper steering, slides at the limit, hard
  brakes, slip between traffic; crashes x1.4 and gunfire x1.5 damage.
- Car economy: crashes above 4.5 t/s impact, repairs ¥5/hp at Torque, a free
  patch-up to 60 % at home, tows cost ¥400 + 10 % of your cash.

### Runs
- **Conditions** on jobs (0-2, in the brief): Blackout (lights and cameras
  out, everyone sees less), Shift change (double patrols), Deadline (the
  target leaves / security sweeps), Elite security (+2 armour, +20 % HP),
  Skeleton crew (fewer guards, less pay).
- **Stealth tools**: non-lethal weapons (stun baton, taser) and gel rounds
  knock out instead of killing (a no-kill run pays +15 %, not on hacks, wetwork,
  sweeps or driving jobs); carry bodies (press
  and hold E; E drops); whistle (V) to lure a guard; unaware guards who see a
  body raise the alert.
- **Fixtures**: a security console (loop every camera and turret), lockers
  (grenades, medkits), safes (cash, valuables).
- **Ammo types** (Sal): gel, APDS (armour counts half), explosive (x1.2,
  walls x2.5, louder), chosen per gun in the loadout. New guns: taser, Hush
  crossbow (near silent), Hornet smart-SMG (homing with a smartlink),
  Cyclone minigun.
- **Enemies**: shield troopers (front blocks bullets), combat riggers (launch
  two drones), bound spirits (bullets x0.25), one **boss** per threat-5 site
  (Iron Saint, Big Mamba, the Jade Widow: telegraphed charge, drops loot).
  Guards, heavies and gangers on threat 3+ carry a grenade for when you
  hide.
- **Hires** at the fixer: Ox (tank), Wisp (sniper), Patch (medic, 3 heals) for
  20-25 % of the pay; they hold fire while you sneak.
- **Basements** on many threat 3+ sites: stairs lead to a concrete level
  where the vault and objective often are. Everyone uses the stairs.
- **The Matrix** (datasteal): a 9x5 node grid; move node to node, grab three
  datastores, avoid ICE (black ICE on threat 4+). Caught: alarm and
  dumpshock (10/25). Your body stays exposed; a hit pulls you out.
- **Mr. Johnson** jobs (30 %) are negotiated: deal, haggle once (Charisma),
  or walk. 15 % are set-ups: a squad arrives by the exit when the job is
  done. Loyal fixers warn you.
- **Grudges**: +1 per job against a faction, -1 a week. 3+: their sites get
  elite security (and Halcyon adds heat); 5+: hitters after every job.
- **Magic**: Astral sight (see the living through walls, 8 s) and Summon fire
  spirit (an ally for 30 s) join Manabolt, Heal, Fireball and Invisibility.

### Career
- A **story** in four chapters from Juno (after 2, 5, 8 and 11 jobs: the
  package, who's buying, loose ends, Iron Spire). Finishing it retires you a
  legend.
- **Legacy** perks earned by careers carry into new ones (old stash, old
  friend, spare chrome, legend's bike).
- **Favours**: at loyalty 2, each contact does one every 5 days (heat wipe,
  getaway driver, camera loop, free TraumaLink, frags, full-value fence,
  faster spells).
- **Loyalty** also grows with every ¥8,000 spent at a contact (up to 3).
- **Injuries**: ending a run below 30 % HP is an injury (-10 % max HP each,
  up to 3) until you sleep or the doc patches you up.
- **Cyberpsychosis**: below 1.5 essence, phantoms that are not there.
- A free TraumaLink on day 1, and a guided tutorial job.
- Quitting mid-run counts as bailing out (ironman).

### Controls
Actions are rebindable (Settings > Controls). Gamepad: left stick move/steer,
right stick aim, RT fire/throttle, LT throw/brake, A dash/handbrake, X use,
B medkit, Y/RB abilities, LB grenade, d-pad weapons, L3 sneak, R3 whistle.

## Third pass

### City
- The city is ~40 % larger (228x170 tiles), with 80 traffic vehicles of nine
  kinds: sedans, vans, taxis, buses, limos, garbage trucks, motorbikes, Lone
  Star patrols (which join the chase if you have heat and they see you) and
  an ice cream truck or two (with its jingle).
- **Landmarks**, one block each: Halcyon Arcology (Spire, tiered tower with a
  beacon), Neon Pagoda (Strip), St. Brigid's cathedral (Barrens), the
  container port with cranes (Docks), Kessler Stadium (Industrial), and
  Memorial Plaza, a fountain square you can drive through.

### Sites
Each style now has its own floor plan: offices are a corridor spine with
rooms (some glass-fronted) and an open-plan floor of cubicle rows; labs are a
ring corridor of rooms round a glass clean room; warehouses are one shelved
hall with a strip of offices and wide loading doors; gang dens are BSP rooms
with walls knocked through and a roofless courtyard; clubs keep their dance
floor and back rooms. When the objective is on the other floor, the HUD
points at the stairs.

### Twists
About one run in eight, the plan changes: the package is moved (retrieve),
a second target appears (eliminate, +35 %), the client wants the target
alive (knock them out and carry them out, +40 %; dead pays half), or the
exfil is compromised and moves.

### Contacts
Trust (loyalty 0-5) now matters on every job: +8 % pay a point from that
contact, and at trust 3 they offer work a threat above your rep. Each contact
has a three-part personal story, opening at trust 1, 2 and 3, with briefings
in their voice, a closing word in the debrief, and a unique reward:
Ichikawa's reflex booster (Reaction +2), Sal's Thunder (LMG), Torque's
Interceptor (car), Null's Ghost protocol (cameras blind, ICE slower), Wen's
gratitude (full value for loot, ¥5,000), Sabine's ward (+2 armour, spells
+10 %).

### PDA (P in the city)
Contacts (trust, what it gets you, story progress, favours), factions and
heat, career stats (jobs by type, kills/KOs, walls, spells, Matrix
downloads, nuyen in and out, distance driven, busts, ambushes) and 27
achievements, kept across careers, plus the journal.

### Enemies
Low tiers vary more: gangers and guards carry different guns, Bruisers rush
with a pipe, Firebugs lob molotovs, corporate sites field K9 units. Corpses
lie flat in a pool of blood (none when knocked out), drawn per kind.

### Drones
Torque sells the Fly-spy (fast scout, unarmed) and the Doberman (SMG). Press
T on a run to jack in and fly it with your view coming from it, while your
body stands still (a hit pulls you out); the signal reaches 15 tiles (30
with the doc's control rig, which also toughens drones and their guns). Off
the stick it follows you, reveals what it sees, and the Doberman fights.
A destroyed drone costs a quarter of its price to rebuild.

## Painted pictures and voices

Everything is still made by the game itself except three small sets of files,
which are generated through `../assetgen` (project `sprawlrunner`, flux-1.1-pro
images in a Blade Runner-style noir, minimax speech) and committed at the size
they are drawn at:

- **Portraits**: the seven contacts, the safehouse and the Spire loft views, on
  the contact panel and the PDA; the three hires and the three faction bosses,
  on the briefing.
- **Briefing banners**: one per site style, cropped to a strip above the title.
  The briefing also shows a face for whoever runs a threat-5 site (every
  one has a boss) and for a hired runner.
- **Greetings**: every contact line in `Tables.Contacts`, spoken in that
  contact's voice when the line is shown.

All of it is optional. A missing file means the procedural portrait comes
back, the banner and faces are left out, and the greeting stays silent.
`assets/art/manifest.txt` and `assets/voice/manifest.txt` list the files with
their prompts, and `scripts/assets.sh` generates whatever is missing. The
self-check ties the manifests to the tables, so a new contact, hire, faction or
site style, or an edited greeting, fails until its file is made.

## The look of the interface

Worn street hardware rather than a clean sci-fi app. The frames are sodium
amber on warm grimy blacks, with two corners cut, bracket marks, ticks and a
procedural dirt-and-scratch texture (`GrimeBox`). The same box draws the
themed screens and the hand-drawn HUDs. Neon is kept for what it signals:
cyan for the Matrix and AR readouts, magenta and red for danger and the corps,
violet for magic. Warnings get hazard stripes (the alarm, police on you, a
courier clock running out).

Each screen is an object in the world (`Dressing`):

- the terminal is an old CRT: bezel, scanlines, roll bar, burnt-in
  SHADOWNET, power light;
- the PDA is a cheap commlink: scuffed casing, status bar, cracked glass, a
  repair-shop sticker;
- the briefing is the fixer's file: file number, EYES ONLY stamp, the site
  photo taped in, the layout blacked out until you buy the schematics;
- the debrief is a credit-union receipt: torn thermal paper, figures that
  count up as it prints, the verdict stamped on it, a barcode;
- new-runner is a fake ID: photo box, serial, barcode, UNREGISTERED stamp;
- shops hang paper price tags, and loyalty and trust are punch cards.

The fonts ship with the game (`assets/fonts`, all under the SIL Open Font
License, each licence beside it): Barlow Condensed SemiBold, spaced out, for
headings; Barlow for body text; Stardos Stencil for stamps; Share Tech Mono for
printed figures; Inconsolata for the terminal. A missing file falls back to the
Windows fonts the game used before (Bahnschrift, Stencil, OCR A, Consolas).

The picture scales to the window (`canvas_items` stretch, expand): 1600x900 is
the design size, larger screens get a larger picture, not a smaller interface.
Settings also offer the window size (1280x720 to 2560x1440, never past the
screen), vsync, a frame-rate cap, and pausing when the window loses focus (a
run or a drive goes to its pause menu; never in agent mode or the checks).

Over everything sits a dirty monitor (`ScreenFx`): grain, scanlines, a
vignette, colour fringing, a warm cast and a rare flicker. On a run it jolts
red when you are hit and tears while the alarm is up, while you are jacked
into a drone, or while a Matrix glitch is on. The "Screen grime" setting
scales it, and 0 turns it off. Small motions complete it: panels power on with
a stutter, buttons flicker on hover, the brief types itself out, and the
title's neon drops out now and then. The walkthrough turns the random parts
off so screenshots stay comparable.

## Fourth pass: a city with a memory

The plan and its reasoning are in `docs/EXPANSION.md`; this is what was
built, with the numbers. Story state is plain data on the career (`Flags`,
`Fired`, `Inbox`, `Turf`, `Clocks`, `Happenings`, `Crew`, `Notoriety`,
`Awareness`, `Etiquettes`), so a save is still the career as JSON, and old
saves load with all of it at zero.

### Storylets (`Sim/Story.cs`, `Sim/Storylets*.cs`)
Small pieces of story with a condition, a channel and choices -- about 80 of
them. Each has an id, a **hook** (Morning: every new day; Debrief: after a
job is settled; Place: on walking into a contact or the safehouse), a weight,
a cooldown (once per career by default) and a `When` test on the career. At
each hook at most one fires, drawn by weight from a random stream seeded by
the career seed, the day and the hook -- never the career's own `Rng`, so
story never reshuffles the job board. Words in braces are filled in when it
fires (`{¥key}` prints a sum of money).

- **Mail** lands in the inbox: the terminal (`mail`, `read N`, `reply N K`)
  and the PDA's Inbox tab (it opens there when something is new). Unread mail
  blinks on the city HUD.
- **Calls** are mail that rings: the city screen opens it as a dialog as soon
  as you are back on the street and nobody is chasing you.
- **Scenes** play on the contact's panel when you walk in (one per place per
  day), with the choices as buttons.
- A choice can need something (money, an etiquette, a contact's trust); it
  says what, and is greyed out without it. Unanswered mail keeps.

### Consequences
- **Fates**: what became of every named target is kept (`fate:<name>`):
  killed, fled, taken alive, rescued. A target who got away writes within a
  few days: pay Juno ¥1,000 to find them first (a revenge job, a threat up,
  x1.6 pay) or let them come (hitters after your next job). A rescued hostage
  sends a thank-you (¥1,500, two medkits, or the floor plans of your next
  job). A killed one sometimes has family who write; ignore them and they
  send hitters later.
- **Run choices** (about one ordinary finished job in three, as a call):
  a collector wants a look at the retrieved item (0.6 x pay, Notoriety +1, and
  one time in four the client finds out: trust -1); the stolen data
  incriminates the Johnson (deliver it, leak it through Mara Quill --
  Awareness +2, karma +3, the employer's trust -1, the faction's plan set back
  -- or sell it back, 0.5 x pay, Notoriety +2); the rescued hostage begs not
  to be handed over (let them go: give back half the pay, karma +3, a friend);
  kids hiding in a cleared den (let them go: karma +1; Lone Star's bounty:
  ¥500, Notoriety +1; Gang etiquette: send their boss a message, grudge -2); a
  witness on a loud hit (¥800 to forget, or heat and awareness).
- **Notoriety** (0-10, never fades): betrayals, cruelty, selling out. 4+:
  Doc Ichikawa and Madame Sabine charge 25 % more; 7+: they sell you nothing
  but TraumaLink and patch-ups. Every point adds 2 % to the chance a Johnson
  sets you up. 5+: Vesper, a fixer with no scruples, offers dark work at
  double pay.
- **Public Awareness** (0-10, -1 every four days): +1 each for an alarm on a
  threat 3+ site, 10+ kills, a boss; leaks add more. Every morning starts
  with `max(0, Awareness - 3) x 0.5` stars of heat; 6+ invites a raid on the
  safehouse.
- **KBN headlines** in the news for loud jobs (alarm, 5+ kills, threat 4+),
  with the corporate spin.

### Burned exits and double-crosses
- A compromised extraction now has a cause, read off the career: a Johnson
  who sold you, heat on your car (2+), a target who got away, a hire who
  doesn't trust you (trust 1 or less), or a Lone Star scanner. The debrief
  names it, and someone follows up: Torque swaps your plates (heat 0), Null
  scrambles your commlink (¥1,500: half the burned exits), Juno finds who
  sold it (a job), the hire apologises (forgive or not).
- A double-cross is a squad at the exit (as before) or, one time in three, a
  **stiff**: no squad, but the pay never arrives. Either way Juno finds the
  Johnson within days: ¥2,000 puts a payback job on the board; at trust 4 she
  can instead squeeze him for half the original pay (Notoriety +1).
- **Tells**: a set-up pays 15 % over the odds, and its Johnson accepts any
  haggle (+30 %) without blinking. Corporate etiquette says so outright.

### Etiquettes
Street, Corporate, Gang, Security, Magic. From the archetype (samurai:
Street, Security; decker: Corporate; face: Street, Corporate; engineer: Gang;
mage: Magic), more for 12 karma each (training room discount applies) on the
Runner tab. They open storylet choices; Corporate adds 10 % to a haggle.

### TraumaLink in the city
- An ambulance is always somewhere in traffic: white, green cross, lights
  flashing, fast, its own siren; cars pull over for it and it never waits
  behind them. Ramming it is heat (+0.5) and Notoriety (+1), once per
  ambulance.
- After a pick-up you come round on a gurney outside Ichikawa's clinic, the
  ambulance pulling away.
- After an alarmed run an ambulance and a patrol head for the site.
- **TraumaLink Gold** (Doc, ¥15,000 once): pick-ups keep your cash, and the
  trauma team pulls out your crew member too.

### The city moves
- **Turf**: each district has a share for each faction, starting from the
  district's mix (its first-listed faction gets an extra half share: the Docks
  are Viper, the Strip Lotus, Industrial Halcyon). Someone else takes the
  district only when they hold 5 points more than whoever runs it. A finished
  job against a faction there takes 5 % of it (Clear and Destroy 8 %) and
  gives it to the others; each week every share drifts a quarter of the way
  back to the district's mix. Generated jobs pick their faction by share. A
  district changing hands makes the news, and the new owners write: offering
  them your services drops their grudge by 2 and raises the old owners' by 1. The PDA's
  City tab draws the bars.
- **Faction clocks** (0-8): the Vipers' gang war, Halcyon's expansion, the
  Lotus buying the council, Lone Star's crackdown. +1 every three days (Lone
  Star: +1 a day while Awareness is 4+, else -1); a job against a faction sets
  its clock back (-1, Destroy and Datasteal -2). Full: the happening below for
  four days, and the clock drops to 2. Street kids hint at clocks at 5+; Null
  writes at 6+ and can set one back (¥3,000).
- **Happenings** (shown in the terminal, the PDA, and the agent's state):

| Happening | When | Effect |
|---|---|---|
| Stadium night | every 7th day from day 3 | heat cools twice as fast |
| Mana surge | ~1 day in 12, 2 days | spell power x1.2 |
| Corporate quarterly | every 14th day from day 10, 2 days | new Halcyon jobs get Shift change and +25 % pay |
| Grid failure | ~1 day in 15, 2 days, one district | every site there is under Blackout |
| Lone Star strike | ~1 day in 20, 2 days | no police chases; ambushes twice as likely |
| Lantern festival | every 15th day | the market's stalls at half price |
| Gang war | Vipers clock | Viper and Lotus jobs +20 %, ambushes after them (60 %); Juno asks you to pick a side |
| Halcyon expansion | Halcyon clock | Halcyon +10 % in the Docks and Industrial, elite on their new sites |
| Council in the pocket | Lotus clock | busts cost double; jobs against the Lotus +25 % |
| Crackdown | Lone Star clock | heat cools at half speed, the informant costs three times as much |

### Crew
The three hires keep a **trust** (0-5): +1 for each job finished together.
- They talk: a line in the debrief, by how it went (loud, quiet, close).
- At trust 2 and 4 they call with an **upgrade** choice (`Career.CrewUpgrades`):
  Ox -- dermal plates (+80 HP, +2 armour) or breacher shells (+40 %), then a
  second wind (up once at half health) or a smaller cut; Wisp -- AP rounds
  (armour counts half) or a faster rifle (x1.5), then spotter (you see what
  she sees) or a smaller cut; Patch -- five patch-ups or a trauma vest (+60
  HP, +2 armour), then stims (patch-ups heal 60) or a smaller cut.
- At trust 3, a **personal job** (Ox's old crew, Wisp's teacher, Patch's
  ward); they come along, and finishing it drops their cut by 5 points for
  good.
- **Permadeath**: a crew member who goes down on a run is gone for the
  career -- unless you carry them to the extraction (hold E on them) or have
  TraumaLink Gold; then they sit out three days. A funeral mail follows a
  death.

### The story, second draft
- Chapters still open after 3, 5, 8 and 11 jobs, but chapters 2-4 also need
  the **lead** the last one left, chased with a contact: Null traces the
  chip's serial (¥1,000, free at trust 2; Corporate reads it yourself);
  Pryce's sister waits at Ichikawa's clinic; Pryce draws the Spire for you at
  the Rusty Halo (the finale's schematics). Three jobs past the mark, the
  chapter finds you anyway.
- **The midpoint**: after "Who's buying", the buyer list -- leak it through
  Mara Quill (Awareness +3), sell it to the Red Lotus (¥15,000, Notoriety +2,
  Lotus grudge cleared) or give it to Halcyon's rival Sorensen-Aoki (¥8,000).
- **The finale remembers**: every finished contact story sends help (Sal two
  frags and a breach charge, Null loops the cameras, Torque waits outside so
  the exit can't be burned, Ichikawa two medkits, Wen ¥5,000 afterwards); the
  most trusted living crew member comes for no cut. The midpoint shapes the
  Spire: leaked -- elite guards and Lone Star SWAT at the exit; sold to the
  Lotus -- a riot draws half the guards off; Sorensen-Aoki -- double pay,
  elite guards, and their "exit team" at the exit.
- **Endings**: the Spire no longer ends the career on the spot. The receipt
  prints an epilogue (the city, each finished contact, districts that changed
  hands, the crew); then Juno calls: **retire** a legend (the epilogue again,
  the legacy, back to the title) or **keep running**. Legacy perks per ending:
  Folk hero (leaked: trust 1 with everyone), Lotus marker (+¥5,000),
  Corporate friend (a smartlink), on top of Legend.
- **Toxic Tide**, a second campaign of four chapters from Madame Sabine, opens
  once the Spire has fallen or her own story is done, a chapter every two
  jobs: an Investigate in the Docks, Destroy the "Clarity" vats, Rescue the
  priest of St. Brigid's, Clear the cult under the port. After the vats,
  Sabine and Ichikawa disagree: starve the spirit (a lighter last job) or save
  the drinkers (karma +5, two medkits, elite cultists). Finishing it is the
  Warded legacy (start with TraumaLink Gold).

### New jobs
| Type | Done when |
|---|---|
| Investigate | three pieces of evidence found on the site (pay x0.9) |
| Plant | a device left at the marked spot -- hold E for 3 s, faster with hacking skill |
| Defend | held for `75 + 12 x threat` s against waves from the edges (pay x1.1); nobody inside at the start; whoever is left runs |

The **safehouse raid** is a Defend at home, offered (as a call) when
Awareness is 6+: be waiting, or clear out for ¥2,000 (Awareness -2). Lose it,
or sleep through it, and the raiders take a third of your cash; hold it and
Awareness drops by 3.

### The block
- **Rent** every morning, a seventh of the week's (¥400 a week for the safehouse,
  ¥1,500 for the loft): moving out the day before rent saves nothing.
  Behind on rent, Mrs. Halloran waits on the stairs; owing at two week-turns, she gives
  your address to Lone Star (+2 heat every morning) until it is paid (which
  happens by itself once you have the money).
- Neighbours between runs at the safehouse: Mama Oyelaran's noodle stand
  (free bowls once you deal with the Vipers' protection racket -- pay, clear
  their den, or Gang etiquette), and Pip, a street kid who will be your
  lookout for ¥2,000 (half the ambushes on the way home, and warnings when
  someone is circling).

### People who only write
Mara Quill (KBN stringer: buys footage of loud runs, the leak channel), Sgt.
Dace (Lone Star, on the take: ¥1,500 a week keeps your heat at nothing and
warns of crackdowns) and Vesper (dark work, at Notoriety 5+) live in the
inbox only: no door, no shop.

### Achievements
Seven more: Low tide, Kingmaker, Ride or die, My house, Legwork, Never there,
Pen pal -- and Legend now counts keeping running after the Spire.

## Fifth pass: the rest of the plan

Everything left over from `docs/EXPANSION.md`, built, with the numbers.

### More jobs
| Type | Who offers it | Done when |
|---|---|---|
| Kidnap | anyone (pay x1.25) | the target knocked out (baton, taser, gel, a spell) and carried to the extraction; dead is a failed job |
| Escort | anyone | the client (who follows you from the start) has stood with you at the meeting spot for 10 s, then walked out alive |
| Bounty | anyone, in the Docks and Barrens | the nest cleared: hellhounds, ghouls, a hungry spirit; nobody's turf, no grudge |
| Hack | Null (x0.85) | three datastores pulled from your own rig at the safehouse; two ICE traces blow it |
| Street race | Torque (x0.85) | you reach the finish before a Viper hotshot who drives the route (faster with every skull); E at the start, E at the finish |

### Legwork
The briefing sells help before the run (not for driving, hacks or defences):
bribe a guard (¥150 x threat, half with Security etiquette: two of the rank
and file are gone), the patrol routes (¥100 x threat: everyone shows for 15 s),
cut the power (¥250 x threat, free with Electronics 2: a blackout), and
Juno's cleaner on the inside (trust 3: every locked door unlocked).

### TraumaLink, the rest
- **HTR teams**: on threat 4-5 Halcyon sites, half the time the first guard
  down brings a three-strong TraumaLink team six seconds later. They are
  neutral (green TRAUMALINK tags), walk to the body, bag it (no body left to
  find) and leave. Shoot at one and all three turn on you -- and TraumaLink
  remembers: contracts cost double, Gold and Platinum are refused, until you
  make amends at the doc (¥5,000).
- **Platinum** (¥40,000, needs Gold): go down with the objective already done
  and the team brings you out with it -- the job counts, at half pay.

### Recurring Johnsons
Every negotiated job has one of five Johnsons: Mr. Grey (never goes more than
15 % over), Dr. Vale (doubles the no-kill bonus), Auntie Mei (sells you out
much more often once the Lotus hold a grudge of 3), Mr. Tusk (¥500 tip when
the job is loud), Ms. Silk (pays 25 % over and crosses you 30 % of the time).
Three clean jobs and a Johnson (not Silk) rarely sets you up again, and sends
a thank-you; one who sells you out never calls again.

### What loot is worth
Wen's price moves with the city: +30 % for a faction's secrets while its plan
is at 5+, -30 % for hot goods (carried out of a loud job, for three days --
and selling them draws heat), and double for whatever his collector wants
this week (a mail from Wen).

### How contact stories end
Part 3 of every contact's story now ends with a call: their ending (the
unique reward) or another. Ichikawa: sell the headhunter's files (¥10,000);
Sal: leave Thunder on his wall for a weekly crate of grenades; Torque: let
him race the Interceptor (¥8,000, free repairs for good); Null: sell Ghost
protocol (¥12,000); Wen: keep the ledger's copy (¥1,500 a week, Notoriety +2);
Sabine: draw on the circle (spells +25 %, -0.5 essence); Dace: sell the
contract back to Halcyon; Quill: bury the story (each ¥15,000).

### Contacts in danger, and lost
When a faction that holds a contact's district has a grudge of 5 against you,
the contact calls for help (at most every 20 days): a Defend at their street
that night, or their shop is wrecked -- shut for a week, trust -1. The second
time, they leave Kessler Bay for good. At Notoriety 9 Ichikawa and Sabine
won't open the door at all.

### The heist
At rep 6+, Juno offers a three-night job on one Halcyon building: case it
(Investigate), kill the grid (Datasteal), then the vault (Retrieve: dark,
mapped, half the guards sent home, x5 pay). Fail or miss a night and it is
off.

### Two more campaigns
- **The Cut** (Juno; opens at 15 jobs and rep 8): Kade Marrow, an ex-runner
  who bought the files on all your jobs, now runs the Vipers. After chapter 2
  he offers you a place: refuse and he raids your home (chapter 3 is a Defend
  there), or play along and wire his house (a Plant), which makes the finale
  lightly guarded and mapped. His finale answers how you work: double patrols
  if you sneak, heavy armour if you shoot.
- **The Wyrm's Ledger** (Null; after the Spire, at rep 12): who really owns
  Halcyon -- a dragon, Veyrasz. Follow the money, pull the ledger, steal a
  scale, then leave a copy on its own desk. It calls: name a price (¥100,000,
  Notoriety +3), tell the world (Awareness +5, karma +10, hitters), or sell to
  Sorensen-Aoki (¥50,000).
- Legacies: Street king (+2 rep), Wyrm's coin (+¥10,000), Dragon-touched
  (Logic +1).

### New faces
- **Sgt. Dace** at The Blue Lamp (Docks) and **Mara Quill** at the KBN Night
  Desk (Neon Strip) now have doors, shops, favours and three-part stories.
  Dace: lose your file (heat to 0), pull a page (Awareness -2), a patrol
  outside the hitters' door, lose the crackdown plans; his badge means Lone
  Star never pulls you over. Quill: sell your story (¥400 a point of Awareness,
  Awareness +2; once a week, and not past Awareness 8), kill
  one (-3), run a story on a faction (its plan -3); her front page wipes your
  notoriety and halves how long fame lasts. They claim their lots after the
  markets, so existing cities keep every address (a job on a lot they moved
  into is dropped, or the active one moved).
- **Recruits**: Ade (Ichikawa's nurse, hireable after part 2 of the doc's
  story: four quick patch-ups) and Byte (Null's kid, after part 2 of Null's:
  blinds a camera, turret or drone near you every 9 s). Both have upgrades,
  banter and a personal job (Ade's testimony is an Escort; Byte's is a Hack
  from your rig).

### The city talks back
- **The car radio** cycles every 8 s at the bottom of the screen: KBN
  headlines, the Lone Star scanner (describing your car when you have heat),
  and a pirate station that plays songs for you once you are famous.
- **Rooftop graffiti**: whoever took a district over tags its roofs; with
  Awareness 5 your name appears ("WAS HERE"), and later what you did.
- **Dreams**: sleeping at the safehouse can bring a dream (a new Sleep hook).
  Mages dream true -- a set-up on the active job, the tower before a finale,
  green water -- and wake charged on a mana surge; a runner with a lot of
  bodies behind them has nightmares; a lost crew member visits.
- **Cyberpsychosis mail**: below 1.5 essence, messages from your own
  commlink you don't remember writing, jobs Juno never sent, and Ichikawa
  offering a neural dampener (¥4,000: phantoms half as often).

### Runners look like their trade
The player's figure follows the archetype: a street samurai in black and red
with a chrome arm and a red visor, a hooded decker with a lit wrist deck, a
face in a pale suit and tie, an engineer in hi-vis with a hard hat and a pack,
a mage in violet with the gift glowing round them. Cybereyes glint.

## Sixth pass: legible systems, towers, the top tier, and the runner's own story

Everything in `docs/NEXT.md` except day runs, plus towers, the top tier and
Torque's car work. Numbers live in `Tables.cs` unless given here.

### Towers (`Run.Tower.cs`, `LevelGen.Tower`)
- A tower is floor bands side by side on one level: the lobby, then each floor;
  stairs join each floor's core to the next, and an elevator panel (`Tile.Lift`)
  stands on every floor. The objective is on the top floor; guards grow with
  height, and secure floors also guard the stairwell door.
- Threat-5 offices and labs are towers half the time (2-4 floors); every
  threat-6 prestige job is one (4-6 floors). Pay x(1 + 0.3 x floors),
  karma +2 a floor. The top secure floor (two of them from 3 floors) needs a pass.
- The elevator: hack the lobby panel, then it serves the public floors (E up,
  Shift+E down). A secure floor opens by hacking its panel from inside (up the
  stairs) or by a Matrix override from below: three pass keys in the security
  host (a deck and Electronics 3, or Electronics 5). Waves arrive by the lift
  on your floor. Owning the helicopter adds a roof pick-up.
- The Iron Spire finale is a 3-floor tower; Wyrm chapter 4 has 4 floors.

### The top tier and prestige
Prestige (street rep 18, or 14 after the Spire) opens the elite stock and
threat-6 jobs. Weapons: Kodiak anti-materiel rifle, Incinerator flamethrower,
Screech sonic rifle, Yakusoku homing missiles, Ogre assault cannon, Kagero
rail rifle, Lancer heavy laser (ignores armour). Armour up to mil-spec battle
armour (15). Chrome and bio: platelet factories, cerebral booster, pain
editor, adrenaline pump, synaptic booster, control rig II, wired reflexes II,
move-by-wire. Decks: Obsidian-class (x1.3) and the top deck (x1.7). Drones,
cars (Rhino security car), the Aerodyne Kite helicopter (free flights).

**Cyberware grades** (`Tables.Grades`): alphaware 0.8x essence at 5x the
price (trust 2 with Ichikawa), betaware 0.7x at 10x (trust 4), deltaware
0.5x at 20x (trust 5 and prestige). Bought new from the box at the best grade
Ichikawa trusts you with, or refitted later for the difference.

### Car work (`Tables.CarMods`, `Career.Ride`)
Torque's mods, per vehicle: engine (+8/16/25 % top speed), turbo (+15/30 %
acceleration), suspension (+5/10 % grip), plate (hull +25/50/100 %, top speed
-3/6/10 %), run-flat and off-road tyres, ram plate, and gadgets -- nitro
(Shift, three a drive), smoke (Q), oil (G), a weapon mount, morphing plates
(heat cools 50 % faster), ECM, nanomaintenance, a smuggling compartment.

### Money sinks
Lifestyle (¥100/400/1,500/5,000/15,000 a week, squatter to luxury: healing,
raids, TraumaLink price, karma), property (four businesses, ¥40k-250k, paying
¥1,500-10,000 a week, half when your enemies hold the street), crew retainers
(¥1,800-3,000 a week instead of a cut), power focus ranks (x1.25/1.4/1.6),
spell ranks (+15 % a rank, karma), loadout kits.

### UX
- The map is its own screen: overlays (1 turf, 2 police, 3 people), labels
  that avoid each other. Click a place to make it your **target** (or
  "Target" on the PDA's Travel tab): a route line round the checkpoints, a cyan
  arrow and minimap ring, and a TARGET line under the job. Click it again or
  right-click to clear it; arriving clears it too, and with no target the job
  leads (a package aboard always shows its drop). Kept in the save. Taxi and
  helicopter travel from the PDA.
- First-time explainers for every system, a "what changed" morning card,
  "call back later", inbox filters, shop sort and filters with comparisons
  (damage per second, noise), condition chips on the run HUD, a minimap per
  floor, text size and a colour-blind palette with shape marks, and gentle
  mode (a save each morning; death offers "wake up this morning").

### Every archetype has a verb
Face: bluff an unaware guard (Charisma + etiquette vs threat), or take a
knocked-out guard's uniform. Mage: astral projection and wards; spell ranks.
Decker: spoof a radio, loop the patrol rota from a console. Rigger: the getaway.

### Story
- **Backgrounds** (street kid, ex-corporate, ex-Lone Star, outsider shaman,
  Barrens-born): an etiquette, one contact at trust 1, choices only that past
  can take, and a four-chapter personal campaign (Story bases 16-32).
- **Crew at home**: two conversations each at the safehouse (trust 1, then 3).
- **Choices on the run** (`Run.Choices.cs`; the clock stops until you press 1
  or 2): a third of rescued hostages beg to be let go a few steps from the
  exit (the job fails; karma +3, a friend, their fate "rescued"); a third of
  datasteals find the Johnson's own dirt (pull it and Null's call follows); a
  guard below 25 % health rolls once to surrender (15 %): spared, he sends a
  floor plan later; shot, +1 notoriety each.
- **Brief voices**: each employer opens a brief their own way (three lines
  each), a Johnson adds their manner, and every brief names its client.
- **Voiced beats**: the campaign openers, midpoints and endings (19 lines,
  `line-<storylet>`), and the intro.
- **The career receipt** at death or retirement: days, jobs, money, karma,
  people saved and lost, guards spared, crew buried, stories finished,
  property, lifestyle, who holds every district, and the radio's last word.

### The intro (`Sim/Intro.cs`, `UI/IntroScreen.cs`)
A new career opens on four cards: Kessler Bay (on film: a 5 s seedance clip
made from its painting), who you were (by background), what a shadowrunner
does, and why tonight. Juno narrates each (`say-intro-*`); the words type out
under a drifting backdrop. Space, Enter or a click moves on; Esc skips.

### The police and the factions
- **Roadblocks** (`CitySim.Roadblocks.cs`): during a crackdown, five Lone Star
  checkpoints close avenue blocks (never within 14 tiles of a door you need).
  Traffic stops and is waved through. You are too, if you stop within 4 tiles
  with no heat and nobody chasing you (2 s). Within 12 tiles with any heat,
  they run your plate (heat +1) and two cruisers pull out, once per checkpoint.
  Morphing plates get you past.
- **Standing** (`Career.Standing.cs`, 0-10): every ordinary job's client is the
  target's strongest rival in that district (named in the brief). Success: the
  client +1 (+2 on their own job), the target -2. At 3, ambushes on their
  streets drop to a quarter; at 4, one job of theirs on your board at x1.25;
  at 5, their item at Juno's: Viper colours (¥6,000: no Viper ambushes or
  courier chasers), Halcyon contractor chip (¥12,000: hacks +25 %), Lotus jade
  marker (¥9,000: loot +15 %).
- **Edge** (`Run.Edge.cs`): 1 + Charisma/3 + bought (karma 15, then 30; at most
  4) a run. The last point always saves you from a killing blow (back up at a
  third of your health, 1.2 s untouchable). Spare points also slip a guard's or
  camera's detection the instant it lands, and tear out a Matrix trace.

### Tooling
- **Balance report** (`tests/SmokeTest.Balance.cs`): every archetype over three
  seeds, a runner who shops, printed at days 20/40/70. Today: about ¥55k by
  day 20, ¥150k by 40, ¥390k by 70 with ¥170k spent. The sim's win rate
  doesn't depend on archetype, so the rows differ little; money still piles up
  late.
- **Agent career** (`scripts/agent-career.sh [DAYS]`): the real UI for N days
  -- creation, every contact's door, calls on the way, the inbox, sleeping --
  through the agent harness (its `answer` command takes a dialog's first open
  choice). It found the creation screen's button off the bottom of the window.

### Calls are calls (`UI/CallView.cs`)
A call rings first: the caller ID (their picture in a round frame, or a
question mark), rings going out, a commlink trill; E or Enter answers, Esc or
"Call back later" keeps it as mail. Picked up, the caller is live on video:
their portrait with scanlines, the odd torn band, LIVE and a timer, their name
on the bar -- contacts, crew, and Pryce, Ilse and Kade Marrow (painted
`caller-*` faces). Null, the Johnsons, Veyrasz and anyone unknown call with
NO VIDEO: static, a line about why, and their voice as a level meter. Their
words type out under the picture like subtitles, with the answers below;
"Hang up" ends it. Reopened later from the inbox, a call reads as a message.


### The high end (`Run.Elites.cs`)
Hostiles that answer what the runner has learned to lean on. All numbers in
`Tables.cs` (`FirstThreat`, `Spider*`, `Cloak*`, `Slam*`, `Howl*`, `Egg*`, `Hive*`).

| Who | Where | What it does | The answer |
|---|---|---|---|
| Security spider | one at the deepest console of every threat-5+ corporate site | while he's up, every hack runs at 70 % (50 % once he's alert), a Matrix host is one notch harder; alert, every 6 s he takes back a hacked device and every 10 s re-locks a door that started locked | find him (SPIDER tag, the deck's glow) and put him down first |
| Ghost operative | Halcyon roster, threat 5+ | invisible beyond 4 tiles; shows for 1.5 s after it fires or is hit; your allies can't see it either | thermographic eyes (7 tiles), a mage's astral sight (9) |
| Bulwark | Halcyon roster, threat 5+ | 360 HP, armour 14: pistols do a quarter; a shoulder slam (35, throws you 1.8 tiles, every 3.5 s); stuns wear off three times as fast | anti-materiel rifle, laser, rail rifle, missiles, explosives |
| Barghest | Viper roster threat 4+, bounty packs threat 3+ | an ash-grey beast with ice-blue eyes (a hellhound is rust-red); a 0.7 s wind-up (a pale blue ring), then a howl that freezes everyone within 5 tiles for 1.1 s and sends every hound within 12 into a frenzy (30 % faster for 5 s) | kill it at range, or step back out of the ring |
| The Hive | a threat-6 Industrial bounty, 20 % of them (prestige), pay x1.3 | a queen in the deepest room and 6 + threat egg sacs; once roused, each sac hatches a worker after 24 s and the queen lays a new one every 11 s | fire, and speed: burn the sacs before they split |

### Polish: sites and people
- **Work you can see** (`RunView.TileLife`): a safe's dial swings while it's cracked, a
  ring fills and three tumblers light as they fall; a console scrolls code and fills a
  bar; a locker rattles; a maglock's keypad flickers through digits and sparks.
  Afterwards a safe hangs open and empty, a locker stands ajar, a console reads LOOP.
- **Machinery alive**: rack lights blink, vats bubble, a light runs along the mainframe,
  console screens flicker, lift panels blink, club floors light in waves to a beat.
- **Tiles** (`LevelArt`): walls cast a shadow on the floor beside them; every brick its
  own shade; concrete with form-tie holes and streaks; plasterboard seams and pinned
  notices; armour plate with rivet rows. Floors: oil stains, cracks and drains on
  concrete, staggered planks with knots, woven carpet with worn patches, flat puddles
  and litter outside. Furniture: wooden crates with slats and stencils or steel cases
  with clamps; desks with a chair, keyboard, papers, a mug; barrels (always red,
  always explosive) with a bung and a flame warning; racks with vents and cables; vats
  with pipes and a riveted rim; consoles with a keyboard and stool; safes with hinges,
  handle and a ticked dial; lockers with vents, handles and a name tag; doors with a
  handle, locked ones with a maglock keypad.
- **People** (`Art.Person`): soft shadows; shoulders rise and fall at rest; guns kick
  back with a muzzle flash; Viper bandanas, Lotus sashes, corporate helmets with a
  visor slit; hounds with a wagging tail and pricked ears. Several pickups at once
  stack their labels.

## Seventh pass: the run (from `docs/NEXT2.md`)

### The security ladder (`Run.Security.cs`)
Security is six named levels, not an alarm switch. The HUD shows six cells
under the objective, the level's name, and what the *next* level brings.

| Level | Name | What happens |
|---|---|---|
| 0 | Quiet | nobody knows you're here |
| 1 | On edge | the instant anyone sees you: guards notice you 20 % faster |
| 2 | Patrols up | two more patrols walk in from the edges, unalerted, between two rooms |
| 3 | Alarm | the old alarm: everyone turns towards where you were, waves every 25 s (as before) |
| 4 | Response team | 2 + threat/2 guards hold the extraction |
| 5 | Hunted | from now on they know where you are (the old after-alarm behaviour) |
| 6 | Kill team | three elite heavies (a bulwark and a ghost operative on threat-5 Halcyon sites) |

- A guard who sees you and is calling it in adds 0.25 a second (0 to 3 in
  about 8 s, the old alarm timing; 30 % faster with a spider awake). A camera
  adds 1.2 and sends the nearest guard to look (it then ignores you 6 s); a body
  found adds 0.5. Once at level 1, a level every 120 s by the clock -- unless a
  console's security loop is running.
- Nobody calling: the unreported part fades (0.15 a second) to the floor of the
  level. **A level, once reached, never falls.**
- Below level 5, alerted guards who lose you go to where they last saw you and
  **search**: that spot, then up to three rooms within 12 tiles, nearest first,
  a look round in each, 30 s at most. After a search they're jumpy for the rest
  of the run (notice you 35 % faster).
- Anything that raised the alarm directly before (a Matrix trace, a missed
  deadline) now lifts security to 3, with levels 1 and 2 happening on the way.

### Fixes
- Reinforcement waves (alarm and defend) are weighted and gated by threat like
  the site's own guards (no high-end hostiles below their first threat, no
  snipers or hounds in waves); the Moved and SecondTarget twists stay inside, on
  the objective's floor; "Never there" counts no-alarm Plants; Blackout pays
  x1.1; the run and car HUDs show the keys actually bound, prompts included
  ("Hold E" becomes your key); the gamepad's right stick opens the PDA in the
  car and Misc/Share jacks into the drone; the run HUD's player panel lays out
  from measured text, so it holds at the largest text size.
- Friends: someone you let go or got out (`friend:`) turns up four days on with
  one favour -- the plans of your next site, two medkits at Ichikawa's, or your
  plate wiped from Lone Star's system.

### The Lamplighter (a campaign: `Career.Campaign`, `Storylets.Later.Lamp`)
Opens after 10 jobs at rep 6 from day 14, through Null's data haven (Story 37-40).
A patron whose messages arrive on parking meters, billboards and vending machines,
who pays before the work is done (¥2,000 with the first message), and who calls
wearing a different borrowed face every 2.6 seconds.
1. **A dead man's deck** (Datasteal, Spire, threat 3) -- the deck of Corvin Ashe,
   a Halcyon orbital engineer dead eleven years.
2. **The dead-man key** (Retrieve, Industrial lab, 4) -- Ashe's ROM key.
   Then Null: *there's nobody at the end of the line*. It's an AI, MERIDIAN, one
   half of a mind Halcyon grew in orbit and cut in two to sell twice. The choice:
   keep working for it (+¥5,000); talk to Registrar Hale of the AI Registry
   (betray it: ¥25,000 at the end, notoriety +1); or, as a decker or with
   Electronics 4, jack in and talk to it (karma +5, and it eases the last job).
3. **Footprints** (Hack, from your own rig, 4) -- erase the trail the Registry
   follows; sold to the Registry, you plant its tracer instead.
4. **The other half** (Datasteal, a four-floor Halcyon uplink tower, 5, elite) --
   open the door to Aubade; sold, **Lights out**: burn MERIDIAN's core racks. If
   it told you its name, it sends half the guards home and hands you the plans
   (skeleton crew, schematics).
- **Whole**: the Lamplight program (not for sale: hacks +25 %, once a run it pulls
  you out of the Matrix before a trace lands), a legacy perk that starts later
  careers with it, and a city whose traffic lights are always green for you.
- **Sold**: ¥25,000, Halcyon's grudge -3, and streetlights that go out on your road.
- The Lamplighter has its own voice (calm, too even); its opener and Null's
  discovery are voiced.

## Eighth pass: the site, the street, the other crew

From `docs/NEXT2.md` (2.2, 2.3, 2.4's entry points and map, light from 3, 4.1, 4.6)
and `docs/EXPANSION.md` (the Drive hook). Runs stay at night: no day runs. Every
number is in `Tables.cs`.

### Light (`Level.LightAt`)
Each lamp lights what it can see (walls cast shadows) over its radius, full near
it, soft at the edge, on top of an `Ambient`: the night (0.08) on generated sites,
full light on the checks' hand-drawn ones. Detection runs from x0.65 in the dark to
x1.15 under a lamp, the same for every observer. At night most rooms have their
lights off (offices 45 % lit, labs and warehouses 60 %, dens 70 %, clubs 80 %; a hash
of the room, not the site's stream), and big halls hang a lamp every 8 tiles: 18-52 %
of the floor is dark, by style. A shot of yours aimed at a lamp puts it out (glass,
a little noise); blasts take out the lamps near them. The HUD says LIT, DIM or DARK.

### Rooms with a purpose (`Room.Kind`, `World/SetPieces.cs`)
Rooms get a kind by style, from their own random stream: server room, break room,
barracks, armoury, manager's office, security office, panic room, ritual room, lab
bay, loading dock, VIP booth (never a corridor). 23 hand-drawn set pieces, in the
`Site()` text format, turn and mirror to fit; plain rooms keep the style's furniture.
One thing each:

| Room | What it does |
|---|---|
| Server room | coolant pipes: rupture one for a freezing cloud (guards in it can't act, you're slowed, nobody sees through it) |
| Break room, security office | guards off duty: half the sight, half as quick to notice, until something's up |
| Barracks | sleepers: blind; wake to a loud noise within 0.6 of its reach, a quiet one within 1.6 tiles, or the alarm |
| Armoury | locked doors, a locker of kit, a grenade |
| Manager's office | a desk, a safe -- and often the target, working late |
| Panic room | armoured walls; on the alarm the target runs there and seals it (hack or blow the door) |
| Ritual room | a chalk circle with a spirit bound to it: it can't leave |

Off-duty and sleeping guards come out of the site's guard budget. Objectives
sometimes move into a room that fits (the target in the office or the booth, paydata
in the server room, the item in the armoury, evidence on the manager's desk). The
runner hears a line on walking into each room with a purpose.

### Sites that play differently (`Run.Rooms.cs`)
| Style | Its rules |
|---|---|
| Lab | a breached vat lets out toxic gas (9 a second; a den's drug vat still burns); sprinklers drown fires x3; walk into the clean room without the suit hung somewhere on site and security goes up a level |
| Office | 1 + threat/3 badge-reader doors deeper in open at a swipe of the keycard one guard carries (dropped when he goes down; hacks and charges still work); two night cleaners -- one who sees a gun runs for a phone, and in 3 s security goes up a level unless you stop them |
| Warehouse | push a row of shelving over (E beside it): whoever is in the next aisle takes 45 and is stunned 2.5 s, and everyone hears it; the loading dock's roller doors are a second way out |
| Den | dogs smell you through walls within 4.5 tiles (rain cuts it to 60 %) and come to look; 35 % of the gangers are drunk (x0.8 pace, x1.6 spread, x0.75 noticing) |
| Club | a gunshot on the dance floor carries 0.45 as far (not in a blackout); a VIP booth by the floor |

A chip on the run HUD names the site's rules.

### Ways in (`LevelPlan.Entries`, `UI/SitePlan.cs`)
The front gate, plus: a fence cut on another side of the yard (18+ tiles from the
gate, 10 from any guard's post), the dock doors, a sewer hatch into a quiet room
(dens, warehouses, labs from threat 2; 7 from any post), a tower's roof (with the
Kite only). The way out stays where it is; `Job.Entry` keeps the choice. The
briefing is two columns: the brief and legwork, and a site plan -- the footprint, or
with schematics the blueprint with the rooms' purposes and the clean room named;
with the patrol routes, the guards, their routes and the cameras' cones; the ways in
numbered, each a button with its risk.

### The street (`CitySim.Street.cs`, `Storylets.Street.cs`)
A `Drive` hook: on a real drive with nobody on you (from day 2, at most twice a day,
a look every 140 s of driving), something waits on a kerb ahead -- someone waving, a
wreck with its hazards on, a Viper toll across the lane on Viper streets. Stop beside
it and press E; closing the scene is driving on. Six of them: a hurt woman to drive
to Ichikawa, a wreck to help or rob, the toll (pay, Gang etiquette, or floor it and
have bikes after you), a kid filming your plates, a stranded runner with a job tip,
a hot package for the Docks. A passenger or package aboard (`Career.Passenger`) pays
when you stop at its kerb, and gets out overnight.

### The other crew (`Career.Rival.cs`, `Run.Rival.cs`, `Storylets.Rival.cs`)
A named crew (a boss and three runners) arrives once you have four jobs done, from
day 5. They bid on a quarter of the Retrieve, Datasteal and Eliminate jobs (R on the
board; those wait three days at most) and take the ones you let lapse. On a
contested job they come in 30-70 s into the run from the far edge as a third side:
they fight the guards and you, take the item and run for their exit, jack the
mainframe (22 s), or go for the target (their kill pays you half); a carrier who goes
down drops it. They escalate (a drone, a sniper, chrome) with every contest. The
career remembers the dead by name; the debrief and the PDA say how it stands. After
two beatings their boss calls: pay them to leave, split the city (Street; they then
send floor plans), or refuse. With two of theirs dead, Juno finds their hideout:
clear it, or kill them all, and they're finished.

### Retiring
The safehouse offers "Retire..." once a campaign is won or `Tables.RetireSavings`
(¥250,000) is put away, with no job on: the receipt and the legacy, as a legend after
a campaign, quietly otherwise.
