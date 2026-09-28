# Vandi

Creatures wear more stars in a biome whose boss you keep killing, and that boss comes back
harder the next time you summon it. All of it follows your own kills, not the server's.

**Written, never played.** The code is here and it compiles. Nothing has been played; the two
scenarios in `scenarios/` have been run in a world, and Testing below says how that went. The
design is on the site,
where players were asked about it before any of this existed:
[Stars worth earning, and bosses that remember you](https://longhouse.thijssensoftware.nl/devlog/stars-worth-earning).

Vandi is Old Norse for trouble.

## The problem

Once a biome's boss is dead, that biome is mostly walking. Its creatures cannot hurt you any
more, and the starred ones actually worth meeting, for the drops or for taming, are rare enough
that finding one is a chore rather than an event. The usual answer is a difficulty setting,
which raises the whole world for everybody at once and lands hardest on the player who has not
got there yet.

The idea came from CRVD in the Discord: let the biomes you have beaten be the ones that get
harder.

## What it does

- Killing a boss raises the chance of starred creatures **in that boss's own biome**. The Elder
  changes the Black Forest, Moder changes the Mountains. A biome whose boss you have never
  beaten stays exactly as it was.
- Every repeat kill of that boss adds more: five percentage points a kill, stopping at twenty
  five extra. The points go on top of whatever the game already rolls where the creature spawns.
  That is ten in an ordinary stretch of a default world, so there a biome whose boss you have
  killed five times spawns starred creatures about a third of the time. The game rolls higher in
  parts of the world far from the centre and lower on a world set to fewer stars, and the same
  twenty five go on top of that. Creatures still cap at two stars: only the odds move, never the
  ceiling.
- Each repeat kill also makes that boss **one star harder** the next time you summon it, capped
  at two.
- All of it is yours. A player who joined last week walks into an ordinary Black Forest while
  the veteran standing beside them does not.

Starred creatures drop more and a harder boss drops more, so the mod pays for the trouble it
makes. It is a reason to go back to a biome rather than a reason to avoid one.

## The part that will start arguments

Credit for a boss kill goes to the player who **made the offering**, and to nobody else. They
are credited even if they are dead, offline or across the map when it falls, because they paid
for it. Help a friend kill their Bonemass and you get nothing from it; summon your own and it
counts.

That is deliberate. Credit shared by everyone standing nearby turns a personal record into a
server score, and the one promise here is that your world reflects what you have done yourself.
The cost is real, and worth saying out loud: a group that always fights on one person's
offerings only moves one person's world, so everyone summons their own.

## Why it is not part of Utangard

Utangard already knows who has beaten what, so the boss half could have lived there. It does
not, because the work is a patch on the roll that **every creature spawn in the game** goes
through, and that is a much wider seam than Utangard's promise. Two different jobs, two mods,
and either one can be uninstalled without the other noticing.

## Multiplayer

Everyone will need it, at the same build. The star roll happens on whichever client owns the
zone a creature spawns in, so one player without Vandi spawns ordinary creatures for everybody
standing around them.

## How it keeps the count

Your kills live in the world's own global keys, one per player per boss, as
`vandi_p_<playerid>_<bosskey>` with the number as its value. Not on your character, and the
reason is the credit rule above: the machine that records a kill is whichever client owned the
boss when it died, and that is often not yours. The world is the one place both machines can
reach.

A count is written only when it changes, which is at most five times per boss per player,
because the boost stops at five kills. That matters more than it looks: accepting one global
key makes the server rebroadcast the whole key list to everybody connected, so a mod that
wrote one every few seconds would be felt by people who do not have it installed.

## For other mods

Another mod can ask how many times a player has killed a boss through `Vandi.VandiApi`:
`BossKills(playerId, bossKey)`, `LocalBossKills(bossKey)` for the player at the keyboard, and
`CountsKillsOf(bossKey)` to check that Vandi records that boss at all. It only reads.

Use that rather than the key above. The key layout is Vandi's own business and can change, and
a mod reading the key directly would then read zero kills for everyone without saying so.

Malmr, the vein mining mod, is the one that uses it. A metal's vein mining
opens only once you have beaten that biome's boss at one star, which is your second kill.

## What decides what, and where

- **The star chance** is one postfix on `SpawnSystem.GetLevelUpChance`, which is the single
  roll behind open-world spawns and every placed spawner, so camps and crypts are covered
  without naming them. Vandi's points are added after the game's own factors, the world's star
  setting and the land's multiplier, and are not scaled by either. A spawn entry can also skip
  the star roll altogether near the world centre (its `m_levelUpMinCenterDistance`), and where
  it does Vandi adds nothing, because the method is never called. It runs on whichever client
  owns the zone, and asks that client's own player. Two people standing in the same forest
  therefore see one answer, the owner's. A player alone in a biome always sees their own.
- **A dungeon counts as the biome above it.** Interiors sit directly above their entrance and
  biome lookups compare only x and z, so a crypt is Swamp and a frost cave is Mountain without
  this mod knowing what a dungeon is.
- **The boss's stars** are set by whichever client owns the altar, reading who made the
  offering, and the summoner's id is stamped on the boss itself so the credit survives the
  handover to a different machine.
- **The kill is recorded** by the client that owned the boss, from that stamp. A boss that was
  never summoned through an altar credits nobody.

## Settings

The file is `BepInEx/config/ezomic.valheim.vandi.cfg`, written on first run.

| Key | Default | What it does |
| --- | --- | --- |
| `Enabled` | true | Off leaves the plugin loaded and changing nothing |
| `Verbose` | false | Log every roll this changes and every kill it credits |
| `StarChancePerKill` | 5 | Percentage points added per kill of that biome's boss, on top of the game's own roll there |
| `StarChanceCap` | 25 | The most that can be added, whatever the count |
| `BossBiomes` | the eight pairings | `boss:biome`, comma separated, spelled the same as Utangard and Vaettir spell it |
| `HarderBosses` | true | A summoned boss you have killed before arrives with stars |
| `BossStarCap` | 2 | The most stars a summoned boss can gain |

On a server with Core the host's values apply to everyone, so these are the server's decision
and not each player's.

## Testing

`scenarios/` holds two Devkit scenarios. `vandi-stars-per-biome` goes to the nearest Meadows,
reads the star roll there, seeds a record and checks how the roll moves at each step, including
that a boss from another biome changes nothing. It measures first because the roll is not 10
everywhere: in 1.0 a biome sector can carry a modifier that multiplies it, and in one Meadows
stretch four kilometres out in the dev world the game rolled 20, on a world with no star setting
changed. `vandi` in the console prints the roll where you stand and how much of it is Vandi's,
which is what the scenario reads.
`vandi-summoner-gets-the-credit` brings its own Eikthyr altar through `location`, so it needs
only a little open ground ahead of you, most simply in the Meadows. It drives the real summoning
path, because the boss half is only real through an altar.

`vandi-stars-per-biome` passed on 2026-09-24, and on 2026-09-28 failed
in that doubled stretch with every reading ten high while the mod added exactly what it should;
it now reads where it stands first. `vandi-summoner-gets-the-credit` first got past the altar on
2026-09-28. Its log shows both halves working, the kill credited to the summoner and the next
boss arriving at one star, and it failed only because it looked for each boss nine seconds after
the offering, which is sooner than this altar sends one. Neither has passed in its current form
yet. Two things they do not cover: what a second player sees while standing in somebody else's
zone, and the rule that helping with a friend's boss earns nothing, which needs two clients.

## Bugs and ideas

Both go to the site. [longhouse.thijssensoftware.nl/bugs](https://longhouse.thijssensoftware.nl/bugs)
is for anything broken, and [longhouse.thijssensoftware.nl/ideas](https://longhouse.thijssensoftware.nl/ideas)
is for what a mod should do next. You can vote on other people's ideas there as well.

Signing in takes a Steam or Discord account. I work from that list, so the votes decide what
I pick up next.

## Licence

MIT. See `LICENSE`.
