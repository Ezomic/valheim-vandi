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
            // world's star setting and by the biome sector, and one Meadows stretch four
            // kilometres out doubles it, so vanilla there is 20 and not 10. Scaling these points
            // the same way would make a kill worth ten there and five nearer the centre. Robbin
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
                + "better one.");
        }
    }
}
