using BepInEx.Configuration;

namespace Vandi
{
    /// <summary>
    /// Everything tunable, bound in one place so the .cfg reads as a document rather than as
    /// whatever order the code happened to need things in.
    ///
    /// Note the standing BepInEx trap: every entry is written to disk on first run and the
    /// saved value beats a new default in code. Changing a default here does nothing on a
    /// machine that has already run the plugin - edit
    /// <c>&lt;profile&gt;\BepInEx\config\ezomic.valheim.vandi.cfg</c> as part of the same
    /// change. When a config-driven change appears to do nothing in game, read the cfg
    /// before reading any code.
    ///
    /// The comments are documentation, not units. They are what somebody reads in the file
    /// instead of the README, so they carry the reasoning and the consequences - including
    /// the ones that will look like a bug.
    /// </summary>
    internal static class VandiConfig
    {
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<bool> Verbose;

        internal static ConfigEntry<float> StarChancePerKill;
        internal static ConfigEntry<float> StarChanceCap;
        internal static ConfigEntry<string> BossBiomes;

        internal static ConfigEntry<bool> HarderBosses;
        internal static ConfigEntry<int> BossStarCap;
        internal static ConfigEntry<bool> BossSpecials;
        internal static ConfigEntry<float> FanFirstDelay, FanCooldown, FanRearSeconds, FanSeconds, FanSpread, FanThrowSpeed;
        internal static ConfigEntry<int> FanSkeletons;
        internal static ConfigEntry<float> BossHealthMultiplier, AddChipPercent, AddChipCapPercent;
        internal static ConfigEntry<float> AddIntervalStart, AddIntervalEnd, AddRingMin, AddRingMax;
        internal static ConfigEntry<int> AddMaxAlive;
        internal static ConfigEntry<bool> BossPace;
        internal static ConfigEntry<float> BossMovePercentPerStar, BossAttackPercentPerStar;
        internal static ConfigEntry<string> BossPaceOverrides;

        internal static ConfigEntry<bool> PullWild, PullRelease;
        internal static ConfigEntry<float> PullRadius;
        internal static ConfigEntry<int> PullPerWave, PullMaxPerFight;

        internal static ConfigEntry<int> DoubleAtKills;
        internal static ConfigEntry<string> DoubleBosses;
        internal static ConfigEntry<string> NeverDouble;

        internal static ConfigEntry<bool> ShowCompendiumPage;

