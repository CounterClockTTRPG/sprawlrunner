namespace Sprawl;

/// <summary>The Help pages' words, in reading order (see <see cref="Help"/>). Keys as {Act.Name}; pictures by walkthrough shot.</summary>
public static partial class Help
{
    public static readonly HelpTopic[] Topics =
    {
        new("career", "Your career", "02_create", new[]
        {
            "One runner, one life. Every career is ironman: it saves after every run and at the safehouse, and there is no going back. Three slots sit side by side on the title screen, so you can keep three runners going at once. Starting over in a taken slot asks first.",
            "Pick an archetype -- Street Samurai, Decker, Face, Combat Engineer or Street Mage -- and a background: street kid, ex-corporate, ex-Lone Star, outsider shaman or Barrens-born. The background gives you an etiquette, one contact who already knows you, and a story of your own.",
            "Jobs pay nuyen and karma. Nuyen buys gear, chrome, cars and a better place. Karma buys attributes and skills. Go down on a run without a TraumaLink contract and the career is over. You can also retire once you've won a campaign or put ¥250,000 away. Gentle mode, ticked at creation, saves each morning and lets you wake up that day instead.",
        }),
        new("city", "The city and driving", "03_city", new[]
        {
            "Kessler Bay is five districts: the Docks, the Barrens, the Neon Strip, Industrial and the Spire. Drive with {Act.Up} and {Act.Down} for throttle and brake, steer with {Act.Left} and {Act.Right}, and drift with {Act.Handbrake}. {Act.Horn} sounds the horn and scatters people on foot. Hard crashes damage the car, and a wreck gets towed home for a fee.",
            "{Act.Map} opens the map. Click a place to make it your target and you get a route line, a cyan arrow and a ring on the minimap. Click it again, or right-click, to clear it. Your active job's site is the magenta pin marked JOB. To go into a place, stop on its door and press {Act.Interact}. Nobody lets you in with chasers on your tail.",
            "Things wait at the kerb: someone waving, a wreck with its hazards on, a toll across the lane. Stop beside one and press {Act.Interact}. Some leave you with a fare, a passenger or a package. Stop at its kerb to get paid.",
        }),
        new("contacts", "Contacts and shops", "08_shop_fence", new[]
        {
            "Your contacts each have a door in the city:\n- Juno Vance, the fixer, has work, intel and hires.\n- Doc Ichikawa does chrome, patch-ups and TraumaLink.\n- Big Sal sells guns, armour, ammo, grenades and medkits.\n- Torque has cars, drones, car mods and repairs.\n- Null sells decks and programs and buys paydata.\n- Old Wen buys the loot you carry out.\n- Madame Sabine sells spells, to the Awakened only.\n- Sgt. Dace of Lone Star can make your file go away.\n- Mara Quill of KBN can sell your story or kill one.",
            "Trust, from 0 to 5, grows with every job you finish for a contact, and with what you spend there. Every point takes 4 % off their prices (Charisma takes off more) and adds 8 % to the pay on their jobs. At trust 2 they do you a favour every five days. At trust 3 they offer work a threat above your rep.",
            "Each contact has a story of their own in three parts, opening at trust 1, 2 and 3. Finish it for a reward you can't buy anywhere else.",
        }),
        new("jobs", "Taking jobs", "05b_terminal_story", new[]
        {
            "The job board is the terminal at your safehouse. Each line shows the job type, who's paying, the district and site, the threat (one to five marks), the pay, the karma and how many days it stays up. Click a job, or type 'info N', for the brief, then 'accept N'. You can only have one job active at a time. 'drop' walks away from it and costs a point of rep.",
            "The letter at the start of a line tells you what kind of job it is:\n- T is the tutorial.\n- S is the main story.\n- C is a contact's own story.\n- ! is someone's personal business.\n- J is a Mr. Johnson, who negotiates when you accept: deal, haggle once, or walk.\n- R is a job a rival crew wants too.",
            "A violet line reading 'moves the story on' marks the next chapter of a campaign. The board only shows threats up to what your street rep allows. Rep rises with every job you finish and falls with every job you fail.",
        }),
        new("briefing", "The briefing", "10_briefing", new[]
        {
            "Once you accept a job, its site is marked on your map. Drive there and go in at the door, and the fixer's file opens: the brief, who the client is, and the conditions on the job, such as a blackout, a shift change or a deadline.",
            "The site plan shows the footprint of the place. With schematics it becomes a blueprint that shows what each room is for. With the patrol routes it shows the guards, where they walk and where the cameras look. Each way in is a numbered button with its risk: the front gate, a cut in the fence, the dock doors, a sewer hatch.",
            "Legwork is help you buy before the run. You can bribe a guard to stay home, buy the patrol routes, cut the power, or get Juno's cleaner to unlock the doors from inside. Pick your way in, and go.",
        }),
        new("stealth", "Staying unseen", "12_run_inside", new[]
        {
            "You only see what's in your line of sight. Places you've been stay as a dim memory, and the rest is black. The same walls block the guards, so if you can see them, they could see you. Hold {Act.Sneak} to sneak: you move slower, your footsteps are silent, and guards are half as quick to notice you.",
            "Light matters. The HUD says LIT, DIM or DARK. Guards notice you about a third slower in the dark and faster under a lamp. Shoot a lamp out to darken a room. A guard who sees you fills a meter: part-way he gets suspicious and comes to look, and full he's alert. Gunshots, explosions and running carry. Cameras call it in.",
            "{Act.Whistle} whistles to lure a nearby guard to where you stand. A melee hit on someone who isn't alert does triple damage, so come up from behind with your melee weapon ({Act.Weapon3}). A guard who finds a body raises the alert, so carry bodies off with {Act.Interact}.",
        }),
        new("alarm", "Alarm and getting out", "13_run_fight", new[]
        {
            "Security on a site has levels, shown as six cells under the objective: Quiet, On edge, Patrols up, Alarm, Response team, Hunted and Kill team. A guard calling you in, a camera spotting you, or a body found pushes it up. Once anyone is on edge, it also climbs a level every two minutes, unless a console loops it. A level, once reached, never falls.",
            "At Alarm, reinforcements arrive in waves every 25 seconds. Later levels put a team on your exit, let them track you wherever you are, and finally send a kill team.",
            "The objective line at the top tells you what's left to do. When it's done, walk into the extraction zone. Leaving early fails the job: hold {Act.Interact} in the zone, or bail out from the pause menu. Failing costs rep and pays nothing. A job done with no alarm raised pays 25 % more.",
        }),
        new("fighting", "Fighting", "40_vfx_guns", new[]
        {
            "You carry two guns and a melee weapon: {Act.Weapon1} for the primary, {Act.Weapon2} for the secondary and {Act.Weapon3} for melee, or use the mouse wheel. Aim with the mouse, fire with left mouse, and reload with {Act.Reload}. Ammo never runs out, but magazines do.",
            "Hold right mouse to see a grenade's arc, and let go to throw it. {Act.Grenade} switches to your next grenade type: frag, flashbang, EMP, incendiary or breach charge.",
            "Every wall has hit points. Bullets chip walls, blasts knock them down, and a breach charge makes a door where there wasn't one. Keep tight behind low cover such as crates and desks: you shoot over it, and it takes the hits aimed at you. Red barrels explode.",
            "{Act.Dash} dashes a short way, and bullets pass through you while you dash. {Act.Medkit} uses a medkit. Medkits heal less the more chrome you carry.",
        }),
        new("loadout", "Loadout and kits", "07_loadout_mage", new[]
        {
            "The safehouse's Loadout tab decides what you take on a run:\n- your primary and secondary guns and your melee weapon\n- the armour you wear\n- the ammo in each gun: standard, gel (knocks out), APDS (armour-piercing) or explosive\n- the drone you take, if you own one",
            "Chrome with an ability goes on {Act.AbilityQ} and {Act.AbilityF}. A Street Mage picks three spells for {Act.Spell1}, {Act.Spell2} and {Act.Spell3}.",
            "Save a setup as kit A or kit B and switch to it with one click: a quiet kit for sneaking, a loud one for a fight. Using a kit only brings back what you still own.",
        }),
        new("chrome-magic", "Chrome, magic and Edge", "23_class_mage", new[]
        {
            "Doc Ichikawa sells cyberware: eyes that see further, smartlinks, dermal plating, muscle. Every implant costs Essence, which starts at 6. Low Essence weakens medkits, and below 1.5 you start seeing things. Some chrome gives you an ability: wired reflexes give Overclock (time slows for 4 s), ruthenium skin gives Cloak (unseen for 5 s), and a cyberdeck gives Hack (turn a turret or drone, loop a camera, open a door from a distance).",
            "Only the Street Mage is Awakened. Spells come from Madame Sabine, and each has its own cooldown. Spell power grows with Logic, and every point of Essence lost to chrome dims it.",
            "Edge is a few points a run to cheat fate. The last point always saves you from a killing blow: you get up at a third of your health. Spare points also let a guard or camera look straight through you the instant it would have spotted you, and tear a Matrix trace off you. Charisma gives more points, and you can buy more with karma on the Runner tab, up to 4.",
        }),
        new("matrix", "The Matrix", "13b_matrix", new[]
        {
            "To jack in, press {Act.Interact} at a site's mainframe, or at your own rig in the safehouse for Null's Hack jobs. The host is a grid of nodes. Move along its lines with {Act.Up}, {Act.Down}, {Act.Left} and {Act.Right}. Hold {Act.Interact} on a datastore to pull it, or on a control node to take it: loop the cameras, open the maglocks, turn the turrets or stop the alarm clock.",
            "IC hunts you through the host: {Act.Dash} or left mouse spikes it. {Act.AbilityQ}, {Act.AbilityF} and {Act.Reload} run your loaded programs, and {Act.Grenade} purges one. The trace fills every second you're inside and with every loud thing you do. When it's full you get dumped out, take dumpshock, and the site's alarm goes off.",
            "Optional paydata sells to Null or Wen, but it takes up deck memory. Programs share that memory too, so load them on the Deck tab with care. Jack out from the entry node. Your body stays on site and is exposed, and a hit pulls you out.",
        }),
        new("debrief", "After the job", "14_debrief", new[]
        {
            "The receipt prints the run line by line: the payment, the ghost and no-kill bonuses, karma, street rep, contact loyalty, faction grudges, and any cash found. A day passes with every run.",
            "Loot doesn't sell itself. Take it to Old Wen. Goods carried out of a loud job stay hot for three days: they fetch less, and selling them draws heat.",
            "Ending a run below 30 % health is an injury: 10 % off your max health, up to three injuries. Sleep or the doc mends them. If you go down with a TraumaLink contract, a trauma team pulls you out: the job fails and a fifth of your cash goes on the fee.",
            "Each job against a faction adds to their grudge, unless they never knew you were there (no alarm, no kills). At 3 their sites get elite security. At 5 they send hitters after you.",
        }),
        new("heat", "Heat and the street", "46_vfx_road", new[]
        {
            "Heat is shown in stars, 0 to 5. Loud runs, hit-and-runs and reckless driving add to it. Every star puts a Lone Star cruiser on you, up to four. Stop beside one for two seconds and you're busted: a fine, and any package aboard is gone.",
            "Heat cools while no patrol is near, and a new day clears it. Lone Star cruisers won't follow you down the narrow lanes.",
            "Gangers come after you after rich or loud runs, on courier jobs, and when a faction's grudge runs high. They drive and they shoot at your car. Get far enough away and stay out of sight for a few seconds and they give up. You can't go through a door with anyone on your tail.",
        }),
        new("safehouse", "The safehouse", "07b_upgrades", new[]
        {
            "Home has six tabs:\n- Terminal: the job board and your mail.\n- Runner: spend karma on attributes, skills, Edge and etiquettes.\n- Loadout: what you carry on a run.\n- Deck: your cyberdeck and its programs.\n- Upgrades: your lifestyle and improvements such as the Matrix rig, med bay, armory and training room.\n- Garage: pick which car to drive, and patch it up.",
            "'Sleep until tomorrow' starts a new day. The board refreshes, heat clears, injuries mend (how many depends on your lifestyle) and the career saves.",
            "Rent is charged a little every morning, by lifestyle, from squat to luxury. A better lifestyle mends more while you sleep, but fall behind and the landlady gives your address to Lone Star.",
        }),
        new("story", "Story, mail and the PDA", "18_pda_inbox", new[]
        {
            "{Act.Pda} opens your PDA in the city and at the safehouse. Its tabs are Inbox, Travel, Contacts, Factions, City, Stats, Street cred and Journal. It opens on the Inbox when something new has come in.",
            "Mail arrives with choices, and some messages ring as calls once you're back on the street with nobody after you. Scenes play out as you walk into a place. A choice can need money, an etiquette or a contact's trust, and it says what. What you choose sticks: people remember, come back, and send help or hitters.",
            "Campaigns run in chapters across the board, starting with Juno's story after your first few jobs. The city moves without you too. Factions fight over districts, which the City tab shows, their plans tick forward, and every job you take pushes one of them back.",
        }),
    };
}
