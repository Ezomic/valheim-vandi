# Changelog

## 1.0.0 - 2026-09-30

First release. Both Devkit scenarios in `scenarios/` pass, and a two-player run on a dedicated
server credited each boss kill once, to the summoner.

- **Starred creatures get likelier in a biome whose boss you have killed.** Five percentage
  points a kill, up to twenty five extra, added to whatever the game already rolls where the
  creature spawns. That is ten in an ordinary stretch of a default world, so there a biome whose
  boss you have killed five times spawns starred creatures about a third of the time. The game
  rolls higher in parts of the world far from the centre and lower on a world set to fewer
  stars, and the points go on top either way. Creatures still stop at two stars, and a biome
  whose boss you have never killed is left as it was.
- **Each boss owns its own biome.** Eikthyr the Meadows, the Elder the Black Forest, Bonemass
  the Swamp and the Ocean, Moder the Mountains, Yagluth the Plains, the Queen the Mistlands, and
  Fader the Ashlands and the Deep North. `BossBiomes` in the config changes the pairs. Open-world
  spawns, camps and dungeon spawners all go through the same roll, and a dungeon counts as the
  biome above its entrance.
- **A boss you have killed before comes back harder.** One star for each earlier kill by whoever
  made the offering, up to two. `HarderBosses` turns this half off and leaves the rest alone.
- **Credit goes to whoever made the offering**, even if they are dead, offline or far away when
  the boss falls. Helping with somebody else's boss earns nothing. A boss that was not summoned
  at an altar with Vandi installed credits nobody.
- **The count is kept in the world, not on your character**, one global key per player per
  boss. The kill is recorded by whichever machine owned the boss when it died, which is often
  not the summoner's, so the world is the one place both can reach.
- **The star roll belongs to the zone owner.** Spawns roll on whichever client owns the zone,
  against that client's own record, so two people in the same forest both get the owner's odds.
  Alone in a biome you always get your own.

Everyone needs it, at the same build. With Core installed it registers at
`Requirement.Everyone`, because a player without Vandi spawns ordinary creatures for everyone
near them. The host's settings apply to everybody.

### For other mods

- **Other mods can read your kill count.** `VandiApi.BossKills` and `LocalBossKills` answer how
  many times a player has killed a boss, and `CountsKillsOf` whether Vandi counts that boss at
  all. It only reads, and answers the same whether Vandi is switched on or not. Malmr uses it to
  open a metal's vein mining once that biome's boss has been beaten at one star.

### Console

- `vandi` prints the boss kills this world has credited to you, one line per boss Vandi
  counts, and the star chance where you stand with Vandi's share of it. It only reads, so it
  is not a cheat and needs no devcommands.
