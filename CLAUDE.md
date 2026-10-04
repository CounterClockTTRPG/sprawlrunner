# Sprawlrunner

An overhead cyberpunk action roguelike in Godot 4.7 (.NET / C#), inspired by
Shadowrun (original names and setting). Drive Kessler Bay, visit contacts,
take jobs from the safehouse terminal, run them on foot in real time
(destructible sites, field of view, stealth, guns, grenades, cyberware), and
spend the pay and karma on attributes, skills, chrome, gear, cars and the
safehouse. One ironman career. Same machine setup as `../warlords`: Windows
Godot and `dotnet.exe` driven from WSL. Design and every number:
`docs/DESIGN.md`, `src/Sim/Tables.cs`.

## Commands

    ./play.sh                   play
    ./play.sh --build           compile only (dotnet, ~5 s)
    ./play.sh --check           headless self-check -- run after every change
    ./play.sh --check --only X  just the sections whose name contains X (Bot plays whole jobs)
    ./play.sh --shots [X]       windowed walkthrough, writes shots/*.png -- look at them
                                (X: title, city, terminal, place, map, run, or a job type)
    bash scripts/assets.sh      generate missing portraits/banners/voices via ../assetgen
                                (and the intro clip in assets/video); --list, --force NAME;
                                needs its Replicate token
    bash scripts/agent-career.sh [DAYS]   a scripted N-day career through the real UI (agent mode)
    ./play.sh --agent           remote control for an agent playtester; drive it with
                                scripts/agent.sh "state; drive fixer; key E; ui" ('help' lists
                                commands). Own career file (user://agent_career.json).

Machine paths default in `scripts/common.sh`; override in `.env` (see `.env.example`).
Sound is off until turned on in Settings. Settings live in `user://sprawlrunner.json`, the career in `user://career.json`.
`--check` and `--shots` use a scratch profile and never touch them.

## Layout

- `src/Sim` -- the rules, no Godot nodes. `Career` (+ `.Shop`, `.Jobs`) is the whole
  persistent state as plain data (a save is it as JSON): derived stats, shops, job
  board, debriefs. `City`/`CitySim` -- the street grid, lots and places, car physics,
  traffic, pedestrians. `Level` -- tiles with hit points, shadowcast FOV, LOS, tile
  walks, distance fields. `Run` (+ `.Player`, `.Combat`, `.Ai`) -- one job on foot:
  shots, grenades, blasts, fire, noise, detection, alarm and waves, objectives.
- `src/World/LevelGen.cs` -- sites from a job seed: yard, BSP building, doors on every
  split, vaults, furniture, objectives, defenders, loot; reachability is enforced.
- `src/View` -- `CityView` (street chunks, leaning buildings, neon, traffic),
  `RunView` (tile chunks repainted on change, lights, decals, entities, fog texture,
  event-driven sound/particles/shake), `LevelArt`, `Art` (figures, cars), `Fx`.
- `src/UI` -- `CityScreen` (driving + HUD + minimap), `PlaceScreen` (safehouse tabs,
  contact shops, briefing), `Terminal`, `RunScreen` (input + HUD), `Screens`
  (title, creation, settings, debrief, modals), `UITheme` (palette, fonts, `DrawPanel`,
  `DrawSegBar`), `GrimeBox` (the worn frame every panel and button uses), `Dressing`
  (stamps, tags, receipt paper, CRT glass, commlink casing), `ScreenFx` (the full-screen
  dirty-monitor shader; `Interfere`/`Jolt` from runs).
- `src/Core` -- `Root` (screen flow, CLI flags), `Profile`, `Sfx`/`Music`/`AudioBuses`
  (all synthesised), `Assets` (the generated pictures and voices, read as bytes).
- `assets/` -- `art/*.webp` (portraits, briefing banners, intro backdrops), `voice/*.ogg`
  (contact greetings, story beats, the intro's narration) and `video/*.ogv` (the intro's
  opening shot, Theora), each with the `manifest.txt` that `scripts/assets.sh` generates
  them from.
  `.gdignore` keeps Godot's importer out.
- `tests/SmokeTest*.cs` -- the self-check, including a bot that plays generated jobs and
  a balance report (`SmokeTest.Balance.cs`, printed every run).
  `src/Dev/ShotRunner.cs` -- the walkthrough.
- Story and the living city: `Sim/Story.cs` (the storylet engine: hooks, mail, calls, scenes,
  choices), `Sim/Storylets*.cs` (every storylet, by theme), `Career.World` (turf, faction clocks,
  happenings, rent, fame, the aftermath of a job), `Career.Crew`, `Career.Campaign` (the Spire
  finale, endings, Toxic Tide, The Cut, The Wyrm's Ledger), `Career.Johnsons` (recurring Johnsons, loot
  prices), `Career.Contacts` (contacts hurt and lost), `Career.Legwork`, `Career.Streets` (radio, graffiti),
  `Run.Jobs` (Investigate, Plant, Defend, Escort, Hack, the TraumaLink HTR team, legwork). Storylets draw their own
  seeded randomness, never `Career.Rng`. `UI/StoryUi.cs` shows one; the PDA has the Inbox and City tabs.
- More `Sim`: `CitySim.Pursuit` (heat, police, gangers, courier jobs), `Matrix` (the decking
  minigame), `Autopilot` (driving to a door, used by the agent harness and chasers).
  `World/HubGen` builds the walkable markets. `Core/Controls` is the input layer
  (rebindable actions, gamepad, agent isolation). `src/Dev/AgentRunner.cs` -- the agent harness
  (input via `Input.ParseInputEvent`, text state with an honest ASCII map, autopilots;
  driving uses `Sim/Autopilot.cs`, which the self-check also tests).

## Rules of the code

- One `.tscn`; everything is built in code. Art, sound and music are generated in code,
  with one exception: the painted portraits, briefing banners and spoken greetings in
  `assets/`, made through `../assetgen` and committed at game size. They are optional --
  every caller falls back to procedural art or silence via `Assets.Picture`/`Voice`
  returning null -- and the `GeneratedAssets` check ties the manifests to `Tables`.
  Never fetch at runtime; nothing in the game touches the network.
- Gameplay lives in `Sim` and never touches Godot (it uses `System.Numerics` as `V2`);
  runs report through `Run.Events`, which the view drains for sound and effects.
- HUD panels draw through `UITheme.DrawPanel`, never a bare `DrawRect` pair, so they match
  the themed screens. Grime goes on frames and backgrounds, never under figures.
- Tile changes go through `Level.Set`/`Damage` so versions move and chunks repaint.
- The same opacity rules drive the player's FOV and enemy sight.
- Rules checks build tiny hand-drawn sites (`Site(...)`) where the answer is certain.
- In agent mode (`AgentRunner.Active`) all input goes through `Controls`; never poll
  `Input` directly or the playtester's game will react to whoever is at the keyboard.
- Commit messages carry `(AI-assisted)` / `(AI-generated)`.
