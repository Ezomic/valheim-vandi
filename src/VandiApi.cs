namespace Vandi
{
    /// <summary>
    /// What another mod may ask Vandi, and so far the only thing: how many times a player has
    /// killed a boss.
    ///
    /// It exists for Malmr, which opens a metal's veins only once its biome's boss has been
    /// beaten at one star - Robbin's rule of 2026-09-26. Vandi brings a boss back one star
    /// harder for each repeat kill by the same summoner, so "beaten at one star" is the second
    /// kill, and the second kill is a count this mod already keeps.
    ///
    /// <b>A method, not a key layout.</b> The count lives in a global key,
    /// <c>vandi_p_&lt;playerid&gt;_&lt;bosskey&gt;</c>, and the obvious shortcut for another mod is
    /// to read that key itself. That works until the day this mod renames the prefix or moves
    /// the record somewhere else, and then the other mod does not break: GetGlobalKey answers
    /// "not set" for a key nobody writes any more, so it reads zero kills for everybody, keeps
    /// every metal shut, and says nothing. Behind a method, the layout is this mod's own
    /// business again, and a rename that forgets this file fails the build instead of a
    /// player's evening. That is why Kills stays internal and this is the door.
    ///
    /// Everything here only reads. Writing a kill is the altar's and the corpse's job, on the
    /// machines that have the answer - see Summon - and a second writer would be a second
    /// story about the same evening.
    ///
    /// <b>The record is answered whether or not Vandi is Enabled.</b> A kill is a fact about
    /// the world, and the switch means "decide nothing new", not "forget". Hiding the record
    /// while the switch is off would shut every metal Malmr had opened the moment somebody
    /// turned Vandi off to look at something else.
    ///
    /// What a count means, precisely: the number of times the world has credited that player
    /// with that boss, which is once per kill of a boss THEY summoned at an altar, with Vandi
    /// running on the machines that made the offering, spawned the boss and owned it when it
    /// died. A boss killed before Vandi was installed, or summoned by somebody else, is not in
    /// it. The count keeps climbing past the star cap; it is a tally, not a star level.
    /// </summary>
    public static class VandiApi
    {
        /// <summary>
        /// How many times this player has killed that boss, as the world remembers it. Zero for
        /// a player with no record, for a key Vandi has never written, and before a world is
        /// loaded.
        /// </summary>
        /// <param name="playerId">Player.GetPlayerID(). It can be negative, and that is fine.</param>
        /// <param name="bossKey">The boss's defeat key, e.g. defeated_gdking. Any case.</param>
        public static int BossKills(long playerId, string bossKey)
        {
            return Kills.Count(playerId, bossKey);
        }

        /// <summary>
        /// The local player's own count for that boss, or zero before there is a local player.
        /// This is the one a client-side mod wants: the swinging, crafting or reading player is
        /// the one at this keyboard.
        /// </summary>
        public static int LocalBossKills(string bossKey)
        {
            return Kills.LocalCount(bossKey);
        }

        /// <summary>
        /// Whether Vandi records kills of this boss at all, which it does only for a key its
        /// BossBiomes setting maps to a biome.
        ///
        /// Here so that a mod gating something on a count can tell "not killed yet" from "never
        /// going to be counted". The second reads as zero forever, exactly like the first, and a
        /// player waiting on it would wait forever without anything saying why - so a caller
        /// should check this once and say so in its log.
        /// </summary>
        public static bool CountsKillsOf(string bossKey)
        {
            if (string.IsNullOrEmpty(bossKey)) return false;

            return Bosses.IsBossKey(bossKey.Trim().ToLowerInvariant());
        }
    }
}