        internal static void Bind(ConfigFile cfg)
        {
            // Every mod here has one, and it means the same thing every time: loaded, bound,
            // patched, and deciding nothing. Not "unloaded" - a plugin cannot unload itself,
            // and a switch that pretends otherwise is a lie somebody will debug.
            Enabled = cfg.Bind("Vandi", "Enabled", true,
                "Off leaves the plugin loaded and changing nothing.");

            // Not synced by intent - see the plugin. A diagnostic flag is personal, and a
            // host turning on someone else's logging is not a thing anybody asked for.
            Verbose = cfg.Bind("Vandi", "Verbose", false,
                "Write every star roll this changes, and every kill it credits, to "
                + "BepInEx/LogOutput.log. Off unless something looks wrong; a busy zone "
                + "rolls this many times a second.");

            // Percentage points added to the game's own roll, not a multiplier. A multiplier
            // would make the first kill worth almost nothing and the fifth worth a great deal,
            // and the thing being promised is "the biome you have beaten gets meaner", which is
            // a flat progression a player can feel after one kill.
            //
            // Flat against the land as well. In 1.0 the game's roll is already multiplied by the
            // world's star setting and by the biome sector. In one Meadows stretch four
            // kilometres out the game rolled 20, not 10, on a world with no star setting changed,
            // which leaves the sector doubling it (Stars.Raise has how that was worked out).
            // Scaling these points the same way would make a kill worth ten there and five
            // nearer the centre. Robbin
            // kept them flat on 2026-09-28, so the text below promises points added and never a
            // total, because the total depends on where the creature spawns.
            StarChancePerKill = cfg.Bind("Stars", "StarChancePerKill", 5f,
                "Percentage points added to a creature's star chance for each time you have "
                + "killed that biome's boss. They go on top of whatever the game already rolls "
                + "where the creature spawns: 10 in an ordinary stretch of a default world, "
                + "higher in parts of the world far from the centre, lower on a world set to "
                + "fewer stars. It is the chance of gaining a star that moves, never the number "
                + "a creature can have, so two stars stays the ceiling.");

            StarChanceCap = cfg.Bind("Stars", "StarChanceCap", 25f,
                "The most that can be added, however many times you kill the boss. At the "
                + "default five a kill this is reached at five kills, and a fully farmed biome "
                + "then sits 25 points above the game's own roll there: 35 where the game rolls "
                + "10, more where the land already raises it. Past that the biome stops being a "
                + "place you visit and becomes a place you avoid, which is the opposite of the "
                + "point.");

            // Boss keys rather than prefab names, because the defeat key is what the game
            // itself writes when the boss dies and what every other mod here already reads.
            // The spelling is deliberately identical to Vaettir's hod jib and Utangard's
            // gate: two mods disagreeing about which boss owns which biome would be two
            // mods giving one player different answers out of one set of keys.
            BossBiomes = cfg.Bind("Stars", "BossBiomes",
                "defeated_eikthyr:meadows, defeated_gdking:blackforest, "
                + "defeated_bonemass:swamp, defeated_bonemass:ocean, "
                + "defeated_dragon:mountain, defeated_goblinking:plains, "
                + "defeated_queen:mistlands, defeated_fader:ashlands, "
                + "defeated_fader:deepnorth",
                "boss:biome, comma separated. Killing that boss raises the star chance in "
                + "that biome and nowhere else, so a biome whose boss you have never beaten "
                + "is exactly as the game ships it. A boss may own more than one biome, "
                + "which is how Bonemass covers the Ocean and Fader the Deep North - the "
                + "same pairing Utangard gates with.");

            HarderBosses = cfg.Bind("Bosses", "HarderBosses", true,
                "A boss you have killed before comes back with stars on it when you summon "
                + "it again. Off leaves bosses exactly as the game makes them and keeps the "
                + "star half of the mod, which is the half the biome feels.");

            BossStarCap = cfg.Bind("Bosses", "BossStarCap", 2,
                "The most stars a summoned boss can gain, at one star per repeat kill. Two "
                + "is what a creature can reach in vanilla, and a two star boss hits hard "
                + "enough that a third would be asking for a different party rather than a "
                + "better one. Holds for what your kills earn. Stars another mod asks for on top, "
                + "such as a Hausi sash at the altar, may pass it.");

            BossSpecials = cfg.Bind("Bosses", "BossSpecials", true,
                "A boss that arrives with stars also uses its own special moves. Bonemass rears "
                + "up and throws skeletons in a fan. Off leaves every boss with only the moves "
                + "the game gives it. A boss with no stars never uses one either way.");

            FanFirstDelay = cfg.Bind("Bosses", "FanFirstDelay", 20f,
                "Seconds from the boss appearing until its first fan. Slow on purpose, so the "
                + "opening of the fight is still about the boss.");

            FanCooldown = cfg.Bind("Bosses", "FanCooldown", 30f,
                "Seconds between one fan starting and the next being allowed.");

            FanRearSeconds = cfg.Bind("Bosses", "FanRearSeconds", 1.5f,
                "How long Bonemass rears up before the first skeleton goes. This is the warning: "
                + "nothing is thrown until it is over, so it should stay long enough to read.");

            FanSeconds = cfg.Bind("Bosses", "FanSeconds", 5f,
                "Seconds over which the skeletons are thrown, one after another.");

            FanSkeletons = cfg.Bind("Bosses", "FanSkeletons", 3,
                "Skeletons in one fan, spread evenly across the angle below.");

            FanSpread = cfg.Bind("Bosses", "FanSpread", 40f,
                "Degrees either side of the line to the target that the outer skeletons go.");

            FanThrowSpeed = cfg.Bind("Bosses", "FanThrowSpeed", 14f,
                "How fast a skeleton leaves the boss, metres a second along the ground.");

            BossHealthMultiplier = cfg.Bind("Bosses", "BossHealthMultiplier", 1.5f,
                "Extra health a summoned boss with stars gets, on top of what its stars already "
                + "give (a star doubles it, two stars triple it). It pays for the adds: with the "
                + "cap below at a third, the adds can take off at most what this added, so the "
                + "player still deals the health the stars alone would have asked for. 1 is off.");

            AddChipPercent = cfg.Bind("Bosses", "AddChipPercent", 3f,
                "Percent of the boss's full health taken off when one of its own adds dies.");

            AddChipCapPercent = cfg.Bind("Bosses", "AddChipCapPercent", 33f,
                "The most, in percent of the boss's full health, that its adds dying can take "
                + "off in a whole fight. The adds shorten the fight; they never replace it.");

            AddIntervalStart = cfg.Bind("Bosses", "AddIntervalStart", 25f,
                "Seconds between adds sent in at the start of the fight, boss at full health. "
                + "Slow on purpose: the opening is about the boss.");

            AddIntervalEnd = cfg.Bind("Bosses", "AddIntervalEnd", 8f,
                "Seconds between adds when the boss is nearly dead. The gap shrinks evenly between "
                + "the two as it loses health, so the fight ends crowded.");

            AddMaxAlive = cfg.Bind("Bosses", "AddMaxAlive", 6,
                "The most adds from this wave alive at once. The throw is separate and not counted.");

            AddRingMin = cfg.Bind("Bosses", "AddRingMin", 18f,
                "Closest to the boss, in metres, that an add is sent in from.");

            AddRingMax = cfg.Bind("Bosses", "AddRingMax", 30f,
                "Furthest from the boss, in metres, that an add is sent in from. They arrive hunting you, "
                + "so this is how long they take to reach you.");

            BossPace = cfg.Bind("Bosses", "BossPace", true,
                "A summoned boss with stars moves and attacks faster for each star. Off leaves its "
                + "speed exactly as the game makes it. A boss with no stars is never touched either way.");

            BossMovePercentPerStar = cfg.Bind("Bosses", "BossMovePercentPerStar", 5f,
                "Percent added to a boss's walk, run, swim and flight speed for each star, so two "
                + "stars at the default is 10 percent faster. The stars counted are capped by "
                + "BossStarCap. Turning speed is never changed.");

            BossAttackPercentPerStar = cfg.Bind("Bosses", "BossAttackPercentPerStar", 5f,
                "Percent added to the speed of a boss's attack animations for each star. The whole "
                + "swing plays faster, so the warning before a hit is shorter by the same fraction "
                + "as the recovery after it; a small number is the point. Other animations are never retimed.");

            BossPaceOverrides = cfg.Bind("Bosses", "BossPaceOverrides",
                "defeated_dragon:0.5:1, defeated_goblinking:0:1, defeated_queen:0:1",
                "boss:moveMultiplier:attackMultiplier, comma separated, keyed by defeat key like "
                + "BossBiomes. The multipliers scale the two percents above for that boss alone: 0 "
                + "leaves that speed alone, 2 doubles the bonus. A flat number is wrong for some "
                + "bosses. Moder flies, so the same percent of a flight speed reads as far more, "
                + "and Yagluth and the Queen barely walk, so a faster walk would only be a "
                + "faster shuffle. A boss not listed gets multiplier 1 for both.");

            PullWild = cfg.Bind("Bosses", "PullWild", true,
                "During a starred boss fight, wild creatures of the boss's own biome that are already "
                + "near it are drawn in and hunt you, on top of the adds it sends. They are the game's "
                + "own creatures: they keep their loot and their deaths do not hurt the boss. Only wild, "
                + "living, non-boss creatures that are not already hunting; tamed creatures, players "
                + "and fish are left alone. Off leaves the biome where it stands.");

            PullRelease = cfg.Bind("Bosses", "PullRelease", true,
                "When the fight ends the creatures it drew in stop hunting you and go back to being wild. "
                + "Off leaves them hunting, which the game saves, so they keep coming after a reload.");

            PullRadius = cfg.Bind("Bosses", "PullRadius", 60f,
                "Metres from the boss within which a wild creature can be drawn in.");

            PullPerWave = cfg.Bind("Bosses", "PullPerWave", 2,
                "The most creatures drawn in each time an add is due, nearest first, so a camp of forty "
                + "arrives a couple at a time. The gap between waves is the adds' own, AddIntervalStart "
                + "shrinking to AddIntervalEnd as the boss weakens.");

            PullMaxPerFight = cfg.Bind("Bosses", "PullMaxPerFight", 8,
                "The most creatures drawn in over one fight, however many are near.");

            SpecialRegistry.Bind(cfg);

            // Kills, not stars: Vandi stores kills, and BossStarCap is a separate decision. Each
            // repeat kill adds a star, so the third kill is the one summoned at two stars, which
            // is the hardest fight the default gives and what the reward was asked for.
            DoubleAtKills = cfg.Bind("Metal", "DoubleAtKills", 3,
                "How many times you must have killed a biome's boss, as Vandi counts them, for the "
                + "metal of that biome to drop double when YOU break the deposit or kill the "
                + "creature. The default is the kill that was summoned at two stars. It does not "
                + "move with BossStarCap or HarderBosses. 0 turns the whole thing off.\n"
                + "Doubling follows the metal, not the place: copper is the Elder's metal wherever "
                + "it drops, so a Mistlands deposit of copper doubles for the Elder's two star "
                + "player and the Queen adds nothing to it. Iron scrap, the scrap piles and a "
                + "fuling's black metal scrap count as their biome's metal. Bars and crafted items "
                + "never do. The blow that breaks the rock or kills the creature decides who "
                + "gets it, and in a group a deposit Malmr breaks goes to the player whose swing "
                + "filled the bar. It happens when the drop is made, never on pickup, so a friend "
                + "cannot mine for you and hand you doubled ore.");

            DoubleBosses = cfg.Bind("Metal", "DoubleBosses",
                "defeated_gdking, defeated_bonemass, defeated_dragon, defeated_goblinking, "
                + "defeated_fader",
                "The boss keys whose biome's metal can drop double. Eikthyr and the Queen are left "
                + "out: the Meadows have no metal, and the Mistlands' copper and iron are the "
                + "Elder's and Bonemass's, so the Queen has nothing of her own to double. A key "
                + "must also be listed in BossBiomes, which is where its biome comes from.");

            NeverDouble = cfg.Bind("Metal", "NeverDouble", "BloodGoldOre, BloodGold",
                "Prefab names that never drop double, even from a boss that is on the list. "
                + "Bloodgold is here because the Deep North's boss is not settled: Fader owns it in "
                + "BossBiomes for now, and doubling a metal for a boss that may turn out to be "
                + "somebody else's is the sort of thing that cannot be taken back from a player. "
                + "Remove it once the Deep North has its own boss.");

            // Personal, and declared so in the plugin: a switch for what one player's screen draws
            // is not the host's to take away, and Core would otherwise impose the server's copy of it.
            ShowCompendiumPage = cfg.Bind("Display", "ShowCompendiumPage", true,
                "Add a Vandi page to the compendium's list, beside Logs and Active Effects: every "
                + "boss grouped by its biome with your own kill count, what each kill has added to "
                + "the star chance there, the stars the boss comes back with, and, when Malmr is "
                + "installed, which of its metals wait for which kill. It only reads what the "
                + "world already holds. Off removes the page and changes nothing else.");
        }
    }
}
